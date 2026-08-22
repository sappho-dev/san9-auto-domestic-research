# San9AutoDomestic STATE

> 当前状态唯一真源；覆盖式更新。快照：2026-08-12。

## 目标与产品边界

在精确 San9PK 1.0.1.0 + San9PKEasy 1.1.0.5 环境中，通过常驻游戏进程的 x86 Bridge，在游戏主线程走原生内政生命周期；不模拟鼠标键盘、不裸写商业/资金/行动位。UI 仅在用户明确确认批次后，将已绑定且重新核验的游戏窗口置于前台一次，使游戏主循环恢复运行。V1 只处理用户已在游戏中原生绑定的当前城市；自动遍历全部直属城市已在 S4 裁决为 HARD NO-GO 并从 V1 删除。

## 当前唯一任务

S5/S6 已完成：当前城市的 Commerce、Cultivate、Patrol、Train、Repair 五类命令均完成真实原生 Apply、业务后置、保存、完全重启和重载持久化复核。S7 全城能力已取消。S8 Basic 与 Wealthy combined 引擎均已完成实机和存档出口：Basic 连续完成 `Commerce -> Cultivate`；Wealthy 连续完成 `Patrol -> Commerce -> Cultivate -> Train -> Repair`；两者均通过保存、完全退出、重启和重载持久化复核。单一可见入口 `San9AutoDomestic.exe` 的 Basic/Wealthy UI 接线、封闭 controller 握手和正式打包也已完成。旧 UI 的“只最小化、未切回游戏前台”问题已经通过精确窗口前台交接修复。修复后最新 live 已成功通过 inspect、probe0、菜单握手并执行 Wealthy 第一项 Patrol（原生证据 `event=1, execute=1, apply=1`），但随后在准备第二项 Commerce 前返回 `BATCH_REBIND_REQUIRED / result=23`。当前真正未解决的问题已经从“没有注入/没有执行”推进为“第一项执行成功后，跨项绑定复核错误地终止批次”。该局失败后要求重启，未自动重试；下一步应只定位这一个跨项 rebind 条件。

### 产品 UI 正式构建（已完成，修复后尚未 UI live）

