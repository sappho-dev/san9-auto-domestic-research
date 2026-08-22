# San9PK 1.01 最小主线程桥静态审计

状态：**离线桥设计与合成测试 GO；向游戏进程安装 ping-only 桥仅为条件 GO，仍需用户另行明确授权；自动内政继续 NO-GO。**

本轮对目标游戏只读取磁盘上的精确文件，未连接、读取或写入任何运行中游戏进程，未启动游戏或修改器，未安装 Hook，也未使用 Computer Use。V5.1 另在自测控制器自己的进程中加载了本项目新编译的 DLL，只运行合成协议与 synthetic idle thunk；这不等于把 DLL 加载进游戏，更不构成进程内桥安全性证明。本文讨论的游戏内架构仍是未来候选，而不是已经证明安全的执行能力。

## 1. 目标锁与边界

- 文件：`D:\三国志9\10101749\San9PK.exe`
- 文件版本：`1.0.1.0`，PE32/x86
- SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- ImageBase：`0x00400000`
- `DllCharacteristics=0`，没有 `DYNAMIC_BASE`

地址只适用于这一份文件。未来 DLL 即使只做心跳，也必须同时校验磁盘哈希、运行模块基址、关键代码字节、IAT/vtable 原值、唯一目标进程和冲突模块；任一不符立即拒绝。

候选设计不修改磁盘 EXE，不使用 `version.dll` 侧载，也不使用远程线程。若采用 `SetWindowsHookEx` 显式映射 DLL，必须如实把它视为一种 Windows Hook 注入；只有用户另行明确允许进程内桥测试后才能进行，不能把“非侧载”表述成“没有注入”。

## 2. 先纠正 task 层级：`0x607560` 才是 scene scheduler

干净原版中 `scene+0x8C` 指向 `0x607560` 并非异常，而是与磁盘静态构造路径完全一致：

```text
0x434480  scene constructor
  0x4344BD  push 0x30
  0x4344C4  call 0x5DEC20             ; game allocator
  0x4344DC  call 0x47EAF0             ; scheduler constructor
  0x4344E9  mov [scene+0x8C], eax

0x47EAF0  scheduler constructor
  0x47EAF8  call 0x47E330             ; generic state-task base
  0x47EAFD  mov [this], 0x607560

0x47EB10  scheduler teardown/reset
  mov [this], 0x607560
```

这个 scheduler 的分配尺寸只有 `0x30` 字节。它的 vtable 为：

| 槽 | `0x607560` scheduler | 静态用途 |
|---:|---:|---|
| `+0x00` | `0x47ECD0` | 析构入口 |
| `+0x08` | `0x47E4C0` | task gate/thunk |
| `+0x0C` | `0x47E360` | 状态 tick |
| `+0x28` | `0x47EB20` | 上层场景 task factory |

`0x610BC8` 是另一种、至少 `0x40` 字节的内政 controller 后代：

```text
0x50F191  push 0x40
0x50F193  call 0x5DEC20
0x50F1B6  call 0x50F380
0x50F398  mov [this], 0x610BC8
0x50F39E  mov [this+0x34], 0x3E8
0x50F3A5  mov [this+0x38], 0
```

| 槽 | `0x610BC8` controller | 与 scheduler 的差异 |
|---:|---:|---|
| `+0x0C` | `0x516220` | controller 状态机，不是 `0x47E360` |
| `+0x18` | `0x5179B0` | 单参数游戏事件 |
| `+0x1C` | `0x514370` | 三参数输入事件 |
| `+0x28` | `0x5125A0` | 内政 handler factory |

因此：

- `scene+0x8C` 必须先精确锁定为 scheduler vptr `0x607560`；
- 通用 task 只读取 `+0x0C child`、`+0x10 pending`、vptr 和 vtable `+0x0C`；
- `+0x30 corps / +0x34 state / +0x38 target` 只能从活动 child 链上 **vptr 恰为 `0x610BC8`** 的节点读取；
- scheduler 本体只有 `0x30` 字节，从它读取上述三项已经越出对象边界；
- 活动链没有 `0x610BC8` 是合法场景；若出现多个 controller，身份有歧义，应故障即停，不能随便取最深或第一个。

