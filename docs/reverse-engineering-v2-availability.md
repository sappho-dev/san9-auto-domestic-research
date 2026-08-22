# 三国志九 PK 1.0.1.0：V2-availability 原生可用性与选将链

状态：**五类内政命令的原生灰化入口、在城且未行动候选链、五人上限、原生排序键和最终选择链表均已静态定位。原生人类界面不会自动选满五人；AI 选择器是带概率的，不等于确定性“本次最优”。实际提交/入队链仍未确认，本轮禁止调用。**

本文只记录磁盘 EXE 静态分析、公开源码交叉和既有进程的只读数据快照。没有写内存、注入 DLL、创建远程线程、调用游戏函数或使用 Computer Use。V2 对 V1 文档中“已行动/本次可选为 unknown”的部分作补充；未明确确认的偏移仍不得用于正式执行。

## 1. 版本锁、范围与证据等级

| 项目 | 值 |
|---|---|
| 目标文件 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0` |
| 架构 / ImageBase | x86 PE32 / `0x00400000` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 公开源码 | San9PKHard commit `47861295df22dbdaef92449acaacbdc9619f4529` |
| 当前只读样本 | PID `22348`，已加载 `Easy.dll` |

地址等级定义：

- **confirmed**：目标哈希对应的磁盘 EXE 中，入口、调用目标、数据流或虚表槽已由反汇编确认；语义另有源码、相邻逻辑或独立数据不变量支持。
- **candidate**：入口或数据流存在，但业务语义、对象生命周期或安全调用条件还缺动态对照。
- **unknown**：没有可靠定位，或不足以安全调用。

当前 PID 的代码段可能已被 Easy 修改，因此所有代码结论以**磁盘 EXE**为准。该进程只用于 RPM 数据基线，不能拿实时代码字节证明原版入口。

## 2. 结论总览

五类命令走同一条人类控制路径：

```text
命令 handler::CanExecute（灰化真值）
  -> handler+0x60 的 city/building
  -> 0x436D60(city, handler+0x40, 0x472D30, 0)
  -> city+0xDC 在位武将链
  -> 过滤 person+0xE8 bit12 == 0
  -> handler+0x40 候选链，数量在 +0x4C
  -> UI task+0x6CC 源候选链
  -> 按命令属性降序显示
  -> 0x570500 多选，max = min(5, 候选数)
  -> task+0x6EC 当前选择链
  -> 0x4E6C80 复制到 task+0x6AC 最终选择链
  -> [本轮未确认的提交/入队链]
