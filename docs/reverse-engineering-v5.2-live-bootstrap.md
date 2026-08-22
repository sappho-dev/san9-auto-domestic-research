# V5.2 ping-only live-bootstrap 离线实现说明

## 1. 当前结论

V5.2 已完成的是一个**可离线编译、可合成验证、可对磁盘产物做机器码审计**的 x86 ping-only live-bootstrap 原型。实现分成两个互不混用的 profile：

- 默认 `SAN9_V52_LIVE_ENABLED=0`：controller 只接受 `--self-test`；无 Hook 安装、进程枚举/打开或目标路径代码；导出的 IdleBridge 为 `xor eax,eax; ret 4`，不会跳 `0x00434100`；
- 显式 `SAN9_V52_LIVE_ENABLED=1`：只有另一个高摩擦脚本才编译；本轮从未加载或运行该 EXE/DLL，只对文件做 PE32/import/export/Capstone 审计。

因此本轮边界是：

- 默认离线构建、合成测试、磁盘 PE/机器码审计：**GO**；
- 编译 live-optin 文件但不加载、不运行：**GO**；
- 对运行中游戏使用 `SetWindowsHookEx`、加载 DLL、claim idle slot 或发 ping：**NO-GO，尚未授权且动态安全未证明**；
- 任何内政业务 opcode、城市/武将字段、游戏业务调用：**NO-GO，源码中不存在**。

本轮没有连接、枚举、读取或写入当前游戏进程，没有运行修改器，没有使用 Computer Use，没有修改 `San9PK.exe`，也没有 `version.dll` 或其他代理侧载方案。

## 2. 文件与构建边界

源码和工具：

- `native/San9BridgeV52/`：共享契约、bootstrap HMAC、合成 loader/idle、live gate、DLL、controller、x86 assembly 与 `.def`；
- `tools/San9BridgeV52/build.ps1`：默认离线构建、自测、磁盘审计；
- `tools/San9BridgeV52/build-live-optin.ps1`：必须给出两条精确确认串，只编译 opt-in 产物并做磁盘审计，绝不执行；
- `tools/re/san9_v52_native_audit.py`：只读普通 PE 文件，不加载 DLL、不启动 EXE、不枚举进程。

输出固定写到被 Git 忽略的目录：

```text
tools/artifacts/San9BridgeV52/offline/
tools/artifacts/San9BridgeV52/live-optin/
```

live build 的随机 32-byte root key 只生成在 `live-optin/generated/`，native 源码目录没有缓存、产物或 key。两套脚本只接受 Zig `0.16.0` 且 `zig.exe SHA-256=086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864`；审计只接受 Python `>=3.11.0`、精确 `pefile==2024.8.26`、精确 `capstone==5.0.7`。

命令：

```powershell
# 默认离线：允许执行纯合成 self-test
tools\San9BridgeV52\build.ps1

# opt-in：只编译与读磁盘审计，不运行、不加载产物
tools\San9BridgeV52\build-live-optin.ps1 `
  -ConfirmCompileOnly I_ACCEPT_COMPILE_ONLY_DO_NOT_RUN_LIVE_ARTIFACT `
  -ConfirmDynamicNoGo I_ACCEPT_DYNAMIC_LIVE_REMAINS_NO_GO
```

opt-in controller 自身还要求 8 组无重复精确参数，包括 PID、thread id、HWND、creation FILETIME、目标 SHA，以及三条 `I_ACCEPT_*` 运行确认；但这只是误触保护，不构成本轮运行授权。它只是诊断用 console prototype，不是产品主 UI，不能靠双击使用；最终用户按钮仍属于后续 UI 工作。

## 3. 协议角色与不可互换性

V5.2 bootstrap envelope 固定 `512` bytes，共享块固定 `4096` bytes；共享块内只复用 V5.1 的 `256-byte` ping proof frame/mailbox。bootstrap 绑定并 HMAC 覆盖：严格 sequence、可信单调时间窗、controller/target PID 与创建代、目标主线程/HWND、registered message、atom、随机 tag、目标/上下文/EXE/DLL/mapping digest、session nonce/key、request id 与 challenge。保留区非零、绑定不等、超时、序列不连续或 MAC 错误均 fail-closed。

