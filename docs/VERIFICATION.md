# San9AutoDomestic 验证账本

本文件记录每一轮可重复验证的输入、证据和放行边界。它不是开发进度宣传；只有对应放行条件全部满足，功能才允许进入下一阶段。

## 2026-08-10 P0 Easy 精确兼容门

状态：P0 静态所有权清单和 Adapter 只读运行时兼容门通过独立复审；P1 主线程桥、ping 及任何内政执行仍未开放。

- 兼容范围只包含 PRD 冻结的精确三件套。Easy 所有权清单逐项覆盖 32 个代码重定向点（1 JMP、29 CALL、2 CALL+NOP）和 6 个辅助物理写点；闭合 76 个静态 `WriteProcessMemory` 调用点、5 个 IAT 引用和 2 个 `VirtualProtectEx` 引用。
- 清单校验器与纯离线变异测试为 `277/277`；验证实际模块基址下的 x86 模 2^32 rel32、CALL+NOP 的五字节 next-EIP、两种合法小兵培养状态、原始/安装整态、页状态、HWND 槽、idle 三锚和镜像地址溢出。运行过程报告 `process access=0; writes to targets=0`。
- Adapter 将同一游戏 PID/创建代、唯一 Easy loader PID/创建代、唯一 Easy.dll 路径/大小/SHA/实际基址、32+6 快照、页状态、HWND 和 idle 锚做 A/B 稳定绑定。缺 Easy、重复、错哈希、错误 rel32、第三方覆盖、A/B 漂移或 fresh scan 不完整均关闭门。
- passive Diagnose 不会 arm epoch。只有未来桥明确安装后才可调用内部 arm；arm 后 Easy 缺失、重复、失联、卸钩/重钩、旧票据、无法绑定进程代或恢复为原字节，都永久转 `RestartRequired`。票据明确 `IsSecurityCapability=false`、`ExecutionAuthorized=false`，不得跨进程或跨 P1 wire 充当授权。
- 独立复审结果：P1 `0`、P2 `0`；Release/x86、warnings-as-errors 下 V0 `7/7`、V2 `33/33`。额外独立反例确认 `exact -> unbound failure -> old ticket` 无法 arm，`exact -> arm -> unbound failure -> exact restore` 始终保持 `RestartRequired` 且无 ticket。
- 全仓 `build.ps1 -LocalRuntimeFallback`（未带 `-RunDiagnostics`）BuildId `530ee9a20e5d41629e249220be2527e8` 全绿：manifest `277/277`、Core `44/44`、UI `22/22`、V0 `7/7`、V1 `9/9`、V2 `33/33`、V4 `14/14`、Bridge `25/25`。随后不带 `-RunDiagnostics` 的标准事务构建以相同测试矩阵发布 BuildId `7638b922c17344a187d9ff724eb0471c`；UI SHA-256 `B17CA67E79F065557F35CBEB3D07D08EEEC74AE1409D4CC0804E531EB53ABD7C`，Adapter SHA-256 `A3DA5D90F3C224B41D03FCB1C4DF60F2DE80B3467E0AF9701559B0337A7EE111`，正式 `bin` 27 个 allowlisted 文件且 Input/runner 为 0。
- 当前 P0 结论只是 `EXACT-EASY READ-ONLY COMPATIBILITY GO`。生产 UI 仍固定禁用两个方案按钮；不存在加载 DLL、安装 slot、发送 ping、调用游戏函数或业务写入路径。

## 2026-08-10 P1 单一 wire 离线合同

状态：新的 512-byte C/C# ping-only wire 经独立审计为离线 GO；尚未接入 Adapter、UI、根构建、V5.2 或任何 live bootstrap。

- 唯一 frame 固定为 512 字节、显式小端和三段零保留区；只允许 `PingRequest/Pending` 与 `PingResponse/Completed|Rejected`。帧绑定 game/helper/Easy-loader PID 与创建代、main TID、HWND、session/request/Easy epoch nonce、10 个 32-byte digest、5 秒期限、严格 sequence 和 HMAC-SHA256；没有城市、武将、命令、原生内存地址或任意业务载荷。
- HMAC 的 canonical frame 同时清零 CRC/HMAC；写入 HMAC 后只清零 CRC 计算 CRC32。密钥必须是非零、恰好 32 字节；HMAC 固定长度比较。每 session 只接受 sequence 1 起的连续 100 次请求，拒绝重放、跳号、重复 requestId、未来/过期帧；可信时钟一旦回拨永久 fault，只有新 session gate 才能恢复。
- 冻结离线结果：Python 标准库独立 oracle `1030/1030`；C# net48/x86/C#5/Werror `2091/2091`；C11/Zig 0.16.0/x86/Werror `1765/1765`。三方 request 和 response 均双向解码并逐字节一致：request SHA-256 `ACADF1F1C5CF70672484632E32865871058E3193B84FFC5C233990985FBE80D5`，response SHA-256 `2DCEF30E7C5B5219F255D97C7A260019748551D2279C868E62539CE204A0E87E`。
- 公开 gate/response API 只能从带 HMAC 的 512-byte 字节帧原子执行 `decode -> accept/verify`；C 的 decoded-frame helper 已变为私有 `static`，C# 对应 helper 为 `internal`。编码器显式接收 512-byte 容量，并在任何写入前拒绝 frame/output、完整 key/output 和跨输出末尾的部分 key/output 重叠；独立反例确认拒绝时输入区逐字节不变。
- Python、MSBuild、net48 reference pack、Zig、pefile 和 PE audit 均有身份门。native 产物先 strip，再在两个不同 ArtifactRoot 独立构建为完全相同的 115,712-byte EXE，SHA-256 `354D9AA96D6CF62E3997556FC28C0A2B6D75CA505F2907763E3AC931B5A6530F`；运行顺序固定为 compile → 整镜像哈希 → PE audit → self-test → 哈希不变 → PE 复审。PE 门还要求 entrypoint 位于 RX/non-W、精确 import 集、无重复 descriptor、无 export/delay import/RWX，并冻结 Zig CRT 的两个 TLS callback。
- 最终独立复审结论 P1 `0`、P2 `0`，该离线合同 GO。此结论只允许被 compile-only bridge 链接；它仍为 `LiveAuthorization=false`，且尚未接入 Adapter、UI、根构建、进程或游戏。

## 2026-08-10 可见行输入原型退役与正式产品收口

状态：可见行 Win32 输入路线仅保留为历史/离线研究证据，已从正式 UI、依赖闭包、根构建和发布物中退役；无感原生执行尚未开放。