`0x47EB20` 会按状态创建多种不同 task，这也解释了为什么 controller 并非一直存在。内存中全局搜索到一个 `0x610BC8` 对象同样不足以证明它属于当前 scene 的活动链。

## 3. 主循环与真正的 idle 边界

应用对象固定地址为 `0x01228340`，构造器 `0x432233` 写入 vptr `0x604DD0`。关键槽位为：

| 应用 vtable 槽 | 目标 | 作用 |
|---:|---:|---|
| `+0x18` | `0x5C5CE0` | 主消息循环 |
| `+0x24` | `0x434100` | 每轮 idle/game update |
| `+0x2C` | `0x5C5A30` | 退出路径 |

主循环的顺序是：

```text
0x5C5CE0
  -> 0x5CADA0                      ; drain Win32 messages
  -> 0x5CAE80                      ; pump cleanup
  -> call [app.vtable+0x24]        ; 0x5C5D0E, return 0x5C5D11
       -> 0x434100(app, 0)
            -> 0x4345C0(scene, 0)
                 mov ecx,[scene+0x8C]
                 0x4345C6 call 0x47E840
                      -> deepest task vtable+0x0C
```

`0x5CADA0` 从 IAT `0x5FF3F4` 把 `PeekMessageA` 缓存在 `ESI`，先在 `0x5CADBE` 调用，随后在 `0x5CAE45` 循环调用直至队列为空。主循环之后才进入 `0x434100`。这给出了一个比 WndProc 更清晰的静态 idle 边界。

静态可推断的调用约定：

- `0x434100`：MSVC x86 `__thiscall` 形态，`ECX=app`，栈上一个参数，`ret 4`，正常返回 `EAX=1`；
- `0x4345C0`：`ECX=scene`，栈上一个参数，`ret 4`；
- `0x47E840`：`ECX=schedulerRoot`，没有显式栈参数，普通 `ret`；
- `0x5CC6F0`：四参数 Win32 WndProc，`ret 0x10`；
- `0x5C9560`：三参数 `WH_GETMESSAGE` callback，`ret 0x0C`。

这些 ABI 仍须在干净环境用只读寄存器/栈观测确认，尤其不能仅凭 `ret N` 就假设异常处理、浮点状态或对象所有权已经清楚。

## 4. 候选 Hook 比较

### 4.1 首选候选：原子替换应用 vtable `+0x24`

- 槽地址：`0x604DF4`
- 精确原值：`0x434100`
- 地址四字节对齐；所在 `.rdata` 在该 PE 中具有可写属性。
- 主循环在消息队列排空后通过 `call [eax+0x24]` 进入它。

与改写 `.text` 相比，这是一次对齐的 32 位函数指针替换，不需要搬运指令或生成 trampoline。未来最小 wrapper 可使用形如 `int __fastcall IdleBridge(App *self, void *unusedEdx, int flag)` 的 ABI 来接住 `__thiscall`；它必须调用保存的 `0x434100` 原函数，并保持栈、非易失寄存器、方向标志和返回值语义。

建议动态心跳阶段采用“先调用原 idle，再处理至多一条无游戏调用的 ping”。这样当前帧 task tick 已结束，业务动作即使未来获准也只能留到下一帧。wrapper 还必须验证：

- 当前线程等于主窗口 `GetWindowThreadProcessId` 的线程；
- 调用者返回地址为 `0x5C5D11`；
- `self==0x01228340` 且 `[self]==0x604DD0`；
- 参数仍为静态观察到的 `0`；
- slot 在安装前仍等于 `0x434100`，没有其他 Hook；
- TLS 重入深度为 1；嵌套消息循环只调用原函数，不处理队列。

静态判断：**首选，但只 GO 到无副作用心跳 harness。** 它仍是进程内存修改，未获用户明确批准前不得安装。

### 4.2 IAT Hook：`PeekMessageA @ 0x5FF3F4`

优点是只改一个导入指针，且原函数返回 `FALSE` 时正处于主线程消息泵即将退出的边界。wrapper 可以把返回地址限定到 `0x5CADC0` 或 `0x5CAE47`，避免把其他调用点误当主循环。

