# 三国志九 PK 1.0.1.0 V7：原生 UI 商业最窄路径静态闭环

状态：**目标哈希磁盘 EXE 中，已经闭合“玩家点城后怎样把目标绑定到 live controller”“菜单灰化怎样调用 CanExecute”“五类选人窗口怎样单选、原生拉满和确认”的静态调用链。结果同时否证了一个关键假设：CanExecute 与真实 `0x005179B0` command dispatch 不是同一 handler 上的原子入口。inner modal 活跃期间的 `top HWND -> permanent CWnd map -> exact type-A selector` accessor 已静态闭合；但对象仍是短命对象，modal generation token、嵌套消息泵的安全分阶段时点和任意城市的原生 target-binding 入口尚未闭合。V7 业务桥仍为 NO-GO。**

本文只使用 `D:\三国志9\10101749\San9PK.exe` 的磁盘字节和离线反汇编；没有打开或读取游戏进程，没有运行 live，没有修改产品源码、游戏文件或游戏状态，也没有使用 Computer Use。

## 1. 版本锁与本轮结论

| 项目 | 精确值 |
|---|---|
| 目标 | `D:\三国志9\10101749\San9PK.exe` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 文件大小 | `2,636,800` bytes |
| PE | x86 / PE32 / ImageBase `0x00400000` |
| SizeOfImage | `0x01759000` |
| 静态工具 | `tools/re/san9_v7_static.py` |

结论分成两层：

| 问题 | 静态结论 | 业务桥结论 |
|---|---|---|
| A. 正常 UI 怎样绑定 `controller+0x38` | **已确认**：可见地图命中坐标经 `0x005135F0` 返回对象，并在 `0x00513915/0x00513A81` 写入 live root；追加的 `0x01232688` 路线已证为 `CForceData` 队列而非 city target | 任意 cityId/current-city 到该字段的可调用入口仍未知；NO-GO |
| B. CanExecute 与 command dispatch 是否有共同原子入口 | **已否证**：灰化使用临时 root + 临时 handler；真实 dispatch 随后另建 handler且不重跑 CanExecute | 不能把“先查后发”当原子事务；NO-GO |
| C. 单人 toggle、原生前缀拉满、accept | **地址和 ABI 已确认**：inner dialog `0x00578090` 的 `0x1D52/0x1D51/0x1D4D`，outer task 的 `0x0BB9`；活跃 inner modal 的 HWND/CWnd accessor 也已静态确认 | modal generation、过期消息和 nested-modal 分阶段调度未动态闭合；NO-GO |

“已确认地址”只表示精确磁盘版本中的局部代码和数据流成立，不表示允许外部调用。

## 2. 最窄商业原生 UI 路线

正常玩家商业路线可以静态还原为：

```text
live controller/root (vptr 0x00610BC8)
  vtable+0x1C 0x00514370(eventId=0x7D1, packedXY, arg3)
    -> 0x00513890(root, packedXY)
       -> idle gate 0x0047E420
       -> 0x005135F0(root, point*) -> target object
       -> root+0x38 = target                         [0x00513915]
       -> target 分类/有效性检查
       -> 当前城市观察量 0x01232474 = city          [0x0051396E]
       -> 0x004C9150(command menu, ..., corps, target)
       -> 0x0041FB00(menu modal)
          -> 临时 root/handler CanExecute 灰化链
       -> root.vtable+0x18(selected event)           [0x00513A96]
          -> 0x005179B0 live dispatch
          -> factory 0x005125A0(commandId=1)
          -> commerce handler 0x004C61F0
          -> 0x0047E6F0(root, handler, 1)
          -> handler tick -> ExecuteUI 0x004C6310
             -> stack-owned commerce task 0x004E6AF0
             -> outer modal 0x0041FB00
             -> inner selector / outer accept
             -> command ctor 0x0048B340
```

这条路径的价值是把“合法绑定”限定在游戏自身的地图命中和菜单生命周期内。它没有给出可以把城市表中的任意 `CITY*` 直接塞给 root 的安全 setter。

## 3. A：target binding 的精确链

### 3.1 native input wrapper

`root vtable 0x00610BC8 + 0x1C = 0x00514370`：

```text
ECX  = live root/controller
arg1 = internal eventId
arg2 = packed coordinate DWORD
arg3 = framework argument，精确语义 unknown
ret  0x0C
```

- `eventId == 0x7D1` 时，把 `arg2` 传给 `0x00513890`；
- `eventId == 0x7D3` 时，把 `arg2` 传给 `0x00513B10`；
- 商业最窄路线使用 `0x7D1 -> 0x00513890`。

`0x00513890` 的 ABI：

```text
ECX  = live root/controller
arg1 = packedXY；low16 / high16 被拆成一对坐标
ret  0x04
```