- 正式 UI 的两个配置方案按钮固定禁用并显示“无感执行开发中・未开放”；只读预览和重新检测继续可用。
- UI 项目及编译后的 UI 程序集均不引用 `San9AutoDomestic.Input.Win32`；生产依赖闭包仅允许经过枚举的只读 P/Invoke。
- 根构建不再编译或运行 Input/SingleCommandRunner，严格 staging allowlist 在测试前和 marker/publish 前各复核一次，拒绝 Input、runner 及任何未列明产物。
- 当前正式事务发布 BuildId `7638b922c17344a187d9ff724eb0471c`，UI SHA-256 `B17CA67E79F065557F35CBEB3D07D08EEEC74AE1409D4CC0804E531EB53ABD7C`；`bin` 共 27 个列明文件（含 marker），Input/runner 产物为 0。
- 正式 `build.ps1` 以 Release/x86、warnings-as-errors 通过：manifest `277/277`、Core `44/44`、UI `22/22`、V0 `7/7`、V1 `9/9`、V2 `33/33`、V4 `14/14`、Bridge `25/25`。Input `92/92` 仅是先前原型离线记录，不再属于正式构建门。

以下是退役原型的历史证据，不构成当前产品放行：

- 原型启动时冻结直属 CITY 集合；其范围仅支持前四条已实测可见 CITY 行，超过四城在任何输入前拒绝，滚动未授权。
- 行号不绑定 cityId。每一行都先打开城市，再从只读 UI 状态取得精确 cityId；必须属于冻结集合且未访问，最终访问集合必须与冻结集合完全一致。
- 整批共享同一 PID/创建代/HWND/路径绑定和全局 lease；每座城、每条命令及最终状态均重新读取。委任/灰项/少于五人/资金不足只跳过，不提交命令。
- 菜单到战略地图采用最多两段 Escape，每段后重新验证；任一点击/按键已尝试后发生异常均为 `AbortUncertain`，不会伪装成“未输入”，也不会自动重试。
- 精确日常 `San9PKEasy.exe`/`Easy.dll` 身份继续由只读兼容 allowlist 识别；身份漂移、Hard/SanIX/未知代理继续阻断。
- 2026-08-10 实机复现并修复设施列表“当前行已选中”分支：行单击后若直接进入精确城市菜单，状态机禁止再点中心坐标，验证城市属于冻结直属集合且未访问后直接进入设施；若仍为战略地图才沿用中心城市点击。错误层、错城、集合外与重复城市继续 `AbortUncertain`，不补点、不重试。

### 实机灰项遍历

- 环境：精确 `San9PK.exe` 1.0.1.0，日常精确 `San9PKEasy.exe`/`Easy.dll` 同时运行；助手发布版 PID 22360，游戏 PID 37692。
- 用户明确授权后点击“基础内政（全部直属城市）”。批次冻结直属集合 `{0 襄平, 16 長安, 42 建寧, 46 烏丸}`，并实际遍历设施列表四条可见行；结束时访问集合与冻结集合完全一致，助手恢复 `ReadOnlyReady`，未触发重启锁。
- 证据目录：`%LOCALAPPDATA%\San9AutoDomestic\execution-evidence\20260809-225337163-basic-52d92eb8f7924daa8184b22563543797`。共 32 张逐动作前后截图，包含四组 `row-escape` 及四组 `row-navigation`；每组导航均记录选行、打开居中城市、打开该城设施菜单。
- 本旬四城的基础项目都不可执行：襄平、長安、建寧没有可用武将；烏丸虽有 9 名 ready 武将，但五项命令的本旬 order bit 均已置位。因此该批次只执行身份可验证的导航，所有内政项目安全跳过，没有点击命令、选人或确认，也没有自动推进旬。
- 批次后独立 V2 只读复核：`dataRead=True`、`blocked=False`、`rawStable=True`、`codeExact=51/51`、`structureRevalidated=True`；直属集合仍为上述四城，军团资金为 `156589`，乌丸五项仍因 `COMMAND_ORDER_BIT_CLEAR` 阻断。

## 永久安全约束

- 生产路线禁止鼠标、键盘、坐标、窗口消息或 Computer Use 执行内政；退役输入原型及其历史实机证据不得作为回退或放行依据。
- 不修改 `San9PK.exe`，不修改存档，不使用 `version.dll` 代理，不占用代码洞。
- V0/V1-read 只允许 `PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`。
- 未确认的字段、对象、调用约定或状态一律是批次故障，不能降级成普通“跳过”。
- 预览后备排序不得进入提交路径；正式提交只接受已验证的原生候选、排序、校验和主线程提交链。
- 每条命令提交后必须重新读取和复核，不能把整份预先计划直接写成命令队列。
- 发现已知修改器进程、目标进程中的修改器模块、目标版本变化、进程换代或批次上下文变化时，执行门禁关闭。

## 精确目标

