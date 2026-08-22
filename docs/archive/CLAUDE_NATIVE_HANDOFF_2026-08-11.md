# San9AutoDomestic Native：完整开发交接、真实盘面与 300 万词元执行预算

> 快照日期：2026-08-11（Asia/Shanghai）  
> 仓库：`C:\codex files\San9AutoDomestic`  
> 当前分支：`main`  
> 当前 HEAD：`a2b2a93 Add Claude development handoff`  
> 本文用途：交给 Claude 做独立技术判断并继续开发  
> 最终目标：在精确 `San9PK 1.0.1.0 + San9PKEasy 1.1.0.5` 环境中，完成真正可用的进程内原生自动内政外挂  
> 当前总预算语义：**300 万词元是从现在到最终产品开发成功的总上限，不是撰写本文的预算**

## 0. Claude 必须先读的结论

这个项目的最终路线已经确定：**不使用鼠标、键盘、坐标、截图识别或 Computer Use 执行游戏操作；采用外置控制器 + 游戏进程内桥 DLL + 游戏主线程原生调用。**

旧的可见输入路线已经彻底退役，不需要再评审，也不得作为失败回退。本文只讨论进程内原生外挂路线。

截至本文冻结时，最准确的状态是：

1. 精确游戏版本、Easy 加载器、Easy DLL、Easy 的 32 个代码重定向点和 6 个辅助写点已经建立了严格兼容档案。
2. 只读 Adapter 能识别精确 Easy 运行状态，但生产执行授权仍固定为 `false`。
3. 512 字节认证 ping 协议、Easy 离线门、共享邮箱事务状态机都已分别完成大量离线测试。
4. 最新 M2b 已经能编译出真实 x86 `controller.exe + bridge.dll`，并通过自身的离线 PE 审计。
5. **M2b 仍没有真正运行过。它的 `controller.exe` 主入口故意只输出 compile-only 文案并返回 78；没有安全的实机启动器为 bootstrap 函数生成绑定配置。**
6. 因而到目前为止：没有加载 M2b live DLL，没有安装 idle slot，没有完成一次 live ping，更没有自动完成商业或其他内政命令。
7. 商业命令的静态主链和一个较合理的 shadow-vtable 候选已经找到，但尚未得到复制存档上的动态时序证明，也没有进入可执行产品路径。

Claude 接手后的优先级不能再变：

1. 先做出**可运行且受控的 ping-only 启动器**。
2. 取得用户单独授权后，实际跑通一次 100-ping 主线程桥。
3. 再用当前直属城市、复制存档，只跑通一次原生“商业”。
4. 只有上述两件事真实成功，才能扩展其余命令、全部直属城市和 UI。

任何“离线测试很多”“地址已经找到”“DLL 已经编译”都不等于跑通。

---

## 1. 最终产品到底是什么

最终交付是一个用户可见的 `San9AutoDomestic.exe`。用户照常先启动自己的 `San9PKEasy.exe`，进入游戏，然后打开助手。助手提供两个默认方案：

- 基础内政：`商业 -> 开垦`
- 有钱内政：`巡察 -> 商业 -> 开垦 -> 训练 -> 修筑`

用户点击助手按钮后，程序在后台处理全部直属城市。正式产品必须满足：

- 不移动鼠标，不点击鼠标，不发送键盘输入。
- 不抢前台焦点，不要求游戏窗口位于前台。
- 不依赖城市菜单、设施列表、选人框或确认框。
- 不直接裸写商业、开垦等数值来伪装执行成功。
- 调用游戏自身原生候选过滤、排序、命令构造、校验、扣费、人员占用和 apply 生命周期。
- 每条命令恰好使用原生最优 5 人；不足 5 人时只跳过该条。
- 每条命令完成后重新读取状态，后续命令不得复用旧候选或已行动武将。
- 灰项、已执行、上限、资金不足、委任或失控只按契约跳过；身份或时序不确定则整批停止。
- 重复点击、超时、迟到回执和双实例不能造成第二次 apply。
- 进入不可逆原生副作用后结果不确定，必须 `AbortUncertain/RestartRequired`，不能自动重试。

最终用户不应该知道 bridge、P1Wire、M1、M2a、M2b 等研发代号，也不需要单独启动控制台程序。

---

## 2. 唯一支持环境

首版只支持以下精确三件套：

| 文件 | 版本/结构 | 字节数 | SHA-256 |
|---|---|---:|---|
| `D:\三国志9\10101749\San9PK.exe` | x86；`1.0.1.0` | 2,636,800 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| `D:\三国志9\10101749\San9PKEasy.exe` | x86；`1.1.0.5` | 24,576 | `CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07` |
| `D:\三国志9\10101749\Easy.dll` | x86；无导出 | 32,768 | `E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0` |

其他固定条件：

