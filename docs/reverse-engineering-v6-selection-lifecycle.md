# V6 选人生命周期只读取证

## 1. 结论与边界

V6 新增的是一个**永久非授权、默认不进入 live** 的选人生命周期取证工具：

- 工具：`tools/re/san9_v6_selection_probe.py`
- 无参数：只校验固定磁盘目标，不打开进程；
- `--self-test`：只使用合成内存，不读取磁盘目标、不打开进程；
- `--pid <PID>`：分析者显式指定后，才允许对该单一 PID 做只读观察；
- 所有输出只写 stdout；脚本没有日志、缓存或其他文件写入路径；
- live 句柄权限固定为 `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`（`0x0410`）；目标内存只经 `VirtualQueryEx` 和 `ReadProcessMemory` 读取；只读冲突清单另用 Toolhelp process/module snapshot；
- 不包含 `WriteProcessMemory`、调试附加、远程线程、DLL 注入、Hook、游戏函数调用、窗口/键鼠输入或 Computer Use。

它只能证明某个短时间窗口内的对象结构和 A/B 稳定性，不能授权自动选择、提交或任何进程内业务执行。自动命令执行仍为 **NO-GO**。

## 2. 精确目标锁

V6 不提供替换目标路径或“允许未知版本”的开关。固定身份如下：

| 项 | 精确值 |
|---|---|
| 路径 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0`（由精确 SHA 锁定） |
| 文件大小 | `2,636,800` bytes |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| PE | x86 / PE32 |
| ImageBase | `0x00400000` |
| SizeOfImage | `0x01759000` |

默认磁盘模式先一次性读取文件，先核对规范路径、解析后的真实路径、大小和 SHA-256；哈希不符时不会解释任何版本专属 VA。哈希通过后才复核 PE machine、optional-header magic、ImageBase 和 SizeOfImage。

live 模式在同一个只读句柄上保存并反复核对：显式 PID、`GetProcessTimes` 创建 FILETIME、`QueryFullProcessImageNameW` 精确路径，以及加载映像的 PE 身份。每个 A/B pass 前后均复核；PID 创建代、路径或加载映像任一变化即丢弃样本。每个已接受样本之后还会重新哈希固定磁盘目标。

每个完整 pass 的目标读取前后还各执行一次 fail-closed 冲突门：Toolhelp 全进程枚举必须成功，并拒绝 `San9PKEasy.exe`、`San9PKHard.exe`、`SanIXPKCheat.exe`；目标 PID 的 32/64 模块枚举必须成功，主模块必须唯一且路径/base/size 精确一致，并拒绝 `Easy.dll`、`SanIXSpy.dll`、`San9Common.dll`，以及从游戏目录加载的 `version.dll`、`dinput.dll`、`dinput8.dll`、`winmm.dll`、`dsound.dll`。模块清单摘要在 pass 前后必须完全相同；任何枚举失败、条目缺失或清单变化都拒绝样本。会话开始和关闭前也重新执行该门。

## 3. 默认安全 CLI

```powershell
# 默认：只读磁盘，不打开任何进程
python tools\re\san9_v6_selection_probe.py

# 纯内存合成测试；不读目标文件、不打开进程
python tools\re\san9_v6_selection_probe.py --self-test