它先调用 `0x0047E420(root)`，若 `root+0x10 != root` 就退出；随后通过 `0x005135F0(root, point*)` 解析命中的游戏对象。返回对象在两个循环位置写入：

| 写点 | 数据来源 | 作用 |
|---:|---|---|
| `0x00513915` | 第一次 `0x005135F0` 返回值 | `root+0x38 = target` |
| `0x00513A81` | 菜单返回 1 后重新解析 | 重新绑定并回到分类/菜单流程 |

`0x005135F0` 是 `__thiscall(root, point*) -> EAX object-or-null`，`ret 4`。它返回的是命中测试结果，不接受 cityId，也不是任意对象 setter。

### 3.2 command menu 保存的是同一个 corps/target

`0x00513A08` 调用 `0x004C9150` 构造 command menu。该构造器为 `__thiscall`、五个栈参数、`ret 0x14`；前三个参数的完整 UI 语义仍 unknown，但后两个参数已经由存储位置确认：

```text
arg4 = live root+0x30 corps  -> menu+0x2668
arg5 = live root+0x38 target -> menu+0x266C
menu vptr = 0x0060A238
```

菜单 modal 返回真实 command event 后，`0x00513A96` 通过 **同一 live root** 的 `vtable+0x18` 分发，而不是在临时 root 上执行。

### 3.3 `0x01232474` 不是反向绑定入口

精确文件中，`0x01232474` 的直接绝对引用只有四处：

```text
0x0043FF27
0x00451646
0x00460936
0x00513970
```

前三处读取该值；`0x0051396E` 在 `root+0x38` 已经解析并通过城市类型检查后写入它。本轮没有发现 `0x01232474 -> live root+0x38` 的反向赋值入口。

因此：

- **已确认**：visible hit-coordinate → game object → live `root+0x38`；
- **未确认**：cityId/CITY 表/current-city 全局 → live `root+0x38`；
- 直接写 `root+0x38`、构造临时 root 后当 live root 使用、或改写 `0x01232474` 均不是授权方案。

### 3.4 `0x01232688` 是 CForceData 队列，不是 CITY target manager

追加线索 `0x0050EC10 -> ECX=0x01232688 -> 0x0046AAD0` 确实能返回一个“当前对象”，但该对象的静态类型已经精确闭合为 **`CForceData`**：

```text
0x0046AAD0(ECX=0x01232688) -> EAX CForceData* or 0
  require [manager+0x44] != 0        # manager+0x38 typed-list 的 count
  node = [manager+0x3C]              # list head
  object = [node+8]
  object.vtable+0x10()
  0x005DED60(object, 0x00605C2C)     # CRuntimeClass CForceData
```

`0x00605C2C` 指向名字 `CForceData`，对象大小字段为 `0xD4`。`CCityData` 的不同 runtime class 是 `0x00605460`，名字位于 `0x00605928`。这不是把同一泛型对象“按上下文解释成城市”的情况。

三个 helper 的 ABI/行为：

| VA | ABI | 静态行为 |
|---:|---|---|
| `0x0046AAD0` | `__thiscall(manager) -> CForceData* or 0` | 只读 non-empty list 的首 payload，并强制 `CForceData` runtime-class gate |
| `0x0046AB10` | `__thiscall(manager, typedList*)`, `ret 4` | `ECX += 0x38` 后 tail-call `0x0046F200`，整表替换 manager 的 typed list；不是单对象选择 API |
| `0x0046AC10` | `__thiscall(manager) -> next CForceData* or 0` | 从 `manager+0x38` 弹出首项，经 runtime-class gate 后对该 `CForceData` 调 `0x00440D80(object,2,1)`，再返回新的首项；bit `2` 的显示/业务名称仍 unknown |

`0x0046AB10` 在全 `.text` 只有四个 callsite，且四条输入链都先由 `0x0046DF70` 构造 **CForceData typed list**：

| callsite | 上游静态来源 |
|---:|---|
| `0x00457BE8` | load/import 路线；按 `0..49` 调 `0x00453100(id)`，后者返回 `0x01253C38 + id*0xD4` 的 force-table 项 |
| `0x00487E4E` | `vptr 0x006079D0 + 0x30 = 0x00487D20` 的一个分支；复制 `0x012326C0` force list 后追加一个 force object |
| `0x00488299` | 同一个 `0x00487D20` 的另一分支；仍是 CForceData typed list，并按 `this+0x44` force object 做包含/移除处理 |
| `0x0050F041` | `0x0050EC90` state machine 内，由 callback `0x0050EC20` 过滤/排序出的 CForceData list |

`0x0046AC10` 的唯一 callsite 是 `0x0050ECDC`。`0x0046AAD0` 的 20 个 direct callsite 固定为：