- 游戏窗口类：`KOEI_SAN9WINDOW`。
- 游戏 PE ImageBase：`0x00400000`。
- 游戏 SizeOfImage：`0x01759000`。
- 一次运行必须绑定唯一游戏 PID、游戏创建时间、主 HWND、主线程 ID、游戏模块身份。
- 还必须绑定唯一 `San9PKEasy.exe` loader PID/创建时间、唯一加载的 Easy 模块路径/大小/哈希/实际基址，以及 Easy hook epoch。
- Easy 实际加载基址不能假设恒为 `0x10000000`；所有 `rel32` 校验都按实际基址计算。
- 只允许启动顺序：游戏 -> Easy 完成注入 -> 助手验证 -> 助手安装自己的桥。
- 同名错哈希、重复游戏、重复 Easy、部分 Hook、未知修改器或任一锚点被第三方覆盖，必须在任何写入前拒绝。

生产程序不修改或分发上述三个文件，不调用 Easy 内部 trampoline 当稳定 API，也不得主动改变 Easy.dll 的引用计数。

---

## 3. 为什么选择进程内原生路线

### 3.1 San9PKEasy 本身已经证明路线可行

对用户精确 `San9PKEasy.exe` 和 `Easy.dll` 的静态逆向确认：

- `San9PKEasy.exe` 是加载器，而不是功能本体。
- 它使用远程加载和代码重定向，让 `Easy.dll` 在游戏进程内工作。
- `Easy.dll` 无导出，内部入口依赖特定寄存器、栈和游戏续点，因此不能从外部直接 `GetProcAddress` 调用。
- Easy 的“多人探索”“军师推荐”等功能证明：在游戏主线程中挂接既有生命周期、无鼠标完成批量行为是现实可行的。
- Easy 没有提供商业、开垦、巡察、训练、修筑的公开 API，因此只能借鉴其加载、主线程和共存范式，不能直接复用其内部函数。

这也是最终架构坚持“独立桥 DLL + 游戏主线程 safe point + 原生命令生命周期”的主要依据。

### 3.2 三个参考项目的真实价值

#### `bbfox0703/Mydev-Cheat-Engine-Tables` / San9WPK

仓库：<https://github.com/bbfox0703/Mydev-Cheat-Engine-Tables>

价值：高，主要用于结构和 AOB 假设。其 Cheat Engine 表包含城市、军团等字段和若干 Hook 方式。对用户精确 `San9PK.exe` 的离线扫描中，部分签名可唯一命中，证明结构高度同源。

局限：没有完整的内政 dispatcher、五人原生选择、资金与人员事务、命令队列/任务树提交。直接写城市商业值只是作弊改值，不是本项目要求的原生事务。表为 GPL-3.0，不能不加区分地复制进发布代码。

#### `tzengyuxio/kaodata`

仓库：<https://github.com/tzengyuxio/kaodata>

实际只对 San9 头像 `.s9` 资源、色盘和图像尺寸有实现；`san9_person()` 仍为空。它不是城市/武将运行时结构或命令生命周期底座，对本项目执行层价值较低。

#### `xs1l3n7x/pcsx2_cheats_collection`

仓库：<https://github.com/xs1l3n7x/pcsx2_cheats_collection>

PS2 `.pnach` 可作为城市字段概念布局的旁证，部分相对 offset 与 PC 表具有一致性。但 PS2 地址、MIPS 指令和 ABI 绝不能用于 PC `San9PK.exe`。该仓库还缺少明确可复用许可证，不能复制分发。

### 3.3 参考项目带来的最终判断

参考项目能够帮助定位“数据在哪里”，但没有一个项目提供现成的“原生内政事务 API”。真正需要完成的是：

1. 在精确游戏版本中找到并验证游戏自身的 handler、候选、command、validator、task tick 和 apply。
2. 让这些函数只在认证的游戏主线程 safe point 运行。
3. 与用户已经安装的 Easy 精确共存。
4. 用有界、可防重放的外部协议驱动，而不是传任意函数地址或任意内存写指令。

---

## 4. 当前生产架构

预期的数据流如下：

```text
San9AutoDomestic.exe
  -> native controller / runnable ping launcher
  -> 当前用户 SID 专属共享映射
  -> 512-byte P1Wire authenticated request
  -> WH_GETMESSAGE 仅用于一次性 bootstrap
  -> bridge.dll 固定驻留游戏进程
  -> app idle slot 由 original 指针 CAS 为 wrapper
  -> wrapper 先恰好调用一次原 idle
  -> 同一游戏主线程、TLS depth=1 时推进一个有界 primitive
  -> M2a mailbox / exactly-once lifecycle
  -> ping 或后续 Commerce shadow-vtable primitive
  -> authenticated response / durable result
```

关键约束：

- `DllMain` 不执行游戏业务。
- `WH_GETMESSAGE` HookProc 只完成 bootstrap，不执行内政。
- 外部线程、IPC 线程、加载线程不能调用游戏业务函数。
- 每次 idle 回调最多推进一个有界 primitive，不能在游戏线程等待 IPC 或跑完整多城循环。
- bridge 目前没有被证明可热卸载；安装后必须 pin 到游戏退出，测试恢复边界是重启游戏。
- 业务协议只允许固定 schema，不允许传函数指针、任意地址或任意写入载荷。
- stop 只在 primitive 安全边界生效，不能杀游戏线程。

### 4.1 已冻结的 idle 候选

当前精确候选为：

