# 三国志九 PK 1.0.1.0：V3-execute 原生命令生命周期与安全执行边界

状态：**目标哈希磁盘 EXE 中，已经静态追到五类内政从 root/controller 工厂、handler、确认窗口、0x40 字节 command object、第二 validator，到若干直接状态写入的路径。这个结果只是地址和局部数据流证据，不等于完整副作用闭环、线程契约或安全执行 ABI。`0x0047E6F0` 的已检查路径包含父子 task 关系写入；在已检查路径中没有识别出持久内政订单入队，但不能据此声称该函数只有这一项效果或全程序不存在其他队列。当前执行结论为 NO-GO。**

本文基于目标磁盘 EXE、San9PKHard 公开源码交叉和既有进程的只读数据快照。没有使用 Computer Use，没有向 PID `22348` 写入任何字节，也没有在已加载 `Easy.dll` 的进程中执行游戏函数。当前进程的代码段可能已被 Easy 修改；所有代码结论以目标磁盘 EXE 为准。本文允许的后续运行时取证也仅限：**用户通过游戏正常 UI 操作，观察者使用只读内存读取与最多四个硬件断点记录状态**。自动调用游戏函数、远程线程、写内存、改代码、DLL/代码注入均为 NO-GO。

## 1. 版本锁与证据等级

| 项目 | 值 |
|---|---|
| 目标文件 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0` |
| 架构 / ImageBase | x86 PE32 / `0x00400000` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 公开源码 | San9PKHard commit `47861295df22dbdaef92449acaacbdc9619f4529` |
| 当前只读样本 | PID `22348`，已加载 `Easy.dll`，仅使用数据结构 RPM 证据 |

等级定义：

- **confirmed**：目标哈希磁盘 EXE 的入口、虚表槽、调用目标或列明的局部数据流已经独立反汇编核对；只对明确列出的静态事实成立，不自动外推完整函数副作用。
- **candidate**：代码位置或对象关系存在，但线程、所有权、阶段或完整业务语义还缺干净动态对照。
- **unknown**：没有闭环，不能用作执行契约。

这里的“confirmed 地址”不等于“允许外部调用”。对老 32 位游戏而言，有效 `this`、线程亲和性、父子 task 所有权和全局事务状态同样是 ABI 的一部分。

## 2. 已观察链路总览

原生玩家路径不是“构造一个订单并放进长期队列”，而是两层短命 task：

```text
玩家场景 root/controller
  vtable+0x18  0x5179B0(event)
  event = 0x2710 + commandId
  vtable+0x28  0x5125A0(commandId) -> handler
  0x47E6F0(root, handler, ref=1)      [短命子 task 挂接]

handler tick 0x4C5280
  -> 0x50EAB0
  -> handler vtable+0x28 Execute      [五条已检查路径进入原生多选/确认 UI]
  -> 返回 0x40-byte command object，或取消返回 0
  -> 0x47E510(command)
       清 command+0x24/+0x28
       tail-call command vtable+0x20  [第二次原生 validator]
  -> validator 成功：0x47E6F0(handler, command, ref=1)
  -> validator 失败：立即调用 command vtable+0 析构

command tick 0x485D20
  0x3E8 -> 0x3E9 -> 0x3EA -> 0x3EB -> 0x3EC
  -> 0x485A10 -> command vtable+0x30 apply
  -> 直接改 city / corps / person 字段
  -> state=2，随后由父 task 完成/卸载
