# 三国志九 PK 1.0.1.0：V1-read 只读数据结构定位

状态：**表基址、步长、城市控制关系、武将在城关系和四项当前能力已定位；“已行动/本次命令可选”尚未定位。**

本文只描述 `ReadProcessMemory` 级别的观察结果，不包含写内存、注入、远程线程、代码洞或游戏内部函数调用。未经验证的偏移不得用于正式执行命令。

## 1. 版本锁与证据边界

### 1.1 唯一支持的目标

| 项目 | 值 |
|---|---|
| 文件 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0` |
| 架构 | x86 / PE32 |
| ImageBase | `0x00400000` |
| SizeOfImage | `0x01759000`（24,481,792 bytes） |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 当前只读验证进程 | PID `22348` |

所有绝对地址都只对上述哈希负责。换汉化版、免 CD 版、修改版或其他 PK 补丁后，必须重新做版本识别；不能因为窗口标题相同就沿用。

### 1.2 证据等级

- **confirmed**：至少有公开源码/目标文件静态代码之一提供明确语义，并由表边界、指针不变量或当前进程 RPM 样本交叉验证。
- **candidate**：结构或语义有较强单一证据，但没有足够的独立样本；只能用于继续观察，不能驱动命令执行。
- **unknown**：没有可靠定位，或已证明常见猜法不成立。

### 1.3 证据来源

1. 目标文件静态检查及其版本/哈希。
2. 公开的 [San9PKHard 源码](https://gitee.com/zhouyoudao/san9pkhard)，本次固定到 commit `47861295df22dbdaef92449acaacbdc9619f4529`。关键文件为 `San9PKHardDlg.h`、`PersonConfigDlg.cpp`、`CityConfigDlg.cpp`、`InsertAsmSrc.h`、`InsertAsmSrc2.h` 和 `InsertAsmSrc3.h`。
3. 对 PID `22348` 的只读 RPM 快照；没有使用 Computer Use，也没有向进程写入任何字节。

### 1.4 Easy.dll 污染警告

当前 San9PK 进程加载了：

| 项目 | 值 |
|---|---|
| 模块 | `D:\三国志9\10101749\Easy.dll` |
| 加载基址 | `0x02080000` |
| SHA-256 | `E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0` |

因此：

- 当前进程里的代码字节不能作为“原版 1.0.1.0”权威样本；静态代码应以磁盘 EXE 为准。
- 数据也可能被插件临时改写。实测 person index `24` 的姓字段为 `33 30 A4 FD 00`，即 ASCII `30` 加 Big5 `王`；而公开源码把 `person+0x10` 当作姓的 C 字符串。这说明当前姓名字段至少受到 Easy 生态的格式污染。
- RPM 验证适合检查地址、步长、指针关系和数值范围，不适合把当前姓名原始字节当作干净基线。
- 姓名只用于日志展示。C# reader 永久保留原始十六进制；严格 Big5 解码失败、缺少终止符或 Easy 前缀无法保守清理时，改用 `武将#ID` / `城市#ID` 并记 warning，不因展示文本问题破坏结构快照。人物身份、链表比对、排序和未来提交均不得使用姓名作主键。

## 2. 三张全局表

公开源码 `San9PKHardDlg.h:49-51,60-62,68-70` 给出了三张表的数量、基址和记录大小。公开源码中的遍历边界与下表一致；当前进程也能完整读取，且所有结构指针按对应步长对齐。

| 表 | 基址 | stride | 数量 | 尾后地址 | 等级 | 关键证据 |
|---|---:|---:|---:|---:|---|---|
| CITY | `0x0124DB58` | `0x1F0` | 50 | `0x01253C38` | confirmed | `CITY + 50*0x1F0` 精确等于 FORCE 基址；当前 50 条 `+0x06` 均为 5 |
| FORCE / 军团 | `0x01253C38` | `0xD4` | 50 | `0x012565A0` | confirmed | 源码常量/循环；CITY `+0xCC` 的非空值精确落在该表步长上 |
| PERSON | `0x01258EE0` | `0x128` | 850 | `0x012965B0` | confirmed | 源码常量/循环；当前 850 条记录的 `word +0x04` 全部等于其表 index |

注意：表里有固定槽位，不等于每条记录当前都是可用对象。必须继续检查各表的类型、身份或有效性条件。

## 3. FORCE / 军团结构