| 项目 | 值 |
|---|---|
| 路径 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0` |
| 架构 | x86 / PE machine `0x014C` |
| 字节数 | `2,636,800` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 窗口类 | `KOEI_SAN9WINDOW` |

任一字段不匹配都不得继续读取或执行。

## 轮次记录

### R0：规则与配置核心

状态：加强版通过复测；仍不授权任何游戏写入。

已覆盖：

- 基础方案为商业、开垦；有钱方案为巡察、商业、开垦、训练、修筑。
- 城市 ID 升序、仅直属城市、委任/非城市排除。
- 每项恰好 5 人；不足只跳当前项；灰项不消耗人；已分配人员不可复用。
- 同军团资金跨城市共享，前序城市消耗会影响后序城市。
- 配置错误、重复命令、后备排序和并发批次门禁的基础用例。

当前结果：Core `44/44`。已覆盖 Fatal/Aborted、批次上下文绑定、不可伪造 observation capability、单命令 challenge、设施身份变化、预览硬门、全进程唯一且按 generation 所有权释放的 lease、中止后清空先前投影，以及训练零费用命令。

### R1：V0 目标、进程和冲突门禁

状态：通过。

验证点：

- 精确路径、版本、大小、SHA-256 和 x86 均匹配。
- `KOEI_SAN9WINDOW` 与进程映像路径共同得到唯一 PID。
- 公共连接面只有读取；源码/导入面没有 `WriteProcessMemory`、`VirtualAllocEx`、`VirtualProtectEx`、`CreateRemoteThread` 或 VM_WRITE 权限。
- 错误路径、错误身份和非 x86 调用被拒绝。
- 修改器冲突只阻止未来执行，不掩盖只读诊断。

当前默认 V0 self-test 为严格非实机模式 `6/6`，不会打开游戏进程句柄；两项实机只读不变量检查已移到显式 `--live-only`，并只由构建参数 `-RunDiagnostics` 调用。V1 合成结构破坏测试另计 `9/9`。

### R2：V1-read 势力、城市和武将结构

状态：通过当前样本、合成破坏用例和全表交叉复核；能力等级仍明确为 `StructureOnly`。

2026-08-07 当前游戏进程样本：

- PID `22348`；两次相关字段读取在第 1 次尝试一致。
- 玩家主势力 `0`，玩家直控军团 `0`。
- 直属城市：`0 襄平`、`8 北海`。
- 襄平有效在城武将 1 名（卑衍），北海 0 名。
- FORCE 50、CITY 50、PERSON 850；Python 权威探针对 50 城链表复核完整，链表集合与“身份 0..3 + 位置链回城”集合差异为 0。
- 检出外部 `San9PKEasy.exe` 和目标进程内 `Easy.dll`；`executionAllowed=false`，`readSucceeded=true`。

上述城市、人数和 PID 只是某一存档时刻的观察值，绝不能写入测试预期或业务代码。另一次同 PID 快照曾得到玩家势力 1、直属城市 22 廬江，证明读取结果随游戏状态变化而非硬编码。

已补齐并复测的代码护栏：PERSON 记录 ID、位置容器 bit0/owner 回指、城市 `+0xDC` 链表节点/环/尾/数量/对齐/重复/精确集合、固定表原始 A/B 双读、外部图 A/B 双读、进程换代、模块基址/大小与读后身份复核。V1 合成破坏测试 `9/9`，并显式输出 `StructureOnly`，不会伪装成可规划快照。

### R3：V1-availability / 只读最优五人

状态：只读观察层通过；正式提交未放行。

已确认并固化：

- 人物 `+0xE8 bit12` 的本旬占用位，以及城市 `+0xDC` 原生在城链顺序。
- 巡察/商业/开垦/修筑/训练五项 handler、`CanExecute`、城市占用位、数值上限和共同直属军团门。
- 巡察按智力、商业/开垦按政治、修筑按统率、训练按武力稳定降序；同分保留原生在城链顺序。
- 51 个磁盘/运行时代码锚点、原始上下文 A/B/A、V1 前后结构摘要和读后进程身份复核。
- V2 合成与非授权投影测试 `25/25`；覆盖顺序耗人、灰项继续、人数不足、同军团跨城共享资金、任务保留金、通用人数值域、重复项目、结构/Evidence/候选分数矛盾事务回滚、未知命令 descriptor 拒绝、训练零费用，以及 `native_best` 请求被静态属性代理时的显式来源标记。当前实机代码锚点 `51/51`，但因 `Easy.dll` 冲突保持 `ObservationBlocked=true`。

`PlanningReady` 与 `VerifiedNativeCapability` 仍恒为 `false`。UI 已开放明确标注的“预览全部方案（只读）/查看阻断诊断”，但两个执行方案按钮、停止按钮和全部提交能力保持禁用。每次点击预览都会先重新读取；快照完成时间、年龄和 token 会显示，超过 30 秒即拒绝，人员行统一标为“静态拟选·未验证”。专用投影 DTO 在结果、城市、任务、武将、资金和问题各层均固定 `IsActionable=false`、`CommitAuthorized=false`；任何技术矛盾会丢弃全部部分选择。当前费用结论按命令区分：静态链显示训练不扣金，其余四项每人 50；动态对照前只作为预览证据。

### R3-A：常驻只读 UI 生命周期

状态：只读边界与常驻失败恢复通过；执行能力仍未开放。唯一常驻窗口是 `San9AutoDomestic.exe`，所有名称带 V0/V1/V2/V4、Diagnostics、Probe 或 SelfTest 的工程工具均为有界运行后退出的一次性工具。

UI 的连接/只读状态与停止显示状态由显式模型和单点 renderer 生成。无游戏、轻量探测失败、worker/渲染异常都保留窗口和手动重试；结构化 `ExecutionUnavailable` 视为稳定阻断，同一 PID/创建代不自动重扫，只允许手刷或进程换代触发。瞬时故障最多按 2.5、5、10、20 秒自动重试四次，每个授权只消费一次；预算耗尽后保持故障态，不形成无限全量 V2 扫描。2.5 秒 presence tick 只使用窗口、PID、精确路径、创建代和 `QUERY_LIMITED_INFORMATION`，不含 VM_READ。

日志固定保留最新 200,000 字符和 2,000 行。每次配置刷新先清除旧按钮的 ToolTip 注册，再从容器移除并 Dispose；重复刷新按钮数不增长。单实例激活监听在 join 超时时不会释放仍由 listener 使用的 wait handle，listener 为后台线程且收到 stop 后在再次等待 activation handle 前退出。

纯离线 UI state suite 为 `19/19`：覆盖等待/只读/稳定阻断/瞬时指数退避与上限、Ready→ProbeFailed 同代恢复只重试一次、30 秒快照、日志上限、三代配置刷新资源回收、早到激活信号、activation join 超时，以及真实 WinForms `ApplicationContext` 消息泵的 `ThreadException` 路由。测试不显示真实主 UI，不调用默认 presence probe/Adapter，也不读取游戏。

### R4：原生命令生命周期与单城单命令提交

状态：静态生命周期已追到 apply；主消息泵、task tick、场景事件和 WndProc 映射已完成独立审计，但没有找到可从外部无 Hook/无注入复用的主线程 dispatcher。任何动态调用/写入仍未开始，当前自动执行明确为 NO-GO。

静态闭环表明，玩家路径是 `root -> handler -> 原生选人窗口 -> command validator -> task tick -> command apply`。`0x47E6F0` 只是短命父子 task 挂接，不是持久订单队列；最终 apply 直接修改城市值、武将占用、资金和本旬项目位。command validator 不补齐“最多五人、全部同城、属于本次候选集”等 UI 不变量，且选择使用单一全局链。因此地址已经定位并不等于存在安全的外部调用入口。

dispatcher 审计进一步确认：`0x5C5CE0 -> 0x5CADA0` 是窗口消息泵，idle 才经 `0x434100 -> 0x4345C0 -> 0x47E840` 推进 task tree；`WM 0x601` 只转送必须位于目标进程堆上的普通 MSG 节点，WndProc/WM_COMMAND 也没有到 `0x4345B0/0x5179B0` 的映射。完整证据见 [V4 dispatcher 审计](reverse-engineering-v4-dispatcher.md)。

已新增有界的 V4 root 生命周期控制台采样器开发件。每份可能输出的样本前后都重新执行一次 `Diagnose`；两侧必须分别通过精确文件、唯一 PID、只读连接、aggregate blocking issue 为零及完整无冲突进程/模块门禁，临时连接随即释放，并与会话初始 PID、创建代和主模块身份一致。任何一侧变脏都丢弃中间样本并停止，启动 baseline 不再被永久复用。

清洁环境夹层内按 `app -> window -> owner -> scene -> schedulerRoot -> child` 做完整 A/B 双读，比较 window/owner/scene/root 和每层 node 地址/vptr/tick/child/pending；不一致最多重试三次，仍变化则 `ROOT_TRACE_CHANGED_DURING_SAMPLE`。同时继续对 32 位地址、空指针、环、最大深度、精确读取、schedulerRoot `vptr=0x607560`、后代 vptr 及其 `vtable+0x0C` 函数指针的主模块范围做故障即停。`+0x30/+0x34/+0x38` 只从活动链中唯一的 `vptr=0x610BC8` 内政 controller 读取；普通后代不会暴露这些字段，出现多个 controller 会拒绝样本。合成测试 `14/14`，覆盖单次跨 pass 变动后稳定、持续跨 pass 变动、伪窗口路径 blocking issue、会话创建代变化、普通后代隔离和重复 controller 拒绝。

2026-08-07 干净原版 PID `19876` 的首轮只读 A/B 实测纠正了旧污染样本的层级：`scene+0x8C` scheduler root 为 `vptr=0x607560`，活动链稳定为 `0x607560 -> 0x610A24 -> 0x610B80 -> 0x610BC8`；末端内政 controller 的 `+0x30=0x01253D0C`、`+0x34=0x3E9`、`+0x38=0`。磁盘构造器 `0x47EB10` 写 `0x607560`，`0x50F400` 才写 `0x610BC8`，与该动态层级一致。旧版 V4 因把二者混同而按预期 fail-closed，未接受错误样本。

该工具仍固定为非授权观察件，只写 stdout，不写日志，不执行游戏函数，也不含调试附加、硬件断点、内存写入、注入或输入模拟。干净进程基线只证明上述对象层级和 A/B 稳定性；它不构成命令生命周期闭环或任何执行放行。

干净复制存档上的一条人工原生命令已由只读轮询完成生命周期观察，见 R4-C；本轮没有使用硬件断点。磁盘静态校验现有 `149/149` 项通过，但其含义仅是版本锁定地址锚点匹配，且固定返回 `execution_authorized=false`。下一动态门仅允许先验证不调用游戏业务函数、不写业务状态的 ping-only 主线程桥；在桥线程身份、重入、hook 共存、固定驻留和完整上下文复核闭环前，禁止自动调用 controller/event/factory/attach/apply 或提交。任一结果不确定立即停止。

### R4-B：离线桥协议与合成宿主

状态：离线协议骨架与仓库构建接入通过；永久非授权，不构成游戏侧 Bridge 或命令执行放行。

协议帧固定为 288 字节、schema `1.0` 和显式小端 offset/width，包含声明尺寸、CRC32、零保留区、16 字节 session nonce、严格连续的 `UInt64 sequence`、16 字节 request ID、精确目标/context token 摘要、创建/过期时间、请求/结果摘要及 canonical Pending request 绑定。精确目标摘要覆盖规范路径、EXE SHA-256、文件大小、ImageBase、SizeOfImage 和四段版本；帧内没有原生指针或可执行命令体。

唯一允许的状态路径为 `Pending -> Claimed -> Completed | Rejected`。状态机只允许一个未决请求，并故障关闭地拒绝重复 ID、重放、跳号、过期/未来时间、时钟回拨、跨 session、目标/context 不符、错误宽度终态摘要、重复 Claim/终态及中止后的请求。Pending 的中止/过期会先内部进入 Claimed，再产生与原请求绑定的 Rejected 终态；所有 `IsExecutionAuthorized` 恒为 `false`。

合成宿主只使用有界、复制帧的纯内存双工 transport；没有命名管道、共享内存、进程发现/句柄、游戏读取/写入、注入、输入或 live 模式。独立静态复审结论为 GO，但仅限该离线、永久非授权边界；CRC32 与无密钥 SHA-256 不提供恶意对端认证，request-ID 历史尚未设生产上限，合成 transport 也没有持久终态确认/重传，因此不得直接升级为生产执行桥。

2026-08-07 构建证据：

- `build.ps1 -TransactionSelfTest`：`7/7`，仅使用并清理唯一事务测试目录。
- `build.ps1 -LocalRuntimeFallback`（未带 `-RunDiagnostics`）：Core `44/44`、UI state `19/19`、V0 `6/6`、V1 `9/9`、V2 `25/25`、V4 `14/14`、Bridge `25/25`。
- UI StateTests、Bridge Protocol 与 Bridge SelfTest 都以 C# 5 / .NET Framework 4.8 / x86、警告即错误、零 NuGet 编译；各自使用独立 intermediate 目录，并同时列为 staging required outputs。
- 官方 .NET Framework 4.8 Developer Pack 安装后，正式事务构建已通过并发布 BuildId `45b855203b6e4af1897ec4317612812b`；`tools\artifacts\bin\.san9-build-id` 为 `Status=Verified`，常驻 UI `San9AutoDomestic.exe` 的 SHA-256 为 `5CDFB6ED28656D4974C0E47F484418739C30BEE0909BF881BC8CE58F81245449`。此前无归属 marker 的旧 `bin` 已完整保留在 `tools\artifacts\bin.unowned-preserved-20260807-1730`，没有被静默覆盖。
- 默认测试块无参数执行 `San9AutoDomestic.UI.StateTests.exe`，并显式调用 `San9AutoDomestic.Bridge.SelfTest.exe --self-test`；`RunDiagnostics` 分支没有 UI/Bridge 变体调用，因此不会把两项离线测试隐式提升为 live 探针。

### R4-C：复制存档单城单命令原生提交

状态：人工原生操作下的“江陵・商业”选人、提交、即时副作用、存档重载持久化和下一旬复位均通过；助手自动提交仍未放行。

环境为唯一干净原版 `San9PK.exe` PID `19876`，进程创建代 `134305582712660784`；目标路径、版本、SHA-256、主模块基址/大小及 `51/51` live 代码锚点全部通过，未加载 `Easy.dll`，也没有 Easy/Hard 进程或应用目录代理 DLL。所有观察只使用 `PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`、`ReadProcessMemory` 和只读地址空间枚举；玩家本人通过游戏原生界面点击，未使用 Computer Use、输入模拟、调试器、代码写入或注入。

提交前证据：

- 直属城市江陵（city `30`，地址 `0x01251578`），军团 `1`，资金 `15273`，商业 `230/800`，商业命令位未设置。
- 商业 handler 为 `0x0AA45EF8`、vptr `0x609F38`、command ID `1`、corps `0x01253D0C`、target `0x01251578`；候选链 43 人并与 V2 原生 ready 集合完全一致。
- 唯一商业选人 task 为 `0x001AEEA4`、vptr `0x60CCB0`。其 `+0x6CC` 源链已按政治稳定降序排列，前五精确为 `253 蔣琬(93), 514 費禕(92), 507 馬良(91), 467 董允(90), 701 黃月英(88)`。
- 用户在模态选人控件勾选时，上层 `+0x6EC` 工作链仍为 0；接受该控件后工作链变为上述精确五人、顺序不变，而 `+0x6AC` 最终链仍为 0。该差异证明不能把“界面已勾选”和“上层选择已回写”混为同一状态。
- 最终确认前 A/B 基线稳定：五人 busy 位均为 0，全局选择链 `0x015455AC` 为空，资金、商业和命令位均未变化。

最终确认后的只读时间序列先观察到全局选择链为上述五人，再观察到商业值更新；随后资金、五人 busy 和商业命令位完成。该多阶段写入证明原生 apply 的业务副作用不是单次原子存储，任何只完成部分后置条件的状态都必须 `AbortUncertain`，不得重试。

稳定后置快照：

- 商业 `230 -> 286`，增加 `56`；
- 军团资金 `15273 -> 15023`，精确扣除 `5 * 50 = 250`；
- 五人 `person+0xE8 bit12` 全部由 0 变 1；
- `city+0x1E0 bit4` 由 0 变 1；
- 江陵 V2 ready 候选由 `43 -> 38`，再次观察商业的唯一失败门为 `COMMAND_ORDER_BIT_CLEAR`；其余命令继续按各自原生门判断；
- controller 最终从 `state=0x3EA,target=江陵,leaf=商业 handler` 返回 `state=0x3E9,target=0,leaf=controller`。

最终确认一开始，旧选人 task 的 vptr 立即改变且其堆地址随后被复用；此后该地址上的 `+0x6AC/+0x6CC/+0x6EC` 数值是无意义的其他对象数据。任何正式执行器必须先检查精确 task vptr 再解释派生字段，并在提交后彻底丢弃 task/handler 指针，改用新的完整 V1/V2 稳定快照验收。

保存到专用测试存档并由用户通过游戏原生界面重载后，PID 和进程创建代保持不变，V1/V2 重新通过路径、版本、主模块、冲突扫描、结构 A/B 稳定门和 `51/51` live 锚点。随后对关键字段再做相隔 120 ms 的独立 A/B 只读复核，两份快照完全相同：军团资金 `15023`、江陵商业 `286`、`city+0x1E0 bit4=1`，上述五名武将的记录 ID 均自洽且 `person+0xE8 bit12=1`。V2 同时读得江陵 ready 候选为 38、商业仍只因 `COMMAND_ORDER_BIT_CLEAR` 不可执行。这一轮证明提交结果已正确持久化到测试存档；它不证明下一旬复位，也不放行助手提交。

用户随后只通过游戏原生“进行”推进一旬，并停在下一战略画面。独立关键字段 A/B 读取仍完全一致：江陵商业保持 `286`，`city+0x1E0 bit4` 已清零，上述五人 `person+0xE8 bit12` 全部清零。完整 V1/V2 再次通过唯一目标、冲突门、进程代、结构稳定门和 `51/51` live 锚点；江陵此刻 39 名在城武将全部处于 ready 集合，商业重新 `knownStaticSubset=True`、`failed=[]`，其排序前五仍精确为 `253,514,507,467,701`。在城人数由上旬的 43 变化为 39 是旬推进后的当前游戏状态，不影响五人行动位复位结论。

军团资金在旬推进时由 `15023` 变为 `14934`，净变化 `-89`。这不是再次发生的 `5 * 50 = 250` 命令扣费；旬推进包含游戏自己的收支结算，正式后置条件必须比较命令提交窗口内的精确费用，而不能要求跨旬资金恒定。至此人工单城命令的“选择 -> 提交 -> 保存 -> 重载 -> 下一旬复位”证据链闭合，但仍不放行任何自动业务调用。

### R4-D：V6 选人生命周期只读探针

状态：离线与只读取证范围通过；自动执行继续 NO-GO。

`tools\re\san9_v6_selection_probe.py` 默认无参数只校验固定磁盘目标，只有显式 `--pid` 才能进入 live read；`--self-test` 为纯内存。live 句柄固定为 `QUERY | VM_READ`，每个 capture pass 前后都重新验证 PID 创建代、精确路径、加载映像、完整冲突进程/模块/应用目录代理清单和模块集合稳定性。探针只接受五类已知 UI-task vptr 的唯一匹配，并对 `+0x6AC/+0x6CC/+0x6EC` 三链、PERSON 对齐/ID、城市原生 source 顺序、能力稳定排序、最多五人及释放后的旧 task 地址做完整 A/B 验证；输出永久 `execution_authorized=false`。

2026-08-07 独立离线复跑：`py_compile` 通过，`--self-test` 为 `18/18`；报告 `disk_accessed=false, process_accessed=false, execution_authorized=false`。默认 disk-only 模式对固定目标的 SHA-256、i386、ImageBase 和 SizeOfImage 全部通过，并报告 `process_accessed=false`。源码静态检查未发现写内存、远程线程、注入、Hook、调试附加或输入模拟 API。本轮没有用 `--pid`。

### R4-E：V5.1 原生 ping-only 离线 proof

状态：离线构建、合成协议和 PE 审计通过；连接/注入游戏、安装 Hook、改 idle slot 及一切业务执行继续 NO-GO。

该 proof 使用独立的 256-byte HMAC-SHA256 ping 帧和 576-byte 单槽 mailbox，不兼容 C# 288-byte 离线协议，也没有 operation code、城市、武将或任意业务载荷。坏 HMAC/坏结构进入可显式 ACK-reset 的 `REJECTED_UNAUTHENTICATED`，清零后同一未消费 sequence 可继续；可信单调时钟回拨会把整个 session 锁入 `SESSION_FAULT`，即使时间恢复也不能继续，必须使用新 nonce 原子重建。gate 在 claim 之前严格绑定 caller/app/thread/slot 地址与值/app vptr/参数/精确目标/冲突/进程代；错误 gate 不领取请求、不推进 sequence。

live 能力被源码硬关闭：Bootstrap 恒 `OFFLINE_ONLY`，未初始化 IdleBridge 恒安全返回 0，controller 只接受 `--self-test`，两个 Hook callback 只调用 `CallNextHookEx`。产物没有 Hook 安装、进程打开/枚举、内存读写、远程线程、输入、网络或业务调用路径；没有 `FreeLibrary`，约定未来只能 pin 至进程退出。PE 存在两个 CRT TLS callback，且审计明确输出 `live_loader_lifecycle_proven=false`，因此当前 DLL 绝不能误装进游戏 idle slot。

工具链门在写构建产物前 fail-closed：Zig 必须精确为 `0.16.0` 且 `zig.exe` SHA-256 为 `086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864`；Python 必须 `>=3.11.0`，`pefile` 必须精确为 `2024.8.26`。两组正确/错误版本合成门均为 `5/5`。

最终独立离线复跑：protocol `102/102`、DLL `115/115`、PE DLL `12/12`、controller `11/11`；连续两次同源构建哈希一致：

- DLL `022AA72AFABEDE97BDE140887E1856A053BEB4CB35D474465A3EF437ACA5AC6C`
- Controller `9788DB7B0C01756A8113A12A38FBE50CC522C1A87EB30E9DD941A98E97501D80`

这些结果只放行离线 proof。实际 TLS/DllMain/pin、跨进程 mailbox、Hook bootstrap、idle wrapper ABI 与卸载生命周期仍须独立设计、用户知情授权和 live ping 验证；所有内政业务调用继续禁止。

### R4-F：V5.2 单次认证心跳桥

状态：默认离线构建与三轮独立审查通过；只对一次 ping-only live 试验给出有条件 GO，尚未加载或运行 live 产物；所有内政业务仍为 NO-GO。

V5.2 只允许用 `WH_GETMESSAGE` 完成一次 bootstrap，再以 CAS 将精确 idle 槽 `0x604DF4` 从原值 `0x434100` 改为已审计 wrapper。wrapper 无条件且恰好一次调用原 idle，随后最多处理一个带 HMAC、nonce、严格序号、目标/context/challenge 与五秒绝对时限绑定的 ping。协议没有命令码、城市、武将或存档载荷；DLL 会固定驻留到游戏进程退出，不支持热卸载或 slot 恢复。

最终生产状态机把所有可逆准备放在 `COMMITTING` 之前；controller 只可用 CAS 赢得取消权，DLL 也必须用 CAS 赢得唯一不可逆提交权。最终锚点后紧贴 commit 再取可信单调时间验证到期；READY 消费必须同时观察 `request=COMPLETE`、`response=READY` 和 `ping_count=1`。连续两次 unhook 失败分类为 `RESTART_REQUIRED`，连续两次 atom 删除失败分类为 `CLEANUP_INCOMPLETE`；pin 后 slot 失败、READY 后门禁失败或 ping 后置失败都要求重启，不允许伪装成已回滚。

2026-08-07 根代理独立重跑默认构建：controller `97/97`、DLL `98/98`、offline PE `18/18 + 14/14`；离线 DLL SHA-256 为 `72AFFDB20F68D7837E0DBE5AB33B41CBB9FBA8E0712A41E6D787B4C7BC14F90E`，controller 为 `171B181629E19226567B1063A89ACA22D0087A3823B73112BC6D2F28948AA9D1`。三轮独立审计所用 live-opt-in 冻结样本的磁盘审计为 `27/27 + 19/19`，DLL SHA-256 `04A3ADFFAFEA7BF07950404210A7B5C6B9EDF0DB06A6DBE312DAD5B09F948E39`，controller SHA-256 `BF0A423C2D4B0513886598B11033AA9ABCFE89C60AE45B2A7A0327B3E2B8CAAF`。由于每次 compile-only 构建都会生成新的认证密钥，根代理随后生成的待测试配对哈希为 DLL `2E4911A485F921F619B0C2A2F868F4FED1D27D3AAE1B8C900FA9995C7748C5B8`、controller `3620AC88C55D0735E7EDAD5FD5BFE27DCDA78B49FEF6D72D3AE85A8A67A71F75`，同样仅做磁盘审计 `27/27 + 19/19`；两组 live 产物都从未被加载或运行。

进行一次 live ping 仍必须同时满足：用户另行明确同意 Hook 注入与进程重启前永久驻留；当前进度已保存；目标、唯一窗口/PID/创建代、磁盘与 live 映像、冲突进程/模块/代理 DLL 全部重新通过；且用户接受无论成功、失败、超时或清理结果如何，任何后续试验前都先重启游戏。本条件授权不能扩张为城市绑定、自动选人或命令提交。

### R4-G：V9 任意城市 target binding 静态边界

状态：静态结论与独立复审通过；没有发现合法 city setter，transient target CAS 与业务 dispatch 继续 NO-GO。

V9 对精确 EXE 闭合了所有 `0x004345A0` 直接发射点：`0x7D1/0x7D3` 都只携带 packed 坐标，经原生命中解析后才写 live controller `root+0x38`，不存在已验证的 `cityId/CCityData*` 参数 ABI。内政 root 的 `0x511560` 城市列表路径以及另外三个 `CCityListDlg` 构造调用方均未写该字段；`0x01232474` 的唯一写点使用 `0x00605268 = CChiikiBuildingData` 动态类型，因此它只能称“当前操作区域建筑”观察量，不能反向充当 current-city setter。

最终 `tools\re\san9_v9_city_binding_static.py` 为 `40/40`，错误 SHA 退出码为 1，永久输出 `native_city_target_setter_found=false`、`transient_root_target_fallback_authorized=false`、`business_bridge_go=false`、`process_accessed=false`。独立复审未发现剩余中高风险；脚本与复审均只读磁盘，未访问游戏进程。

最小候选仅记录为：在已认证主线程 idle、精确 scene/root generation、完整 V2 A/B/A token、直属控制与原生 CanExecute 全部一致时，对空的 `root+0x38` 做 `0 -> exact city` 条件交换，证明 handler 已同步保存目标后再按相同代际做 `exact city -> 0` 条件恢复。构造态 `0x3E8` 不能冒充 live idle 常量；当前干净动态 idle 样本为 `0x3E9`。在只读时序、无 dispatch CAS 往返、factory snapshot 与异常恢复尚未动态证明前，该候选不得实现或运行。

### R4-H：V8 单命令离线权威事务契约

状态：离线权威契约与独立复审 GO；Core/UI/V5.2/live/native 接入继续 NO-GO。

V8 将一个请求冻结为精确命令 descriptor、city/corps、原生顺序恰好五人、sequence/deadline、process generation、context/generation digest 和完整 fingerprint。进程内全局 coordinator 提供单飞、sequence/replay、fault latch、严格新 generation 与同 PID 新创建代淘汰；已知灰项、委任、人数/资金不足只在 mutation 前产生 skip。任一 mutation 后的不确定状态都禁止自动重试。

action 与 cleanup 的真实副作用入口均由 coordinator-owned invoker 控制：在实际 callback 边界重验可信单调时钟、active identity、PID 创建代、generation、request、stage/typed method、deadline 与 exact claim，CAS 只允许一次进入；随后释放全局锁同步调用 callback，返回后再次进入协调器结算。重复/并发投递、错方法、claim 后过期的实际副作用计数均为 0 或 1；entered callback 的 throw/null 在全部七个 action 阶段统一 `HaltRestart`。production raw `Submit*` 与所有 testing seam 都通过条件编译从实际 DLL 移除。

cleanup callback 只执行 side effect，不自行提交回执。A/B post-read tickets 仅随 exact cleanup permit 创建，并在 callback 返回后由 coordinator 依次签发、锁外读取、锁内认证；ticket/claim/receipt 全部绑定 private authority、PermitId、ordinal 与一次性状态，不能拿 cleanup 前的读数冒充后置条件。DoNotRollback、对象代际、部分提交、严格下一 generation 仍以认证 A/B 为准。

根代理独立 Release/warnings-as-errors 重跑 `55/55`，实现方额外并发重复 `20/20`；冻结哈希：DLL `06690240E44448748F3B2A1A7CAA2F9073253B0A32C3AE328DC1DE8B5B47DEEF`，SelfTest `09B6E6918D44FF9E86AEFE9453BB6E4BCBF3A68CEC8BEFBF29BF186C4CEC1E06`。新增 recorder 用例实际贯穿 production callback switch，并以独立 oracle 核对七个 action 与四个 cleanup 路由。实际 production DLL 反射确认 IVT、ForTesting/raw Submit、native/cleanup callback 实现和 P/Invoke 均为 0。独立复审未发现剩余中高风险离线缺陷。

接 live 前仍必须另行完成：从真实进程句柄认证 PID creation；跨 helper/AppDomain 的 OS single-flight 与持久 fault/receipt journal；模块 hash/ASLR/vptr/profile 实机认证；只接受 permit 的可信 native adapter 与不可缓存真实 A/B；hung callback watchdog。synthetic factory/evidence 永远不能作为 live 证据。

### R4-I：V10 原生适配离线契约

状态：离线、非授权数据契约与独立复审 GO；任何进程加载、游戏调用或业务执行继续 NO-GO。

V10 冻结五项命令的 native id、费用、outer task/event、handler、CanExecute、ExecuteUI 与 factory case；冻结 BindTarget、OpenOuter、OpenSelector、Clear、NativeFillMax、AcceptInner、AcceptOuter 七条路线；cleanup 数值严格匹配 V8 公共 `AbortCleanupKind` 的 `1..6`。所有动态生命周期和 live 授权位均为 false。

请求验证复刻 V8 `.NET BinaryWriter` 的精确字段顺序、字符串 7-bit 长度、`Guid.ToByteArray()` 混合字节序与 SHA-256，并用独立金标准向量核对。x86 ABI 固定为 176 字节，关键 offset 被独立 oracle 检查；该结构明确不是跨信任边界 wire format，不能直接 memcpy 后授权。

根代理与独立审计均复跑：内部 `45/45`、独立 oracle `202/202`、PE `27/27`；DLL `8AECD7C80C6BE0E531F5216DC1883E5B181F2F35AC3F24A10E8562C2D8B9C3EC`，SelfTest `81BD8CC9A1D24AC4ED6CEFD59FBC5C614E6099CCC44FB2878A57469B2D112078`。PE 门拒绝 ordinal/delay imports、未命名或 forwarded exports、非 x86、无 ASLR/NX 与 W+X；源码和成品均无进程访问、写入、注入、hook、窗口消息、输入或游戏函数调用能力。独立审计未发现 P1/P2，但该结论只覆盖离线非授权契约。

### R4-J：P0 单命令影子纵切

状态：离线 shadow scope 与独立复审 GO；UI/Core 产品接线、IPC、进程读取及 native submission 继续 NO-GO。

P0 从冻结 profile 生成稳定的城市 ID 升序、任务配置顺序游标；每次只签发当前 ordinal 的一次性 ticket。fresh synthetic observation 必须绑定相同 generation/city/corps/command，从完整排名中按当次剩余人员取准确前五；灰项、委任、非玩家、少于五人、资金或保留金门产生无消耗 skip。只有 exact pending shadow commit 才从该城市的影子人员账本移除五人并从共享军团影子资金扣费；训练费用为零。

首版复审曾实际复现 provider 在锁内回调 Stop 后，旧 run 被外层复活且与第二 run 同时存在。修复后 provider 完全锁外执行，回锁必须通过 one-shot evaluation nonce、owner、run、ticket、cursor、generation 和状态全量复核；Stop/Dispose/recursive Evaluate/Commit、异常、迟到返回均不能复活或消耗。错误 ordinal 在有活动 cursor 时优先 fail-close，依赖构建先执行 V8 权威套件并 pin Core/V8 production hashes。

根代理和独立复审均通过 V8 `55/55`、slice `46/46`，另有 `20/20` 重复；library `F6253F961F4A4F5D5B20298987DBE511FDCB469827AC09C713DC7D24E320EB38`，SelfTest `5F1D370D779C67248F9D97FB32D5E40358374FC4A5C9E3EEA13E9C27B01525C9`。反例覆盖停止后第二 run、递归 Evaluate/Commit、非平凡排名、已消耗人员过滤、重复候选、共享资金与训练零费用。组件永久 `ShadowOnly=true`、`LiveAuthorized=false`，无 P/Invoke、进程、IPC、Adapter、Bridge 或 native callback。

### R4-K：P1 离线命令代理与持久故障门

状态：离线安全外壳与独立复审 GO；任何外部传输、进程、Bridge、native callback 或游戏执行继续 NO-GO。

P1 以 PID/创建代/目标摘要组成的 process-session identity 派生当前用户专属 named mutex 与唯一 session ledger。每条尝试先在受保护、可继承且仅当前 SID 的目录中登记 canonical journal；ledger 与 journal 均定长、校验和、独占打开、write-through 并 `Flush(true)`。启动逐项核对 ledger 登记、canonical 路径、完整 binding、文件 owner/ACL、终态与未登记 artifact。持久重放身份固定为 `(process session, request fingerprint, stage ordinal)`，只更换 `ConsentId` 仍返回 `ReplayRejected`。

首轮复审实际复现：把终态 journal 改名到旧 glob 外即可绕过扫描；加 ledger 后又复现“当次返回 JournalCorrupt，但同一 Host 未永久锁存，恢复文件后可继续”。最终实现使 scan/create/operation 的所有 restart、损坏、写入不确定、`AbortUncertain`、stop-before-side-effect、非终态 Dispose、mutex release timeout/failure 都同时锁存 operation 与 Host；可重试的 Busy/InUse、调用方 binding mismatch 和 exact replay 不误锁存。释放超时保留 unknown ownership 与 operation，显式有界 retry 后即使释放成功也不清 Host 锁；retry 最终失败则卸下 operation，但仍要求重启。ledger 在第 4097 项写入前拒绝，不污染既有 durable bytes。

最终根代理与独立审计均通过 Release/x86 `57/57`；审计另重复五轮，共 `285` 次测试执行。DLL `0C70EDFE0CE3417734DE684172D051A2A0EB639EE469A729C15130E216FC5419`，SelfTest `B69ABF9422AF769A41A245F462FBD829025D44F84074632A6CD3B344B7181926`。源码/production assembly 无 P/Invoke、进程访问、pipe/socket、Bridge 或 native callback。独立结论 P1=0、P2=0，仅覆盖 offline-only scope。

Live 前仍有两个硬边界：受控目录必须升级为稳定原生目录 handle/file-id，关闭 managed validate 后被具有祖先 rename/`DELETE_CHILD` 权限主体替换的 TOCTOU；ledger 与全部 journals 的协同删除/回滚必须由外部单调信任锚检测。未闭合前不得把 P1 接入真实命令执行。

### R4-L：P1 单一 512-byte wire 离线合同

状态：跨语言 wire、认证 gate 与独立复审 GO；未接 Adapter/UI、共享内存、Hook 或游戏，live 继续 NO-GO。

P1Wire 只定义固定 512-byte `PingRequest/PingResponse`。字段布局、小端编码、保留区、CRC32、HMAC-SHA256、严格 request/response 形状、sequence/replay、绝对时限与时钟回拨永久故障均由 C#、C11 和独立 Python 标准库 oracle 三方逐字节验证；协议不含 city、command、person、函数地址或业务授权字段，`LiveAuthorization=false`。公开入口只允许 authenticated decode 后进入 gate/response verifier，raw decoded helper 不属于生产公开面；C encode 在任何写入前拒绝 frame/output、完整或部分 key/output alias。

最终离线复跑：Python oracle `1030/1030`、C# `2091/2091`、C `1765/1765`。request SHA-256 为 `ACADF1F1C5CF70672484632E32865871058E3193B84FFC5C233990985FBE80D5`，response 为 `2DCEF30E7C5B5219F255D97C7A260019748551D2279C868E62539CE204A0E87E`；两个独立 ArtifactRoot 的 stripped native EXE 均为 `354D9AA96D6CF62E3997556FC28C0A2B6D75CA505F2907763E3AC931B5A6530F`。PE 审计在执行前完成，冻结整映像、entrypoint RX/non-W、精确 imports、TLS callbacks，拒绝 duplicate descriptors、unnamed/delay exports 与 RWX；执行后同哈希再审。独立结论 P1=0、P2=0，仅放行下一阶段 compile-only 工作。

### R4-M：P1 M1 原生 Easy A/B 离线 verifier

状态：M1 纯离线 verifier 与独立复审 GO；无进程访问、目标写入、IPC、Hook 或 live code。

M1 从已认证 manifest 与精确 San9PK 镜像生成私有 native table，覆盖 Easy 的 32 个代码重定向点、6 个辅助物理写点、页面状态及 3 个 idle anchors。固定 buffer/read/query callback 完成两次完整 A/B；CALL+NOP 的 rel32 基准固定为 source+5，按实际 Easy base 做 x86 模 2^32 计算；失败 snapshot 全清零，报告 fail closed，并只记录最早故障点。构建顺序固定为双目录编译与整文件同一性 -> 执行前 PE 审计 -> 离线 self-test -> 哈希不变 -> 执行后 PE 复审。

最终独立复跑：generator `13/13`、native self-test `727/727`、5 个恶意 PE fixture 全部拒绝。四份独立 EXE 均为 `E6BC0FEB88FA280A879072CCD48F39E9A0FF02238268580CFA9631F724BB65AF`；generated header 为 `CCA306579C23581A660CD6972CC37104DBE5339094DDD841FED8C91A40E98F7A`；PE audit 脚本为 `B5748E658BFE8F96E29E3C7C44E08F4B7A593B1175147CF00D652C23B7947745`。独立 mutation 反证确认：移除失败清零会失败 84 项；伪造 compatible 位会失败；恢复覆盖式 first-failure 会失败 4 组；篡改 TLS 直达 callee 会被 whole-image SHA 立即拒绝。独立结论 P1=0、P2=0，仅放行纯离线 M2a mailbox/lifecycle 建模。

### R4-N：P1 M2a mailbox / exactly-once 离线模型

状态：纯离线、同进程协作方 SPSC 模型与针对性独立复审 GO；真实共享内存、ACL、Hook、跨进程并发和 live 继续 NO-GO。

M2a 直接链接冻结的 P1Wire 与 M1 源码，不复制 codec 或 Easy verifier。4096-byte mailbox 固定 request/response 两个 512-byte 槽；非原子 key/frame 写完后才以 release CAS 发布，消费者用 acquire 领取。每轮冻结私有 authenticated request、sequence/requestId/challenge 与 one-shot round token；未消费前不复用槽位。`VALIDATING -> COMMITTING` 是唯一提交权竞争点，取消赢则 fake side effect 为 0，提交赢后任何不确定失败进入 first-poison-wins 的 `POISONED_RESTART`。target、controller publish/consume/discard 与 public poison 共用一个原子 processing owner，避免 poison 在轮次处理或响应消费期间清空 pending 数据；所有 claim 路径统一释放，旧 token/旧 completion/旧 owner 不能触及下一轮。所有 action 仍是 fake callback，`LiveAuthorization=false`。

最终流水线：generator `13/13`、M2a self-test `366/366`（含 100 次成功与第 101 次预算拒绝）、5 个恶意 PE fixture 全部拒绝。根代理另在独立 ArtifactRoot 复跑同样通过；双根及执行前后 EXE 均为 `398DDAC22B75E4088548BF2954A902390F6DD45AB86B7DC5DD178947D31EA46E`，generated header 为 `CCA306579C23581A660CD6972CC37104DBE5339094DDD841FED8C91A40E98F7A`，PE audit 为 `0CEF442329574FE7CB05D437F5ABC8D3395604CA4F14974798A7EEF5D07EA2A4`。针对失败 token 清零与 poison/target/consume 同根竞态的独立冻结复审结论 P1=0、P2=0。PE 审计在 self-test 前后均确认 process access、target writes、IPC 与 live code 为 0。

### R5：全城与异常回归

状态：未开始。

至少覆盖 PRD 中的不足 5 人、灰项、资金不足、共享资金、委任城、途中控制权变化、场景切换、重复点击、停止、进程退出、冲突模块和两种默认方案。全部通过后才可打包为可执行版本。

## 发布判定

当前版本是开发期只读诊断件，不是可执行的一键内政成品。不得仅因界面能启动、城市能显示或离线规划测试通过，就启用方案按钮。