| 项目 | 地址/值 |
|---|---|
| app object | `0x01228340` |
| app vtable | `0x00604DD0` |
| idle slot | `0x00604DF4` |
| original idle | `0x00434100` |
| expected caller return | `0x005C5D11` |

wrapper 的约束是：

- 原 idle 必须先被调用且每次恰好一次。
- 保存并恢复 EBX/ESI/EDI/EBP/ESP、原 EAX、DF、x87 control 和 MXCSR。
- TLS 深度只有 outer depth=1 可处理请求；nested depth 只调用 original。
- 任一 app、vtable、caller、slot、Easy epoch 或线程身份不符，不能取请求。

这些约束已经进入 M2b 源码和静态审计，但尚未由一次 live 调用证明。

---

## 5. 已完成的技术层

### 5.1 正式 UI 与只读 Adapter

正式 UI 当前是只读预览件，两个执行按钮固定禁用。旧输入组件已从 UI 引用、根构建和正式发布物中移除。

当前正式发布基线：

| 项目 | 值 |
|---|---|
| BuildId | `7638b922c17344a187d9ff724eb0471c` |
| UI SHA-256 | `B17CA67E79F065557F35CBEB3D07D08EEEC74AE1409D4CC0804E531EB53ABD7C` |
| Adapter SHA-256 | `A3DA5D90F3C224B41D03FCB1C4DF60F2DE80B3467E0AF9701559B0337A7EE111` |

正式 `bin` 中不存在 Input 或 SingleCommandRunner 产物。最近账本记录的根构建测试为：

- manifest `277/277`
- Core `44/44`
- UI `22/22`
- V0 `7/7`
- V1 `9/9`
- V2 `33/33`
- V4 `14/14`
- Bridge `25/25`

这些结果只证明只读程序和离线合同，不证明 native 执行。

### 5.2 P0：Easy ownership manifest

机器真源：`docs/easy-compatibility-manifest.json`。

已闭合：

- 32 个代码重定向：`1 JMP + 29 CALL + 2 CALL+NOP`。
- 6 个辅助物理写点。
- 76 个静态 `WriteProcessMemory` callsite。
- 5 个 WPM IAT 引用。
- 2 个 `VirtualProtectEx` 引用。
- Easy 实际基址下的 x86 模 2^32 `rel32`。
- 小兵培养成对状态、Easy HWND 槽、页面保护、idle 三锚。

离线变异测试：`277/277`。

manifest SHA-256：

`72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE`

Adapter runtime gate 对同一游戏代做稳定 A/B 观察。passive Diagnose 不会 arm epoch；未来桥显式安装后，Easy 缺失、卸钩、重钩、失联或恢复均应永久 `RestartRequired`。

### 5.3 P1Wire：唯一 512 字节 ping 协议

目录：

- `prototypes/San9AutoDomestic.P1Wire`
- `native/San9BridgeP1Wire`

合同：

- 固定 512 字节、小端、保留区必须为零。
- 只允许 `PingRequest/Pending` 和 `PingResponse/Completed|Rejected`。
- 绑定游戏/helper/Easy-loader PID 和创建代、main TID、HWND、session/request/Easy epoch nonce、10 个 digest、期限和 sequence。
- 使用 HMAC-SHA256 和 CRC32。
- 100 ping 上限，拒绝重放、跳号、重复 request ID、过期、未来时间和时钟回拨。
- 不含城市、命令、人员、原生指针或任意业务载荷。

冻结结果：

- Python 独立 oracle：`1030/1030`
- C#：`2091/2091`
- C：`1765/1765`
- request SHA-256：`ACADF1F1C5CF70672484632E32865871058E3193B84FFC5C233990985FBE80D5`
- response SHA-256：`2DCEF30E7C5B5219F255D97C7A260019748551D2279C868E62539CE204A0E87E`

它仍固定 `LiveAuthorization=false`。

### 5.4 M1：native Easy 离线门

目录：`native/San9BridgeP1EasyPing`。

M1 把 manifest 生成成私有 C header，编译纯离线 native verifier，并要求：

- x86 C11、warnings-as-errors。
- 双根确定性构建。
- 运行前 PE 审计，运行后哈希不变并复审。
- 无进程访问、无目标写入、无 IPC、无 live 代码。
- original 和 installed 整态严格区分。
- partial、mixed、unknown、unstable、溢出和 occupied anchor 全部拒绝。

最近冻结记录为 native self-test `727/727`；该数字来自本轮审计消息，本文没有重新运行 M1。

### 5.5 M2a：离线共享邮箱事务模型

目录：`native/San9BridgeP1EasyPingM2`。

M2a 是纯离线 4096-byte mailbox 模型：

- 不含 Windows header、进程、Hook、mapping、ACL 或 live 授权。
- 直接链接冻结 P1Wire 和 M1 verifier，不复制 codec。
- 生命周期只通过 C11 atomic CAS 前进。
- `COMMITTING` 之后失败单调进入 `POISONED_RESTART`。
- processing-owner 串行化 publish、target、consume、discard 和 poison。
- 单槽、严格 ack、防止旧 token 修改下一轮。

最近冻结结果：self-test `366/366`；双根 EXE SHA-256：

`398DDAC22B75E4088548BF2954A902390F6DD45AB86B7DC5DD178947D31EA46E`