但 `0x5CADA6` 会把 IAT 值缓存到 `ESI`，同一轮随后多次 `call esi`。恢复 IAT 后，栈上仍可能保存旧 DLL 地址；若 DLL 随即卸载，下一次 `0x5CAE45` 会跳进已经释放的代码。IAT Hook 还覆盖所有经该导入项调用 `PeekMessageA` 的位置，范围大于 vtable idle 槽。

静态判断：**备选，不首选；除非 DLL 固定驻留到进程退出，否则卸载风险不可接受。**

### 4.3 Inline/call-site detour

可见三个候选：

| 地址 | 原始指令边界 | 风险 |
|---:|---|---|
| `0x434100` | 前 6 字节为 `push esi; push edi; mov edi,[esp+0x0C]` | 需 trampoline；安装时写 5/6 字节，非原子 |
| `0x4345C0` | 前 6 字节为 `mov ecx,[ecx+0x8C]` | 比入口更窄，但仍是 `.text` detour |
| `0x4345C6` | 完整 5 字节 `call 0x47E840` | 可替换单个 call target，但 rel32 位移写入未对齐，安装/卸载仍可能撕裂 |

`0x4345C6` 在语义上最接近 task tick 前边界，然而从工作线程改写正在执行的代码存在竞争；暂停主线程又会扩大死锁和锁持有风险。由于已有对齐的 vtable 指针候选，没有必要把 inline detour 作为第一版。

静态判断：**当前 NO-GO；仅作为 vtable 候选动态证伪后的后备研究。**

### 4.4 `WH_GETMESSAGE`

目标程序自己已经这样做：

```text
0x5CA0C0  push 0x5C9560
0x5CA0C5  push 3                    ; WH_GETMESSAGE
0x5CA0C7  call [SetWindowsHookExA]

0x5C9560  game GetMsgProc
  checks WM_MOUSEFIRST..WM_MOUSELAST
  ...
0x5C95E7  call [CallNextHookEx]
```

这证明本版本的主窗口线程已经有一条 `WH_GETMESSAGE` 链。另一个 32 位线程定向 Hook 理论上可以共存，但必须始终调用 `CallNextHookEx`；Hook 顺序、修改器冲突、桌面/session、完整性级别和 DLL 位数都需动态验证。

`WH_GETMESSAGE` callback 位于 `PeekMessageA` 内部回调语境，不是已证明的游戏 task 安全点。它适合：

1. 由用户明确点击后，让 32 位控制器把极小 DLL 显式映射进目标线程；
2. 校验进程/线程身份；
3. 将模块固定驻留；
4. 原子安装 idle vtable wrapper；
5. 通过预先建立的固定共享块回报 ready，并继续 Hook 链。

控制器收到 ready 后可撤销这条 bootstrap Hook；idle wrapper 此后直接轮询有界共享 mailbox，不再依赖 Hook callback。微软明确说明 `UnhookWindowsHookEx` 返回时，另一个线程上的 callback 仍可能正在执行，所以 DLL 必须先成功 pin，且绝不因 unhook 而热卸载。需要唤醒消息泵时只投递无业务语义的 `WM_NULL`，不能把 Win32 消息号伪装成游戏事件。

它不适合直接调用 `0x5179B0`、`0x5125A0`、`0x47E6F0` 或任何内政 apply。

静态判断：**只作为加载/唤醒通道条件 GO，作为游戏调用执行点 NO-GO。**

### 4.5 `WH_FOREGROUNDIDLE`

Windows 还提供 hook id `11`：当前台线程将要 idle 时调用。它不需要修改 San9 的 vtable/IAT/`.text`，因此可以作为 **更早一轮的 ping-only 对照实验**，只记录 callback 线程 ID、调用频率和当前返回地址。

但它不是目标程序自己的显式 task 边界：是否在持续渲染的 San9 主循环中稳定触发、退到后台后是否饥饿、模态循环和场景切换时位于什么栈帧，都只能动态测量。长期把它用作业务 dispatcher 还要求 Hook 一直存在，生命周期面反而大于 bootstrap 后撤销 Hook 的 vtable 方案。

静态判断：**GO 到无副作用 heartbeat 对照；作为游戏函数执行点 NO-GO。**

### 4.6 `WH_CALLWNDPROC`、WndProc subclass 与 WndProc inline Hook