它不是现有 C# `San9AutoDomestic.Bridge.Protocol` 的 `288-byte` 生产 schema；长度、magic、布局、状态机均不可互换。两者只共享 HMAC、target/context、nonce、request id、challenge、有限 freshness 和严格序列这些安全语义。V5.2 没有 command type、operation code、城市、军团、武将、数值或任意业务 payload。

随机 mapping/message 名、registered message、atom 和 build root key 只按**非敌对同用户**威胁模型减少误碰：同一用户下的恶意进程可以观察对象名，build key 也能从 controller/DLL 中提取。因此它们不是对抗同用户攻击者的安全边界，HMAC 不能被描述成秘密硬件根或生产凭据。`CreateFileMappingW` 的 security attributes 为 `NULL`，使用调用 token 的 default DACL；default DACL 也不能把该模型提升为“抵抗恶意同用户”。

## 4. Hook/bootstrap 生命周期原型

预期的静态状态序列为：

```text
EMPTY -> WRITING -> SEALED -> CLAIMED -> INSTALLING -> COMMITTING -> READY -> STOPPED
                         \          \                \-> REJECTING -> REJECTED
                          \---------- controller CAS ----------------> STOPPED
```

controller 在任何 Hook 前做 fresh target probe；DLL callback 在目标主线程 claim 后重新做完整 probe；controller 看到 READY 后立即 `UnhookWindowsHookEx`，删除 atom，再做 post-ready fresh probe。controller 的初始/pre-hook 清单必须没有 V5.2 DLL；post-ready 只允许**恰好新增一个** sibling `San9BridgeV52Live.dll`，并核对 path、文件 hash、非零 base、与本地产物相同的 `SizeOfImage`，同时要求去掉该 DLL 后的模块摘要与 pre-hook 完全相同。

`SEALED/CLAIMED/INSTALLING` 是可取消区。controller 不再从 `PostThreadMessage` 后另起一个 5 秒窗口，而是每轮直接把同一可信 `GetTickCount64` 与已认证的 `envelope.expires_at_ms` 比较；到期只能逐状态用 CAS 竞争 `→ STOPPED`。只有赢得 STOPPED 后才把 `claim_enabled` 清零，绝不先清 claim 再猜 DLL 是否仍会提交。DLL 在 INSTALLING 内完成所有可逆准备：两次完整 target/module probe、controller creation generation、DLL identity、ping session 和 final code/app/slot anchors。final probe/anchors 之后重新采样 `expected.now_ms=GetTickCount64()`，立即做一次**完整 HMAC/binding/freshness validate**；只有结果仍为 OK 且紧接着赢得 `INSTALLING → COMMITTING` CAS 才可调用 PIN 或 CAS idle slot。最终采样恰好等于 expiry 也会得到 `EXPIRED`，pin/slot 调用数保持零。

controller 一旦观察到 `COMMITTING`（或终态发布中的 `REJECTING`），就不再拥有取消权，只等待 READY/REJECTED。二级 watchdog 到期只返回本地 `INDETERMINATE/RESTART_REQUIRED`，不改 shared state、不清 claim，且明确要求重启目标后才能重试，不能把它报告为 cancelled。pin 失败、slot conflict 等 commit 后失败由 DLL 先 CAS `COMMITTING → REJECTING`，取得终态写权后才写 result/清 claim/发布 REJECTED；其他 rejection 也只能从其精确 owned state 进入 REJECTING。因此 late rejection 不能覆盖 controller 已赢得的 STOPPED，也不能覆盖 READY。

DLL claim 保存注入后完整 baseline：PID、thread、HWND、creation generation、目标路径、EXE/context digest、全模块 count/digest、去 V5.2 后摘要，以及自身 DLL path/base/size。只有 mailbox request 为 READY 时，IdleBridge post helper 才再次 fresh probe；此时以上全部字段必须与 baseline 严格一致。未知新增/消失/换址模块即使不在 denylist 也不能通过。