M2a 仍没有任何 live 能力。

### 5.6 M2b：真实 Windows bridge 的 compile-only 产物

目录：`native/San9BridgeP1EasyPingM2b`。

已经落盘的内容：

- 当前用户 SID 专属 8192-byte 共享映射。
- `WH_GETMESSAGE` 一次性 bootstrap。
- 真实 x86 bridge DLL 和 controller。
- Easy 32+6/page/HWND/idle pre-arm 与 post-arm 验证。
- idle slot `original -> wrapper` CAS。
- wrapper 保存寄存器、DF 和 FPU/SIMD 上下文。
- 外层 ingress/egress 与目标进程内部 M2a runtime 分离。
- 100-ping 控制循环。
- Commerce ABI 常量和不可达 shadow-vtable 骨架。

2026-08-10/11 实际运行的离线构建结果：

```text
P1_M2B_PE_AUDIT PASS
P1_M2B_OFFLINE_SELFTEST passed=14 failed=0 live_runs=0
P1_M2B_BUILD PASS live_loaded=0
```

产物：

| 产物 | SHA-256 |
|---|---|
| `offline.exe` | `29E1102DEB7C1A09FEDE8CD1B92C0604BE5EE534013CA069CA3E82BB6C16E5B6` |
| `controller.exe` | `8B29917ADF7A13F88DD9BB5B418303AC29154D1902C9575993E7BE6B31DC36B7` |
| `bridge.dll` | `DF830583C8D787DC5E7A6F18BFFE92BCBC42FC9542090FF8B8BED0E5A60834B4` |

重要说明：构建脚本最初有一处 PowerShell 换行语法错误，已最小修正并重新完整跑绿。当前 `build.ps1` SHA-256 为：

`D6F3D60B6C07BF713A91258A58D0FD199CBD5A607A0DDFAD8E21B081788D1D59`

M2b 的真实状态仍是：

- `LIVE_EXECUTION_DEFAULT=0`
- `UI_CONNECTED=0`
- `COMMERCE_LIVE_AUTHORIZATION=0`
- live DLL 未加载
- live controller 未执行
- live ping 未发生

最直接的代码阻断位于 `src/controller.c`：内部函数 `san9_p1_m2b_controller_bootstrap_compile_only(...)` 已经存在，但 `main(void)` 只保留函数地址、打印 compile-only 文案并返回 78。也就是说产物能编译，却没有受控方式发现并认证实机、生成 bootstrap config、调用该函数。

### 5.7 其他离线合同的地位

仓库还包含 V8Transaction、V10 native contract、CommandBroker、SingleCommandSlice 等大量离线合同。它们证明了若干重要不变量：

- 单命令全局 single-flight。
- action/cleanup side-effect entry gate。
- 认证 post snapshot A/B。
- 防重放 journal 和 RestartRequired。
- 五命令 descriptor、费用和 ABI 字段。

但这些目录目前没有接入 M2b、正式 UI 或游戏。Claude 可以复用其中的不变量和测试思路，不应再复制一套平行状态机，也不得把离线 GO 解释成 live GO。

---

## 6. 商业命令当前逆向结论

### 6.1 已知静态地址

| 语义 | 地址/值 |
|---|---|
| command id | `1` |
| root event | `0x2711` |
| root event dispatcher | `0x5179B0` |
| factory | `0x5125A0` / case `0x512900` |
| Commerce handler ctor | `0x4C61F0` |
| handler vptr | `0x609F38` |
| CanExecute | `0x4C6400` |
| ExecuteUI | `0x4C6310` |
| command allocator | `0x5DEC20`，size `0x40` |
| command ctor | `0x48B340` |
| command vptr | `0x607D98` |
| validator | `0x48B4B0`，经 `0x47E510` |
| attach | `0x47E6F0` |
| state/apply driver | `0x50EAB0` |
| native apply | `0x48B5D0` |
| handler candidate list | handler `+0x40` |
| candidate count | handler `+0x4C` |

### 6.2 当前最窄候选：shadow vtable

当前静态上最合理的方案不是从外部手工重排整个 handler 生命周期，而是：

1. 在游戏主线程 safe point 让 root event `0x2711` 正常创建并 attach Commerce handler。
2. 读取 `root+0x10`，要求 handler 的精确 vptr 为 `0x609F38`。
3. 对该单个 handler 实例做 vptr CAS，从原表切到 bridge DLL 内的 shadow 表。
4. 原表精确为 12 槽、`0x30` 字节，只复制 `0x609F38..0x609F67`；绝不能复制 `vptr[-1]`。
5. shadow 只替换 `+0x28` Execute 槽，其他函数仍走原游戏实现。
6. scheduler 先调用原 CanExecute，使游戏生成并排序候选；随后虚调用 shadow Execute。
7. shadow Execute 根据原生候选前五名构造 command。
8. 在返回 command 或 0 之前，CAS 把 handler vptr 恢复为原表。
9. 原生 `0x50EAB0` 继续负责 validator、attach、状态字段、tick、apply 和析构。

这比手工复制 scheduler/handler 状态更接近游戏正常生命周期。