框架 WndProc 是 `0x5CC6F0`，由 `0x5CC83D` 写入窗口类并注册。V4 已确认它没有把自定义 Win32 消息映射到游戏事件/task factory。

- `WH_CALLWNDPROC` 在同步发送/窗口分发上下文中运行；外部 `SendMessage` 会引入阻塞、重入和潜在死锁，callback 也不能把这一上下文自动变成 idle safe point。
- `SetWindowLong(GWL_WNDPROC)` subclass 必须正确链到原 WndProc，并与框架/修改器的再次 subclass 协调；卸载前恢复存在同样的 in-flight callback 竞争。
- inline Hook `0x5CC6F0` 会覆盖所有使用该框架过程的窗口，范围过大。

静态判断：**均不作为执行桥；最多只能做 wake transport，而且 `WH_GETMESSAGE` 已更贴合现有消息泵。**

## 5. 推荐的最小桥形态

```text
32-bit external controller
  ├─ exact target/process/conflict gate
  ├─ authenticated fixed-size shared mailbox
  └─ thread-specific WH_GETMESSAGE bootstrap (explicit user action)
             │
             ▼
minimal x86 DLL in San9PK
  ├─ DllMain: POD only; no hook install, no IPC, no game calls
  ├─ GetMsgProc: initialize once, pin module, install idle slot, signal ready, chain
  └─ IdleBridge at app-vtable[+0x24]
       ├─ TLS reentry guard
       ├─ call original 0x434100
       ├─ validate exact main-thread/caller/app/task snapshot
       └─ V5 harness phase: poll/acknowledge ping only; zero game calls/writes

controller observes ready
  └─ UnhookWindowsHookEx bootstrap; pinned DLL remains until process exit
```

DLL 应在 Hook callback 返回到正常环境后使用 `GetModuleHandleEx(...PIN...)` 或等价、可证明的自持引用固定到进程退出；固定失败就不安装 vtable wrapper。控制器关闭、IPC 断线或点击“停用”只把桥切到 no-op，不运行时卸载 DLL。

“不卸载直到游戏退出”不是便利性选择，而是第一版的安全约束：即使 vtable slot 已恢复，wrapper 仍可能处于调用栈上；Hook 回调也可能正在链式返回。没有一个静态可证明的时刻允许外部线程立即 `FreeLibrary`。

消息 callback、idle wrapper 和 IPC 工作线程都不得：

- 在 `DllMain` 或 Hook callback 中阻塞等待控制器；
- 持锁调用游戏函数；
- 处理超过一个有界请求；
- 捕获异常后继续未知状态；
- 在重入深度大于 1 时处理请求；
- 把断线重连解释为自动重试业务命令。

## 6. 必须动态验证的项目

下列每项都缺一不可，而且通过一层不会自动授权下一层。

### A. 无 Hook 的只读基线

- 精确哈希、模块基址、app vptr/vtable、`0x604DF4==0x434100`；
- `scene+0x8C` vptr 恰为 `0x607560`；
- child 链允许没有 controller，恰一个才读取 controller 专属字段，多个拒绝；
- 当前映像中没有 Easy/修改器/未知 Hook，关键代码页和 IAT 未变。

### B. Hook 加载与无副作用心跳

- 控制器和 DLL 均为 x86，Hook 只绑定主窗口线程；
- DLL load/unload 次数、Hook 链顺序和游戏自带 `0x5C9560` callback 不受破坏；
- idle wrapper 的线程 ID、caller、`self`、参数、调用频率与静态推断一致；
- 仅发送 ping/ack，持续运行、切后台、切前台、菜单、模态窗口、读档、换场景、退出均不崩溃、不挂死；
- 重入时只走原函数，断线时桥自动 no-op；
- 游戏退出由 OS 回收 DLL，不尝试热卸载。

### C. 只读 task 生命周期

- 在 wrapper 内对 app→scene→scheduler→child 做同帧 A/B 双读；
- 确认 `0x47E840` 只在该窗口线程调用；
- 由玩家原生点击一条内政命令，观测 controller、target、pending、handler、command 的创建/销毁顺序；
- 确认 nested message loop 和原生选人窗口是否造成 idle wrapper 重入。

### D. 任何游戏调用之前仍需单独闭环