# 只有显式 PID 才进入 live；样本数和间隔都有硬上限
python tools\re\san9_v6_selection_probe.py --pid 19876 --samples 20 --interval-ms 100
```

`--pid` 取值为 `1..4294967295`，`--samples` 为 `1..120`，`--interval-ms` 为 `0..10000`。没有 `--pid` 时携带 live 参数会拒绝。Ctrl+C 返回 130，退出上下文时关闭只读句柄。工具没有“自动找 PID”、无限采样、原始地址猜测或未知版本继续执行模式。

文中 PID `19876` 只是 2026-08-07 已完成样本的进程代标识，不是未来运行时可以复用的身份。以后即使数值 PID 再次相同，也必须以新句柄取得的新创建 FILETIME 为另一进程代。

## 4. UI task 唯一扫描与结构门

工具只扫描 32 位用户空间内 `MEM_COMMIT + MEM_PRIVATE + 可读写 + 非 PAGE_GUARD` 的堆区域；这是动态 UI task 的预期分配域。扫描覆盖不完整（区域数、扫描字节数或命中数超过硬上限）时拒绝，不能用局部结果宣称“唯一”。五类精确 vptr 为：

| 命令 | UI task vptr | 原生源排序字段 |
|---|---:|---|
| 巡察 | `0x0060B920` | `person+0x58` 当前智力 |
| 商业 | `0x0060CCB0` | `person+0x60` 当前政治 |
| 开垦 | `0x0060BBA0` | `person+0x60` 当前政治 |
| 修筑 | `0x0060BCE8` | `person+0x68` 当前统率 |
| 训练 | `0x0060C370` | `person+0x50` 当前武力 |

稳定 pass 中允许两种结果：五类 vptr 全部缺席，或全空间恰好一个命中。两个及以上命中立即 fail-closed。唯一命中还必须再次从对象首地址读取 vptr；若已变化，后续 `+0x6AC/+0x6CC/+0x6EC` 不解释。

### 4.1 三条 task 人物链

| task 偏移 | count | 用途 | 上限 |
|---:|---:|---|---:|
| `+0x6AC` | `+0x6B8` | committed/final | 5 |
| `+0x6CC` | `+0x6D8` | source | 850 |
| `+0x6EC` | `+0x6F8` | working/current | 5 |

每个链头按 16 字节对象解释：type/vptr、first、last、count；每个节点按 `next, previous, PERSON*` 三个 32 位字段解释。验证条件包括：

- 三个链对象 vptr 必须精确等于本版本人物链类型 `0x00606C8C`（构造器 `0x00470DF0` 的写入值）；
- 空链必须同时 `first=last=0`；非空链必须端点非空；
- 按 declared count 精确遍历，首节点 previous 为 0，双向链接连续，节点不重复、不成环，count 后 next 必须为 0，最终节点必须等于 last；
- payload 必须精确落在 `PERSON_BASE + id*0x128`，并且 `person+0x04` 的嵌入 ID 等于表 index；同链人物不得重复；
- working/committed 必须是 source 子集，且人数不超过 `min(5, source_count)`。

固定 PERSON 表为 `0x01258EE0`、stride `0x128`、850 项。每个 pass 一次捕获整张表并验证全部 850 个嵌入 ID；A/B 摘要覆盖每人的 ID、四项当前能力、身份、`+0xE8` ready/busy 标志和 `+0xF4` residence 指针。

### 4.2 原生 source 顺序

V6 不按人物 ID 重排，也不自行发明 tie-break。source 中每个人都必须满足：身份 `0..3`、`person+0xE8 bit 0x1000 == 0`、能力值不超过已验证上限 255；`person+0xF4 -> container+0x20 bit0 -> container+0x64` 必须解析到同一条精确 CITY 表记录。

随后工具严格读取该城 `city+0xDC` 原生在位人物链，复核城市 vptr/type、节点、PERSON 指针以及每个人的 residence 回指。source 必须等于居民链中 ready 人员按当前命令能力**稳定降序**排列的完整结果：能力相同者保留 `city+0xDC` 原相对顺序。输出的 `native_source_top5` 只截取这个已经验证的原生 source 前五，不另行排序。

全局选择链 `0x015455AC` 同样按完整节点/PERSON/人数不超过五验证，并参与 A/B 摘要。存在活动 task 时，它还必须使用同一人物链 type/vptr；但工具不会仅凭全局链就声称它必然属于当前 task，而是分别报告两者。

## 5. A/B 与对象释放规则

每份可输出样本由两个完整 pass 组成。A、B 都重新执行：进程代/路径/加载 PE 门、前置冲突进程/目标模块门、全 PERSON 表读取、全堆五类 vptr 唯一扫描、唯一 task 三链、城市居民/原生 source、全局选择链、后置冲突门，再做读后身份复核。规范摘要必须逐字段完全相同；不稳定最多重试三次，仍变化则不输出业务解释。

采样器不会缓存一个 task 指针并在下一轮直接解引用。每一轮都必须重新全扫描并重新通过唯一性和三链验证。前一轮 task 地址/vptr 在后一轮缺席或改变时，本地生命周期只输出 `task_lifetime_ended`，其中仅保留旧地址、旧 vptr 和本地 lifetime ID；不会附带或重新读取旧三链。若同一数值地址以后再次出现，即使 vptr 相同，只要中间观察过缺席，也建立新的 lifetime ID。

这一规则非常重要：**旧 task 释放后，该地址的数据可能是 allocator 残留，也可能已被另一对象复用；从 vptr 改变或身份丢失开始，旧地址上的 `+0x6AC/+0x6CC/+0x6EC` 永久失去解释资格。** 轮询无法排除两次采样之间“释放后在同地址、同 vptr 立即重建”的不可观察事件，因此 V6 仍只是取证护栏，不是执行授权机制。

## 6. 2026-08-07 商业人工原生提交证据

以下数据来自此前已完成的、干净原版 PID `19876`、一次性复制存档上的“江陵・商业”人工原生操作。本轮编写 V6 工具和文档没有再次读取该游戏进程。

| 观察点 | 只读动态证据 |
|---|---|
| 活动选人 task | `0x001AEEA4` |
| task vptr / 命令 | `0x0060CCB0` / 商业 |
| 原生 source | 43 人；政治稳定降序，前五为 `253,514,507,467,701` |
| working | `0 -> 5`，精确 ID `253,514,507,467,701` |
| global selected | 5 人链 |
| 城市商业 | `230 -> 286` |
| 军团金 | `15273 -> 15023`，差额 250，与五人每人 50 一致 |
| 人物占用 | 上述五人的 `person+0xE8 bit 0x1000` 全部置位 |
| 城市本旬项目位 | `city+0x1E0 bit 0x10` 置位 |
| 后续 ready/source | `43 -> 38`；再次读取时商业变灰 |

证据支持的最窄结论是：这一次商业流程中，原生 source 前五进入 working，提交阶段出现五人全局链，最终城市值、资金、五人占用和项目位同时发生了与原生商业命令一致的变化。它没有证明五种命令的所有异常分支，也没有证明可从外部安全调用内部函数。

提交开始后，原 task 随即析构或被复用；因此不能继续把 `0x001AEEA4` 当作永久对象地址。提交后的结果验收必须使用短期全局链证据和全新的 V1/V2 稳定快照，不能沿用旧 task 内存。

## 7. 离线验收与仍未授权事项

允许的离线验收命令：

```powershell
python -m py_compile tools\re\san9_v6_selection_probe.py
python tools\re\san9_v6_selection_probe.py --self-test
```

合成测试覆盖有效三链/原生顺序、环、反链错误、非 PERSON 指针、PERSON 槽 ID 冲突、能力排序错误、同分居民顺序错误、选择不属于 source、`min(5,source)` 上限、同链人物重复、旧 task 生命周期释放，以及干净/已知进程/已知模块/游戏目录代理/不完整模块清单五类冲突门。它不连接游戏，也不能替代未来由用户明确启动的只读 live 取证。

V6 明确不提供以下能力：选中人物、写链表、发送输入、调用确认、提交命令、修改资金/城市/人物标志、注入桥或打开产品执行按钮。`execution_authorized` 在所有正常和错误输出中均为 `false`。