```text
0x00413E42  0x00413EE4  0x00437BCD  0x00439BB2  0x0044488A
0x004454EC  0x00450C40  0x00451990  0x0045AF68  0x0045C538
0x0045C63B  0x00463496  0x0046973C  0x004FC9F7  0x0050EDB7
0x00511869  0x00511BA3  0x0051D514  0x005219F9  0x0052444C
```

另有三个已审 tail-jump：`0x0046AC53/0x0046AC67`（advance 的两条返回路径）和 `0x0050EC15`（固定全局 manager 的 getter wrapper）。

真正决定本路线性质的是 controller tick 后半段：

```text
controller vptr 0x00610B80 + 0x0C = 0x0050EC90

state 0x3E9:
  0x0050ECDC -> 0x0046AC10(force manager)
  state = 0x3EA

state 0x3EA:
  0x0050F16E -> 0x0050EC10 -> 0x0046AAD0
  ESI = selected CForceData*
  allocate 0x40
  0x0050F1B6 -> 0x0050F380(newRoot, parent, ESI, context)
     0x004BDF10 stores ESI at root+0x30
     root vptr = 0x00610BC8
     root+0x34 = 0x3E8
     root+0x38 = 0                         # target explicitly remains null
     root+0x3C = separate third context
  0x0050F1F3 -> 0x0047E6F0 attach
```

因此这条链是在给正常 root/controller 绑定 `root+0x30` 的 **force/corps context**，随后再等待独立 target；它没有把 `ESI` 写到 `root+0x38`，也没有 cityId/`CCityData*` 参数。四个 list-replace callsite 的上层分别是加载路线、无 eventId 参数的 force-dialog 虚方法和 force-controller state tick；没有发现“按 CITY 对象或 city ID 原生选中”的上层 event/ABI。结论是明确的：**`0x01232688` 不能补上 blocker A，更不能通过直接改其全局链来伪造城市选择。**

### 3.5 cityId getter 已确认，但它不是 setter

精确 EXE 有一个纯 lookup helper：

```text
0x00452F70(cityId) -> CCityData* or 0
  require 0 <= cityId < 50
  return 0x0124DB58 + cityId * 0x1F0
```

返回对象的 exact vptr 为 `0x00605938`；其首虚函数 `0x0043A2A0` 返回 `CCityData` runtime class `0x00605460`，构造器写点为 `0x0043A2E0`。live root 对 target 的正式类型读取是 `0x0050FCD0`：取 `[root+0x38]` 后按 `0x00605460` 动态转换。

商业 factory 在 `0x00512925` 调 `0x0050FCD0`，随后 `0x0051292F -> 0x004C61F0`；商业 handler vptr 为 `0x00609F38`，构造过程中把验证后的 `CCityData*` 快照到 `handler+0x60`。这些地址证明了 city 指针的 **读取、类型验证和 handler 快照**，仍没有给出一个 native `SetTargetCity(root, city)` 上层入口。

## 4. B：CanExecute 与 dispatch 是两次独立构造

### 4.1 菜单灰化链

command menu 的 `vtable+0xF8 = 0x004C9320`。ABI 为：

```text
ECX  = command menu (vptr 0x0060A238)
arg1 = framework output/context A，精确类型 unknown
arg2 = writable result/context B；下层会写 *arg2 = CanExecute result
arg3 = command event code
ret  0x0C
```

当 `arg3 >= 0` 时，它在自己的栈上构造另一个 root：

```text
0x0050F3C0(tempRoot,
           parent=0,
           corps=menu+0x2668,
           target=menu+0x266C)
  tempRoot vptr = 0x00610BC8
  tempRoot+0x30 = corps
  tempRoot+0x38 = target
```

随后调用：

```text
0x00514300(tempRoot, arg1, arg2, commandEvent)
```

`0x00514300` 为 `__thiscall`、`ret 0x0C`。其命令分支为：

```text
0x0050FC90(tempRoot, &handler, commandEvent)
  commandId = commandEvent - 0x2710
  handler = tempRoot.vtable+0x28(commandId)

0x0047E510(handler)
  handler+0x24 = -1
  handler+0x28 = 0
  tail-call handler.vtable+0x20 CanExecute

*arg2 = CanExecute result
0x0047E770(handler, arg1)  # 失败原因/文本路径；arg1 完整类型 unknown
handler.vtable+0(handler, 1)
0x0050F400(tempRoot)
```

即菜单灰化的 root 和 handler 都是短命查询对象，查询后已析构。

### 4.2 真实 dispatch 另建 handler，且不重跑 CanExecute

`0x005179B0` ABI：

```text
ECX  = live root/controller
arg1 = eventCode
ret  0x04
```

命令块 `0x00517BD9..0x00517C05`：

```text
commandId = eventCode - 0x2710
handler = liveRoot.vtable+0x28(commandId)
if handler:
    0x0047E6F0(liveRoot, handler, 1)
    liveRoot+0x34 = 0x3EA
```