- 指定城市如何成为合法 `controller+0x38 target`；
- 事件、handler、选人窗口、全局选择链、validator、command apply 的完整对象所有权；
- 五人上限、同城、当前候选集、资金、已行动、项目位等 UI 不变量；
- 失败、异常、读档、切场景和控制器断线时的幂等性与恢复策略；
- 修改器/Hook 冲突的硬拒绝规则。

在 D 未经独立验证前，idle wrapper 即使稳定，也只能证明“代码在观察到的主线程边界被调用”，不能证明“调用某个游戏地址安全”。

## 7. Go / No-Go

| 范围 | 结论 |
|---|---|
| 继续磁盘静态分析 | GO |
| 修正 V4 为 `0x607560 scheduler → active child 0x610BC8 controller` | GO，且必须修 |
| 编写不安装到游戏的 DLL/协议合成测试 | GO |
| 经用户再次明确同意后，在复制存档环境做 ping-only 主线程桥心跳 | 条件 GO |
| `WH_FOREGROUNDIDLE` ping-only 对照 | 条件 GO；不得调用游戏函数 |
| 用 `WH_GETMESSAGE` callback 直接调用游戏函数 | NO-GO |
| IAT/inline/WndProc Hook 作为首选执行桥 | NO-GO |
| 自动调用 controller/event/factory/attach/apply | NO-GO |
| 修改 `San9PK.exe` 磁盘文件或使用 `version.dll` 侧载 | 永久禁止 |

当前首选只是一条待动态证伪的桥：**`WH_GETMESSAGE` 显式 bootstrap + 原子 app idle vtable wrapper + DLL 固定驻留 + ping-only**。它把“进入主线程”与“执行游戏业务”严格分开。

Windows Hook 相关操作系统契约以微软官方文档为准：[SetWindowsHookExA](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexa)、[GetMsgProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/getmsgproc)、[CallWndProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/callwndproc)、[UnhookWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unhookwindowshookex)、[GetModuleHandleExA](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandleexa)。其中位数匹配、始终继续 Hook 链、`WH_CALLWNDPROC` 不能修改消息、unhook 后仍可能有 in-flight callback，以及 `PIN` 持续到进程结束，均不是本项目自行假设。

## 8. 可复核静态工具

运行：

```powershell
py -3 tools\re\san9_v5_static.py --json
```

脚本单次读取磁盘文件，先校验 SHA-256，再检查主循环、IAT、scene scheduler、controller、task tick、游戏自带 `WH_GETMESSAGE` 和 WndProc 锚点；`main_thread_bridge_authorized` 与 `domestic_execution_authorized` 永远为 `false`。静态锚点全通过只表示本文描述的精确文件布局仍匹配。

## 9. V5.1 离线 ping-bootstrap proof 实现状态

V5.1 已实现并通过离线构建/自测，但它刻意停在 **ping-bootstrap proof**，没有 live 安装能力：

- `native/San9BridgeV51/`：可由 Zig 0.16 构建的 x86 Windows DLL、x86 自测控制器、共享头与 C 实现；
- `tools/San9BridgeV51/build.ps1`：独立构建入口，不接入仓库根 `build.ps1`；
- `tools/re/san9_v51_native_audit.py`：只读两个输出文件的 PE32/export/import 审计；
- 输出位于被忽略的 `tools/artifacts/San9BridgeV51/`，与 native 源码目录隔离，且不是可部署版本。

构建与验证命令：

```powershell
tools\San9BridgeV51\build.ps1
```

2026-08-07 的离线结果：