### 6.3 PersonList 静态候选

进一步静态分析得到：

- list ctor：`0x470DF0`
- list dtor：`0x470E10`
- copy-first-N：`0x46EF80`
- `ECX=dest`，参数为 source list 和 N，`ret 8`
- list vptr：`0x606C8C`
- handler 的 list 嵌入范围推定为 `+0x40..+0x5F`，下一个字段从 `+0x60` 开始；base ctor 会写到 `this+0x1C`，因此临时存储候选为 `0x20` 字节

但是生产头文件仍把 `COMMERCE_COMMAND_CONSTRUCTION_READY` 固定为 0，M2b README 也仍写“typed-list object size/ownership 未认证”。因此 `0x20` 只能作为下一轮要冻结和验证的候选，不能直接视为已发布 ABI。

### 6.4 商业尚缺的动态证据

至少还需要一次复制存档上的时序证明：

- event `2711` 返回并 attach handler 后，vptr CAS 必须发生在下一次 scheduler 激活前。
- shadow `+0x28` 只命中一次。
- 原 vptr 恢复 CAS 成功。
- 原生 command 只 apply 一次。
- 五人、资金、商业值、本旬 order bit 和人员行动位同时符合原生后置。
- 保存/重载后结果一致。
- 任一不确定结果锁止，不自动重试。

在这些证据前，Commerce compile-only 候选可以继续开发，但 live 业务仍为 `NO-GO`。

---

## 7. 当前真实阻断清单

### P0：没有可运行的 ping launcher

这是眼下第一阻断。当前 controller 不是用户可运行的 live 工具。下一步必须实现一个受控入口，自动完成：

- 唯一游戏和唯一 Easy loader 发现。
- 文件路径、版本、大小和 SHA 校验。
- 游戏 PID/创建代/HWND/main TID 绑定。
- Easy 实际模块基址、大小、路径和 runtime 32+6 快照绑定。
- 随机 HMAC key、session/request/Easy epoch nonce 和 digest 生成。
- canonical P1Wire binding frame 编码。
- 固定 bridge DLL 路径和整镜像哈希验证。
- 只有显式确认词存在时调用 bootstrap；默认运行仍只检查并退出。

不能让用户从命令行传任意 PID、任意 DLL 或任意地址来绕过认证。

### P1：M2b 尚无独立冻结复审

M2b 自身 PE gate 和离线自测通过，但最后一次独立审计在预算/代理中断前没有形成冻结结论。下一轮只需做一次聚焦复审，不应再衍生新协议或新原型。

复审范围应限制为：

- ingress/egress 与 M2a 私有 token 的进程边界。
- pre-arm original slot 与 post-arm exact wrapper slot。
- 每 ping 新鲜 Easy 32+6/page/HWND 复核。
- wrapper 的 original-once、DF、TLS token、FXSAVE/FXRSTOR。
- mapping ACL、Hook 生命周期和失败后的 RestartRequired。
- controller 的 runnable entry 不扩大成任意注入器。

### P2：第一次 live safe-point 尚未证明

即使 launcher 完成，也必须先只跑 ping，不能直接跑商业。需要证明：

- 100 次回调都在同一认证游戏主线程。
- caller、app、vtable、slot 和 Easy epoch 每次正确。
- nested/modal 情况不会取业务请求。
- original idle 每次恰好一次。
- 游戏业务快照、鼠标位置和前台窗口均不因 ping 改变。
- bridge 固定驻留到游戏退出，测试结束后重启游戏恢复。

### P3：Commerce 动态时序和 command 构造仍未闭合

shadow-vtable 是候选，不是已经验证的实现。必须在 ping 成功后单独授权业务测试。

### P4：其余四命令不能由 Commerce 自动推广

巡察、开垦、修筑、训练有不同 handler/factory/validator/apply。每类都需要自己的 descriptor 和至少一次独立动态闭环。训练费用为 0，也不能复制收费命令假设。

### P5：全部直属城市绑定仍是后续难点

当前 V9 没有找到 `cityId/CCityData* -> domestic controller target` 的已验证通用 setter。单城当前城市可以绕开这个问题；全部直属城市不能靠旧设施列表点击回退。若原生城市绑定始终无法闭合，P5 必须明确 `NO-GO`，不能破坏无感目标。

### P6：产品 UI 尚未连接 native 执行

正式 UI 按钮固定禁用。只有 ping、单城商业、五命令和全城安全链逐级通过后才接 UI，避免按钮先开放、执行层后补。

---

## 8. 预算历史与失败复盘

### 8.1 不能精确重建的早期预算

早期对话中用户曾口头给出较大的试验预算，但当时没有每轮都用 Goal 工具记录，且其中包含已经退役的方向。本文不伪造精确消耗，只保留结论：早期开发产生了过多平行原型、审计轮次和设计文档，实际可运行结果明显落后于词元消耗。

### 8.2 有系统记录的 20 万 Goal

目标：在精确游戏 + Easy 环境跑通一次当前城市商业。

- 预算：200,000
- 系统最终记录：203,438
- 结果：`budget_limited`
- 产出：P1/M1/M2a 证据进一步收口，M2b 开始形成真实 Windows bridge 骨架，Commerce 静态候选得到推进。
- 未完成：没有最终 build freeze，没有 live ping，没有商业。