```

**边界结论**：在本轮检查的 `0x0047E6F0` 函数体中，可见行为包括处理 child 关系/引用并把 child 写进 `parent+0x10`。这足以否定把该调用点直接命名为 `enqueueDomesticOrder`，但“唯一域无关效果”或“全程序没有其他持久队列”都超出证据。五类 command 的 `+0x30` 是本轮所追状态路径中最早观察到直接业务字段写入的派生方法；是否还有未追到的前置、旁路或异步副作用保持 unknown。

## 3. root/controller：工厂、上下文与取得方式

### 3.1 虚表与字段

root/controller 构造器和虚表为 **confirmed**：

| 项目 | 地址 / 偏移 | 静态语义 |
|---|---:|---|
| root 构造器 A | `0x0050F380` | 写 vptr `0x00610BC8`；`+0x38=0`，第三参数写 `+0x3C` |
| root 构造器 B | `0x0050F3C0` | 同一 vptr；第三参数写 `+0x38`，`+0x3C=0` |
| root vtable `+0x18` | `0x005179B0` | 场景事件分发；识别 `0x2710..0x2756` 命令事件 |
| root vtable `+0x28` | `0x005125A0` | 按 commandId 创建 handler 的工厂 |
| root `+0x10` | task child/current | 等于自身表示空闲；不同于自身表示已有活动子 task |
| root `+0x30` | corps 指针 | 工厂传给五类 handler；V1 已确认的军团结构 |
| root `+0x34` | task/scene state | 初始 `0x3E8`；命令 handler 挂接后写 `0x3EA` |
| root `+0x38` | 当前目标 context | 城市/设施对象；可以为 0，不是永久“当前城市” |
| root `+0x3C` | 构造变体状态 | 精确业务枚举 candidate |

工厂取得目标的方式也已确认：

- 巡察、商业、开垦调用 `0x0050FCD0(root)`，把 `root+0x38` 动态转换为类型标识 `0x00605460`。
- 修筑、训练调用 `0x0050FCB0(root)`，把同一字段转换为更一般的设施类型 `0x00605268`。
- 五类都直接读取 `root+0x30` 作为 corps。

这给出了“**已有有效 root 时**”的 current corps / current target 取得契约；它没有给出一个可在任意时刻调用的 root singleton。

### 3.2 尚未识别稳定的全局 root getter

静态 xref 显示 root 既会在栈上临时构造，也会由上层 task 分配后作为 child 挂接。`0x0050F430` 返回的是另一类主界面对象，调用其 vtable `+0xA8`；它不是 `0x00610BC8` root 的稳定 getter，不能拿来替代。

当前 PID 的只读扫描只找到一个 vptr 为 `0x00610BC8` 的对象 `0x1BE44C50`：

```text
+0x08 parent = 0x09B49038
+0x0C child  = 0
+0x10 self   = 0x1BE44C50
+0x30 corps  = 0x01253C38
+0x34 state  = 0x3E9
+0x38 target = 0
+0x3C        = 1
```

该样本只证明布局吻合；`root+0x38=0` 反而证明“扫描到 root 后直接复用当前选中城”不成立。对象地址会随场景和分配变化，严禁硬编码。

### 3.3 `0x01232474` 不是执行上下文替代品

`0x0051396E` 在场景选择流程中，只有当 `root+0x38` 可转换为相应城市类型时，才把城市指针写入 `0x01232474`。San9PKHard 源码也把它注释为“当前操作都设”。它适合作为 UI 当前城市的只读观察量，但：

- 它可以为 0、过期或与当前短命 root 生命周期不同步；
- 它不提供 root、父 task 或 corps 所有权；
- 多城市一键内政不能靠依次改写这个全局指针来切城。

所以当前只读 target city 来源仍是 V1 城市表枚举。至于可写执行所需的 root/handler 取得、生命周期和线程契约，本轮没有成立；本文不授权据此设计调用入口。

## 4. `0x5125A0` 工厂与五类 handler

### 4.1 工厂 ABI

`0x005125A0`（confirmed）：

```text
ECX = root/controller this
arg1 = commandId
合法范围先检查 0 <= id < 0x47，再限制 id <= 0x34
jump table = 0x00513128
返回 EAX = 新 handler；分配/ID 失败返回 0
```

目标 root vtable `0x00610BC8 + 0x28` 槽值为该入口；构造器在 `0x0050F398` 写入该 vptr。本轮没有据此证明所有同类对象或其他 vtable 都不存在工厂入口。

### 4.2 五类映射

| ID | 命令 | factory case | handler ctor | handler vptr | `+0x20` CanExecute | `+0x28` Execute UI |
|---:|---|---:|---:|---:|---:|---:|
| 0 | 巡察 | `0x005128B8` | `0x004C1720` | `0x00609B90` | `0x004C1930` | `0x004C1840` |
| 1 | 商业 | `0x00512900` | `0x004C61F0` | `0x00609F38` | `0x004C6400` | `0x004C6310` |
| 2 | 开垦 | `0x00512948` | `0x004C1D40` | `0x00609BF0` | `0x004C1F50` | `0x004C1E60` |
| 3 | 修筑 | `0x00512990` | `0x004C2060` | `0x00609C20` | `0x004C2270` | `0x004C2180` |
| 5 | 训练 | `0x00512A20` | `0x004C35F0` | `0x00609CE0` | `0x004C3890` | `0x004C37A0` |

五类 factory case 均分配 `0x64` 字节 handler，并传入：

```text
arg1 = root
arg2 = root+0x30 corps
arg3 = 50FCD0/50FCB0(root+0x38) target
```

构造链 `derived ctor -> 0x4C5180 -> 0x50E940` 把 commandId 写 `handler+0x34`、corps 写 `handler+0x38`；derived ctor 把 target 写 `handler+0x60`，并在 `handler+0x40` 构造候选链容器。

### 4.3 共同玩家控制门的已检查分支

V2 中 `0x004C52C0` 的业务语义现在可以升级为 confirmed：

```text
0x4C52C0
  -> 0x50E990：确认 handler+0x38 corps 非空/有效
  -> call [corps.vtable+0x40]

corps vptr 0x00605C50
  +0x40 = 0x0043F9F0

0x43F9F0:
  eax = [corps+0x34]
  eax &= 1
  return eax