- Zig `0.16.0` 成功生成 DLL 与 controller，二者均为 `IMAGE_FILE_MACHINE_I386 / PE32`；
- build 对 Zig 做精确版本与二进制锁：只接受 `0.16.0` 且 `zig.exe SHA-256=086CE9D47BA42F33A514E1A6E04EB1D4A8FA1D75E0868E0213CAAD447C91E864`；Python 要求不低于 `3.11.0`，`pefile` 精确要求 `2024.8.26`。Zig identity 与 Python/pefile version gate 的错误版本合成测试均为 `5/5`，实际工具链不符立即停止构建；
- controller 内嵌协议测试 `102/102`，DLL 测试 `115/115`；
- synthetic 重入用例调用注入的原 idle thunk 恰好两次，只在最外层处理一次已认证 ping，返回值保持，TLS 深度回到零；
- DLL 六个导出名无 x86 修饰：`San9Bridge_GetMsgProc`、`San9Bridge_ForegroundIdleProc`、`San9Bridge_IdleBridge`、`San9Bridge_Bootstrap`、`San9Bridge_GetContract`、`San9Bridge_OfflineSelfTest`；
- DLL 的 PE 审计 `12/12`、controller `11/11`，未发现 ordinal import、`SetWindowsHookEx*`、`UnhookWindowsHookEx`、`OpenProcess`、进程内存读写、远程线程、进程枚举、`FreeLibrary*`、消息模拟或网络导入；
- `process_accessed=false`、`hook_installed=false`、`live_mode_present=false`、`business_execution_authorized=false`。

### 9.1 它与 C# `Bridge.Protocol` 不可互换

V5.1 的 `256-byte` frame 是独立的、用途封闭的 ping-bootstrap proof wire format；现有 C# `San9AutoDomestic.Bridge.Protocol` 是 `288-byte` schema。两者只复用下列安全语义：HMAC、target/context binding、nonce、request id、challenge、有限时效和单调序列。它们的长度、magic、字段布局、状态机与兼容承诺均不同。

DLL contract 把关系写成机器可检查的不变量：

```text
protocol_role = PING_BOOTSTRAP_PROOF
ping_frame_size = 256
mailbox_size = 576
ping_only = 1
production_business_protocol_compatible = 0
csharp_bridge_288_compatible = 0
unauthenticated_recovery_requires_ack = 1
trusted_monotonic_time_source_required = 1
clock_rollback_requires_new_session = 1
execution_authorized = 0
live_bootstrap_enabled = 0
```

该 frame 没有 `operationCode`、命令类型、城市、武将、数值或任意业务 payload，因此不能被解释为生产业务协议，也不能向 C# `288-byte` parser 投递。未来若有生产协议，只能在完成独立设计、版本协商与验证后另行实现，不能通过“放宽长度/兼容解析”复用本 proof。

### 9.2 固定有界 mailbox 与认证顺序

mailbox 固定为 `576` 字节：`64-byte` 控制区、一个 `256-byte` request、一个 `256-byte` response。发布主路径按 `EMPTY → WRITING → READY → CLAIMED` 前进；已认证请求最终进入带签名 response 的 `COMPLETE`，未认证/坏帧进入无 response 的 `REJECTED_UNAUTHENTICATED`，时钟回拨进入 `SESSION_FAULT`。发布使用 32 位原子 CAS 和 memory barrier；槽非空时拒绝第二条请求，不存在动态长度、队列扩容或无界循环。

每个请求由私有 `32-byte` key 做 HMAC-SHA256，并绑定：

- 非零 session nonce、request id、target digest、context digest、challenge；
- `issued_at < expires_at` 且 lifetime 不超过 `5000 ms`；
- 从 1 开始、严格连续的 sequence；成功请求才推进 `last_accepted_sequence`；
- response 回显并核对所有不可变绑定，另做 response HMAC。

结构校验或 HMAC 失败的请求不生成 response，避免提供未认证 oracle；controller 看到 `REJECTED_UNAUTHENTICATED` 后必须显式调用 ACK-reset，CAS 独占该终态、清零两帧后才回到 `EMPTY`。它既不会自动重试，也不会把单槽永久留在无法回收的 `COMPLETE`。合成测试分别覆盖坏 HMAC、坏 magic、ACK-reset，以及随后用同一未消费 sequence 成功处理合法请求。

已认证但 binding/time/sequence 被拒绝的请求才收到签名 rejection。比较 MAC 与绑定字段使用恒定时间比较。上述 key/session 目前只存在于合成测试；**没有任何 live key provision、共享内存创建或游戏内 session 初始化入口。**

freshness 的 `now_ms`、`issued_at`、`expires_at` 必须来自两端共享的可信单调 boot-domain（未来 Windows 候选例如同一系统启动域的 `GetTickCount64`），不得使用可校时的 wall clock。session 保存 `last_observed_time_ms`；若一次处理采样小于上次采样，就先把请求置为 `SESSION_FAULT`，锁住 session，且不生成 response、不推进 sequence。后续时间即使再次前进也继续返回 `CLOCK_ROLLBACK`。恢复必须原子执行显式 clock-fault reset，并提供与旧值不同的新 session nonce；reset 同时清空 mailbox、sequence 与时间基线，复用旧 nonce 会拒绝。