该基本块没有到 `0x0047E510` 的调用，也没有间接调用新 handler 的 `vtable+0x20`。它使用的是 **另一次 factory 构造出的新 handler**。

所以正常 UI 的顺序实际是：

```text
tempRoot/tempHandler.CanExecute -> destroy both
         [modal 等待；状态可以变化]
liveRoot/newHandler dispatch -> attach without handler CanExecute
```

外部把 `0x00514300` 和 `0x005179B0` 顺序调用，也只是复制这段 TOCTOU，不会把它们变成原子入口。command object 后面的第二 validator 仍然保留，但 V3 已证明它不重建全部 UI 候选/人数/来源不变量。因此 B 的最终答案是：**没有找到共同或原子入口；这是明确的 NO-GO，而不是待命名函数。**

## 5. C：商业选人内外两层 UI

### 5.1 outer commerce task 的来源

`handler ExecuteUI 0x004C6310` 建立 `0xF48` 字节栈帧，在 `[esp+8]` 构造商业 task：

```text
0x004E6AF0(task, handler+0x40 source)
task vptr = 0x0060CCB0
task+0x6AC = committed/final list
task+0x6CC = source list
task+0x6EC = working list
0x0041FB00(task)  # outer modal
```

这个 task 是 handler 主线程栈上的短命对象。outer modal 返回后，`0x004E69C0` 立即析构它；它不是可缓存的 heap singleton。

商业 task `vtable+0x28 = 0x004E72D0`，ABI 为：

```text
ECX  = outer selection task
arg1 = eventId
arg2 = framework argument
ret  0x08
```

`eventId == 0x3E8` 时：

1. 取 `source_count = task+0x6D8`；
2. `max = min(5, source_count)`；
3. 保持 `task+0x6CC` 的原生排序进入 type-A selector；
4. inner modal 成功后，把返回选择写入/排序 `task+0x6EC`；
5. 其他 event 交给 `0x004CB4C0`。

### 5.2 inner selector 的对象来源与 ABI

五类 outer task 都调用 `0x00570500`。该 wrapper 把 selector type `0x0A` 加入参数后进入 `0x00570150`。type-A factory 路线是：

```text
0x0056EA20(type=0x0A, ...)
  jump-table case 0x0056ED4D
  allocate 0x5F44 bytes
  ctor 0x0056A230
  dialog vptr = 0x0061F0B8
```

`0x00570150` 只把返回指针保存在自己的局部寄存器/栈上下文中，调用 dialog `vtable+0x80 = 0x0041FB00` 进入 inner modal，返回后再析构。当前静态链没有发现把这个 dialog 指针持久写回 outer task 的字段，也没有持久的 `GetActiveTypeASelector()`；但框架在 **modal 活跃期间** 提供了一条可重新取得 exact 对象的链：

```text
0x005701D0 -> 0x0056EA20             # ESI = exact inner dialog
0x005701FB -> dialog.vtable+0x80
               0x0041FB00 -> 0x005B8480
0x005B8551 -> 0x005B8330
0x005B840F -> 0x005CD650             # native window creation
0x005CD6DE push ESI
0x005CD6DF -> 0x005CD5E0             # pending exact CWnd + WH_CBT setup
CBT hook 0x005CD540
  0x005CD58D push HWND
  0x005CD590 -> 0x005CD480(ECX=CWnd, HWND)
     [CWnd+4] = HWND                  # 0x005CD4A0
     -> 0x005D2C90 permanent-map insert
0x005B846E -> 0x005B7B80             # after successful creation
0x005B7B9E -> 0x005CF050
             -> 0x005CB0E0 -> 0x005CCBA0 -> 0x005CCB10
             # push HWND into tracked modal stack
```

tracked stack 的 count 为 `0x01B41890`，数组为 `0x01B423E0`。两个只在游戏线程、modal 阶段内有意义的 accessor 是：

```text
0x005CA7D0() -> top HWND or 0
0x005CD460(HWND) -> CWnd* or 0       # ret 4；只查 permanent map，不创建 temporary wrapper
```

候选调用方每个 stage 都必须重新执行：

```text
h = 0x005CA7D0()
p = 0x005CD460(h)
require h != 0
require p != 0
require [p]   == 0x0061F0B8
require [p+4] == h
```

这只是 **active-modal accessor**，不是持久句柄、所有权或代际 token。`0x1D4D` 接受后必须立即丢弃 `p/h`；不能跨 callback 缓存，不能用旧 HWND/CWnd 地址补做下一步。若 top 是其他弹窗，vptr 校验必须直接失败。

dialog 的上层 event ABI：

```text
0x00578090 / dialog vtable+0x28
ECX  = live type-A dialog (vptr 0x0061F0B8)
arg1 = eventId
arg2 = framework argument
ret  0x08
```

### 5.3 单人 toggle

可见行 event `0x1CE9..0x1CF7` 在 `0x00578090` 中进入 `0x00572250`：