问题：任务范围虽然说“跑通一个”，实际又同时扩展 wrapper、协议、Easy gate、Commerce ABI 和多轮审计；可执行入口没有被设为最早的强制里程碑。

### 8.3 有系统记录的 5 万 Goal

目标：收口 M2b 编译审计，并在另行授权后跑通 ping-only。

- 预算：50,000
- 系统最终记录：51,771
- 结果：`budget_limited`
- 完成：M2b 双根确定性构建、PE pre/post audit、offline self-test 14/14。
- 未完成：controller 的 main 仍是 compile-only，无法构造实机 config；因此没有到达请求用户 live 授权的条件，更没有 ping。

这个结果最重要的教训是：**“内部 bootstrap 函数存在”不等于“产物可运行”。** 后续里程碑必须以用户可调用的可执行入口为准。

### 8.4 新 300 万预算的准确语义

用户在 2026-08-11 明确：

- 300 万是从现在到最终开发成功的**全部后续开发总预算**。
- 不是给本文写作的预算。
- Claude 的核心任务是：第一，实际跑通；第二，在总预算内跑通。

本文只是进入下一阶段所需的交接件，完成后必须立即回到 runnable ping launcher，不得围绕本文继续开审计循环。

在用户纠正预算语义后的 Goal 快照中，系统计数为：总预算 `3,000,000`，已使用 `43,849`，剩余 `2,956,151`，状态因对话中断暂为 `paused`。这 43,849 包含最初误开的三路交接汇总和盘面读取；三路代理已立即停止。Claude 应以剩余值为实际起点，并把 H0 的未用额度归还总储备，而不是继续消耗到 100,000。

---

## 9. 300 万总预算执行方案

下面分配是硬上限，不是鼓励用满。任何阶段提前完成，剩余自动进入总储备，不得用来扩写文档。

| 阶段 | 目标 | 最大词元 | 必须交付的可执行证据 |
|---|---|---:|---|
| H0 | 本交接、当前盘面冻结、清理歧义 | 100,000 | 本文 + 一份确切文件/hash/阻断清单；完成后不再扩文档 |
| H1 | 可运行的受控 ping launcher | 250,000 | 默认只读；显式 opt-in 才调用 bootstrap；离线恶意参数矩阵和 PE gate 全绿 |
| H2 | 第一次 live ping-only | 150,000 | 用户授权后 100/100 主线程 ping；零业务变化；结束后按约定重启 |
| H3 | 当前直属城市单次 Commerce | 750,000 | 复制存档真实完成一次原生商业；apply=1；五人/资金/order bit/保存重载一致 |
| H4 | 单城五类命令 | 650,000 | 五类分别完成可执行、灰项、少人、资金和已执行回归 |
| H5 | 全部直属城市 | 550,000 | 原生城市绑定闭合；全城方案一次完整批次，无输入回退 |
| H6 | 正式 UI、Easy 回归、发布包 | 350,000 | 一个 EXE；两方案；Easy 两功能回归；20 轮批次；发布审计 |
| Reserve | 仅用于已证明阻断，不用于新原型 | 200,000 | 用户明确批准后才能转入具体阶段 |
| **合计** |  | **3,000,000** |  |

### 9.1 强制预算规则

1. 每阶段最多一个实现线程和一个独立审计线程；禁止多层代理树。
2. 文档、设计和审计合计不得超过该阶段预算的 20%。
3. 每消耗 25,000 词元必须汇报一次：新增文件、实际运行命令、测试数、是否更接近 live 里程碑。
4. 阶段用到 50% 仍没有可执行产物，必须停止扩设计并把全部资源转到最小 runnable slice。
5. 阶段用到 70% 仍没有达到核心里程碑，立即停止该阶段，交付阻断，不允许靠追加审计消耗到 100%。
6. 同一个问题最多允许“一次实现 + 一次独立复审 + 一次修复复审”。第三轮仍失败则交用户决定，不无限循环。
7. 不再新建与 P1Wire/M1/M2a 等价的平行协议或状态机。
8. 不以测试数增长作为主要进度指标；主要指标必须是：可执行入口、live ping、apply=1、保存重载、全城批次。
9. 每个阶段结束都要给出 Goal 工具的实际消耗和剩余总预算。
10. 未取得用户明确授权时，live 写入预算不能偷偷转化为更多静态研究；应停下来等授权。

### 9.2 成功优先级

300 万不能平均花在每个子系统上。优先级固定为：

```text
runnable launcher
  > live ping
  > one Commerce apply
  > five commands
  > all cities
  > UI polish
```

只要上一层没跑通，下一层一律不扩。

---

## 10. Claude 的下一步最小实施单

### 10.1 第一步：把 M2b controller 变成真正可运行但默认安全的 launcher

不要改 P1Wire schema，不要写新协议，不要接 UI。最小工作面只在 M2b 或其单一后继目录。

建议入口行为：

```text
controller.exe --inspect
    只发现、只校验、只打印，不安装 Hook

controller.exe --ping-only --confirm <精确确认词>
    只有确认词、精确环境和完整门全部通过才 bootstrap
```