`last_observed_time_ms` 只是第二道 sampled rollback gate：如果两个请求之间时钟先前进、再回拨但最终仍不小于上一次已采样值，它无法检测中间轨迹。因此它不替代可信单调时钟源，也不把真实 IPC freshness 从条件项提升为已证明能力。测试覆盖直接回拨、故障持续、旧 nonce 重置拒绝、新 session 恢复、sequence 从 1 重启，以及相等单调时间边界。

### 9.3 导出与生命周期硬边界

- 项目自己的 `DllMain` 只保存模块句柄，不安装 Hook、不启动线程、不建 IPC、不调用游戏；但 Zig/MinGW 产物的 PE 入口仍经过 CRT，并带两个 CRT TLS callback，imports 可见 `TlsGetValue`、critical-section、`Sleep`、`calloc/free`、`VirtualProtect/VirtualQuery`。所以不能把“用户 DllMain 很小”外推为“loader-lock 下完全没有 CRT/TLS 工作”；这不影响离线 proof，却使 live loader 生命周期继续 NO-GO/未证明；
- 两个 Hook callback 导出只调用 `CallNextHookEx` 继续链；DLL 自己没有 `SetWindowsHookEx*` 导入；
- `San9Bridge_Bootstrap` 对所有输入恒返回 `OFFLINE_ONLY`，没有可切换 live 的 flag 或隐藏 setter；
- `San9Bridge_IdleBridge` 在未有未来经授权并完整验证的 session/bootstrap 前恒安全返回 `0`，**绝不跳到固定地址 `0x434100`**；
- 真实 idle 次序“原函数一次 → 最外层至多一个已认证 ping”只由内部 core 在离线自测中验证，原函数来自注入的 synthetic thunk；
- `San9Bridge_OfflineSelfTest` 在 `DllMain` 外通过 `GetModuleHandleExW(FROM_ADDRESS|PIN)` 固定 DLL，并核对句柄；controller 不调用 `FreeLibrary`，仅随自己的测试进程退出；
- contract 固定 `hot_unload_allowed=0`。pin 失败即自测失败，不降级为可卸载模式。

exact gate 在处理 ping 前同时要求 caller `0x5C5D11`、app `0x01228340`、app vptr `0x604DD0`、thread id、slot 地址 `0x604DF4`、slot 当前值、参数 `0`、精确目标验证、零 Hook 冲突和稳定 process generation 全部成立。离线 synthetic snapshot 能证明 gate 逻辑按字段 fail-closed，但不能证明这些值在真实游戏里始终成立。

### 9.4 当前 Go / No-Go 没有扩大

V5.1 证明的是“x86 artifact 可构建、固定协议能认证、重入 core 的合成语义成立、危险导入未出现”。它没有证明 Windows Hook 生命周期、真实主线程 ABI、vtable 原子替换、页面保护变化、修改器冲突或游戏退出路径。

因此当前结论仍是：

- 离线编译、合成测试、PE 静态审计：**GO**；
- 任何连接/注入运行中游戏、`SetWindowsHookEx` bootstrap、idle slot 写入：本实现不存在；未来也仅在用户另行明确授权的 ping-only 动态阶段才可 **条件 GO**；
- 任何内政业务调用、业务 frame、controller/event/factory/attach/apply：**NO-GO**；
- 修改磁盘 EXE、`version.dll` 侧载、远程线程、热卸载：**禁止**。

## 10. V5.2 后续状态

V5.2 已另建默认离线 profile 与 compile-only live-optin profile；它没有改变本页 V5.1 的硬关闭边界。详细设计、V0 等价冲突门、IdleBridge 机器码契约、测试与动态 NO-GO 见 [V5.2 ping-only live-bootstrap 离线实现说明](reverse-engineering-v5.2-live-bootstrap.md)。本轮没有加载或运行 live-optin 产物，任何 live Hook/slot claim 与所有内政业务仍未授权。