```text
eventId + dialog+0x194 scroll offset
  -> 取得 dialog+0x154 row record
  -> dialog.vtable+0xF0 = 0x005726C0(rowRecord*)
```

`0x005726C0` 检查：

- dialog 选择模式标志；
- row disabled bit；
- 当前选择数；
- `dialog+0x180` max。

通过后才 toggle row record 的 bit `0x02`。因此单选的可调用上层入口是 dialog `vtable+0x28` 的 row event，不是直接改 row bit、`task+0x6EC` 或链节点。由于行 event 还受滚动偏移影响，`0x1CE9` 不能在未知 scroll 状态下无条件解释为 source 第 0 人。

### 5.4 原生 source 顺序前缀拉满

`0x005727C0` 从 source 链首节点开始顺序遍历，每个节点的 payload 按原顺序追加到 `dialog+0x154` row vector；没有按人物 ID 重排。

dialog event `0x1D51` 在 switch 表 `0x0057827C` 映射到 `0x00578134`，再调用：

```text
dialog.vtable+0x15C = 0x00575A30
```

`0x00575A30` 的精确语义是：

1. 读取已有选择数；
2. `remaining = dialog+0x180 max - selected_count`；
3. 从 row index 0 递增；
4. 跳过已经置 bit `0x02` 的 row；
5. 若 `dialog+0x184` validator 非空，构造“当前选择 + 候选”并调用它；validator 返回 0 时结束本次 prefix-fill；
6. 通过 `0x0056AA30` 置 row 选择状态，直到 remaining 为 0 或 source 结束。

因此产品所需的准确表述是：**按原生 source 顺序做受原生组合 validator 约束的前缀拉满，上限 `min(5, source_count)`。** 它通常对应“前五”，但静态契约不能承诺 validator 失败时仍恰好五人。

五类 validator callback：

| 命令 | callback |
|---|---:|
| 巡察 | `0x004D87F0` |
| 商业 | `0x004DADC0` |
| 开垦 | `0x004DADC0` |
| 修筑 | `0x004DADC0` |
| 训练 | `0` |

### 5.5 clear 与 inner accept

dialog event 表还给出两个独立按钮事件：

| event | switch block | virtual target | 静态行为 |
|---:|---:|---:|---|
| `0x1D52` | `0x00578148` | `vtable+0x160 = 0x00573170` | 顺序把全部 row state 清零并刷新 |
| `0x1D51` | `0x00578134` | `vtable+0x15C = 0x00575A30` | 原生前缀拉满 |
| `0x1D4D` | `0x005780AE` | `vtable+0x84 = 0x00577170` | 接受 inner selector |

`0x00573170` 对 `dialog+0x154` 的全部 row 逐项调用 `0x00572550(this,index,0,0)`；后者 tail-call `0x0056AA30`，把整条 row state DWORD 置零，然后刷新最多 15 个行控件以及相应虚槽。因此 `0x1D52` 是完整 clear/reset，不只是清 bit `0x02`。

没有在这张 event 表中发现一个同时执行 clear、prefix-fill 和 accept 的单一 event。全 `.text` 中，虚槽 `+0x15C` 的精确调用点只有 `0x00578138`，虚槽 `+0x160` 的精确调用点只有 `0x0057814C`；`0x00575A30/0x00573170/0x00577170` 也没有 direct-rel32 caller，只有各 selector vtable 表项。`0x1D4D` 返回 inner modal 后，`0x00570150` 才通过原生 `0x00572810` 路线把 dialog 选择提取到 caller 提供的 working list。

已经找到比 hook 内连续 direct-thiscall 更高一层的原生消息 envelope：selector 自己的键盘路径在 `0x00577AEB` 和 `0x00577B64` 分别调用 `0x005CF150`，向 `[dialog+4]` HWND 发送：

```text
message = WM_COMMAND (0x0111)
wParam  = 0x1D51 or 0x1D52
lParam  = 0
0x005CF150 -> USER32.SendMessageA
```

同一框架还提供 `0x005CF180`。特殊 HWND 分支直接进入 `USER32.PostMessageA`；普通 dialog 分支则 tail-call `0x005CA9F0`，分配一个 `0x1C` 字节 envelope，依次保存 `{target HWND, message, wParam, lParam}`，再执行：

```text
PostMessageA(mainHWND=0x01B40800,
             message=0x0601,
             wParam=targetHWND,
             lParam=envelope*)
```

发送失败会释放 envelope。未来可把这条框架 packet queue 作为“每次 callback 只排一个原生 `WM_COMMAND`，返回消息泵后再处理”的分阶段候选。这里仅有 `0x1D51/0x1D52` 的实际发送点；把 `0x1D4D` 以相同 envelope 排队，是由同一 `WM_COMMAND` event dispatcher 得出的候选推论，尚需 clean live ping 验证，不能写成已证 ABI，更不是单一 auto-select-and-accept 入口。