| 偏移 | 类型 | 语义 | 等级 | 证据与限制 |
|---:|---|---|---|---|
| `+0x34` bit 0 | bit | 当前由玩家控制 | confirmed | 源码多处 `test [force+34h],1`；当前主军团 1 为 `0x03`，委任子军团 3/7/8/9 为 `0x02` |
| `+0x34` bit 1 | bit | 未定位 | unknown | 当前普通有效军团普遍置位；不能据此判断主军团、玩家或有效性 |
| `+0x34` bit 2 | bit | 蛮族势力 | confirmed | 源码明确按 `4` 测试；当前军团 46–49 为 `0x06`，对应四个蛮族势力样本 |
| `+0xB8` | `FORCE*` | 所属主军团/势力主记录 | confirmed | 源码注释为“所属势力/主军团”；主记录自指，委任子军团指向主记录；当前样本全部按 `0xD4` 对齐 |
| `+0xBC` | `PERSON*` | 军团君主/都督指针 | confirmed | 源码直接称“军团君主/都督”，并以非 0 判断“有效军团”；当前非 0 值均落在 PERSON 表记录上 |

### 3.1 有效性与归属规则

对本版本可采用以下只读判定：

```text
force_addr = FORCE_ADDR + index * 0xD4
valid_force := u32(force_addr + 0xBC) != 0
main_addr   := u32(force_addr + 0xB8)
main_aligned := main_addr 是 FORCE 表的精确记录首地址
```

`+0xBC != 0` 是源码本身使用的有效性条件；实现时仍应额外要求 `+0xB8` 和 `+0xBC` 分别对齐到 FORCE/PERSON 表，防止在存档切换瞬间解引用陈旧指针。

验证快照 A（PID 内较早的存档态）中的有效军团为 `0,1,2,3,4,5,7,8,9,46,47,48,49`。与玩家有关的关键关系是：

- 主军团 index `1`：`flags=0x03`，`main=1`，`leader person=621`。
- 委任子军团 index `3/7/8/9`：`flags=0x02`，`main=1`。
- 因而“同属玩家势力”和“当前可直接控制”是两个不同条件。

同一 PID 随后切换到快照 B：主军团变成 index `0`、`flags=0x03`；子军团 `1/2/3/4/15` 均为 `flags=0x02` 且 `main=0`。这说明军团 index 是存档态，不能硬编码；应每次从 `+0xB8/+0x34` 重新推导。

## 4. CITY 结构与可控城市

| 偏移 | 类型 | 语义 | 等级 | 证据与限制 |
|---:|---|---|---|---|
| `+0x06` | `u8` | 都设类型；值 5 为都市 | confirmed | 源码多次 `cmp byte ptr[city+6],5`；当前 CITY 表 50 条全部为 5 |
| `+0x20` | Big5 C-string | 城市名 | confirmed | `CityConfigDlg.cpp:128-129`；当前 50 条均为合理城市名 |
| `+0xCC` | `FORCE*` | 城市所属军团 | confirmed | 源码大量直接读取；当前非空值均对齐到 FORCE 表 |
| `+0xDC` | list object | 都设“在位武将链表”对象起点 | confirmed | 源码明确命名；当前链表节点和 PERSON 表/在城链完全交叉验证 |
| `+0xE0` | node pointer | `+0xDC` 链表首节点 | confirmed | 节点 `+0` 为 next、`+4` 为 prev、`+8` 为 `PERSON*` |
| `+0xE4` | node pointer | `+0xDC` 链表尾节点 | confirmed | 当前 50 城遍历与尾节点一致 |
| `+0xE8` | `u32` | `+0xDC` 链表项数/在位武将数 | confirmed | 源码读 `[city+0xDC+0x0C]`；当前 50 城均与实际节点数一致 |

CITY 表没有发现单独的“此槽有效”布尔字段。它是 50 个固定城市槽；`+0x06==5` 是类型判别，不应被描述成生命周期标志。

### 4.1 归属、直属与当前可控制是三件事

```text
legion = u32(city + 0xCC)
main   = u32(legion + 0xB8)

owner_main_force       := main
structural_main_corps  := (legion == main)
currently_controllable := (u8(legion + 0x34) & 1) != 0
commissioned           := 玩家主势力相同，但 currently_controllable == false
```

对“一键内政”而言，最终应以 `legion+0x34 bit0` 判断**当前能否由玩家控制**。只比较 `main` 会把委任军团的城市错误纳入；只比较 `legion==main` 又会忽略可能由游戏或插件动态改变的控制状态。

快照 A 只有一个城市满足控制位：

| city index | 地址 | 名称 | legion | main | flags | 结论 |
|---:|---:|---|---:|---:|---:|---|
| 22 | `0x012505F8` | 廬江 | 1 | 1 | `0x03` | 当前直属、可控制 |

