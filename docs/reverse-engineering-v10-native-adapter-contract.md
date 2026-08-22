# V10：单城单命令原生适配契约（离线）

状态：**离线、非授权契约已实现并通过本地交叉验证；任何 live/native 业务执行仍为 NO-GO。**

V10 不调用游戏函数，也没有进程访问、写内存、注入、hook、窗口消息或输入能力。它只把 V3/V7/V9 已确认的五类命令和原生 UI 阶段冻结成 x86 数据契约，供后续 broker/native adapter 实现时做版本锁定和回归门。不得把“地址已登记”解释成“地址已获准调用”。

## 1. 精确目标

| 项目 | 值 |
|---|---|
| EXE | `D:\三国志9\10101749\San9PK.exe` |
| 版本 | `1.0.1.0` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| PE | x86 / PE32 |
| ImageBase | `0x00400000` |
| SizeOfImage | `0x01759000` |

## 2. 五命令注册表

| V8 command | native id | 费用 | outer task vptr | outer event | handler vptr | CanExecute | ExecuteUI | factory case |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Patrol | 0 | 250 | `0x60B920` | `0x4D8BA0` | `0x609B90` | `0x4C1930` | `0x4C1840` | `0x5128B8` |
| Commerce | 1 | 250 | `0x60CCB0` | `0x4E72D0` | `0x609F38` | `0x4C6400` | `0x4C6310` | `0x512900` |
| Cultivate | 2 | 250 | `0x60BBA0` | `0x4DA670` | `0x609BF0` | `0x4C1F50` | `0x4C1E60` | `0x512948` |
| Train | 5 | 0 | `0x60C370` | `0x4DF8E0` | `0x609CE0` | `0x4C3890` | `0x4C37A0` | `0x512A20` |
| Repair | 3 | 250 | `0x60BCE8` | `0x4DB170` | `0x609C20` | `0x4C2270` | `0x4C2180` | `0x512990` |

费用是五人总额。训练原生费用为零；其余四项每人 50、恰好五人共 250。配置只能组合、增删、重排这五个已注册命令，不能通过配置引入任意 native id 或地址。

## 3. 七个带副作用阶段

| V8 stage | 静态路线 | event | 对象 vptr | 当前动态状态 |
|---|---|---:|---:|---|
| BindTargetCandidate | `root+0x38` 条件 CAS 候选 | — | root `0x610BC8` | 未证明、未授权 |
| OpenOuter | root event `0x5179B0` | `0x2710+nativeId` | root `0x610BC8` | 静态链确认；生命周期未证明 |
| OpenSelector | 命令各自 outer event | `0x03E8` | 命令各自 task vptr | 静态链确认；modal 时序未证明 |
| Clear | selector event `0x578090` | `0x1D52` | selector `0x61F0B8` | 静态链确认；消息 envelope 未证明 |
| NativeFillMax | selector event `0x578090` | `0x1D51` | selector `0x61F0B8` | 静态链确认；消息 envelope 未证明 |
| AcceptInner | selector event `0x578090` | `0x1D4D` | selector `0x61F0B8` | 静态链确认；析构/迟到消息未证明 |
| AcceptOuter | 命令各自 outer event | `0x0BB9` | 命令各自 task vptr | 静态链确认；工作链/确认时序未证明 |

所有 route 的 `dynamic_lifecycle_confirmed` 与 `live_authorized` 固定为 `0`。三个验证阶段（VerifyExactlyExpected5、VerifyWorking、VerifyCommitted）只登记为未来只读验证，不提供调用入口。

## 4. 请求与跨语言指纹

V10 的请求字段与 V8 `SingleCommandRequest` 对齐：

- protocol v2、非零 request ID、严格正序 sequence；
- city `0..49`、corps `0..49`；
- officer count 必须为 5，五个 ID 唯一且位于 `0..849`；
- reserve money `0..1,000,000`；TTL `1..10,000ms`；
- context/generation/request fingerprint 均为 32 字节；
- fingerprint 在 V10 内重新计算并做常数时间比较，不能由调用方自报一个非零值蒙混通过。

指纹严格复刻 V8 的 `.NET BinaryWriter` 顺序：小端整数、字符串的 7-bit UTF-8 长度、`Guid.ToByteArray()` 的 16 字节顺序。独立 C# 金标准向量为：

```text
requestId = 00112233-4455-6677-8899-aabbccddeeff
Guid.ToByteArray = 33221100554477668899AABBCCDDEEFF
sequence = 0x0102030405060708
command = Commerce, city=30, corps=1, officers=100..104
reserve=777, ttl=5000
fingerprint = DFD94456128CAAB757A25389725184C5250B230A4BC98E82D7D784789093E0B7
```

当前 x86 ABI 固定为 176 字节，关键偏移：`sequence=24`、`officer_ids=48`、`context_digest=76`、`request_fingerprint=140`。该结构仅是本地编译契约，**不是 IPC wire format**；后续 IPC 必须逐字段显式编码，禁止跨信任边界直接 `memcpy`。

## 5. 清理边界

清理枚举数值明确匹配 V8 的公开 `AbortCleanupKind`：

```text
1 NoMutation
2 ConditionalRestoreTarget
3 CancelSelector
4 CancelOuter
5 DoNotRollbackAfterAccept
6 HaltRestart
```

当前只有 `ConditionalRestoreTarget` 存在静态 CAS 候选；它仍未动态证明、未授权。CancelSelector、CancelOuter 与 DoNotRollbackAfterAccept 没有可调用路线；未来 adapter 不能因 V8 给出了 cleanup directive 就假装能够安全回滚。无法完成精确清理时必须升级为 HaltRestart。

## 6. 离线验证

构建入口：

```powershell
.\tools\San9BridgeV10\build.ps1
```

当前结果：

- contract 内部检查：`45/45`；
- 独立 descriptor/route/ABI/金标准 oracle：`202/202`；
- PE 审计：`27/27`；
- DLL SHA-256：`8AECD7C80C6BE0E531F5216DC1883E5B181F2F35AC3F24A10E8562C2D8B9C3EC`；
- SelfTest SHA-256：`81BD8CC9A1D24AC4ED6CEFD59FBC5C614E6099CCC44FB2878A57469B2D112078`。

构建锁定 Zig `0.16.0` 及其二进制 SHA；复用的 V51 SHA-256 实现也按 `.c/.h` 哈希锁定并纳入源能力扫描。PE 门要求 x86/PE32、ASLR、NX、无 W+X、精确 imports/exports，拒绝 ordinal imports、delay imports、unnamed exports 与 forwarded exports。

## 7. GO / NO-GO

| 能力 | 结论 |
|---|---|
| 五命令/七阶段精确版本数据契约 | GO（离线） |
| V8 请求 shape 与跨语言 fingerprint | GO（离线） |
| 进程访问、主线程 dispatcher、业务 IPC | 未实现 |
| V9 transient target CAS | NO-GO |
| V7 nested-modal 分阶段调用 | NO-GO |
| 原生 cleanup | NO-GO |
| 单城单命令 live 执行 | NO-GO |
| 多城市一键内政 | NO-GO |

下一步必须先完成 P0 影子单命令游标和 P1 broker/journal/IPC；随后再按用户对当前复制存档和精确进程代的逐阶段同意，依次验证 ping、只读生命周期、CAS 往返、modal 分阶段和最后一条真实命令。任何一次阶段同意都不自动授权下一阶段。