### 5.6 outer accept 必须走 task event

inner modal 返回并完成 `task+0x6EC` 后，outer task 仍处于自己的 modal。正确的上层 UI event 是：

```text
task.vtable+0x28(task, eventId=0x0BB9, arg2)
  -> 0x004CB4C0
  -> task.vtable+0x84 = 0x004E6C80
     clear task+0x6AC
     copy task+0x6EC -> task+0x6AC (limit 0x7FFFFFFF)
     -> 0x004CB480
        task+0x680 = 1
```

`0x004E6C80` 的 ABI 虽然是 `__thiscall(task)`，但它只是 virtual accept 实现。未来若能证明调用时点，也应从 `task.vtable+0x28(event=0x0BB9)` 进入，而不是直接调用 `0x004E6C80` 或自行写三条人物链。

## 6. 五类 task 的同构边界

| 命令 | task vptr | vtable+0x28 event handler | selector call | vtable+0x84 |
|---|---:|---:|---:|---:|
| 巡察 | `0x0060B920` | `0x004D8BA0` | `0x004D8C1A -> 0x00570500` | `0x004E6C80` |
| 商业 | `0x0060CCB0` | `0x004E72D0` | `0x004E734A -> 0x00570500` | `0x004E6C80` |
| 开垦 | `0x0060BBA0` | `0x004DA670` | `0x004DA6EA -> 0x00570500` | `0x004E6C80` |
| 修筑 | `0x0060BCE8` | `0x004DB170` | `0x004DB1EA -> 0x00570500` | `0x004E6C80` |
| 训练 | `0x0060C370` | `0x004DF8E0`（内部 `0x004DF880`） | `0x004DF8C1 -> 0x00570500` | `0x004E6C80` |

五类共同点：

- `event 0x3E8` 进入 type-A selector；
- `max=min(5, source_count)`；
- source/working/committed 偏移同为 `+0x6CC/+0x6EC/+0x6AC`；
- inner selector vptr 同为 `0x0061F0B8`；
- outer `event 0x0BB9` 共用 `0x004E6C80` accept。

差异仍由各自 source 排序、validator callback、显示配置和 handler CanExecute 决定，不能只凭共同 vptr 抹掉。

## 7. 唯一候选的 nested-modal 集成路线

在“不裸写业务结构、不模拟键鼠、不直接 apply”的限制下，本轮只剩一条值得后续动态证伪的候选：**业务从已认证 main-idle dispatch 进入后，利用 inner/outer modal 自己的消息泵分阶段调用原生 UI event。**

候选时序：

```text
outermost authenticated idle command
  -> live root normal command dispatch
  -> handler ExecuteUI
  -> stack-owned outer task enters 0x41FB00
  -> event 0x3E8 synchronously enters inner selector modal

thread-specific WH_GETMESSAGE callback in nested modal pump
  stage 1: h = 0x5CA7D0(); p = 0x5CD460(h)
           require p && [p]==0x0061F0B8 && [p+4]==h
           queue one WM_COMMAND 0x1D52 through 0x5CF180 # native clear
  stage 2: reacquire h/p and validate fresh context
           read-only verify all row state reset
           queue one WM_COMMAND 0x1D51 through 0x5CF180 # native prefix fill
  stage 3: reacquire h/p and validate fresh context
           read-only verify selected row/person IDs and count
           queue one candidate WM_COMMAND 0x1D4D        # inner accept

after inner modal returns
  -> native extraction/sort fills outer task+0x6EC
  -> top HWND/CWnd must now reacquire as exact expected outer-task vptr
  -> read-only verify exact working IDs
  -> later outer-modal stage: task event 0x0BB9 # outer accept; envelope still needs ping
  -> handler constructs command
  -> existing command validator/state machine
```

这个路线不是当前 GO：

- V5 只证明游戏线程已有 `WH_GETMESSAGE` 链和 hook bootstrap 候选；没有证明在 hook callback 内调用游戏虚函数不会重入正在分发的 UI 对象；
- `0x005CA7D0 -> 0x005CD460` 已静态闭合 active-modal 的 top HWND → permanent CWnd，但没有 modal generation token，也未证明 hook stage 观察到的 top 一定不会在排队后变化；
- outer task 是 handler 栈对象；inner dialog accept 后会马上返回并析构，任何晚到 callback 都可能使用已释放对象；
- modal message 的准确先后、同一消息重入、hook 链顺序和 callback in-flight 停机都未动态测量；
- `0x1D52`、`0x1D51`、`0x1D4D` 是三个独立 event，没有找到更高层的原子 “auto-select-and-accept” queue/入口；`0x1D4D` 的 `PostMessage` envelope 仍是待 ping 的推论；
- 一个 idle 请求同步进入 modal 后，原 outer idle 尚未返回；现有 V5.1 重入守卫会怎样与 nested modal hook 协作尚未验证。