其他属于玩家主势力 1 但挂在军团 3/7/8/9 下的城市属于委任范围，V1 应跳过。切换到快照 B 后，可控城市动态变成 index `0` 襄平和 index `8` 北海，二者均挂在主军团 0；这进一步证明不能硬编码“22 廬江”。

## 5. PERSON 结构

### 5.1 身份、位置与记录完整性

| 偏移 | 类型 | 语义 | 等级 | 证据与限制 |
|---:|---|---|---|---|
| `+0x04` | `u16` | 人物表 index/ID | confirmed | 当前 850 条全部满足 `value == table index` |
| `+0x10` | Big5 C-string | 姓 | confirmed（布局） | `PersonConfigDlg.cpp:94`；当前字节可能被 Easy.dll 加前缀，不能当干净内容 |
| `+0x15` | Big5 C-string | 名 | confirmed（布局） | 同上 |
| `+0x80` | `i32` | 伤势 | candidate | 源码多处按 0 判断无伤；本轮未做受伤/无伤对照实验 |
| `+0x84` | `i32` | 身份状态 | confirmed | 源码 UI 与大量 32 位条件判断；无效样本为 `-1` |
| `+0x8C` | index | 官爵/官职表索引 | confirmed | 静态函数 `0x44D6C0` 读取此字段并映射官职表；**它不是已行动标志** |
| `+0xF4` | container pointer | 所属部队/内嵌部队 | confirmed | 源码明确命名；当前所有在城人物共享其城市内嵌部队容器 |

`+0x84` 的已确认子集：

- `0`：君主，confirmed。
- `1`：都督，confirmed。
- `2`：属于有效在位身份范围，但精确中文名称未由本轮证据独立确认，candidate。
- `3`：一般武将，confirmed。
- `4`：俘虏，confirmed。
- `8/9/-1`：当前可见于特殊/非普通活动记录；精确全枚举 unknown。

对国内命令候选人的**结构层预筛**可使用 `status in 0..3`。两次验证快照中，所有 50 城的 `+0xDC` 在位链表，均与“身份 0..3 且位置链解析到该城”的人物集合完全相等。这是活动身份范围和在位链结构的强交叉验证，但仍不代表这些人本次命令可选。

### 5.2 武将在城的确认链

公开源码将 `person+0xF4` 称为所属部队，并把 `container+0x20 bit0` 称为内嵌/在所属都设，把 `container+0x64` 称为所属都设。当前进程可用以下链稳定恢复城市：

```text
container = u32(person + 0xF4)
embedded  = (u32(container + 0x20) & 1) != 0
city      = u32(container + 0x64)

in_city :=
    status in 0..3
    AND embedded
    AND city 是 CITY 表的精确记录首地址
```

快照 A 的廬江样本：

- city record：`0x012505F8`。
- 其人物内嵌容器：`0x01250650`，即 `city+0x58`。
- `container+0x20` 为 `0x00000001`。
- `container+0x64` 回指 `0x012505F8`。
- 城市 `+0xDC` 链表项数为 27，恰好等于身份 0..3 且上述位置链回指廬江的 27 人。

状态 9 的左慈/大乔/小乔也可共享该城市容器，但不进入活动在位链，因此仅靠位置指针不够，必须叠加身份条件。

### 5.3 基础能力与当前/有效能力

三国志九这组结构公开并交叉验证的是**四项能力**，不是五项：

| 能力 | 基础值偏移 | 当前/有效值偏移 | 存储类型 | 等级 | 证据 |
|---|---:|---:|---|---|---|
| 武力 | `+0x4C` | `+0x50` | `u32` | confirmed | 源码把前者称基础武力，多处直接以 `dword [person+0x50]` 作当前判定；实时样本成对合理 |
| 智力 | `+0x54` | `+0x58` | `u32` | confirmed | 源码直接比较/读取 `dword [person+0x58]` |
| 政治 | `+0x5C` | `+0x60` | `u32` | confirmed | 源码明确以 `dword [person+0x60]` 作政治阈值；实时样本体现加成差值 |
| 统率 | `+0x64` | `+0x68` | `u32` | confirmed | 源码明确区分基础统率和 `dword [person+0x68]` 当前统率 |
| 第五项/魅力 | unknown | unknown | unknown | unknown | 公开源码的人物配置只读取上述四项，结构周边也没有经验证的独立魅力字段；不得虚构偏移 |