bootstrap atom 在 target 已取得 mapping 的 terminal 状态后立即删除；只有 `GlobalDeleteAtom` 返回成功才清本地 atom，首次失败保留句柄值、记录 Win32 诊断并在统一 cleanup 有界重试。`UnhookWindowsHookEx` 同样保留失败句柄并有界重试。连续两次 unhook 失败统一分类为 `RESTART_REQUIRED`、exit `3`，禁止同一 target generation 重试；连续两次 atom 删除失败分类为 `CLEANUP_INCOMPLETE`、exit `4`，并明确报告 atom 仍注册。如果两种故障同时存在，restart 分类优先，但仍单独打印 cleanup-incomplete 诊断。controller 的 mapping view/handle、ping session 统一释放；它不调用 `FreeLibrary`。若已经 READY，cleanup 只关闭 claim并 CAS 发布 STOPPED；若观察到 COMMITTING/REJECTING，则不伪造取消。DLL 自己的 mapping/view 和 pin 故意持续到目标进程退出。

controller 看到 mailbox response READY 后不会立即消费。它用 interlocked load 有界等待 `request_state==COMPLETE && ping_count==1`；READY/count=0 只等待，count>1 立即 fail-closed，条件精确成立后才唯一一次调用 `take_response`。READY 后 fresh post-gate 失败、任何 ping 发布/完成/验证失败，以及“PIN 已成功但 slot CAS 失败”都由统一恢复规则分类为 `RESTART_REQUIRED`。

取消、拒绝、deadline、ping 完成判定、清理失败 streak 和 recovery 分类集中在生产源码 `san9_v52_lifecycle.h/.c` 的纯函数中；默认和 live 两个 profile 共用，离线测试直接调用这些函数，不再维护一份会漂移的 synthetic 状态机。合成竞态矩阵覆盖：timeout 分别赢得 SEALED/CLAIMED/INSTALLING、commit 赢得竞争、pin fail、pin 后 slot conflict、STOPPED/READY 后 late reject、双 callback、COMMITTING 卡死/target exit、unhook 连续两次失败、atom 失败/重试和连续两次失败、controller creation lookup failure；另有“初验有效但 final validate 恰到期，pin/slot 均为零”和“response READY/count0 不消费，COMPLETE/count1 只消费一次”的精确回归。creation lookup 失败固定报告 `GENERATION_FAILED`，不会沿用前一个 `OK` result。

`UnhookWindowsHookEx` 与 in-flight callback、COMMITTING 二级 watchdog、目标退出、loader-lock/CRT TLS 等真实竞态仍只能动态验证，不能由这份静态状态机宣称已安全闭合。

## 5. 精确目标与 V0 等价冲突门

每次完整 probe 都 fail-closed 验证：

