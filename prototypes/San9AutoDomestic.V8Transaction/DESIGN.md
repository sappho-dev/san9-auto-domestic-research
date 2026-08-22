# V8 单城市单项目权威离线事务契约

状态：**离线契约 GO；产品接入和 live/native 业务执行 NO-GO。**

V8 的原子单位只有 `{cityId, corpsId, command, orderedOfficerIds[5]}`。方案、城市遍历、优先级、任务增删、前项耗人后的重新观察仍由 C# Core 负责；本目录不含 `basic`、`wealthy`、profile 或跨城市循环。

## 1. 可见性与信任边界

以下全部是 `internal`：

- 合成 stage evidence；
- 纯状态机及其构造器；
- `ProcessGenerationCoordinator`；
- one-shot action intent/receipt；
- in-memory fake transport；
- native callback 接口声明；
- session observation capability、认证工厂和 post-state read。

发布程序集完全不声明 `InternalsVisibleTo`。self-test 工程直接链接同一组 `src/*.cs` 源文件并在独立 EXE 内编译，同时从磁盘加载脚本先构建出的 production DLL，反射检查其真实 IVT/export surface，并断言全部 `*ForTesting` 与 raw `SubmitActionReceipt/SubmitCleanupReceipt` seam 不存在；弱命名同名程序集不能取得发布 DLL 的内部 capability。产品程序集不存在 native callback/cleanup callback 实现，也不存在公开 API 可把一份自行构造的 synthetic evidence 解释成授权。所有 `AuthorizesLiveMutation`、`CommitAuthorized` 和 transport authorization 位恒为 `false`。

## 2. 五命令封闭 registry

| command | native id | typed outer task | exact outer vptr | cost | count |
|---|---:|---|---:|---:|---:|
| Patrol | 0 | Patrol | `0x0060B920` | 250 | 5 |
| Commerce | 1 | Commerce | `0x0060CCB0` | 250 | 5 |
| Cultivate | 2 | Cultivate | `0x0060BBA0` | 250 | 5 |
| Train | 5 | Train | `0x0060C370` | 0 | 5 |
| Repair | 3 | Repair | `0x0060BCE8` | 250 | 5 |

descriptor 同时绑定 stable key、native id、typed outer enum、type key、vptr、money model、cost 和 exact count。以上所有字段都进入 `SingleCommandRequest.RequestFingerprint`。unknown/default enum 在构造边界抛出，不能降级为某个默认命令。

`OpenOuter` 不接受调用者自报的字符串 key；状态机比较实际 typed observation 的 `{nativeId, outerTaskType, outerTaskVptr}` 与冻结 descriptor。

## 3. 原子请求

`SingleCommandRequest` 包含并验证：

- 非空 `requestId`、严格递增 `sequence`；
- registry 内的一个 command；
- `cityId 0..49`、`corpsId 0..49`；
- 顺序有意义、互不重复的恰好 5 个 `officerId 0..849`；
- 单项冻结 `reserveMoney`；
- 32-byte 非零 `contextDigest` 与 `generationDigest`；
- 1..10000 ms 的相对 TTL；绝对 monotonic start/deadline 只由 coordinator 在抢到 slot 时冻结；
- 对上述字段及完整 descriptor 的 SHA-256 fingerprint。

请求描述待验证事务，不是 capability，`AuthorizesLiveMutation=false`。

## 4. 全局进程代 coordinator

所有 coordinator 共用一个静态统一锁和 `PID -> 当前 processCreationUtcTicks` 的 session 表。session 内冻结：

- 当前 `generationNumber + generationDigest`；
- 唯一可信单调 clock 实例；
- session id 与不可伪造的 observation capability；
- 全局 active machine/handle；
- request replay 集合与 sequence 高水位；
- fault latch、人工确认和 cleanup 状态。

同一进程创建代、同一 generation 的多个 coordinator 只能抢到一个 transaction slot。真实 `Barrier` 双线程测试证明竞争开始时只有一个成功。相同 generation number 配不同 digest、旧 generation、活动事务期间切 generation 均拒绝。

generation 升级必须同时满足：

1. number 严格增加；
2. digest 与旧 generation 不同；
3. 无活动事务；
4. 若曾 `AbortUncertain`，必须已完成可恢复 cleanup 且收到精确人工确认。

同 PID 更大的 process creation 视为 PID 的新所有者：在同一次临界区内先退休旧 session、撤销 pending intent/read ticket/cleanup intent/observation factory，再替换为全新 session。旧 coordinator 的所有 API 和已缓存 factory 都失效；更小 creation 一律按 stale 拒绝。session 表有 256 项硬上限，达到上限 fail closed；只有从未开始请求、没有 fault/verified-next/active machine 的空 session 才可显式移除，不能靠 retire 清除 replay/fault 状态。