- 唯一可见用户入口：`tools/artifacts/bin/San9AutoDomestic.exe`，SHA-256 `D831DC1B1FA13D4E1BD25B55B3CC738B2B4479A537AD62EA667592F772D096E8`。
- UI 只开放“基础内政”和“有钱内政”两个按钮。启动写入型 controller 前先展示精确范围、最高费用/人数、失败不重试和 Bridge 驻留至游戏重启的确认；用户取消时不启动 controller。
- UX 启动顺序已闭合：每次点击都先刷新同一进程代的 V2 状态，换档后不沿用旧快照；同一次点击在检测成功后自动续到首次写入确认。确认前要求停留在战略地图且不要预先打开城市菜单；确认后 UI 重新核验精确 PID、创建时间、路径与 HWND，调用一次 `ShowWindow` / `SetForegroundWindow`，确认游戏确为前台后才最小化并启动 controller，使游戏主循环进入 probe0/WAITING。到达唯一 WAITING 后 UI 才恢复，再由第二次确认发送唯一信号。焦点交接失败时 controller 启动数为零；批次或待检测续跑期间两个按钮禁用。
- controller 先执行封闭的 `--inspect`；批次进程隐藏运行。只有收到唯一 `WAITING_FOR_USER_MENU_SIGNAL` 后，UI 才提示“请打开当前城市菜单”；用户再次确认后只向 stdin 写入一次 `OPEN_CURRENT_CITY_MENU`。没有鼠标、键盘、Input.Win32 或自由参数/路径 fallback，也不自动重试。
- UI 解析并展示 controller NDJSON 的逐项命令、top5、skip、批次完成、重绑和最终结果。成功必须同时满足 `BATCH_COMPLETE`、最终 `result=0` 与进程 `exit=0`。
- `STEP_SKIPPED` 已将少于 5 人、资金不足、数值已完成、本旬已执行和训练无兵力翻译为用户可读原因；`executed=0` 显示 `[NO ACTION]`，不误报 `[PASS]` 或“完成”。
- 每次点击从 UI 强制检测阶段开始，将 UI 阶段、controller stdout/stderr、WAITING、菜单信号、进程退出、最终判定与异常写入独立的时间戳会话日志，同时镜像到 `tools/artifacts/bin/logs/native-batch-latest.log`。新会话不再覆盖旧会话证据；本次正式构建没有启动 UI，因此发布后尚无新会话日志。
- 旧失败 UI smoke 的 PID `40408` 中，首轮已安装 Wealthy Bridge，但共享映射显示 `POISONED_RESTART / RESTART_REQUIRED`，probe0、request、machine、menu、evidence 与 terminal 计数全为零；`0x00604DF4` 精确指向该驻留 Bridge，证明旧 UI 未把游戏切前台、业务尚未开始。第二次点击的 `point=39/detail=512` 是自家驻留 Bridge 占用 idle slot，不是 Easy 未安装。该进程已退出。
- 修复后 live 的 PID `33720` 成功完成精确前台交接、inspect、probe0 和菜单信号。Patrol 请求以 `native_id=0` 发布，随后原生证据为 `event=1, shadow=1, execute=1, restore=1, apply=1, controller_ack=1`；紧接着 controller 输出 `BATCH_REBIND_REQUIRED executed=1 skipped=0` 和最终 `result=23`。因此焦点/注入/首项 Apply 已跑通，当前故障只在首项完成后的批次重绑定或第二项启动边界。
- 可见界面冒烟发现的三处旧只读文案矛盾已修复：V2 只读通道仍不写入，旧 V2 配置提交/停止仍禁用；绿色原生批次就绪时 Basic/Wealthy 按钮独立可用。此修正没有改变执行功能。
- 发布目录 `runtime` 精确只含三件哈希绑定依赖：
  - `controller.exe`：`7533734E1AACB3C52561C495CA54670F99DCC8AC4B6C43F5531F3E2D4522F90F`
  - `bridge_s8_basic_batch.dll`：`61F7D085D6DA47B9D6D1FDBEF73348D9E68EBA518553FFA0846C5D1F40E3ECCE`
  - `bridge_s8_wealthy_batch.dll`：`224D8A8CC750002ECCD4D0D659B3514DB4C36BD783EC6A30574F4FCCEB3E98FA`
- 正式根构建 `ce639f7b824a4922b3915ca9fb78180c` 已事务发布；UI 状态/协议测试 `29/29`，native M2b 离线测试 `112/112`，PE/确定性与既有回归全部通过；`live_loaded=0`，本次构建没有启动 controller 或访问游戏。发布事务已清理，`bin.previous` 与发布 journal 均不存在。

### S8 Basic combined 离线里程碑

- 唯一活动实现仍为 `native/San9BridgeP1EasyPingM2b`；未改 P1Wire/M2a schema，未接 UI。此条记录 Basic 当时的离线边界；Wealthy 后续离线里程碑见下。
- combined DLL 运行时锁存 Commerce/Cultivate descriptor 与已认证 request；首个可执行项走现有菜单 handoff，第二项只在 reset ACK 后由 exact idle safe point 启动，不发送第二次菜单消息。
- 正式 `build.ps1` 双根确定性构建通过；现有 PE pre/post audit 通过；offline `112/112`，`live_runs=0`。
- 正式产物目录：`tools/artifacts/San9BridgeP1EasyPingM2b/s8-formal`。
- `controller.exe` SHA-256：`E6C01C1B30E5051082319939FADB9C1D25D8897C2AD943B0461C74FFF0705C51`。
- `bridge_s8_basic_batch.dll` SHA-256：`0B66DE4E72DA5AEF31EB33DF5CB448E26C298CC3BDEFF7F7C999D89A96CE72A6`。
- 离线里程碑本身只证明构建与既有单命令回归未破坏；随后已完成下述一次真实 Basic live。