```

结合 V1 已确认的 `corps+0x34 bit0` 玩家控制标志，`0x4C52C0` 的列明分支等价于“corps 通过 `0x50E990` 检查，且玩家控制 bit0 为 1”。委任子军团 bit0 为 0 时该分支返回失败，符合产品的直属控制过滤；`0x50E990/0x405840` 所属完整 RTTI/生命周期契约仍不外推。

## 5. CanExecute 与 Execute 并非一个原子调用

需要区分两套 `+0x20`：

1. **handler vtable `+0x20`** 是 V2 已定位的菜单灰化/CanExecute；它构造 ready 候选链并检查资金、城市状态和上限。
2. **command vtable `+0x20`** 是确认 UI 返回后由 `0x47E510` 调用的第二 validator。

`0x5179B0` 的命令事件分支在工厂返回 handler 后，直接调用 `0x47E6F0(root, handler, 1)` 并把 `root+0x34` 写成 `0x3EA`；这段代码**没有再次调用 handler CanExecute**。正常玩家路径依靠菜单灰化阻止无效事件，随后再由 command validator 防止确认期间状态变化。

因此尚未发现一个可以安全命名为：

```text
handler.CanExecute();
handler.Execute();
```

的单一事务入口。静态证据只说明派发 event 不能被当作“会自动重跑灰化门”的安全封装；由于当前自动执行整体为 NO-GO，本文不把这段顺序转写为可调用方案。

## 6. handler Execute：已检查的五条路径均进入 UI 并构造 command

五个 `handler vtable+0x28` 共享以下已观察骨架：

1. 在栈上构造约 `0xE28/0xF48` 字节的任务选择窗口对象。
2. 候选来源是 `handler+0x40`。
3. 调 `0x0041FB00` 进入原生模态窗口。
4. 返回值不是 1 时返回 `EAX=0`。
5. 确认后分配恰好 `0x40` 字节，传 `handler` 和 UI 最终选择链构造 command object。

| 命令 | modal call | command ctor call | command ctor | command vptr |
|---|---:|---:|---:|---:|
| 巡察 | `0x004C187A` | `0x004C18D5` | `0x00488410` | `0x00607A80` |
| 商业 | `0x004C634A` | `0x004C63A5` | `0x0048B340` | `0x00607D98` |
| 开垦 | `0x004C1E9A` | `0x004C1EF5` | `0x00488B00` | `0x00607B30` |
| 修筑 | `0x004C21BA` | `0x004C2215` | `0x00489080` | `0x00607B88` |
| 训练 | `0x004C37DA` | `0x004C3835` | `0x00489D80` | `0x00607C38` |

所以这五个已检查的 `handler+0x28` 入口不能被标注为“一键无界面 Execute”。本轮也没有识别或授权其他 headless 构造/调度入口。

## 7. command object：全局选择事务、validator 与所有权

### 7.1 0x40 字节对象不持有人员链

五个构造器都进入：

```text
derived ctor -> 0x48A320 -> 0x487100 -> 0x485B20
```

`0x485B20` 的 confirmed 行为：

- 初始化通用 task base；
- 清空全局链容器 `0x015455AC`；
- 把传入选择链复制到 `0x015455AC`；
- 写 `command+0x30 = 0x3E8`；
- 写 `command+0x34 = commandId`；
- 写 `command+0x38 = 0`；
- 写 `command+0x3C = 2`。

全局容器关键布局：

```text
0x015455AC  list object base
0x015455B0  +0x04 head/node pointer
0x015455B8  +0x0C count
```

选择人员不在 0x40 字节 command 内。构造第二个 command 会覆盖第一个尚未 apply 的选择链，这是明确的**单事务、不可重入**约束。

### 7.2 目标由“第一名已选武将”反推

`0x485E50` 从全局链首节点取得第一个 person；随后：

| helper | 数据流 | 用途 |
|---:|---|---|
| `0x00485F10` | first person -> person vtable `+0x34` | 取得 corps/资金上下文 |
| `0x00485F40` | first person -> `0x0044F460` | 取得城市目标，巡察/商业/开垦使用 |
| `0x00485F70` | first person -> `0x0044C080` | 取得一般设施目标，修筑/训练及共同门使用 |

所以“预选链第一项”不只是显示顺序，它决定整个 command 的目标城市/设施和资金来源。

### 7.3 `0x47E510` 与失败路径

`0x0047E510(command)`（confirmed）：

```text
command+0x24 = -1
command+0x28 = 0
tail-call [command.vtable+0x20]
return validator bool
```

`0x0050EAB0` 在 Execute 返回 command 后立即调用它：

- 成功：`0x47E6F0(handler, command, ref=1)`，写 `handler+0x10=command`，并通过 `0x47E530` 增加 child 引用计数。
- 失败：调用 command vtable `+0`，参数 1，立即析构释放；handler 进入失败/完成状态。
- Execute 返回 0：不创建 child，handler 结束。

`0x47E6F0` 不检查 parent 是否已有另一个 child，也不销毁被覆盖的旧 child；正常调用者依靠上层状态机保证空闲。外部并发调用可能造成旧 task 丢失、引用计数泄漏或崩溃。

### 7.4 第二 validator 的已观察覆盖范围

五类 `command vtable+0x20`：

| 命令 | validator | 主要复核 |
|---|---:|---|
| 巡察 | `0x00488580` | common validator、城市有效、共同状态、非空、资金、`+1E0 b3`、巡察 `<1000` |
| 商业 | `0x0048B4B0` | 同上；`+1E0 b4`、商业 `<上限` |
| 开垦 | `0x00488C70` | 同上；`+1E0 b5`、开垦 `<上限` |
| 修筑 | `0x004890E0` | common validator、设施状态、非空、资金、`+3E b0`、耐久 `<上限` |
| 训练 | `0x00489F10` | common validator、设施状态、非空、兵力 `>0`、`+3E b1`、士气 `<100` |

共同 validator `0x0048AB30` 会：

- 确认全局选择链非空；
- 通过第一名 person 解析设施并检查共同可用状态；
- 遍历选择链，拒绝任何 `person+0xE8 bit12` 已置位的人。

但它**没有**在已确认窗口内重新检查：

- 人数 `<=5`；
- 所有人都来自同一城市；
- 所有人都是 `handler+0x40` 原生候选链成员；
- 选择链第一项是否与外部宣称 target 一致。

这些不变量由正常 UI 构造候选链和 `max=5` 保证。因而“自己塞五人链后调用原生 validator”仍不等于完整安全提交。

## 8. command tick 与首次观察到的直接业务写入点

五个 command vtable 具有相同骨架：

| 槽 | 公共/派生实现 | 语义 |
|---:|---:|---|
| `+0x0C` | `0x00485D20` | task 状态机 tick |
| `+0x20` | 五个派生 validator | 挂接前的第二校验；不是完整 UI 不变量校验 |
| `+0x30` | 五个派生 apply | 本轮观察到的直接业务字段写入入口 |
| `+0x34` | `0x0045DE60` | 恒返回 1 |
| `+0x38` | `0x004024D0` | 恒返回 0 |
| `+0x3C/+0x40` | `0x0059A850` | no-op |

对 `command+0x3C=2` 的五类内政对象，`0x485D20` 的确定性路径为：

| 当前 `command+0x30` | 动作 | 下一状态 |
|---:|---|---:|
| `0x3E8` | mode 2 分支 | `0x3E9` |
| `0x3E9` | `vptr+0x34` 返回 1；`vptr+0x3C` no-op | `0x3EA` |
| `0x3EA` | `vptr+0x38` 返回 0 | `0x3EB` |
| `0x3EB` | `vptr+0x40` no-op | `0x3EC` |
| `0x3EC` | `0x485A10 -> vptr+0x30 apply` | `2` |

在所追踪的这条状态路径内，构造器和 `0x47E510` 没有出现表中业务字段写入；首次观察到这些直接写入的是最后一行的派生 apply。这不证明构造器/validator 没有其他未列出的全局、分配器或表现层副作用。

## 9. 五类 apply 的已观察直接写入

| 命令 | command apply | 城市数值写入 | 武将 busy | 扣资金 | 城市占用/订单位 |
|---|---:|---|---:|---|---|
| 巡察 | `0x00488690` | `0x0043A480`，饱和增加 `city+0x1C4` | `0x0048893F -> 0x486150` | `0x00488949 -> 0x488450`；`0x00488958 -> 0x43D8D0` | `0x0048896C -> 0x43AC00(mask=0x08,set=1)`，即 `city+0x1E0 b3` |
| 商业 | `0x0048B5D0` | `0x0043A500`，饱和增加 `city+0x1CC` | `0x0048B861 -> 0x486150` | `0x0048B86B`；`0x0048B878` | `0x0048B883 -> 0x43AC00(mask=0x10,set=1)`，即 `b4` |
| 开垦 | `0x00488D90` | `0x0043A570`，饱和增加 `city+0x1D0` | `0x00489027 -> 0x486150` | `0x00489031`；`0x0048903E` | `0x00489049 -> 0x43AC00(mask=0x20,set=1)`，即 `b5` |
| 修筑 | `0x00489310` | `0x00435670`，饱和增加 `city+0x3C` 耐久 | `0x0048956E -> 0x486150` | `0x00489578`；`0x00489583` | `0x0048958E -> 0x435CD0(mask=1,set=1)`，即 `city+0x3E b0` |
| 训练 | `0x00489FF0` | `0x0045DDF0`，增加 `city+0x90` 士气 | `0x0048A275 -> 0x486150` | 无 | `0x0048A280 -> 0x435CD0(mask=2,set=1)`，即 `city+0x3E b1` |

其中：

- `0x00486150(list)` 遍历人员并调用 `0x0044C8A0(person, 0x1000, 1)`，直接置 `person+0xE8 bit12`。
- `0x00488450(count)` 明确返回 `count*50`；`0x0043D8D0` 饱和扣减 corps `+0x14` 资金。
- 巡察还有 `0x004432D0(corps,1)` 的额外军团侧效果；精确业务名仍是 unknown，正式复刻不能漏掉。
- 修筑和训练还对参与武将调用 `0x0044DC80` 更新相应熟练/经验类状态；精确字段名不在本轮展开。
- 五类 apply 还包含消息、表现和概率事件调用；上表只列本轮静态追到、与风险判断最相关的状态写入，不宣称这些字段必然持久化，也不宣称穷尽全部副作用。

### 9.1 已检查路径中未识别独立持久订单队列

在本轮检查的 command 构造、task 挂接和 apply 路径中，没有观察到把“内政订单记录”追加到另一个域对象的调用：

- `0x47E6F0` 的已检查函数体包含 task 关系/引用处理并写 `parent+0x10`；
- `0x49CA60 -> 0x487180 -> 0x59A850` 是 no-op，不是提交器；
- 资金、城市值、占用位和人员行动位都在 apply 内直接写入。

因此当前用于继续研究的工作模型是：**短命 command task + 对活动游戏状态字段的直接变更**。这不是全程序完备模型。城市/人物/军团结构如何被存档系统序列化、占用位何时清零，以及是否存在未被本轮 xref 覆盖的旁路队列，均保留 unknown；不能把“结构最终会被正常存档”扩写成已确认的序列化 ABI。

## 10. 线程、阶段与重入：尚未成立执行契约

### 10.1 原生事件路径作为观察候选

`root vtable+0x18 = 0x5179B0` 是已定位的原生场景事件处理函数。其命令分支可概括为：

```text
commandId = eventCode - 0x2710
if 0 <= commandId < 0x47:
    handler = root.vtable+0x28(commandId)
    if handler:
        0x47E6F0(root, handler, 1)
        root+0x34 = 0x3EA