- 目标路径 `D:\三国志9\10101749\San9PK.exe`、大小 `2,636,800`、x86/PE32、ImageBase `0x00400000`、SizeOfImage `0x01759000`、SHA-256 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`；
- 目标文件在一个 `FILE_SHARE_READ` 句柄上完成 size、PE、hash，并比较 hash 前后 volume/file index/size/last-write identity；不共享 WRITE/DELETE；
- 全局唯一窗口类必须精确为 `KOEI_SAN9WINDOW`，且唯一目标窗口的 PID/thread/HWND 与显式参数一致；
- Toolhelp 全进程枚举必须成功，并拒绝 `San9PKEasy.exe`、`San9PKHard.exe`、`SanIXPKCheat.exe`；
- 目标 32/64 模块枚举必须成功，主模块恰好一个且 path/base/size 精确；拒绝 `Easy.dll`、`SanIXSpy.dll`、`San9Common.dll`；
- 游戏目录和已加载模块都拒绝 V0 frozen 五项代理：`version.dll`、`dinput.dll`、`dinput8.dll`、`winmm.dll`、`dsound.dll`；
- 模块条目有界为 256，排序后做 A/B count/digest；枚举失败、过量、空字段或 A/B 变化全部拒绝；
- pre-hook、DLL claim、post-ready、每个 READY ping 都重新取得 generation/target probe，不复用旧 probe 冒充 fresh gate。

名单统一来自 `san9_v52_safety_contract.h`，合成测试同时锁定 3/3/5 精确表，避免 V0 与 native gate 漂移。路径 helper 在读取 `path[directory_length]` 前先验证总长度，并只接受游戏目录的直接子项。

## 6. IdleBridge 机器码契约

live-optin 的 `San9BridgeV52_IdleBridge` 是单独的 x86 assembly：

1. 保存 EBP/EBX/ESI/EDI、app 与参数，清 DF；
2. 调用 enter-depth helper；helper 返回任何 token 后都没有条件跳转；
3. 无条件且恰好一次执行 `mov eax,0x00434100; call eax`；
4. 保存原 EAX 与 original 返回后的 DF；
5. 调用 post helper；恢复 DF、原 EAX、非易失寄存器；`ret 4`。

因此 enter helper 返回失败、nested token、post helper 返回失败或任一业务 gate 不通过时，正常控制流仍先调用 original 一次并保持返回值。合成 fault injection 覆盖这些返回型故障；**不声称捕获硬件异常**。Capstone 审计锁定：original call raw motif 全 DLL 恰好一次、其前无 branch/ret、EAX save/restore、post call、非易失寄存器、DF 和唯一 `ret 4`。

post helper 只有 TLS outer depth 1 且 caller `0x005C5D11`、app `0x01228340`、arg0、主线程、app vptr `0x00604DD0`、slot `0x00604DF4 -> IdleBridge`、目标/上下文、generation、完整模块 baseline 全通过时，才处理至多一个 authenticated ping。READY 分支还重新验证：

- original `0x00434100` 前缀；
- `0x005C5D05` 主 callsite；
- `0x004345C0` scene tick；
- app vptr 与当前 slot。

这能拒绝 bootstrap 后、模块清单不变但关键代码/slot 已改变的情形。它仍不能仅靠静态审计证明真实调用约定或所有异步异常路径。

## 7. 离线结果

2026-08-07 当前冻结候选本地结果（仍待独立复审，不代表 live GO）：

- 默认 controller：`97/97`，其中 inherited V5.1=`102/102`、bootstrap=`12/12`、loader=`47/47`、race=`22/22`、idle=`15/15`；
- 默认 DLL：`98/98`；synthetic original calls=`5`，authenticated pings=`1`，`live_code_executed=0`；
- offline PE 审计：DLL `18/18`，controller `14/14`；
- live-optin 只读磁盘审计：DLL `27/27`，controller `19/19`；两者均为 x86 PE32，六个 DLL export 全部具名、无 forward/delay/ordinal import；
- live IdleBridge 的 original motif、CFG 前缀、EAX/非易失寄存器/DF/`ret 4` 契约全通过；
- 四个产物均观测到两个 MinGW CRT TLS callbacks。项目自己的 `DllMain` 只保存 HMODULE，但不能据此外推 loader-lock 下没有 CRT/TLS 工作。

审计输出明确固定：`artifact_executed=false`、`dll_loaded=false`、`process_accessed=false`、`hook_installed=false`、`business_execution_authorized=false`、`live_dynamic_safety_proven=false`、`live_loader_lifecycle_proven=false`。

## 8. 动态 Go / No-Go

**当前 live 结论是 NO-GO。** 仍需用户另行明确授权，并在独立审计后只对一次性存档/干净原版环境做最窄 ping-only 动态验证，至少覆盖：

- MinGW CRT/TLS 与 `WH_GETMESSAGE` 注入在目标 loader-lock 下的真实生命周期；
- callback 线程身份、消息投递、READY 后立即 unhook 以及 in-flight callback；
- `__fastcall`/`ret 4`/DF/EAX/非易失寄存器在实际主循环的 ABI；
- slot 页面属性、CAS 唯一替换、pin 至进程退出和无 slot restore 的可接受性；
- 目标退出、controller 超时、INSTALLING/COMMITTING/READY 边界、mapping/atom 清理；
- fresh 模块/anchor probe 的耗时与主线程影响。

即使未来 ping-only 动态验证通过，也不会自动授权内政业务。商业、开垦、巡查、训练、修筑、选人、提交、资金/城市/人物字段都需要后续独立 PRD、协议与安全证明，不能通过扩 opcode 或放宽本 bridge gate 偷渡。