```

对产品需求的直接含义：

1. “按钮变灰就跳过”已有原生真值入口，不必仅靠猜字段。
2. “在城且未行动”已有共同原生谓词，不应再把全部 `city+0xDC` 人员当可选。
3. 原生界面只负责排序和手动多选；候选多于一人时不会自动取前五。
4. 原生 AI 的 `0x00473280` 是权重/概率选择，不满足确定性“本次最优”。基础版可自行对已过滤候选按原生**源列表**规则（属性降序、同分保留 `city+0xDC` 原顺序）取前五，同时保留原生 CanExecute 作为最终门禁。
5. 目前还不能从外部直接调用这些入口：有效 `this`、游戏线程、窗口状态和提交事务尚未形成安全契约。

## 3. 五个原生 CanExecute / 灰化入口

### 3.1 handler 虚表与 ABI

以下地址均为 **confirmed**，且脚本已核对 `handler_vptr+0x20 == CanExecute`、`handler_vptr+0x28 == execute_ui`：

| 命令 | ID | handler vptr | `+0x20` CanExecute | `+0x28` 打开任务 UI | 本函数候选构造 call |
|---|---:|---:|---:|---:|---:|
| 巡察 | 0 | `0x00609B90` | `0x004C1930` | `0x004C1840` | `0x004C199F` |
| 商业 | 1 | `0x00609F38` | `0x004C6400` | `0x004C6310` | `0x004C6467` |
| 开垦 | 2 | `0x00609BF0` | `0x004C1F50` | `0x004C1E60` | `0x004C1FB7` |
| 修筑 | 3 | `0x00609C20` | `0x004C2270` | `0x004C2180` | `0x004C22D7` |
| 训练 | 5 | `0x00609CE0` | `0x004C3890` | `0x004C37A0` | `0x004C38FF` |

CanExecute 的已确认调用约定：

```text
ECX = 有效命令 handler this
显式栈参数 = 无
返回 EAX = 1 可执行；EAX = 0 灰化/不可执行
this+0x38 = 军团/资金上下文对象
this+0x40 = 临时候选链表对象
this+0x4C = 候选数
this+0x60 = city/building 指针
```

函数还通过 `0x0046A680`（confirmed）写入原生原因/提示消息。也就是说，返回值与游戏菜单灰化共用同一逻辑，而不是另造的一套近似规则。

**安全边界**：入口和 ABI 已确认，不等于可以从外部任意调用。handler 的构造、所有权、线程亲和性和窗口生命周期仍是 candidate/unknown；本轮只允许把地址用于断点观察。

### 3.2 共同前置链

五个入口的开头字节模式一致，依次执行：

| 地址 / 槽 | 静态事实 | 等级 | 限制 |
|---:|---|---|---|
| `0x004C52C0` | 五类 CanExecute 首先调用的共同门禁；构造链把 corps 写到 `handler+0x38`，最终读取 corps `+0x34 bit0` | confirmed raw equivalent | `4C1720→4C5180→50E940`、`50E990`、corps vtable `+0x40→43F9F0` 已闭环 |
| `0x00405840` | 对 `this+0x60` 做对象类型/有效性检查 | confirmed（调用） | 精确类型体系 candidate |
| `0x00436160`，参数 6 | 对城市/设施做另一项共同状态检查 | confirmed（调用） | 参数 6 的业务名称 unknown |
| city vtable `+0x90` | 另一项共同禁止状态；非零即灰化 | confirmed（槽位/分支） | 精确业务名称 unknown |
| `0x00436D60` | 构造并过滤本次候选武将链 | confirmed | 见第 4 节 |
| `0x0046A680` | 保存相应提示消息 | confirmed | 消息文本本轮未逐条解码 |

公共失败消息 ID 为：共同状态 `0x176F/0x1770`，无可用武将 `0x1752`。巡察、商业、开垦、修筑还比较军团 `+0x14` 资金与 `0x00488450(context, 1)` 的支出结果，资金不足时使用 `0x1701`。San9PKHard 源码多处明确把军团 `+0x14` 注释为资金，因此该资金字段语义为 confirmed。后续 V3 静态链又确认 `0x00488450(count)=count*50`，这四项 apply 会调用它并从军团扣金；训练 validator/apply 不走该费用链。

### 3.3 命令专属灰化条件

下表的**比较本身和地址均为 confirmed**；“进行中/占用位”是根据共用失败消息和命令位置作出的 candidate 业务命名。未来执行器应优先使用整个 CanExecute 返回值，不要只复刻最后一行比较。

| 命令 | 专属位检查 | 数值/资源检查 | 返回 1 的消息 ID | 满值失败消息 |
|---|---|---|---:|---:|
| 巡察 | `city+0x1E0 bit3` 必须为 0 | vtable `+0xE8` 当前值 `< 1000` | `0x1773` | `0x1772` |
| 商业 | `city+0x1E0 bit4` 必须为 0 | vtable `+0xF0` 当前值 `< +0xF4` 上限 | `0x1775` | `0x1774` |
| 开垦 | `city+0x1E0 bit5` 必须为 0 | vtable `+0xF8` 当前值 `< +0xFC` 上限 | `0x1777` | `0x1776` |
| 修筑 | `city+0x3E bit0` 必须为 0 | vtable `+0x50` 当前耐久 `< +0x12C` 上限 | `0x173F` | `0x1778` |
| 训练 | `city+0x3E bit1` 必须为 0 | vtable `+0x80` 资源值 `>0`，`+0x84` 士气 `<100` | `0x177B` | `0x177A` |

训练资源门禁为零时使用消息 `0x16FF`。由现有结构和样本看，vtable `+0x80` 是城中兵力，`+0x84` 是士气；前者的业务命名保留为 candidate，分支和阈值为 confirmed。

最新只读样本中，玩家可控城市为 index `0/8`；city 0 的巡察值为 1000，商业为 600/600，开垦为 400/400，耐久为 600/600，兵力 20030、士气 100，静态上与“已有武将但多项达到上限而灰化”的情形一致。该样本来自加载 Easy 的进程，只作数据交叉，不作为原版代码证据。

## 4. “在城且未行动”共同候选链

### 4.1 城市在位链来源

以下链为 **confirmed**：

```text
0x00436D60 wrapper
  ECX = city/building
  arg1 = destination list
  arg2 = predicate
  arg3 = context
  callee cleanup: ret 0x0C
  EAX = destination count

