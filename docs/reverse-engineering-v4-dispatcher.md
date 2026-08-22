# San9PK 1.01 主线程 dispatcher 审计

状态：**NO-GO（自动调用）**。本轮只做目标文件静态分析和现有只读 root 链复核；没有写入、注入、Hook、远程线程、键鼠模拟或游戏函数调用。

## 1. 目标锁

- 路径：`D:\三国志9\10101749\San9PK.exe`
- 版本：`1.0.1.0`，x86
- SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- ImageBase：`0x00400000`，本版本无 ASLR

本页地址只适用于上述精确文件，不能推广到 Steam、日文版、其他汉化或被修改器改写后的运行映像。

## 2. 已确认的主循环

```text
0x5C5CE0
  ├─ 0x5CADA0：PeekMessage / TranslateMessage / DispatchMessage
  └─ 无窗口消息时
       └─ app vtbl+0x24 = 0x434100
            └─ scene
                 └─ 0x4345C0
                      └─ 0x47E840：游戏 task tree tick
```

`0x47E840` 会沿 `task+0x0C` 查找活动最深子任务，处理 `task+0x10` 的单个 pending task，并调用 leaf `vtbl+0x0C` 推进状态机。附加和 tick 周围没有观察到可供其他线程使用的锁或通用工作队列。

因此 `0x47E6F0` 的契约是“把一个已经合法构造、引用计数正确的游戏 task 放进 parent 的单个 pending slot”，不是线程安全 dispatcher，更不是外部 RPC 入口。

## 3. 场景事件入口

当前活动链中的内政 controller vtable 为 `0x610BC8`；它是 scheduler root 的后代，不是 `scene+0x8C` 本身：

- `+0x18 = 0x5179B0`：单参数游戏事件；
- `+0x1C = 0x514370`：三参数输入事件；
- `+0x28 = 0x5125A0`：task factory。

已检查命令路径：

```text
0x4345B0
  -> 0x47E4D0
  -> current deepest task
       -> deepest controller vtbl+0x18 = 0x5179B0
       -> event in [0x2710, 0x2757)
       -> controller vtbl+0x28 = 0x5125A0(commandIndex)
       -> 0x47E6F0(task, 1)
```

找到的 `0x4345B0` 调用点来自游戏自身 UI。它们把游戏内部事件交给当前 scene；并不表示 `PostMessage(hwnd, event)` 会进入这条链。

## 4. Win32 消息候选全部不成立

| 候选 | 已检查契约 | 结论 |
|---|---|---|
| 主窗口 WndProc `0x5CC6F0` | 由 `0x5C731B -> 0x5CC820` 注册；最终走窗口记录的普通消息处理 | 无消息映射到 `0x4345B0`、`0x5179B0` 或 task factory |
| `0x5CA9F0 / WM 0x601` | `PostMessage(main, 0x601, target, node*)`；`node` 必须是目标进程分配器产生的 `0x1C` 字节 MSG 节点，并由接收端释放 | 只是跨线程转送普通窗口消息；外部进程不能提供合法目标堆节点 |
| 临时消息链表 `0x5CC3B0 / 0x1B42500 / 0x5CC4B0` | 抽取、筛选并重新投递 Win32 MSG | PeekMessage 辅助缓存，不是任务队列 |
| 输入抑制链 `0x5CC630 / 0x1B40818 / 0x5CC6C0` | 暂存键鼠消息并在循环末释放 | 没有 callback 执行能力 |
| WM_COMMAND | `0x5CDA10 -> 0x5CD360 -> 0x5D0D40 -> 0x5D2340` | 应用映射只见 `0xE141 -> 0x5D0020`，没有通用命令 dispatcher |

主窗口消息映射链 `0x604C98 -> 0x625D18 -> 0x6265F8 -> 0x626270` 只包含创建、激活、绘制、关闭、尺寸等框架消息。PE 也没有导出表，未导入 `PostThreadMessage`、`QueueUserAPC` 或 `RegisterWindowMessage` 形成外部任务通道。

## 5. 只读 root 链及第二个阻断

稳定只读链：

```text
app           = 0x01228340
window        = [app + 0x04]
owner         = [window + 0x1C]
scene         = [owner + 0x18]
schedulerRoot = [scene + 0x8C]
leaf          = repeatedly [node + 0x0C]
```

干净原版 PID `19876` 的只读 A/B 快照稳定得到：

```text
schedulerRoot  vptr=0x607560
  -> depth 1   vptr=0x610A24
  -> depth 2   vptr=0x610B80
  -> depth 3   vptr=0x610BC8
```

磁盘构造器 `0x47EB10` 明确给 scheduler root 写 `0x607560`，而 `0x50F400` 才给内政 controller 写 `0x610BC8`；`0x4345C0` 把 `scene+0x8C` 交给 `0x47E840`。因此旧文档把两类对象合称 root/controller 是错误的，现已拆开。

已观察末端 controller 的关键字段为：

- `+0x10` pending/self sentinel；
- `+0x30` corps；
- `+0x34` state；
- `+0x38` target。

观察时 `controller+0x30=0x01253D0C`、`controller+0x34=0x3E9`、`controller+0x38=0`。而 `0x5125A0` 创建命令 task 会读取 controller `+0x30/+0x38`，并对 target 做类型转换。因此即便将来找到主线程投递方法，也还必须闭环“如何通过游戏原生 UI/状态机把指定城市绑定成合法 target”；读取一个当前城市 ID 或裸写 target 都不能替代这个对象契约。

## 6. 决策

当前安全边界下：

- 远程线程直接调用：线程、对象、堆和生命周期均不安全；
- 直接写 root/task：会绕过状态机和引用关系；
- 把游戏事件号当 Win32 消息：没有映射证据；
- 无 Hook、无注入、无进程写入的领域命令提交：不存在已证实入口。

所以正式执行继续 **NO-GO**。只读预览不受影响；方案按钮与提交能力必须保持关闭。

下一轮唯一允许的动态工作，是在关闭所有冲突修改器、干净重启并使用一次性复制存档后，由玩家手动进入一次原生城市内政流程；观察器只读高频记录 scheduler/task 的 `+0x0C/+0x10` 与唯一 `0x610BC8` controller 的 `+0x30/+0x34/+0x38`，并分轮确认 `0x4345B0 / 0x5179B0 / 0x47E6F0 / 0x47E840` 的线程 ID 和生命周期。该轮最多补全对象契约，不自动授权任何调用或提交。