### S8 Wealthy 五项 combined 离线里程碑

- 固定顺序为 `Patrol -> Commerce -> Cultivate -> Train -> Repair`；同一 controller 会话最多一次菜单信号，后续可执行项只在 exact idle safe point 自动接续。每项重新捕获 A/B、top5、资金与命令位；已完成、命令位冲突、少于五人或资金不足只跳过当前项；绑定漂移返回 `BATCH_REBIND_REQUIRED`，不写 `root+0x38`。
- 正式 `build.ps1` 双根确定性构建通过；既有 PE pre/post audit 通过；offline `112/112`，`live_runs=0`。Commerce/Cultivate Basic 与五条单命令产物仍纳入同一正式回归。
- 正式产物目录：`tools/artifacts/San9BridgeP1EasyPingM2b/s8-wealthy-formal`。
- `controller.exe` SHA-256：`7533734E1AACB3C52561C495CA54670F99DCC8AC4B6C43F5531F3E2D4522F90F`。
- `bridge_s8_wealthy_batch.dll` SHA-256：`224D8A8CC750002ECCD4D0D659B3514DB4C36BD783EC6A30574F4FCCEB3E98FA`。
- 该里程碑本身只证明离线实现与构建闭合；随后已完成下述一次 Wealthy 真实 live。

### S8 Wealthy 五项 combined 真实 live 与持久化复核（已完成）

- game PID `9648`；本次新的等级 3 Wealthy 批次授权已消费，不得重用。
- Patrol top5：`[278,213,463,621,129]`；Commerce top5：`[79,253,386,101,523]`；Cultivate top5：`[480,51,474,586,611]`；Train top5：`[376,95,416,502,282]`；Repair top5：`[65,490,289,55,809]`。
- 批次终态：`BATCH_COMPLETE executed=5 skipped=0 rebind_required=false`，controller `result=0`。
- 独立只读后检：Patrol `469 -> 504`；Commerce `276 -> 317`；Cultivate `303 -> 341`；士气 `87 -> 100`；耐久 `598 -> 644 / 700`。
- 军团资金 `996750 -> 995750`，精确扣除四个收费命令共 `1000`；ready `34 -> 9`；兵力 `55256` 不变。
- 城市内政命令位 `0x00 -> 0x38`；Train/Repair 共用的设施 WORD 命令位 `0x0000 -> 0x0003`，五项命令位均与预期一致。
- V2 后检稳定、精确、结构重新验证通过；未见 Easy 身份或只读结构漂移。
- Wealthy 同一 Bridge、同一 controller 会话、最多一次菜单信号的五项连续原生 Apply 已跑通。用户随后把结果保存回第 4 格测试存档并完全退出；旧 PID `9648` 已消失，驻留 Bridge 随旧进程释放。
- 重启并重载第 4 格后的新 game PID `12864`，创建时间 `2026-08-12 05:37:10+08`。
- 重载后 V2 仍稳定、精确、结构重新验证通过；资金 `995750`、ready `9`，五项均只因相应命令位已置而返回 order conflict。
- 独立 ReadProcessMemory A/B 稳定：耐久 `644 / 700`、兵力 `55256`、士气 `100`、Patrol `504`、Commerce `317`、Cultivate `341`、城市命令位 `0x38`、Train/Repair 命令位 `0x0003`。
- 因此 S8 Basic 与 Wealthy combined 引擎的实机执行和存档持久化出口均已完成。

### S8 Basic combined 真实 live 与持久化复核（已完成）