必须自动得到而非信任用户传入：

- 游戏 PID/创建时间/HWND/main TID。
- Easy loader PID/创建时间。
- game/Easy 模块路径、大小、哈希、实际基址。
- runtime 32+6/page/HWND/idle 快照。
- helper 自身 PID/创建时间和 controller/DLL 整镜像哈希。
- 随机 HMAC key、owner token、session/request/Easy epoch nonce。
- canonical binding frame。

不允许提供“任意 PID + 任意 DLL + 任意地址”的通用注入参数。

inspect 模式必须可先实际运行，并输出结构化 JSON 或固定字段日志，证明它对用户当前环境的识别与现有 Adapter 一致。inspect 是只读，不等于 live 授权。

### 10.2 第二步：一次 ping-only live 授权

launcher 离线门和 inspect 通过后，向用户请求一条明确授权，文字应至少包含：

> 授权本批次 P1 ping-only 注入测试；不执行任何内政；接受 bridge 固定驻留至游戏重启，并会在测试结束后重启游戏。

执行前：

- 用户保存当前进度，最好使用复制存档。
- 游戏和精确 Easy 已启动并稳定。
- 没有 Hard/SanIX/未知修改器。
- 记录前台 HWND 和鼠标位置，仅用于证明没有输入副作用。

验收：

- bootstrap 只发生一次。
- slot CAS 只成功一次。
- 100 个 request/response 全部认证并连续。
- main TID、caller、app/vtable、Easy digest 全程一致。
- original idle 每次恰好一次。
- 无业务字段、无城市/人员变化、无输入 API。
- 任何失败输出明确状态并要求重启，不能自动重试。

### 10.3 第三步：单次 Commerce

ping 成功并重启后，才解锁 H3。仍然不要接 UI，只做一个当前直属城市、一次 request、复制存档。

开发顺序：

1. 冻结 shadow vtable 12 槽和完整原函数指针表。
2. 冻结 PersonList `0x20` 候选的 ctor/dtor/copy ABI，增加机器码和合成 ABI 检查。
3. 实现只接受 command=Commerce 的内部 primitive，协议层不能传任意地址。
4. 先做 live no-apply 时序观测，证明 event attach 后能在 scheduler 前 arm shadow。
5. 再单独取得业务写入授权。
6. 只提交一次 Commerce；不做循环、不做第二城、不做其他命令。
7. 后置验证和保存重载通过后才宣告 H3 完成。

---

## 11. Live 授权边界

本文、PRD、编译和离线测试均不构成 live 授权。授权逐级独立：

1. 只读 inspect：零写入，可单独运行。
2. ping-only bootstrap：首次安装 bridge，必须用户明确授权。
3. no-apply Commerce timing probe：如涉及 handler vptr CAS，也必须单独授权。
4. 单次 Commerce apply：必须再次明确授权。
5. 五命令和全城：分别再授权。

上一层授权不能自动沿用到下一层。任何 `AbortUncertain` 后必须重启游戏，不能在同一进程代继续试。

---

## 12. 仓库地图

### 12.1 权威需求与证据

- `docs/PRD.md`：当前无感原生产品 PRD。
- `docs/VERIFICATION.md`：验证账本。
- `docs/easy-compatibility-manifest.json`：Easy 32+6 机器真源。
- `docs/easy-compatibility-p0.md`：Easy ownership 解释与闭包。
- `docs/reverse-engineering-v3-execute.md`：五类命令静态执行链。
- `docs/reverse-engineering-v7-native-ui.md`：原生 UI/生命周期。
- `docs/reverse-engineering-v9-city-binding.md`：城市绑定结论。
- `docs/reverse-engineering-v10-native-adapter-contract.md`：native descriptor/ABI 合同。

### 12.2 当前 native 链

- `native/San9BridgeP1Wire`：C 版 512-byte wire。
- `prototypes/San9AutoDomestic.P1Wire`：C# 和 Python oracle。
- `native/San9BridgeP1EasyPing`：M1 离线 Easy gate。
- `native/San9BridgeP1EasyPingM2`：M2a 离线 mailbox/lifecycle。
- `native/San9BridgeP1EasyPingM2b`：当前真实 Windows compile-only bridge。

### 12.3 正式产品

- `src/San9AutoDomestic.Core`
- `src/San9AutoDomestic.Adapter.San9Pk101`
- `src/San9AutoDomestic.UI`
- `build.ps1`
- `tools/artifacts/bin`

### 12.4 历史文档

- `docs/CLAUDE_HANDOFF_2026-08-09.md` 是旧快照，不能代表当前 Easy 兼容和 native bridge 状态。
- 旧输入项目仍可能作为未跟踪历史源码存在，但不属于生产构建和发布闭包。

---

## 13. 可重复命令

### 13.1 M2b 离线构建

```powershell
cd 'C:\codex files\San9AutoDomestic'
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\San9BridgeP1EasyPingM2b\build.ps1
```

该命令只运行 `offline.exe`；只编译和静态审计 live controller/DLL，不加载它们。

### 13.2 P1Wire