例如快照 A 的 person index `24`（王基，姓名字段已受 Easy 前缀污染）基础值为武力 75、智力 75、政治 76、统率 77；当前值为 75、75、80、77。政治 `+0x60` 的 80 与基础 `+0x5C` 的 76 清楚表现了“当前/有效值”配对。切换存档态后该人物的当前政治回到 76，也符合“当前值随状态变化、基础值不变”的解释。

如果产品需求里的“当前五维”只是泛称，应把接口改名为 `current_abilities` 并只暴露这四项；若确实要第五项，必须另开定位任务。

## 6. “已行动/本次可选”仍是 unknown

本轮没有找到可安全宣称为 `person.acted` 的稳定字段。已经排除两个高风险误判：

1. `person+0x8C` 是官爵/官职索引，不是行动位。
2. `city+0xDC/+0xE8` 是在位武将链表及数量，不是未行动/空闲数量。当前 50 城逐城验证时，它包含每一个身份 0..3 且在城的人物；不能据此判断某人是否已经接了本旬命令。

公开源码中确实出现“空闲武将链表”，但这些多为 AI 任务期间在栈上或任务对象中临时构造的链表，不是已确认的 PERSON 固定偏移。

因此 V1-read **只能生成结构候选集，不能安全生成最终可提交的五人集**。下一步最小验证应是：

1. 在无 Easy.dll 的干净 1.0.1.0 进程中，对同一存档做两次 RPM 快照。
2. 由用户手动让恰好一名武将接一个内政命令，再做第二次快照；不由分析工具点击或写入。
3. 对该人物记录、城市内嵌容器、命令/任务对象做差分；再换一名武将和一种命令复验。
4. 优先定位游戏自己构造“该命令可选武将列表”的只读谓词/列表，而不是猜一个布尔字节。

在该问题解决前，正式自动执行层必须让游戏自己的候选列表做最后过滤；任何“读到在城就直接提交”的实现都有选中已行动人物或崩溃的风险。

## 7. V1-read 安全枚举算法

下面的算法只负责列出当前可控城市和结构候选人物：

```text
assert sha256(on_disk_exe) == supported_hash
assert live_process.creation_time 在读取前后相同
assert live_main_module.base == 0x00400000
assert live_main_module.size == 0x01759000

// CITY 到 PERSON 尾部是一段约 0x48A58 bytes 的连续区域。
// 每次尝试必须整段读 A、整段读 B，并要求原始字节完全相等。
// 不能分别接受 force/city/person 三个时刻的局部稳定结果。
raw_A = RPM(CITY_ADDR, PERSON_END - CITY_ADDR)
graph_A = 双轮之一：读取所有非空 person+F4 容器和 50 城 +0xDC 链
raw_B = RPM(CITY_ADDR, PERSON_END - CITY_ADDR)
graph_B = 双轮之二：读取相同语义的容器和链
require raw_A == raw_B
require canonical(graph_A) == canonical(graph_B)

for city_index in 0..49:
    city = CITY_ADDR + city_index * 0x1F0
    require u8(city + 0x06) == 5

    legion = u32(city + 0xCC)
    require legion 精确对齐 FORCE 表
    require u32(legion + 0xBC) != 0
    if (u8(legion + 0x34) & 1) == 0:
        continue  // 委任或非玩家当前控制

    walk city+0xDC list with cycle/count/address guards
    require 空链 first==last==0
    require 非空链 first/last 与实际首尾节点一致
    require 每个 node 可读，prev/next 连贯，尾节点 next==0
    require node PERSON* 精确对齐且 PERSON 不重复
    for person in list:
        require person 精确对齐 PERSON 表
        require u16(person + 0x04) == person_index
        require i32(person + 0x84) in 0..3
        require person 的 +0xF4 -> +0x20/+0x64 链回指当前 city
        read current abilities from +0x50/+0x58/+0x60/+0x68
        emit as structural_candidate, acted = unknown

    independently scan PERSON table:
        require u16(person+0x04) == table_index
        container = u32(person+0xF4)
        if identity in 0..3: require container 非空且可读
        if (u32(container+0x20) & 1) != 0
           and u32(container+0x64) 精确落在 CITY 表:
            add person to that city's position-derived set

    require city-list PERSON set == position-derived active identity 0..3 set
```

任何指针未对齐、链表出现环、报告项数大于 850、读操作短读或存档切换导致前后不一致时，都应丢弃整次快照，而不是“尽量继续”。

这里的 `container+0x64` 也可能指向港、关、阵或其他设施；这不是错误，只是不归入城市集合。只有 `container+0x20 bit0` 置位并且 owner 精确落在 50 条 CITY 表时，才宣称“在城”。反过来，身份 `0..3` 的人物若其非空容器根本不可读，则视为结构污染并阻断快照。