该静态表仅提供单个 AppDomain 内的离线互斥，不等价于跨 helper 或跨进程 single-flight。桥内/OS 级锁仍属产品接入前置条件，未实现即 live NO-GO。

## 5. 可信时钟、非重入 gate 与证据时间

发布 DLL 只暴露使用 sealed `SystemTrustedMonotonicClock` 的 `Attach(key)`；可注入 clock 的 `AttachForTesting` 仅在 `V8_SELF_TEST` 条件编译中存在。所有 coordinator API 先进入不可同线程重入的外层 operation gate，在未持有内部 `GlobalSync` 时读取 clock，随后进入 `GlobalSync` 并重新核验当前 session/key/handle。clock 回调若尝试 Attach/换代/推进，会在碰到状态前被 gate 拒绝；clock 抛错则：未开始的请求被拒绝，已有事务一律 `AbortUncertain(TrustedClockFailure)`。

请求只提交相对 TTL。coordinator 抢到 slot 后用 clock frequency 做 checked 换算并冻结半开 `[start, deadline)`。以下位置均重新采样：

- 开始事务前；
- 签发任何 action intent 前；
- actual callback side-effect entry 前；
- callback 返回并消费 receipt 前；
- 显式 expiry poll。

半开截止边界为 `now >= deadline`。若 receipt 已排队但消费前过期，coordinator 只撤销当前 machine 的 pending capability；绝不读取或消费未验证 receipt 携带的 foreign intent。

evidence 的 `ObservedAtUtcTicks` 只保留诊断用途。测试把它伪造成请求前和极远未来，可信 clock 未过期时事务仍按 clock 正常推进，证明自报时间不参与授权。

## 6. action intent 与一次性 callback

下列七个阶段是 action：

```text
BindTargetCandidate
OpenOuter
OpenSelector
Clear
NativeFillMax
AcceptInner
AcceptOuter
```

每次必须：

1. coordinator 采样可信 clock，进入状态锁后复核 session/context/generation/deadline；
2. machine 先把本 stage 记入 issued set；
3. `AcceptInner/AcceptOuter` 的 attempt 先增为 1；
4. 签发绑定 `{session, requestId, fingerprint, sequence, stage, ordinal, nonce, attempt=1}` 的 intent；
5. dispatcher 调用 `ClaimActionExecution`，以 CAS 完成 `Issued -> Executing`，但 claim 本身还不允许副作用；
6. coordinator-owned invoker 在**实际 callback 边界**重新采样 trusted clock，并在锁内逐项复核 current coordinator/session、PID+creation、generation+digest、request+fingerprint、exact active claim、stage+native method 和半开 deadline；随后对 claim CAS `SideEffectEntered: 0 -> 1`，签发绑定上述字段及 claim nonce 的 sealed permit；
7. invoker 释放 `GlobalSync` 和 process-global operation gate，才用 exact typed method 同步调用 callback。callback 收到冻结 request 与 permit，不能凭裸 request/intent/claim 进入副作用；
8. callback 返回后 invoker重新进入 operation、重新采样 clock并复核同一 active permit；由冻结 session factory完成 `Executing -> Receipted`。失败 receipt 不要求伪造 stage evidence；
9. coordinator 以 CAS 完成 `Receipted -> Consumed`，再核验 session capability、pending intent、permit 和 typed evidence 后推进。

完整状态为 `Issued -> Executing(claimed) -> SideEffectEntered(invoker CAS) -> Receipted -> Consumed`；Abort 可令尚未执行的 `Issued -> Revoked`。同一成功 claim 的串行/并发双投递只有一个能通过 `SideEffectEntered`，错 method/stage 和 claim 后排队至 deadline 均在 callback 之前 fail closed。所有 Abort 都尝试撤销未执行 intent，因此 cleanup/ACK/新 generation 后的旧 callback 无法迟到 claim。若 Abort 观察到 `Executing` 且尚无 receipt，或已 entered 的七类 action callback 抛错/返回空而没有认证成功 evidence，无法证明 native 后置状态，统一升级 `HaltRestart`；cleanup 和人工 ACK 均不能解锁。

外部 callback 整段不持有 `GlobalSync` 或 process-global operation gate；测试让 callback 同线程重入 coordinator，并让第二线程在首 callback 阻塞时提交同一 claim，证明不存在 gate 型死锁且副作用计数仍为 1。callback 返回前若另一路触发 fault，首 callback 的结果不得解锁，entered-but-unsettled 状态保持 `HaltRestart`。

