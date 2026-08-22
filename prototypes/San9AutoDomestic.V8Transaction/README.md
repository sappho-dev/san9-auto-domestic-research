# San9AutoDomestic V8 权威离线单任务事务契约

本目录定义“一座城市的一条内政命令”的故障安全事务边界。它仍然是**永久非授权、纯离线**组件：不连接游戏、不读取进程、不调用游戏函数、不发送 Windows 消息，也没有 live 开关。

当前结论：

- 权威离线事务契约及合成验证：GO。
- 接入现有 Core、Adapter、UI、V5.2：尚未进行。
- 任何 live/native 业务执行：NO-GO。

这里的静态锁只证明**单个 AppDomain 内**的离线事务互斥；它不是跨 helper/跨进程锁。未来产品接入必须另行设计桥内或 OS 级 single-flight 并重新审计，在此之前不得把本目录的 GO 外推为 live GO。

## 已封闭的入口

- `SyntheticStageEvidence`、状态机、one-shot intent、fake transport、进程代协调器和 native callback 声明全部为 `internal`。
- 发布程序集不声明 `InternalsVisibleTo`；self-test 直接链接同一组 `src/*.cs` 源文件编译，且验收会另外加载实际构建出的 production DLL 检查 IVT/export surface 与 test/raw-settlement seam 不存在，不能靠同名弱友元程序集取得 capability。产品程序集没有 native callback 或 cleanup callback 实现。
- 状态机构造器为 `internal`，所有正常推进只经同一 `ProcessGenerationCoordinator` 全局锁完成。
- 多个 coordinator 实例按当前 `PID -> process creation + generation` 共享一个全局 single-flight 槽、sequence 高水位和 fault latch；同 PID 新 creation 会原子退休旧 session、撤销旧 capability/factory。表有 256 项硬上限，未使用 session 可显式回收，使用中/已有历史的 session 禁止被“回收”绕过。

## 核心安全语义

- 请求只携带相对 TTL（protocol v2）；coordinator 读取自身可信单调时钟并冻结绝对截止点。发布版只能使用 sealed、无 callback 的系统时钟；测试注入仅在 `V8_SELF_TEST` 编译符号下存在，外层非重入 operation gate 使 clock 回调无法换代，异常统一 fail closed。
- 行为阶段先签发 intent，再经 coordinator 完成 `Issued -> Executing` CAS claim；真正调用 callback 前，coordinator-owned invoker 再以 fresh trusted clock 复核 active claim/session/process creation/generation/request/exact stage+method/deadline，并对该 claim CAS 一次 `SideEffectEntered`。同一 claim 串行或并发双投递、错 method、claim 后排队到 deadline 都不会第二次进入副作用。receipt 只能令同一 intent `Executing -> Receipted -> Consumed` 一次。
- invoker 分三段：锁内授权、释放 `GlobalSync` 和 process-global operation gate 后同步调用外部 callback、重新进入锁内认证结算；外部/native callback 不在 coordinator 锁或全局 gate 下运行。
- 任意 Abort 会原子执行 `Issued -> Revoked`；若 capability 已 `Executing` 且没有 receipt，则 fault 直接升级 `HaltRestart`，cleanup/ACK/换代均不能解锁。callback 失败无需伪造 stage evidence。
- evidence 自带时间只作诊断；不能延长或缩短请求寿命。队列/副作用前的时限判定只采用 coordinator 内冻结的可信单调时钟。
- 任意 `AbortUncertain` 都锁住同一进程/批次。只有显式人工确认后再进入严格更高且 digest 不同的新 generation，或游戏进程创建代变化，才可能恢复。
- cleanup 不接受裸 `bool`：必须先 claim 一次性 cleanup intent；actual-entry permit 只负责一个 typed cleanup side effect，callback 返回后 coordinator 才依次签发绑定 exact permit/PermitId 的 A/B read permits 并完成 post-state 采样。receipt 回绑 session/fault/directive/process/request、精确 root/target/modal generation 及后置状态。失败、错代、未知、重放、pre-action read 或并发消费升级 `HaltRestart`；`DoNotRollbackAfterAccept` 也必须交付稳定人工审查快照。
- cleanup 也使用与 action 对称的 actual-side-effect admission：exact cleanup kind/method、fresh deadline 和 active claim 全部在 callback 边界复核，且同一 cleanup claim 只能进入一次。
- post-state 必须使用同一 session 签发的独立、有序、不可重放 A/B read tickets；禁止同一对象、同 ordinal 或 receipt replay。generation 严格递增，摘要绑定进程、context、city、corps、descriptor、顺序五人、busy 五人、order、command state 和 money。

## 文件

- [DESIGN.md](DESIGN.md)：完整协议、不变量、cleanup 和 GO/NO-GO。
- `src/`：descriptor/request、进程代 coordinator、one-shot intent、状态机、认证 post snapshot。
- `tests/`：真实并发 barrier、多 coordinator、故障锁存和逐字段失败测试。
- `build-and-test.ps1`：只构建本目录，不调用仓库根构建。

## 离线运行

```powershell
cd 'C:\codex files\San9AutoDomestic\prototypes\San9AutoDomestic.V8Transaction'
.\build-and-test.ps1
```

成功输出必须包含：

```text
V8 OFFLINE SELF-TEST PASS: 54 tests
live_authorization=false; process_accessed=false; native_callbacks_invoked=false
V8 RESULT: OFFLINE-ONLY GO; LIVE/NATIVE BUSINESS EXECUTION NO-GO.
```

若本机缺少 .NET Framework 4.8 targeting pack，脚本会明确标注本地 runtime/GAC 验证回退；该产物不得发布。

本原型没有 live callback watchdog。虽然 callback 期间不持有全局 gate，其他线程仍可 `PollExpiry` 并把 entered-but-unsettled 状态锁死为 `HaltRestart`，但未来产品还必须提供跨 helper single-flight、受控超时/进程终止和 callback 卡死后的外部监督；这些能力未实现前 live 继续 NO-GO。