```powershell
.\prototypes\San9AutoDomestic.P1Wire\build-and-test.ps1
.\native\San9BridgeP1Wire\build.ps1
```

### 13.3 M1/M2a

```powershell
.\native\San9BridgeP1EasyPing\build.ps1
.\native\San9BridgeP1EasyPingM2\build.ps1
```

### 13.4 根只读产品

```powershell
.\build.ps1
```

不得为了方便把 live M2b 自动接入根 build 并执行。正式发布接入应等 live ping 和单命令通过。

---

## 14. 当前 Git 与证据可信度

当前工作树不是 clean。大量 P0/P1/M2 文件仍为未跟踪目录，Adapter/UI/build/PRD/VERIFICATION 也有未提交修改。HEAD `a2b2a93` 只提交了旧的 Claude 交接文档，不能代表当前实现。

因此 Claude 接手时必须：

1. 先保存 `git status --short`。
2. 不使用 `git reset --hard` 或 checkout 覆盖现有修改。
3. 以文件哈希、可重复构建和本文列出的产物为证据，不以 HEAD 名称推断功能。
4. 在首次 live 前建立一个明确的新 checkpoint/commit，但只纳入经过核验的范围。

M2b 当前关键源哈希：

| 文件 | SHA-256 |
|---|---|
| `build.ps1` | `D6F3D60B6C07BF713A91258A58D0FD199CBD5A607A0DDFAD8E21B081788D1D59` |
| `audit_pe.py` | `9464B9611EEB33259FB7C73C6EEA743A91F010FDC1C0B92F41A37C251DADEB78` |
| `include/san9_p1_m2b.h` | `F471486FF58AC07DB76D6EEA44040B75E06DB616775B47F61C83000375A99E8F` |
| `src/controller.c` | `0AED427AACFDAE62A82E3BCE56F865586BC47D22FBBA7CA9B88A3819625990C8` |
| `src/bridge_dll.c` | `D8860F3EE1846FA7D9264F27002B785272106B3A9F92448E60DED087115755AC` |
| `src/idle_bridge.S` | `CD5F6A8E8BD487E7B3B55814D1C3AEEA20B6DAF32BA4D0CAEA3DD8805ADC90F2` |
| `src/offline_selftest.c` | `0F5CB0F1F3D40D41D4FAD77BED5C6C172DDAB3E5106FA12E721FC3EF97C2D162` |

---

## 15. Claude 应重点回答的问题

Claude 在开始写代码前应先给出简短结论，但不能再次形成长期设计循环：

1. 当前 M2b 的 `controller_bootstrap_compile_only` 是否能安全作为唯一 live bootstrap 内核？
2. 最小 runnable launcher 应复用哪个现有进程发现/哈希/创建代代码，避免再造一套？
3. 如何让 inspect 和 ping-only 共用同一认证结果，同时确保 inspect 不能隐式 arm epoch？
4. M2b 当前 PE audit 对新增 launcher imports 的 exact allowlist 应如何扩展？
5. 当前 wrapper 的 DF/TLS/FXSAVE 机器码检查是否足以进入第一次 ping？
6. bridge pin 到游戏退出的失败/重启语义是否完整？
7. shadow-vtable Commerce 方案是否确实比手工 handler lifecycle 更接近原生路径？
8. PersonList `0x20` 假设还缺哪一个最小静态或动态证明？
9. 单城 Commerce 成功后，V8/V10/CommandBroker 中哪些合同应复用，哪些不应接入？
10. 按第 9 节预算，Claude 能否承诺先在 H1/H2 内跑通 ping，而不是继续增加平行原型？

Claude 的审查输出最好只有三部分：

1. `GO/NO-GO` 和不超过 5 个真正阻断。
2. H1 的文件级最小修改清单和预算估计。
3. 第一次 ping 的明确验收命令与回滚边界。

---

## 16. 最终完成定义

只有以下全部满足，才叫“开发成功”：

- 一个正式用户入口 `San9AutoDomestic.exe`。
- 精确游戏 + 精确 Easy 日常环境下工作。
- 不使用鼠标、键盘、坐标、截图或焦点操作。
- Basic 与 Wealthy 两个方案均由配置生成。
- 全部直属城市按稳定顺序处理。
- 五类命令都走原生候选、原生五人、原生费用、原生 apply。
- 委任、灰项、人员不足、资金不足和失控按契约处理。
- 重复、超时、停止、断连和迟到回执不产生重复 apply。
- Easy 多人探索和军师推荐在桥安装前、安装后、一次内政后都通过回归。
- 保存/重载、下一旬和 20 轮批次回归通过。
- 正式包不含旧输入执行组件或隐藏回退。
- 用户无需手工启动诊断器、controller 或 DLL。
- 全程实际词元消耗不超过从 2026-08-11 起算的 3,000,000 总预算。

在达到这一完成定义前，任何阶段都只能称为对应范围的 GO，不能称产品已完成。

---

## 17. 一句话交接

**别再证明“理论上可以”：先把 M2b 变成默认只读、显式授权才运行的真实 launcher，跑通 100 次主线程 ping；随后只做当前城市一次 Commerce，成功后再谈五命令、全城和 UI。整个后续开发总预算是 300 万，不是本文预算。**