- game PID `32996`；一次新的等级 3 Basic 批次授权已消费，不得重用。
- controller 首项日志：`native_id=1`，触发方式 `menu`，实际 top5 `[278,621,213,79,253]`。
- controller 第二项日志：`native_id=2`，触发方式 `idle`，实际 top5 `[463,386,101,523,480]`。
- 批次终态：`BATCH_COMPLETE executed=2 skipped=0 rebind_required=false`，controller `result=0`。
- 独立只读后检：军团资金 `997883 -> 997383`，精确扣除两项共 `500`；ready `34 -> 24`；Commerce=`276`；Cultivate=`303`；城市 order=`0x30`。
- V2 后检仍为稳定、精确、结构重新验证通过；未见 Easy 身份或只读结构漂移。
- 用户把本次结果保存回第 4 格测试存档，完全退出游戏；旧 PID `32996` 已消失，驻留 Bridge 随旧进程释放。
- 重启并重载第 4 格后的新 game PID `9648`，创建时间 `2026-08-12 05:13:27+08`。
- 重载后 V2 仍为稳定、精确、结构重新验证通过；军团资金 `997383`、ready `24`，Commerce 与 Cultivate 均只因各自 order bit 已置而返回 order conflict。
- 独立 ReadProcessMemory A/B 稳定：Commerce `276`、Cultivate `303`、order `0x30`。
- 因此同一 Bridge、同一 controller 会话和一次菜单信号内的两项原生命令连续 Apply 已跑通，并且资金、商业、开垦与命令位均通过保存、完全重启和重载持久化复核。S8 Basic 第一片的实机与存档出口全部满足。

### 本次真实 Apply 证据

- 用户明确授权等级 3 写入；使用游戏内第 4 格测试存档 `D_Sav003.S9`，不是第 1 格原档。
- game PID `16212`，当前城乌丸 `city=46`、军团 `corps=1`。
- 冻结并实际使用的五人：`[278,621,213,79,253]`。
- controller 终态：`result=0,state=8,restart_required=0,event=1,shadow=1,execute=1,restore=1,menu_wake=1,menu_restore=1,apply=1,controller_ack=1`。
- Bridge 构造原生 Commerce command 后交还游戏原生 driver；validator、attach、task tick 与 Apply 均由游戏自身状态机执行。Bridge 没有直接写商业、资金、命令位或人物行动位。
- 随后独立 V2 只读报告：`dataRead=True, blocked=False, rawStable=True, codeExact=51/51, structureRevalidated=True`。
- 独立 ReadProcessMemory A/B（间隔 120 ms）稳定后置：乌丸商业 `114 -> 226 / 600`；军团资金 `1,000,000 -> 999,750`；城市命令位 `0x00000010`；五人的 busy bit `0x1000` 均已置；乌丸 ready `34 -> 29`。
- V2 对乌丸 Commerce 返回 `KnownStaticSubset=False`，唯一失败 `COMMAND_ORDER_BIT_CLEAR`，与商业本旬已执行完全一致。

这是第一条真正跑通的无输入、进程内原生商业，不是 ping、计数或 NO_APPLY。native Apply 累计：`1`。

### 保存重载证据（S5 出口）

- 用户把结果保存回第 4 格测试存档，完全退出并重启游戏，再载入第 4 格。
- 新游戏进程 PID `14976`，创建时间 `2026-08-11T19:36:36.3238193+08:00`，与执行 Apply 的 PID `16212` 不同，证明旧驻留 Bridge 已随旧进程释放。
- 重载后 V2 仍为 `dataRead=True, blocked=False, rawStable=True, codeExact=51/51, structureRevalidated=True`。
- 重载后独立 ReadProcessMemory A/B 稳定：乌丸商业 `226/600`、资金 `999750`、order `0x00000010`、人物 `[278,621,213,79,253]` 的 busy bit 全为 `1`；V2 ready 仍为 `29`。
- 因此商业增量、扣款、命令位、人物行动占用均由游戏原生存档生命周期正确保存并恢复。S5 出口条件全部满足。

### S6 开垦真实 Apply（已完成）