因此当前只能把它列为 **条件 NO-GO 的唯一候选**。若后续验证，hook 每次 callback 至多推进一个 stage，所有 stage 都必须重新验证 process/session、thread、top HWND、CWnd/dialog vptr、对象代、source digest、target/corps/root token 和上一 stage 序号。任何不一致进入 `AbortUncertain`，不得重试提交。

明确禁止的替代方案：

- 直接写 `task+0x6EC/+0x6AC` 或节点；
- 直接写 dialog row bit；
- 用 V6 的全堆扫描命中当作可调用对象 getter；
- 直接调用 `0x004E6C80`；
- 直接构造 command/global selected list；
- 发送键鼠输入或伪造按键/鼠标消息；在 live 门闭合前发送任何 `WM_COMMAND`；绕过原生 validator。

### 7.1 没有 native setter 时的两个备选，仅作风险评估

下面两条都涉及写入/调用，**当前均未授权，也没有运行 live**。这里只回答“若后续单独授权，哪条更窄以及必须先闭合什么”。优先级仍是继续寻找原生 setter。

#### 备选一：idle live root 的 transient `+0x38`

候选是在同一个认证过的 main-idle callback 内，把唯一的 `CCityData*` 临时放到 live `root+0x38`，走正常 root factory/dispatch，确认 handler 已同步快照 city 后有条件地归零。它比新建 attached root 少一层 heap/所有权状态机，但仍是裸写 live 业务字段。

必须同时满足的 pre-invariants：

1. 精确 EXE/SHA、clean conflict gate、PID 创建代、session nonce、main thread 与 root generation 全部不变；
2. exact live root 可追溯，`[root]==0x00610BC8`，`0x0047E420(root)` 为 idle，`root+0x10==root`；`root+0x34` 必须与同一进程代刚取得的只读 idle 基线一致（当前干净动态样本为 `0x3E9`，`0x3E8` 只是构造器初始值，不能冒充 live idle 常量）；
3. `root+0x38==0`，绝不覆盖游戏已有 target；`root+0x30` 是当前玩家可控的 exact `CForceData*`；
4. `cityId` 唯一，`0x00452F70(cityId)` 非空，`[city]==0x00605938`，runtime class 为 `0x00605460`；城市归属、军团委任、source/corps/context digest 与请求 token 一致；
5. 同一 session 只有一个 pending command，没有 modal/hook callback in-flight，没有上一条不确定提交；
6. native CanExecute 必须对同一 `{root, force, city, command, context}` 快照返回 true，并在 dispatch 前重验全部字段。B 已证明 query/dispatch 不原子，所以这只是必要门，不是充分保证。

候选操作/rollback 约束：

```text
snapshot root/session/context
require root+0x38 == 0
root+0x38 = exactCity                  # 唯一候选业务写
native query + revalidate
normal live-root dispatch
require expected handler attached
require handler vptr/command/city snapshot exact
finally:
  if same root/session generation && root+0x38 == exactCity:
      root+0x38 = 0
  else:
      do not overwrite; AbortUncertain
```

不能在字段已被原生逻辑改成其他值时“回滚为 0”，否则会覆盖新的合法 target。dispatch 返回不等于命令完成；必须继续验证 `root+0x10` 的 expected handler、`root+0x34` 状态、商业 handler `+0x60==city`、后续 task/command 结果，并以 single-sequence、no-retry 处理任何 attach 后异常。还需 copied-save ping 证明 factory 快照发生在归零前、归零不会影响 handler 后续 ExecuteUI、SEH/finally 不会留下悬挂 target。以上任一门未闭合即 NO-GO。

#### 备选二：heap `0x0050F3C0` root 再 attach

`0x0050F3C0(newRoot,parent,force,target)` 的静态效果是：base `0x004BDF10` 保存 `force`，设置 vptr `0x00610BC8`、state `0x3E8`、`root+0x38=target`、`root+0x3C=0`。单独把它作为菜单 query 的临时 root 并析构，是原生已有模式；但要让它真正执行，就还需把这个 heap root 接进 live owner/tick 链，这不是菜单 query 已证明的行为。

这条方案新增未知：game allocator/deallocator 配对、parent 参数、`0x0047E6F0` 的所有权转移、parent 原 handler 替换、root tick 注册、完成/取消时由谁析构、callback in-flight、detach 顺序、失败后的 double-free/leak。attach 后不能由外部“直接 free 回滚”，只能由已证明的 owner 生命周期回收；目前没有这份契约。

风险排序：**transient live `root+0x38` 较低，attached heap root 明显更高；但两者当前都是 NO-GO。** 前者只在未来单独授权写入、先做 clean live 单命令 ping、再做 copied-save 人工验证后才可能升级；后者在完整 owner/destructor/detach 契约闭合前不应进入动态阶段。