## 7. 事务阶段与名单约束

完整路径：

```text
Begin(observation)
 -> issue/receipt BindTargetCandidate
 -> issue/receipt OpenOuter
 -> issue/receipt OpenSelector
 -> issue/receipt Clear
 -> issue/receipt NativeFillMax
 -> VerifyExactlyExpected5(observation)
 -> issue/receipt AcceptInner
 -> VerifyWorking(observation)
 -> issue/receipt AcceptOuter
 -> VerifyCommitted(authenticated stable snapshot)
```

每份 observation 重新绑定 request id/fingerprint/sequence/stage/ordinal/context/generation/city/corps，并且必须由当前 session capability seal。

- `Begin` 是唯一 SkipBeforeMutation 门：委任、灰色、少于 5、资金/预留金不足可解释跳过。
- `Begin.availableOfficerCount` 必须在 `0..850`。
- `OpenSelector.sourceCount` 必须等于 Begin 的最终数量、至少 5 且不超过 850。
- source prefix、selected、working、post ordered、post busy 都必须精确等于请求的有序五人。
- root/target/outer/selector 使用 `{kind, identity, generation, alive}` token；销毁或同 identity 换 generation 都 Abort。
- `AcceptInner` 后 selector token 立即失效；`AcceptOuter` 后 outer/selector 均不得参与 post-state 验证。

## 8. 认证稳定 post-state

`VerifyCommitted` 只接受当前 session capability 生成的 `AuthenticatedStablePostSnapshot`：

- coordinator 只签发一次 ordinal 1/2 的独立 read tickets；A 必须完成 receipt 后 B 才能 claim；
- 两个 ticket/claim/receipt 都是 one-shot，禁止同一对象、同 ticket、同 ordinal、错序、重放或并发消费；
- A/B 两次 read 分别计算 canonical SHA-256，必须完全相同；
- result generation number 必须严格高于 coordinator generation；
- result generation digest 不得复用初始 digest；
- request id、sequence 和完整 request fingerprint 必须回绑，禁止把前一事务快照重放给下一事务；
- process PID/creation、context、city、corps 和完整 typed descriptor 必须相同；
- ordered five 与 busy five 都必须精确相同；
- command state 和 native order 必须已验证；
- moneyBefore 必须等于 Begin，moneyAfter 必须精确等于 `before - descriptor.cost`。

canonical digest 覆盖上述每个字段。一次成功提交后，coordinator 还会锁住下一次 start，直到调用方 attach 的 generation 精确等于该认证 post-state 的 number/digest。测试逐一破坏 city、corps、command/descriptor、ordered list、busy list、order、command state、money、generation 和 A/B 稳定性，每项都独立 fail closed。

## 9. fault latch 与 cleanup

任何 `AbortUncertain` 都在 coordinator 同一锁内把进程/批次置为 faulted，并阻止后续 request。Abort 同时产生一条阶段化 directive：

| 不确定点 | directive |
|---|---|
| 尚未签发任何 action | `NoMutation` |
| Bind 可能已执行 | `ConditionalRestoreTarget` |
| selector 可能存在 | `CancelSelector` |
| inner 已接受或 outer 可能存在 | `CancelOuter` |
| outer accept 已尝试 | `DoNotRollbackAfterAccept` |
| cleanup 失败 | `HaltRestart` |

directive 绑定 `{session, PID, creation, generation+digest, requestId+fingerprint, context, stage, root/target/outer/selector identity+generation}`。本目录没有执行 cleanup 的 live 实现，只定义如下强制协议：

1. coordinator 对当前 fault/directive 只签发一个带 TTL 的 cleanup intent；
2. cleanup callback 先 CAS claim，但 actual side effect 仍必须经 coordinator-owned invoker：fresh clock、current session/PID creation/generation/fault/directive、exact active claim、cleanup kind+typed method、deadline 全部复核后，CAS 一次 `SideEffectEntered` 并签发 exact permit；A/B tickets 此前不存在；
3. invoker 释放所有 coordinator 锁/gate 后调用与 directive 对应的唯一 typed cleanup method；同一 claim 串行/并发双投递、错 method 或 claim 后到期都在副作用前失败并直接 `HaltRestart`；cleanup action callback 只执行 `void` side effect，不能提交 receipt；
4. action callback 返回后，coordinator 才依次签发绑定 exact cleanup permit、PermitId 与 session-private authority 的 A/B read permits；每次锁外采样、锁内认证，A receipt 完成后才可签 B。callback 无 authority，不能在 action 前或 action 中 claim read ticket；
5. authenticated receipt 的 A/B canonical digest 必须稳定，pre tokens 必须精确回绑 directive；一般 cleanup 要求原 root 同代存活且 target/outer/selector 全部消失；
6. `DoNotRollbackAfterAccept` 不允许“裸确认”：必须给出全新的严格后继 generation/digest、无 stale UI token、`manualReviewCompleted=true` 的稳定 A/B 人工审查快照；下一代必须精确等于该审查 generation；
7. callback 失败、clock/TTL 失败、错对象代、未知 receipt、重放或并发消费都升级 `HaltRestart`。