- 在重载后的第 4 格测试存档、新 game PID `14976` 上，用户另行明确授权一次乌丸开垦 Apply；失败不重试。
- 冻结并实际使用的五人：`[463,386,101,523,480]`。
- controller 终态：`result=0,state=8,restart_required=0,event=1,shadow=1,execute=1,restore=1,menu_wake=1,menu_restore=1,apply=1,controller_ack=1`。
- V2 后检仍为 `dataRead=True, blocked=False, rawStable=True, codeExact=51/51, structureRevalidated=True`；乌丸 ready `29 -> 24`，Cultivate 唯一失败为 `COMMAND_ORDER_BIT_CLEAR`。
- 独立 ReadProcessMemory A/B 稳定后置：开垦 `209/600`；资金 `999750 -> 999500`；order `0x10 -> 0x30`；人物 `[463,386,101,523,480]` 的 busy bit 全部置位；商业保持 `226`。
- 用户随后保存到第 4 格、完全退出并重启，再载入第 4 格。新 game PID `32964`，创建时间 `2026-08-12T03:34:22.1382068+08:00`。
- 重载后 V2 仍为全量稳定精确读；独立 RPM A/B 保持开垦 `209/600`、资金 `999500`、order `0x30`、第二批五人 busy bit 全为 `1`，商业仍为 `226`。
- native Apply 累计现为 `2`（Commerce 1、Cultivate 1）。开垦子项的实机与存档出口全部满足。

### S6 巡察真实 Apply（已完成）

- 在第 4 格测试存档、新 PID `32964` 上，用户单独授权一次乌丸 Patrol Apply。
- 实际选择 `[129,498,289,57,65]`；controller 终态 `result=0,state=8,restart_required=0,event=1,shadow=1,execute=1,restore=1,apply=1,controller_ack=1`。
- V2 后检全量稳定精确；乌丸 ready `24 -> 19`，Patrol 唯一失败变为 `COMMAND_ORDER_BIT_CLEAR`。
- RPM A/B 稳定后置：民心 `469/1000`；资金 `999500 -> 999250`；order `0x30 -> 0x38`；第三批五人 busy bit 全部置位；商业 `226`、开垦 `209` 保持不变。
- 用户保存到第 4 格并完全重启重载。新 PID `2172`，创建时间 `2026-08-12T03:55:05.3534929+08:00`。
- 重载后 V2/RPM A/B 保持民心 `469/1000`、资金 `999250`、order `0x38`、第三批五人 busy bit 全为 `1`；商业 `226`、开垦 `209` 均保持。
- native Apply 累计 `3`。Patrol 子项出口全部满足。

### S6 训练真实 Apply（已完成）

- 在第 4 格测试存档、新 PID `2172` 上，用户单独授权一次乌丸 Train Apply；费用必须为 0。
- pre：兵力 `55256`、士气 `70/100`、训练 order word `0x0000`、资金 `999250`；冻结五人 `[376,95,416,502,282]` 均未占用。
- controller 终态 `result=0,state=8,restart_required=0,event=1,shadow=1,execute=1,restore=1,apply=1,controller_ack=1`。
- V2 后检全量稳定精确，乌丸 ready `19 -> 14`，Train 唯一失败变为 `COMMAND_ORDER_BIT_CLEAR`。
- RPM A/B 稳定后置：士气 `70 -> 87/100`；资金保持 `999250`；训练 order `0x0000 -> 0x0002`；第四批五人 busy bit 全部置位；兵力 `55256`、民心 `469`、商业 `226`、开垦 `209` 均保持。
- 用户保存到第 4 格并完全重启重载。新 PID `32852`，创建时间 `2026-08-12T04:17:18.1047548+08:00`。
- 重载后 V2/RPM A/B 保持士气 `87/100`、资金 `999250`、训练 order `0x0002`、第四批五人 busy bit 全为 `1`。
- native Apply 累计 `4`。Train 子项出口全部满足。

### S6 修筑真实 Apply（已完成）