```

关键寄存器必须按具体断点位置区分：在 `0x00517BD9` 执行前，**`ESI=root/controller`，`ECX=eventCode`**；执行 `lea eax,[ecx-0x2710]` 后才得到 `EAX=commandId`。`ECX=root` 只在后面的 `0x517BEB` 被显式写入后用于虚调用，不能把 `0x517BD9` 的 ECX 误记为 root。

该函数出现在游戏自身事件路径中，因此适合在用户正常 UI 操作时观察线程 ID 和对象生命周期；这只是 **candidate native event-loop context**。精确 Windows thread 身份、调用源、重入规则以及任何可安全 post 的 dispatcher 均为 unknown，本文不称其为“已确认安全主线程入口”，也不授权调用它。

### 10.2 空闲条件

`0x0047E420(obj)` 已检查的 11 字节函数体为：

```c
return obj->child_or_current_at_0x10 != obj;
```

`0x005138B5` 在进入目标选择流程前调用它，非零就退出。这给出了只读观察时应记录的风险条件，但不是足以成立执行器的契约：

- root/handler 的 `+0x10 == this`，没有活动 child；
- root/handler state 与原生入口预期相符；
- 全局选择链没有被另一 task 使用；
- 正常 UI 一次只观察一个城市的一条命令，等待 child 自然完成后再开始下一轮记录。

### 10.3 为什么不能外部同步调用

从外部线程直接 `CreateRemoteThread` 调构造器/validator/apply 会同时违反：

- x86 `thiscall` 和 SEH/CRT 分配器上下文；
- UI/DirectX 主线程亲和性；
- task 父子引用计数和活动 child 约束；
- 单一 `0x015455AC` 全局选择事务；
- 城市、日期、资金可能在预览与 apply 间变化；
- command apply 内还有消息、概率事件和表现层调用。

所以地址已确认不支持“外部读写内存 + 直接函数调用”。该路线在本项目当前安全政策下为 NO-GO。

## 11. 能否无 UI 用预选五人走同一 validator

静态答案是：**现有地址可以描述正常 UI 路径中的若干部件，但不足以组成安全的无 UI 提交链；自动执行为 NO-GO。** 不应把这些地址拼成调用脚本或 PoC。证据缺口至少包括：

1. 没有任意城市可用的稳定 root getter；`root+0x38` 可能为 0。
2. command ctor 会覆盖全局选择链；未知 UI task 活跃时的所有权和重入无法证明。
3. command validator 不验证最多五人、全部同城、属于 handler 候选集；缺失的 UI 不变量不能靠“validator 返回 1”补齐。
4. apply 在已观察路径中由原生 task tick 到达；直接调用 `+0x30` 会跳过已观察生命周期，并可能遗漏未知表现层或全局副作用。

因此 V3 不提供可执行 PoC，也不把未来 dispatcher 作为本轮获准的开发项。后续若继续取证，也限于第 13 节：满足严格预检后由用户正常操作、观察者只读记录；即使观察结果符合预期，也不会自动升级为写入授权。

## 12. ContextToken：只读计划与观察记录的一致性快照

### 12.1 四个全局量

| 地址 | 宽度 | 当前样本 | 等级与语义 |
|---:|---:|---:|---|
| `0x01232474` | u32 pointer | city 0 指针 | confirmed 写点 / candidate“UI 当前操作城市”；只作诊断，不作 target 权威来源 |
| `0x01232480` | 代码常读 u32；`0x45B00D` 编码路径只取 low byte | `0x13` | confirmed 场景/任务 selector index；在 mode 2/9 下按 `0..5`、`0..0x1F` 分支，具体枚举名 unknown；该路径是否为存档序列化本轮不硬命名 |
| `0x01232484` | 代码常读 u32；`0x45B01C` 编码路径只取 low byte | `1` | confirmed 游戏/场景 mode discriminator；常比较 1、2、9，枚举标签 unknown；该路径是否为存档序列化本轮不硬命名 |
| `0x0123269C` | u32 | `0xE358` | confirmed 战略日历标量；`0x46A580/0x46A5A0/0x46A5D0` 拆年月日，`0x46AA50` 从月份算季度 |

`0x0123269C` 是一个重要的上下文变化量：`0x485104` 还直接以 `%30` 判定月内日期阶段。它适合用于判废旧的 dry-run 预览，但单独相等不能证明其他城市、人员或 UI task 状态未变。

### 12.2 推荐 RawContextToken

Token 必须来自 V2 已采用的同一进程句柄、进程代复核和 `V1-before -> raw A/B/A -> V1-after` 稳定读取；不能只保存“选中的五个人”。推荐按固定字段顺序序列化后计算摘要，同时保留以下原始字段供诊断：

```text
ProcessGeneration:
  pid, processCreationTime, imagePath, imageSize
  diskExeSha256
  loadedModuleIdentityDigest, conflictScanResult
  liveCodeAnchors = (matchedCount=51, expectedCount=51, stableAEqualsB,
                     liveEqualsDisk, anchorSetDigest)