需要 cleanup 的 directive 未通过以上 receipt 前，人工确认被拒绝。`NoMutation` 只有在 pending action 尚处 `Issued` 且 Abort 成功撤销、或确实从未签发 mutation 时才自动完成；`Executing` 无 receipt 永远不能靠 cleanup 解锁。

一般 Abort 的恢复顺序为：审查不确定状态 → 按 directive 完成人工/未来受控 cleanup → 精确人工确认 `ACKNOWLEDGE_ABORT_UNCERTAIN` → 进入 number 更高且 digest 不同的新 generation。人工确认本身绝不清除当前 generation。

## 10. 离线验收

独立 Release/Werror self-test 当前为 54 项，覆盖：

- actual production DLL 的 public/internal 边界与无弱 IVT、native/cleanup callback 零实现和 protocol v2 TTL；
- 五个 descriptor 的逐字段 exact mapping、构造 fail-closed、unknown/default 拒绝、fingerprint；
- 五命令完整成功链和四类 Skip；
- 真实 Barrier global single-flight、多 coordinator；
- intent-before-callback、执行前双线程 CAS claim、禁止重签、Abort revoke、deadline 迟到 claim、无 receipt 执行中必须重启、无 evidence failure receipt；
- action 同一 claim 串行/并发双投递实际 side-effect count=1、wrong method count=0、claim 后到 deadline count=0，以及 callback 期间 coordinator 可重入；
- 七类 action stage 的 throw/null entered-failure 矩阵全部 `HaltRestart`，cleanup/ACK/generation advance 均不能解锁；
- clock 重入换代拒绝、clock throw fail-closed、TTL 的 intent/claim/receipt 边界与伪造 evidence 时间；
- 错城、对象销毁/复用、typed outer 三字段、source count 和名单/context；
- 认证 post snapshot 各字段独立失败及 A/B 错序/同对象/同 ordinal/replay；
- 成功提交后必须精确切换到认证 post-state generation；
- fault 后新任务阻断、旧 intent 跨 cleanup/ACK/新 generation 迟到拒绝、人工确认+新 generation；
- 同 PID 新 creation 在旧事务 active 时原子退休、旧 factory/API 失效、session 表硬上限与安全回收；
- 六类 cleanup、认证 one-shot cleanup A/B 对象后置、错代/foreign/replay/并发/failure → HaltRestart、post-accept 稳定审查；
- cleanup 同一 claim 串行/并发双投递实际 side-effect count=1、wrong method count=0、claim 后到 deadline count=0，以及 callback 期间 coordinator 可重入；
- cleanup pre-entry 不存在 A/B permit、action callback 无 authority 不能提前 claim A、A/B 只能在 callback 返回后由 coordinator 按独立 ordinal 采样；
- 七 action stage 与四 cleanup kind 的 typed method 使用独立 oracle 逐项核对，避免实现与测试自洽错配；
- fake transport internal/non-live 和所有授权位恒 false。

## 11. GO / NO-GO

| 能力 | 结论 |
|---|---|
| 本目录的权威离线单事务契约 | GO |
| 并发、时限、one-shot、fault/cleanup、post-state 合成验证 | GO |
| 作为后续 Core/bridge 接入的审计基线 | GO |
| 接入 Core/Adapter/UI/V5.2 | NO-GO；本轮未做 |
| 跨 helper/跨进程 single-flight | NO-GO；当前只有 AppDomain 内静态 gate |
| callback watchdog/卡死终止与外部监督 | NO-GO；callback 期间 gate 可用但本原型不终止 hung native call |
| native callback/cleanup 实现 | 不存在 |
| 调用 V7 地址、发 `WM_COMMAND`、写 root target | NO-GO |
| 任意 cityId 的合法 native target binding | NO-GO |
| nested modal generation/重入/过期消息 live 闭环 | NO-GO |
| 自动提交单条或全城内政 | NO-GO |