- 在第 4 格测试存档、新 PID `32852` 上，用户单独授权一次乌丸 Repair Apply。
- pre：耐久 `575/700`、order word `0x0002`、资金 `999250`；冻结五人 `[490,55,474,809,611]` 均未占用。
- controller 终态 `result=0,state=8,restart_required=0,event=1,shadow=1,execute=1,restore=1,apply=1,controller_ack=1`。
- V2 后检全量稳定精确，乌丸 ready `14 -> 9`，Repair 唯一失败变为 `COMMAND_ORDER_BIT_CLEAR`。
- RPM A/B 稳定后置：耐久 `575 -> 598/700`；资金 `999250 -> 999000`；order word `0x0002 -> 0x0003`；第五批五人 busy bit 全部置位；民心 `469`、商业 `226`、开垦 `209`、士气 `87` 均保持。
- 用户保存到第 4 格并完全重启重载。新 PID `32996`，创建时间 `2026-08-12T04:40:06.7755426+08:00`。
- 重载后 V2/RPM A/B 保持耐久 `598/700`、资金 `999000`、order word `0x0003`、第五批五人 busy bit 全为 `1`；民心 `469`、商业 `226`、开垦 `209`、士气 `87`、domestic order `0x38` 全部保持。
- native Apply 累计 `5`。Repair 子项与 S6 全部出口满足。

### 当前进程代边界

执行 S8 Wealthy 的旧 PID `9648` 已退出，驻留 Bridge 已释放。失败 UI smoke 的 PID `32452` 与最近一次失败 smoke 的 PID `40408` 均已完全退出；两次都在业务请求发布前停止，驻留 Bridge 已随进程释放。此前所有等级 3 授权及失败 smoke 的授权均不得复用；最新正式构建没有启动 controller 或加载 Bridge。

## 本次 APPLY_ONCE 产物

目录：`tools/artifacts/San9BridgeP1EasyPingM2b/final-apply-once-offline`

| 文件 | SHA-256 |
|---|---|
| `controller.exe` | `C3DE59E33470037271DCC6B9CC77B65381553F19270F18A457C8CEB9CBBB92EA` |
| `bridge_apply_once.dll` | `C352933857551154CA1B02DEDD7F153EC61137C5C864B96BD78F997A45B5D55C` |
| `bridge.dll`（NO_APPLY） | `0E53583E796BEE5EEB87A9D783552A5C288E17F07305539CE721F60AE8B46BC1` |
| `offline.exe` | `7A0BA54ECCBA0A2DF2F7954CAECFA37AC1B48EB8E3AE40764654296FE2743681` |

构建把 NO_APPLY 与 APPLY_ONCE DLL 物理分开；只有 `--s5-apply-once` 加精确确认词才选择写入版。离线构建 104/104、PE pre/post audit 通过、双根确定性一致；这些只作为产物完整性证据，阶段进度以上述真实 Apply 为准。

S6 Cultivate 产物目录：`tools/artifacts/San9BridgeP1EasyPingM2b/final`。`controller.exe` SHA `09154668D588B856437BEF2E7568CBE054C0358E5CDB097CAA113FB4E34D5201`；`bridge_s6_cultivate_apply_once.dll` SHA `6762D9F6BD74394209F1A05646621EE7EFFF8B0FB1CE12D009C03A96D9E6CAE3`。

S6 Patrol 最新产物同目录：`controller.exe` SHA `80BF37015C390CA9309F0834EDB9993792BDB8DAB56AED7B9A111B6735146309`；`bridge_s6_patrol_apply_once.dll` SHA `AACC7D5321E10963912F5447335FF94147FDB35BF566647B96BF13ECC2EA2216`。

S6 Repair 最新产物同目录：`controller.exe` SHA `F6482C68EAF3CDCA03270D59278CDAF999EB45E7D32C176FC6625639299DB500`；`bridge_s6_repair_apply_once.dll` SHA `90DA1FBACC3E484F7AB6DB487411D51AE7A6CB8DF4101C8CA8DC30A0D934BD47`。

## 精确支持环境