`city+0x7A` 当前仅保留为 `raw candidate byte`。没有完成对照实验前，C# reader 输出 `IsInCombat = unknown`；它不能驱动跳过逻辑。城市交战、混乱等最终都必须服从后续定位的原生 `canExecute`/灰色原因。

### 7.1 C# reader 的故障安全上下文

Adapter 的 read report 绑定并输出：

- PID 与进程创建 FILETIME/UTC，用于区分同 PID 的不同进程代。
- 主模块基址和 SizeOfImage；读取前、读取后均通过只读句柄/模块快照复核。
- 读取完成 UTC。
- 对“完整固定表原始字节 + 规范化外部容器/链图”计算的 SHA-256 稳定摘要。

任何 PID 代、映像路径、主模块基址或大小在读取后不一致，整次快照记 blocking。该摘要只是 V1 结构快照的证据，不代替未来必须加入的场景、日期、旬、阶段和命令状态令牌，也不能单独授权执行。

投影到 Core 时显式使用 `SnapshotReadiness.StructureOnly`，并携带 PID 与进程创建 UTC ticks；不会伪装成 `PlanningReady`。因此即便调用方拿到 `GameSnapshot`，现阶段 planner 也必须因缺少已验证的场景、旬、阶段、CanAct 和命令状态而中止，而不是把 unknown 当作普通跳过。

## 8. 只读验证脚本

脚本：`tools/re/san9_v1_read.py`

C# 实机诊断：`tools/San9AutoDomestic.V1ReadDiagnostics`；合成故障测试：`tools/San9AutoDomestic.V1ReadSelfTest`。

运行当前进程：

```powershell
py -3 "C:\codex files\San9AutoDomestic\tools\re\san9_v1_read.py" --pid 22348
```

默认仅输出当前控制城市中的活动在城人物；加 `--all-cities` 可做全表一致性检查。脚本：

- 先校验磁盘 EXE SHA-256；不匹配时默认拒绝解释版本锁定偏移。
- 只以查询/读取权限打开既有进程并向 stdout 输出 JSON。
- 对表指针、节点数、循环链和 32 位用户地址做边界检查。
- 明确把 `acted_or_command_available` 输出为 `null`。
- 不调用游戏函数，不修改进程，也不把这些只读偏移宣传成可执行命令地址。

C# 合成测试覆盖：正常快照、链表成环、错误尾节点、人物记录 ID 错配、活动人物污染容器指针、A/B 固定表跨读变化、A/B 外部链图跨读变化、读取中进程代变化，以及跨越 `0xFFFFFFFF` 的读取范围拒绝。实机验收还要求 50 城的链表集合与“身份 0..3 + 位置容器回城”集合逐城完全相等。

快照 A 的核心摘要为 `controlled_city_indices=[22]`、廬江 27 名活动在城人物；快照 B 为 `controlled_city_indices=[0,8]`。两个存档态中用 `--all-cities` 检查时，50 城的在位链全部完整，且均与身份 0..3、位置链回城的集合相等。

严格 C# reader 完成后的实机复验仍是 PID `22348` 的快照 B：单段固定表与外部链图均在第 1 次 A/B 尝试稳定，主模块 `base=0x00400000, size=0x01759000`，读后进程代复核成功，结构摘要为 `64AD8430767D17C23974F8A7E168A3A0358DA5A549C911E5284BEC65AD2A8C17`；直属城市为 `0 襄平（1 人）`、`8 北海（0 人）`。同一时刻 Python `--all-cities` 复核 50 城，链表完整/集合不等计数为 0。该摘要随存档状态变化是正常现象，不是版本哈希。

## 9. 可进入下一阶段的边界

V1-read 已足以可靠回答：

- 哪些固定记录是城市、军团和人物。
- 城市属于哪个主势力、哪个委任/直属军团。
- 当前哪些城市由玩家控制，应跳过哪些委任城市。
- 哪些身份有效的武将实际在某城。
- 四项基础值和四项当前/有效能力是多少。

它还不能可靠回答：

- 某个内政按钮此刻为何变灰、该命令是否可用。
- 某名在城武将是否已行动、是否正被其他命令占用。
- 游戏对“本次最优”的真实评分、排序及并列规则。
- 如何安全提交最多五人以及如何调用/复用游戏内部执行函数。

这些必须在后续 V2-availability / V3-execute 阶段单独定位和验证；不能从本文件的只读偏移直接跨级实现。