V2ObserverGlobalGates:
  targetHashAndImageBaseMatch
  processGenerationStable
  conflictScanClear
  live51AnchorsStableAndMatchDisk
  strategicInputPhaseCandidate       // 当前仅 candidate，不能改名为 confirmed
  rawABAStable
  v1BeforeAfterStrongSummaryEqual

V1StrongSnapshotSummary:
  snapshotReadiness, fixedLayoutVersion
  forceTableBase/stride, cityTableBase/stride, personTableBase/stride
  v1BeforeStrongSummaryDigest, v1AfterStrongSummaryDigest
  directlyControlledCityIdsInTableOrder[]
  perCityStructure[] =
    (cityIndex, cityPtr, cityVptr, type, selfPtr,
     corpsPtr, corpsIndex, corpsVptr, corpsFlags34,
     mainCorpsPtrB8, leaderPtrBC,
     residentListHead/tail/count, residentSourceOrder[])

GlobalRaw:
  uiCurrentCityPtr1232474       // diagnostic only
  scenarioIndexRaw32_1232480
  gameModeRaw32_1232484
  strategicDateRaw32_123269C

PerCityV2RawGates:
  targetCityIndex, targetCityPtr, cityCorpsPtr
  directControlAndStructureValid
  corpsFlags34, corpsFunds14, corpsPlayerBit0
  cityStateCode7C
  cityVptr, embeddedVptrAt58, embeddedFlags78
  commonVtable90RawEquivalentResult
  cityOrderFlags1E0, cityOrderFlags3E
  patrolCurrent1C4
  commerceCurrent1CC, commerceMaximum1D4
  cultivateCurrent1D0, cultivateMaximum1D8
  durabilityCurrent3C, durabilityMaximum1C8
  troops88, morale90
  rawRangeHashA, rawRangeHashB, rawRangeHashA2