0x00436D60 -> 0x00436CC0
0x00436CC0 -> 0x00435940(city, temp list)
0x00435940: ECX += 0x58; jmp 0x00460E30
0x00460E30: ECX += 0x84; call 0x0046F200
总偏移：0x58 + 0x84 = 0xDC
```

因此源容器就是 V1 已确认的 `city+0xDC` 在位武将链。`0x00436CC0` 随后用通用过滤器 `0x0046F330` 和传入谓词生成目标链；五个 CanExecute 都传 `predicate=0x00472D30, context=0`，目标为 `handler+0x40`。

公开源码 `InsertAsmSrc2.h:6899-6925` 也把 `0x00436D60` 的输入描述为都设及“在位武将链表”，并保留原函数跳转，构成独立交叉证据。

### 4.2 ready 谓词与行动位

`0x00472D30`（confirmed）的核心反汇编语义为：

```c
return ((person->flags_E8 & 0x00001000) == 0);
```

它读取传入链表项中的人物指针，完成对象检查后取 `dword [person+0xE8]`，右移 12 位、取反并与 1。`0x00472D70`（confirmed，非本需求谓词）检查的是 bit14，不能混用。

bit12 不只是“看起来像行动位”。下列闭环均为 confirmed：

1. `0x0044C8A0(person, mask, bool)` 是通用人物标志设置器。
2. `0x00486150(list)` 遍历已选人物链，对每个人调用 `0x0044C8A0(mask=0x1000, bool=1)`。
3. 五类命令执行区都调用 `0x00486150`：巡察 `0x0048893F`、开垦 `0x00489027`、修筑 `0x0048956E`、训练 `0x0048A275`、商业 `0x0048B861`。
4. 五个 CanExecute 又用 `0x00472D30` 排除 bit12 已置位的人。

所以可以把 `person+0xE8 bit12` 的**静态语义**确认为“已被命令占用/本旬不再 ready”；一次干净进程的手动前后快照仍应作为运行时验收，而不是继续猜偏移。

PID `22348` 的最新 RPM 基线中，直属 city 0 的在位人物 index `515` 为 `person+0xE8 = 0x00090024`，bit12 为 0，符合 ready 谓词；city 8 当前无在位人物。由于没有在该污染进程中手动执行命令，本样本只确认字段可读，不承担 0→1 动态转变证据。

## 5. 人类任务 UI：候选、五人上限与最终链

### 5.1 三个链表对象

五个 UI 构造函数均初始化相同的三个链表对象，地址和用途为 confirmed：

| task 偏移 | 数量偏移 | 用途 |
|---:|---:|---|
| `+0x6CC` | `+0x6D8` | 从 handler 候选链复制来的源候选 |
| `+0x6EC` | `+0x6F8` | 多选窗口里的当前选择 |
| `+0x6AC` | `+0x6B8` | 接受选择后供命令对象使用的最终选择 |

它们是游戏的链表头对象，不是五个连续人物指针槽。构造函数和 UI vptr：

| 命令 | UI 构造函数 | UI vptr | `vptr+0x84` |
|---|---:|---:|---:|
| 巡察 | `0x004D8400` confirmed | `0x0060B920` confirmed | `0x004E6C80` confirmed |
| 商业 | `0x004E6AF0` confirmed | `0x0060CCB0` confirmed | `0x004E6C80` confirmed |
| 开垦 | `0x004D9F50` confirmed | `0x0060BBA0` confirmed | `0x004E6C80` confirmed |
| 修筑 | `0x004DA9B0` confirmed | `0x0060BCE8` confirmed | `0x004E6C80` confirmed |
| 训练 | `0x004DF350` confirmed | `0x0060C370` confirmed | `0x004E6C80` confirmed |

`0x004E6C80` 清空 `task+0x6AC`，再用 `0x0046EF80` 把 `task+0x6EC` 全量复制过去，随后进入基类处理。因此它是“最终五人链提交给后续 UI/命令层”的确认边界，不是任务真正入队的证明。

### 5.2 原生多选与五人限制

通用多选窗口 `0x00570500`（confirmed）是 7 个栈参数、调用者清栈的包装器，内部追加对话框类型 `0xA` 后调用 `0x00570150`（confirmed）。五个调用点：

| 命令 | call 地址 | 最大人数计算 |
|---|---:|---|
| 巡察 | `0x004D8C1A` confirmed | `min(5, [task+0x6D8])` |
| 商业 | `0x004E734A` confirmed | 同上 |
| 开垦 | `0x004DA6EA` confirmed | 同上 |
| 修筑 | `0x004DB1EA` confirmed | 同上 |
| 训练 | `0x004DF8C1` confirmed | 同上 |

逻辑参数从 arg1 到 arg7 为：工作选择链 `task+0x6EC`、源链 `task+0x6CC`、最大人数、`0x1F65`、命令对话框样式、常量 1、回调/0。返回 EAX 非零表示用户接受；调用者随后整理选择链。

原生自动选择行为也已确认：当且仅当源候选数为 1 时，五个 UI 初始化路径用 `0x0046EF80` 把该人复制到 `task+0x6EC`；候选数大于 1 时不自动选人。因此不存在可直接复用的“人类界面自动拉满五人”函数。

## 6. 原生排序与“本次最优”

### 6.1 源候选显示顺序

多选窗口打开前，五类任务用 `0x0046F640(list, fieldId, orderFlag=0)`（confirmed）排序 `task+0x6CC`：

| 命令 | field ID | 属性 | 排序 call |
|---|---:|---|---:|
| 巡察 | `0x1D` | 智力 | `0x004D8BFB` confirmed |
| 商业 | `0x1E` | 政治 | `0x004E732B` confirmed |
| 开垦 | `0x1E` | 政治 | `0x004DA6CB` confirmed |
| 修筑 | `0x1B` | 统率 | `0x004DB1CB` confirmed |
| 训练 | `0x1C` | 武力 | `0x004DF8A5` confirmed |

`0x0046F430` 的 orderFlag 0 分支在 `0x0046F500` 开始做插入排序；当前项分数高于前项时交换，所以 **0 明确是降序**（confirmed），不是根据界面观感推测。

源字段提取链也已静态闭合：`0x0046F640 -> 0x0046E600 -> person vtable+0x18 -> 0x00450C20`（均 confirmed）。字段跳转分别落到 `0x00450F9B`（`+0x68` 统率）、`0x00450FA8`（`+0x50` 武力）、`0x00450FB5`（`+0x58` 智力）、`0x00450FC2`（`+0x60` 政治），只返回属性值。

orderFlag 0 的比较在分数相等时**不交换**，所以源候选链的完整规则是：属性降序，同属性保留 `city+0xDC` 原有相对顺序。这才是按原生候选显示顺序截取前五时应采用的 tie 规则。

### 6.2 接受选择后的稳定键

用户接受后，`0x0046F610(list, keyFn, orderFlag=0)`（confirmed）再次按任务属性降序整理 `task+0x6EC`：

| 命令 | key 函数 | 当前能力字段 | 排序 call |
|---|---:|---:|---:|
| 巡察 | `0x00471D30` confirmed | 智力 `person+0x58` | `0x004D8C2F` confirmed |
| 商业 | `0x00471D80` confirmed | 政治 `person+0x60` | `0x004E735F` confirmed |
| 开垦 | `0x00471D80` confirmed | 政治 `person+0x60` | `0x004DA6FF` confirmed |
| 修筑 | `0x00471DD0` confirmed | 统率 `person+0x68` | `0x004DB1FF` confirmed |
| 训练 | `0x00471CE0` confirmed | 武力 `person+0x50` | `0x004DF8D6` confirmed |

普通人物的确切 key 为：

```text
current_stat * 1000 + person_id
person_id = u16(person+0x04)，由 0x004467E0 取得
```

因此在**已经选定的成员内部**，同能力值时 person ID 更高者在前。身份字段 `person+0x84 == 0` 的君主走特殊 key `0x30D40`（十进制 200000），会优先于常规能力分。上述字段、常量和方向均为 confirmed。

必须区分两件事：

- **决定选哪五人**：原生人类界面只把源候选按属性稳定降序展示；若要“自动取界面前五”，同分必须保留 `city+0xDC` 原顺序。
- **五人选定后的内部顺序**：才使用 `current_stat*1000+person_id` 以及君主特殊值重排；它不应反过来决定同分时谁跨过第五名边界。

这给确定性基础版提供了一个可复刻的原生展示契约。但“属性最高五人”是否等价于游戏所有收益公式下的数学最优仍是产品语义问题；V2 只确认游戏界面的筛选与排序行为。

### 6.3 为什么不直接复用 AI 选择器

AI 五类任务最终都调用 `0x00473280`（confirmed），对应 call 为巡察 `0x004AAF00`、开垦 `0x004AB7D0`、修筑 `0x004ABD10`、训练 `0x004AE200`、商业 `0x004B3540`（均 confirmed）。San9PKHard `InsertAsmSrc.h:683-690` 明确把该函数描述为按概率挑选，目标 EXE 的 `0x00473190/0x00473280` 也表现为带评分/随机过程、最多五人的 AI 选择链。

所以：

- `0x00473280` 是已确认的 AI 加权/概率选择器；
- 它不是确定性 top-5；
- 为了“本次最优、结果可预览、可重复”，不推荐直接复用它。

公开源码 `InsertAsmSrc.h:666-680` 还说明电脑内政任务包括训练、商业、开垦、巡察、修筑，并处理“候选为空”的 AI bug；这与静态识别的五类任务集合相互印证，但不改变人类 UI 的候选/选人规则。

## 7. 提交链：只标候选，禁止调用

已确认到命令对象构造层的入口如下：

| 命令 | 命令对象构造函数 | 等级 |
|---|---:|---|
| 巡察 | `0x00488410` | confirmed（构造入口） |
| 商业 | `0x0048B340` | confirmed（构造入口） |
| 开垦 | `0x00488B00` | confirmed（构造入口） |
| 修筑 | `0x00489080` | confirmed（构造入口） |
| 训练 | `0x00489D80` | confirmed（构造入口） |

`0x00485A00`（confirmed）本身只是 `mov eax,[ecx]; jmp [eax+0x2C]` 的通用虚调用分派，不能把它直接命名为一个固定“提交函数”。公开源码会在任务提交窗口处引用它，但具体 vtable 目标取决于对象。

当前状态：

- `task+0x6AC` 作为后续命令层所用人物链：confirmed。
- 上表五个构造入口及其所在命令区域：confirmed。
- 构造函数完整参数、对象所有权和析构责任：candidate/unknown。
- 构造后真正写入任务队列、扣资金、置城市占用位、错误回滚的调用链：unknown。
- 在任意外部线程调用 CanExecute、构造函数或 `0x00485A00` 的安全性：unknown，禁止。

这一边界非常重要：`0x00486150` 置人物 bit12 只是执行链中的一个副作用，不代表只调用它就能建立一条有效内政任务。

## 8. 最小动态验证方案（仍不写游戏数据）

应在**未加载 Easy.dll 的干净 1.0.1.0 进程**中，由用户手动操作菜单；分析工具只读寄存器/内存。为避免改写代码字节，使用硬件执行断点（DRx），不用软件 `INT3`。

### 验证 A：CanExecute 与灰化一致

1. 在五个 CanExecute 入口中选一个设置硬件执行断点。
2. 用户手动打开对应城市菜单；记录入口 `ECX`、`[ECX+0x60]` 和返回 `EAX`。
3. 入口后观察 `[ECX+0x4C]` 候选数；在自然满值/资金不足/无武将样本与可执行样本各做一次。
4. 验收：菜单灰化时 EAX=0，可点时 EAX=1；城市指针必须与当前城市精确相同。

### 验证 B：bit12 的 0→1 与候选剔除

1. 命令前 RPM 保存所选人物 `person+0xE8` 和城市 `+0xDC` 链。
2. 可选地对该 dword 设置硬件**写**断点，用户手动提交一条内政命令。
3. 验收写入调用栈应经过 `0x00486150 -> 0x0044C8A0`，bit12 从 0 变 1；再次打开内政时 `0x00472D30` 对该人返回 0，候选数减少一。
4. 不修改该位来制造样本；需要恢复时由游戏正常过旬或重读存档。

### 验证 C：排序、参数与五人上限

1. 对 `0x00570500` 设置硬件执行断点，用户打开候选多于五人的命令。
2. 读取入口 7 个栈参数，遍历源 `task+0x6CC` 与工作链 `task+0x6EC`。
3. 验收 arg3 等于 `min(5, source_count)`；源链按对应属性降序，同属性保持命令前 `city+0xDC` 相对顺序。
4. 用户手选并确认后，先验证 `+0x6EC` 才按属性、人物 ID 键重排，再在 `0x004E6C80` 观察它被复制为 `+0x6AC`，人数不超过五。

四个 x86 硬件断点槽有限，应分三轮完成，不需要同时布置全部入口。当前加载 Easy 的 PID 只保留 RPM 基线，不用于上述代码断点验收。

## 9. V2 对实现层的只读契约

在提交链完成前，安全的 dry-run 可以做到：

```text
for 每个 V1 已确认可控城市:
    从 city+0xDC 读取在位链
    保留 person+0xE8 bit12 == 0
    按命令当前属性做稳定降序；同分保留 city+0xDC 原顺序
    预览前 min(5,n) 人
    若没有人：跳过该命令
    若只读复刻的专属上限明显已满：标为“预计灰化”