| 文件 | 版本/字节 | SHA-256 |
|---|---:|---|
| `D:\三国志9\10101749\San9PK.exe` | x86 1.0.1.0 / 2,636,800 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| `D:\三国志9\10101749\San9PKEasy.exe` | x86 1.1.0.5 / 24,576 | `CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07` |
| `D:\三国志9\10101749\Easy.dll` | x86 / 32,768 | `E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0` |

窗口类 `KOEI_SAN9WINDOW`；启动顺序固定为游戏 → Easy 注入完成 → 助手验证 → Bridge。

## 存档边界

- 原档：`%USERPROFILE%\Documents\Koei\San9 TC\Savedata\D_Sav000.S9`（游戏第 1 格），禁止测试写入。
- 工作副本：同目录 `D_Sav003.S9`（通常为游戏第 4 格）。S8 Basic 与 Wealthy 的结果均已保存回该槽，并完成完全退出、重启和重载持久化验收。任何 UI smoke 只能在用户另行选择新的游戏旬次和测试档授权后进行。

## 冻结资产

- Easy ownership：`docs/easy-compatibility-manifest.json`，32 重定向 + 6 写点，SHA `72E1B0A8…90CDE`。
- P1Wire：`native/San9BridgeP1Wire` 与 `prototypes/San9AutoDomestic.P1Wire`，永久冻结。
- M1 Easy gate：`native/San9BridgeP1EasyPing`，冻结。
- M2a mailbox：`native/San9BridgeP1EasyPingM2`，冻结。
- 唯一活动实现目录：`native/San9BridgeP1EasyPingM2b`。

禁止扩展 P1Wire schema、复制协议实现或在 C# 侧实现 wire/进程发现。

## Commerce 已闭合的原生链

command id `1`；root event `0x2711`；dispatcher `0x5179B0`；handler vptr `0x609F38`；handler Execute `+0x28`；PersonList ctor/append/dtor `0x470DF0/0x46EF70/0x470E10`；command allocator/ctor `0x5DEC20/0x48B340`；command vptr `0x607D98`（22 槽）；validator `0x48B4B0`；attach `0x47E6F0`；driver `0x50EAB0`；command Apply `+0x30 = 0x48B5D0`。

Bridge 的一次性路径：冻结当前城与原生 top5 → 构造本地 PersonList → 原生 command ctor → command vtable 只替换 Apply 槽用于 exact-once 计数 → 返回 command 给原生 driver → Apply wrapper 恢复原 vptr并调用原 Apply 一次 → 等 root idle → A/B 后置验证。

## 授权状态

等级 3 的 Commerce、Cultivate、Patrol、Train、Repair 五次单次授权、S8 Basic combined 批次授权、S8 Wealthy combined 批次授权，以及首次失败 UI smoke 的授权均已消费；不得重用。当前没有新的业务写入授权；修复后的 UI smoke 也必须取得新的明确授权。

## 预算

总预算 `3,000,000`。历史已登记基线 `694,754`；后续未创建平台 Goal，因此不虚构精确词元使用量。S5、S6、S8 combined 引擎与产品 UI 正式构建均已完成；S7 全城能力已取消且预算不自动挪用。首次 UI live 在业务请求前失败且已闭合根因，修复后尚未重新 live。

## 下一步

1. S8 Basic 与 Wealthy 引擎、实机执行、存档出口和产品 UI 接线已经完成，不再重复开发或执行既有批次。
2. 前台交接、隐藏 controller、WAITING、窗口恢复、菜单握手与第一项 Apply 均已由修复后 live 证明。下一步只定位“Patrol 成功后为何返回 BATCH_REBIND_REQUIRED”，不得回头扩写焦点、注入或安全门设计。
3. 当前游戏进程代要求重启，且本次授权已消费。任何新的 live 验证仍需新的测试旬次/存档和等级 3 明确授权；不得自动启动、重试或复用历史授权。

## Git

工作树非 clean，且大量实现目录未跟踪。禁止 reset/checkout 覆盖；以文件哈希、可重复构建和实机日志为证据。