## 8. 仍属 unknown 的执行契约

| unknown | 为什么仍阻断 |
|---|---|
| stable live root getter + scene generation | vptr 扫描或旧地址不能提供所有权和代际 |
| 任意 cityId 到合法 native hit target | 当前只闭合可见 UI 坐标命中；多城遍历尚无入口 |
| `0x01232688` 作为 city selector | 已静态否证：payload/typed list/root 写入均为 `CForceData -> root+0x30`，`root+0x38` 明确置零 |
| native city target setter | `0x00452F70` 只 lookup；`0x0050FCD0` 只从 root 读取/动态转换，尚无上层 setter |
| 原子 CanExecute + dispatch | 已确认正常 UI 本身是两次 handler 构造；需另加 context snapshot/二次门 |
| active selector 的 modal generation/token | top HWND → permanent CWnd → exact vptr 已闭合，但没有“这是本请求第 N 个 modal”的代际证明 |
| nested modal 的安全调用时点 | WH_GETMESSAGE callback 可能位于 UI 分发栈中，重入规则未知 |
| outer task 的安全 stage/token | inner 返回后可候选地用同一 top-HWND map 重取并校验五类 task vptr，但过渡时点和本请求绑定未动态证明 |
| clear/max/accept 的跨 callback 原子性 | 三个 event 之间状态可变，必须逐 stage 验证和不可重放 |
| cancel/窗口关闭/进程退出竞态 | 任一对象可能先析构；必须 fail-closed，不得用旧地址补做 |

## 9. GO / NO-GO

| 阶段 | 结论 |
|---|---|
| 精确 EXE 的离线 SHA/PE/静态锚点复核 | **GO** |
| 把 A/B/C 地址写入永久非授权 DTO/文档 | **GO** |
| 用户通过游戏正常 UI 点城、选择、确认 | 游戏原生行为，不属于自动桥 |
| 外部直接调用 `0x00514370/0x00513890` 绑定任意城市 | **NO-GO** |
| 把 `0x01232688/0x0046AB10` 当 CITY 选择器 | **NO-GO**：它是 CForceData 队列，只建立 root+0x30 force context |
| `0x00452F70(cityId)` + exact city vptr 的静态事实 | **GO（静态事实）**：只证明 lookup/type，不授权写 root |
| `0x00514300` 后紧接 `0x005179B0` | **NO-GO**：不同 handler、TOCTOU |
| inner dialog 三 event 的静态 ABI | **GO（静态事实）** |
| active modal `0x5CA7D0 -> 0x5CD460` accessor 静态链 | **GO（静态事实）**：仅当前 top modal，绝非持久授权 |
| `0x5CF180` 分阶段排 native `WM_COMMAND` | **条件 NO-GO**：需独立 clean live ping，尤其是 `0x1D4D` envelope、过期消息和生命周期 |
| WH_GETMESSAGE nested-modal 分阶段调用 | **条件 NO-GO**：需独立 clean live ping/重入/生命周期验证 |
| transient live `root+0x38` 备选 | **NO-GO**：两种无 setter 方案中风险较低，但 pre/post/conditional rollback 尚未 live 证明 |
| heap `0x0050F3C0` root + attach | **NO-GO（更高风险）**：owner/tick/detach/destructor 契约未闭合 |
| copied-save 人工验证 | 只可在上述动态门闭合后另行授权 |
| 直接写人物链、row bit、root target、command/apply | **禁止** |
| 当前 V7 正式业务桥 | **NO-GO** |

## 10. 离线复核

```powershell
py -3 -m py_compile tools\re\san9_v7_static.py
py -3 tools\re\san9_v7_static.py
py -3 tools\re\san9_v7_static.py --json
```

2026-08-07 本轮离线结果：`173/173` 静态检查通过，`all_static_anchors_ok=true`；同时固定输出：

```text
business_bridge_go=false
business_execution_authorized=false
process_accessed=false
live_mode_present=false
can_execute_and_dispatch_atomic_entry_found=false
force_selection_manager_route_static_confirmed=true
force_selection_manager_payload_type=CForceData
force_selection_manager_is_city_target_route=false
city_by_id_and_exact_vptr_static_confirmed=true
native_city_target_setter_found=false
transient_root_target_write_authorized=false
attached_heap_root_authorized=false
active_modal_selector_accessor_static_confirmed=true
postmessage_staging_candidate_static_confirmed=true
persistent_selector_object_accessor_found=false
stable_selector_object_accessor_found=false
```

这里的 `stable_selector_object_accessor_found=false` 特指“可缓存、带所有权/代际的持久 accessor”仍不存在，不否认已经闭合的 active-modal 临时重取链。工具没有 `--pid` 或任何 live 开关；SHA 不符时在解释版本专属 VA 前 fail-closed。它只向 stdout 输出，不写日志、缓存或其他文件。