ResidentAndReadySets:             // 数组顺序属于 token，不得按 ID 重排
  residentSourceOrder[] =
    (sourceIndex, personIndex, personPtr, personId,
     residencePtrF4, identity84, flagsE8,
     currentMight50, currentIntelligence58,
     currentPolitics60, currentLeadership68)
  readySetInSourceOrder[] = 上述 resident 中 flagsE8.bit12==0 的完整集合

PerCommandV2Decision:
  commandId
  everyRawGateResult =
    (direct/valid corps, player-control gate, city state gate,
     common vtable+90 gate, ready-nonempty gate,
     native money gate when observed, product reserve+commandCost gate,
     command order-bit gate, command current/cap gate)
  relevantAbilityField
  rankedReady[] = (personIndex, sourceIndex, relevantCurrentAbility)
  previewSelection[]             // 有资格时恰好5人；不足5人为空
  knownStaticSubsetWouldPass
  productWouldPlan
  planningReady                  // V2 当前强制 false
  verifiedNativeCapability       // V2 当前强制 false

CanonicalSummary:
  canonicalSchemaVersion
  canonicalTokenDigest
```

在用户正常 UI + 硬件断点的观察记录中，可以额外附加以下瞬时字段；它们仍不是调用授权：

```text
observedWindowsThreadId
rootThis, rootVptr, rootChild10, rootCorps30, rootState34, rootTarget38
handlerThis, handlerVptr, handlerChild10, handlerCorps38, handlerTarget60
globalSelectedHead15455B0, globalSelectedCount15455B8
```

V1 前后摘要、任一 raw A/B/A、51 个 live anchor、进程代、完整 ready 集合、源顺序、相关当前能力或任何 V2 门发生变化，都必须把旧 dry-run 预览标为过期并重新读取。只比较日期、资金或五名 selected 并不充分。

## 13. 最小动态验证断点方案

当前 PID 已加载 Easy，**禁止在它上面执行本方案**。这里的“动态验证”仅指未来的人工观察计划，不是本轮已执行事项。

### 13.1 每一轮之前的 fail-closed 预检

以下条件必须全部满足，否则不附加调试观察：

1. 用户先在游戏正常 UI 中建立一个专用、一次性的验证存档，并关闭自动保存；退出游戏后手工复制该存档作只读备份，记录原件与备份哈希。不得使用唯一存档或正在游玩的主存档。
2. 重新启动目标哈希 EXE，不加载 Easy、San9PKHard、伴侣、覆盖层或其他未知模块。先做只读已加载模块/冲突扫描；发现任何已知或未知冲突即停止。
3. 记录并绑定进程代 `(PID, creationTime, imagePath, imageSize)`。每轮开始和结束都复核；PID 复用、进程重启或路径变化即废弃全部记录。
4. 用 V2 观察器在同一只读句柄中核对 **51/51 live code anchors**：live A/B 稳定、live 与目标磁盘短窗逐字节一致、磁盘 EXE SHA-256 正确。任一锚点不符即停止。
5. 完成 `V1-before -> raw A/B/A -> V1-after`，要求 V1 强结构摘要一致、raw A/B/A 一致，并生成第 12 节完整 ContextToken。只要 ready 集合、源顺序或任一门禁不稳定，就不进入该观察轮。
6. 只用 x86 硬件执行/数据断点；**每一轮合计不超过 DR0-DR3 四个槽**。禁止 `INT3`、软件断点、代码补丁、内存写入、函数调用、远程线程、注入、自动点击或 UI 宏。

用户只能通过游戏正常 UI 选择城市、打开命令、选人、确认或取消。观察者只记录寄存器/内存。不得为了命中失败分支而制造竞态、篡改资金/状态、暂停后改上下文或提交非法人员链。每轮完成后退出且不保存；需要重复时，只在游戏关闭状态下由用户手工恢复备份。

### 13.2 Round A：事件、root 与 task 关系（每个命令单独一轮，3 槽）

- DR0 execute：`0x00517BD9`。执行前记录 thread ID，**`ESI=root`、`ECX=eventCode`**，以及 `[ESI+0x10/+0x30/+0x34/+0x38]`；单步越过 `lea` 后记录 `EAX=eventCode-0x2710`。
- DR1 execute：只设置本轮所选命令的一个 factory case：巡察 `0x5128B8`、商业 `0x512900`、开垦 `0x512948`、修筑 `0x512990` 或训练 `0x512A20`。不得同时占用五个槽。
- DR2 execute：`0x0047E71F`，记录当次 `EDI=parent`、`ESI=child` 和旧 `[EDI+0x10]`。

由用户正常完成一条命令。五类需五次独立轮次。结果只能表述为“这些样本中观察到的 thread/root/target/task 关系”，不能据此确认通用安全主线程入口。

### 13.3 Round B：UI 返回与第二 validator

成功路径一轮使用恰好 4 槽：

- DR0 execute：`0x0050EAD2`，记录 handler `+0x28` 调用前状态；
- DR1 execute：`0x0050EADD`，记录返回 command；
- DR2 execute：只设本轮命令的一个 validator 入口；
- DR3 execute：`0x0050EAE2`，记录 validator 返回和全局选择链摘要。

如用户正常操作中**自然**出现 validator 失败，可另开一轮使用 DR0=该 validator、DR1=`0x50EAE2`、DR2=`0x50EAF9` 观察析构；不得主动制造失败或竞态。若未自然出现，失败分支的动态证据就保持 pending，不能把静态分支提升为实机保证。

### 13.4 Round C：状态机与直接写入必须拆成两轮

Round C-state 使用 3 个 execute 槽：DR0=`0x485D20`、DR1=`0x485E17`、DR2=本轮命令的一个派生 apply 入口。只记录正常 UI 命令的状态序列。

恢复一次性存档后，Round C-data **只设置数据写断点，不与 C-state 合并**：

- DR0：该命令的城市数值 dword；
- DR1：`corps+0x14` dword（训练没有此槽）；
- DR2：一名用户正常选中武将的 `person+0xE8` dword；
- DR3：城市 `+0x1E0` dword，或 `+0x3E` byte。

记录命中时 EIP/调用栈和写前后值。样本只能支持“所观察写入经过何处”，不能证明表中列出了所有写入、所有副作用或持久化行为。

### 13.5 Round D：全局 ContextToken（最多 4 槽）

可把 DR0-DR3 分别设为 `0x01232474/80/84/269C` 的硬件写断点，由用户正常切城、推进战略阶段、存读专用存档；同时在每个正常 UI 停点做完整 V1/V2 只读快照。记录哪些量在样本中变化即可，不预设 `0x1232480/84` 必然整局稳定，也不把四个全局量替代第 12 节完整 token。

## 14. 静态复核脚本

脚本：`tools/re/san9_v3_static.py`

```powershell
python .\tools\re\san9_v3_static.py
python .\tools\re\san9_v3_static.py --json
python .\tools\re\san9_v3_static.py --disassemble
```

脚本只调用一次 `read_bytes()` 捕获磁盘文件；同一份不可变字节用于哈希、PE 解析、锚点核对和可选反汇编，避免“哈希一个文件版本、分析另一个版本”的 TOCTOU。SHA-256 或 PE ImageBase 不符时，在读取任何版本专属 VA 前立即 fail-closed。`--json` 与 `--disassemble` 互斥。

它核对：

- EXE SHA-256 / ImageBase；
- root vtable 的事件槽和 factory 槽；
- factory jump table、五类 `0x64` handler 分配和 ctor 链；
- handler `+0x0C/+0x20/+0x28`；
- `0x47E510`、`0x47E420`、`0x47E6F0` 关键字节和数据流；
- 五类 `0x40` command 分配、派生/中间/base ctor 链及全局选择链清理/复制；
- command vtable `+0x0C/+0x20/+0x30`、共同 validator 对全局链和 `E8.bit12` 的检查；
- busy helper 的 `mask=0x1000,set=1`、person `+0xE8` setter；
- 五类数值写入、order setter 的精确 `mask,set=1`、四类 `count*50`/扣金调用；
- `0x488450` 的 `count*50` 函数体，以及训练 apply 范围内不存在指向 fee helper/扣金函数的 rel32 call；
- mode/scenario 的 low-byte 编码路径锚点（不预设它就是存档序列化）。

`--disassemble` 按单个函数、函数前缀或明确基本块分别输出窗口，不再把多个相邻函数拼成一个窗口。当前目标文件的静态锚点为全部 PASS；JSON 中 `all_ok_scope=version_locked_static_anchor_match_only` 且 `execution_authorized=false`。因此 `all_ok=true` **只表示所列磁盘地址/版本锚点匹配，不表示运行时代码未被改动，更不授权调用或写入**。live 51-anchor 与冲突检查仍必须由 V2 只读观察器另做。

## 15. 开发决策

### 当前可做

- V1/V2 只读扫描：枚举直属可控城市、跳过灰化命令、按原生候选/排序规则预览最多五人。
- 把本页地址作为目标磁盘 EXE 的静态版本签名。
- UI 上显示 dry-run 计划，不对游戏执行。
- 在用户另行同意、且第 13.1 节全部通过后，由用户正常 UI 操作，研究者按分轮、最多四槽硬件断点进行纯观察；这仍不属于产品执行能力。

### 当前 NO-GO

- 把 `0x47E6F0` 当订单 enqueue。
- 从任何自动化线程或注入代码直接调 factory、handler Execute、command ctor/validator/apply。
- 用 `0x1232474` 或扫描到的一个 root 地址硬切多城市。
- 同时构造多条 command；全局 `0x15455AC` 会互相覆盖。
- 只依赖 command validator 而省略同城、候选成员、人数上限检查。
- 任何 `WriteProcessMemory`、远程线程、APC、窗口消息伪造、DLL/代码洞注入、IAT/vtable/代码补丁或字段补写。
- 任何 Computer Use、UI 宏或自动点击来替用户提交命令；动态取证只允许用户本人正常 UI 操作。
- 使用 `INT3` 软件断点，或在 live 51 anchors/冲突扫描/进程代/V1-V2 稳定性任一失败时继续。
- 把静态脚本的 `all_ok=true`、一次正常样本或“同一线程 ID”解释成执行授权。

### 推荐下一里程碑

下一里程碑仍是只读：补齐 V1/V2 token、静态副作用账本，并在条件允许时完成第 13 节人工观察。不要在本里程碑之后自动进入 dispatcher、注入器或可写 PoC 设计。任何未来可写方向都必须由用户重新授权，并经过独立的线程/所有权/完整副作用与可恢复性审查；第 13 节观察通过本身不构成该授权。

在政策改变和新审查完成前，产品的“一键所有直属城市”保持 dry-run。