注意：预计灰化 != 原生 CanExecute 最终真值
```

正式执行版仍需满足：

1. 在游戏自己认可的 handler/context 中取得原生 CanExecute 结果；
2. 在游戏线程和正确生命周期内提交；
3. 提交前后以城市、军团、日期/旬和人物 bit12 做一致性检查；
4. 任一条件变化即重新枚举，而不是沿用旧人物指针。

不建议现在做“外部线程直接 call 绝对地址”的试验。老式 x86 C++/MFC 游戏大量依赖隐式 `this`、虚表、SEH、窗口对象和全局状态，地址正确也可能因上下文错误而崩溃。

## 10. 地址状态清单与未决项

| 项目 | 状态 |
|---|---|
| 五个 CanExecute、虚表槽、返回布尔 | confirmed |
| `0x00436D60/0x00436CC0` 候选构造 ABI | confirmed |
| `city+0xDC` 源在位链 | confirmed |
| `0x00472D30` 与 `person+0xE8 bit12` ready 规则 | confirmed（静态闭环）；干净实机转变待验收 |
| `handler+0x40/+0x4C` 候选链/数量 | confirmed |
| `task+0x6CC/+0x6EC/+0x6AC` 三链及数量 | confirmed |
| `0x00570500` 参数形状与五人上限 | confirmed |
| 源候选属性稳定降序、同分保留 city+0xDC 顺序 | confirmed |
| 已选成员按属性*1000+ID 降序的内部规范顺序 | confirmed |
| 人类 UI 仅单候选自动选择 | confirmed |
| `0x00473280` AI 加权/概率选人 | confirmed |
| `0x004C52C0` 共同门禁等价于有效 handler corps 的 `corps+0x34 bit0` | confirmed raw equivalent |
| city vtable 各专属 getter 的底层字段偏移全映射 | candidate |
| 有效 handler `this` 的获取、生命周期、线程约束 | unknown |
| 命令构造后的真正入队、扣款、占用和失败回滚 | unknown |
| 可安全调用的原生提交 API | unknown，当前禁止 |

## 11. 可复现静态检查

脚本：`tools/re/san9_v2_static.py`

```powershell
py -3 "C:\codex files\San9AutoDomestic\tools\re\san9_v2_static.py" `
  --exe "D:\三国志9\10101749\San9PK.exe" `
  --disassemble
```

机器可读输出使用 `--json`。脚本只读取磁盘 EXE，先校验 SHA-256 和 ImageBase，再核对五组 handler/UI 虚表、候选构造、多选、排序与置 busy 的 rel32 调用目标；不包含任何进程打开、RPM/WPM、注入或游戏函数调用代码。

## 12. V2 严格只读观察器实现与验收（2026-08-07）

Adapter 已加入 V2 availability/ranking observer，但它仍不是规划输入或执行能力：

- 同一个只读进程句柄上先后执行 V1 强结构快照、V2 原始区 A/B/A 稳定读取、代码锚点读取、第二次 V1 强结构快照和进程代复核。前后 V1 稳定摘要必须完全相等。
- 原始区从 `0x01232474` 连续覆盖到人物表尾，包含四个 global、所需 city/person/corps 字段；`RawContextToken` 还绑定直属城市的 corps 指针/flags。`0x01232484 == 1` 只命名为 `strategic input phase candidate`，语义仍未验证，其他值一律 blocking。
- ready 人物严格沿 V1 已验证的 `city+0xDC` 链原顺序过滤 `person+0xE8 bit12 == 0`。按智/政/政/统/武稳定降序；同分保留源链顺序。至少五人才生成恰好五人的 preview。
- `KnownStaticSubsetWouldPass` 只表达已复刻静态子集，不代表调用或取得原生 CanExecute。旧的 `NativeEntryWouldEnable` 被保留为 obsolete 且恒 false；`PlanningReady` 和 `VerifiedNativeCapability` 恒 false。
- 产品资金门按命令显式计算并采用长整型无溢出比较：巡察、商业、开垦、修筑为 `reserve + 人数*50`；训练为 `reserve + 0`。该区别来自目标哈希 V3 的 validator/apply 静态链，动态对照前仍只用于非授权预览。
- `city vtable+0x90` 的原始等价按 embedded flags 完整分支复刻：bit0 为 1 时看 bit22；否则看 bit2 或 bit22。城市/embedded vptr 必须匹配目标。
- 代码门同时核对 31 个关键函数/getter/构造链/派生短窗口、14 个 city/embedded 虚表槽、五个 handler `+0x20` 槽和一个 corps `+0x40` 槽，共 51 个锚点。每个锚点都要求目标磁盘短窗 SHA-256 等于固化预期、live A/B 稳定且 live 与磁盘逐字节相同。
- 已知 Easy 进程/模块冲突、基线未验证、任一代码差异、原始区不稳定、V1 前后变化或进程换代都会 blocking；观察报告仍可输出已稳定读取到的逐城灰化原因，但非授权 Projector 必须返回 `DiagnosticOnly`，且城市、人员和资金投影全部为空。V2 观察层只报告完整候选、每人费用和原生门禁；人数、总费用及保留金由冻结配置计划计算，不再夹带固定五人的产品判断。

合成 fake-memory 验收覆盖五命令、恰好五人预览、同分源顺序、命令占用灰化、四人、249 金、phase 变化、原始区跨读变化、代码跨读变化、无效基线及 reserve 溢出。当前实机 PID `22348` 只读验收结果：V1 前后结构一致、raw A/B/A 稳定、51/51 代码锚点精确、phase candidate 为 1；因检测到 Easy 冲突，观察保持 blocking。直属城为襄平和北海；襄平 ready 1 人且五项数值门均已满，北海 ready 0 人，报告均未产生产品计划。

仍未解决：phase/global 的业务命名、有效 handler 生命周期/线程亲和性、可供外部使用的安全主线程 dispatcher、完整副作用与异常/回滚事务。V3 已把主要 apply 写点和四项收费/训练零费静态闭环，但这不等于允许外部调用；以上 unknown 在解决前不得升级为 verified capability。
