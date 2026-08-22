# San9AutoDomestic 当前开发情况与 Claude 交接说明

> 快照日期：2026-08-09
> 仓库：`C:\codex files\San9AutoDomestic`
> 分支：`main`
> 代码基线：`a9fd903 Add audited offline command broker`
> 工作树：生成本文档前为 clean
> 项目状态：**未完成；现有正式 UI 仍为只读预览件**

## 0. 给 Claude 的最短结论

这个项目已经完成了需求建模、配置化计划、目标版本锁定、只读城市/武将/命令门扫描、只读方案预览、常驻 UI、人工单命令全生命周期验证，以及多套离线事务/防重放安全契约。

但是，**助手尚未自动向游戏提交过任何一条内政命令**。两个方案按钮仍然禁用。当前最关键的两个技术缺口是：

1. V5.2 的一次性 `ping-only` 主线程桥还没有经过用户授权的 live 试验；
2. 精确版本中尚未找到可验证的 `cityId/CCityData* -> domestic controller target` 原生 setter，任意城市绑定仍未闭合。

按“用户能否点击按钮完成全城内政”计算，当前实际完成度约 **55%**。代码量和测试数量很大，但不能据此把用户可用完成度说成 80% 或 90%。

后续应收缩开发方式：不再继续堆平行原型；只做“单城单命令真实自动执行”这一条纵切，成功后再接全城和两个方案。

## 1. 原始产品目标

目标游戏：`D:\三国志9\10101749\San9PK.exe`，《三国志 9 PK》1.01，32 位。

用户需要一个独立小程序窗口，提供两个配置驱动的方案：

- 基础版：`商业 -> 开垦`
- 有钱版：`巡察 -> 商业 -> 开垦 -> 训练 -> 修筑`

执行语义：

- 遍历玩家当前能够直接控制的全部城市；委任军团城市跳过；
- 每座城市按方案中配置的项目顺序依次尝试；
- 每个项目必须选择**恰好 5 人**；不足 5 人则只跳过该项目；
- 每个项目都从当时仍可行动的武将中重新选择本次最优 5 人；
- 已被前序项目使用的武将不能在本旬复用；
- 高优先级项目变灰、已执行、满值、资金不足或原生门不通过时，跳过并继续后续项目；
- 城市按稳定顺序处理，共享同一军团资金，因此前序城市会影响后序城市；
- 不自动推进旬；
- 配置可以增删、重排、启停现有五类项目，不应重写 planner；
- 未知命令和越界配置必须 fail-closed。

用户明确限制：

- 不使用 Computer Use；
- 不修改 `San9PK.exe`；
- 不用 `version.dll` 侧载；
- 严格校验目标版本、进程代和修改器冲突；
- 从只读扫描和 dry-run 开始，再做复制存档单命令、全城和异常回归。

## 2. 精确目标与环境边界

唯一支持的目标：

| 字段 | 值 |
|---|---|
| 路径 | `D:\三国志9\10101749\San9PK.exe` |
| 文件版本 | `1.0.1.0` |
| 文件大小 | `2,636,800` bytes |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| PE Machine | `0x014C` / i386 |
| ImageBase | `0x00400000` |
| SizeOfImage | `0x01759000` |
| 窗口类 | `KOEI_SAN9WINDOW` |

任一字段不一致都必须阻断。相同哈希但路径不同也按不支持处理。

已知冲突包括：

- `San9PKEasy.exe` / `Easy.dll`
- `San9PKHard.exe`
- `SanIXSpy.dll`
- 游戏目录代理 DLL：`version.dll`、`dinput.dll`、`dinput8.dll`、`winmm.dll`、`dsound.dll`

项目最早的“点了没反应”已经定位到游戏目录本地 `VERSION.dll`：它是 `speedhack_rs` 代理/Hook DLL，不是 Windows 系统 DLL；当时三个崩溃都为该模块卸载后的 `0xc0000005`。因此本项目明确禁止再次使用 `version.dll` 作为加载入口。

## 3. 当前可运行程序

常驻 UI：

`C:\codex files\San9AutoDomestic\tools\artifacts\bin\San9AutoDomestic.exe`

已验证发布信息：

- BuildId：`45b855203b6e4af1897ec4317612812b`
- SHA-256：`5CDFB6ED28656D4974C0E47F484418739C30BEE0909BF881BC8CE58F81245449`

当前 UI 行为：

- 无游戏、连接失败、配置错误或只读阻断时保持窗口打开；
- 每 2.5 秒只做轻量 presence 检查；
- 完整 V2 读取只在上线、换进程代、手动刷新或有限退避后运行；
- 可显示配置生成的全部方案与只读预览；
- 两个执行方案按钮保持禁用；
- 状态横幅明确显示“只读预览 · 一键执行尚未开放”；
- 第二次启动会唤醒已有窗口；
- `V0/V1/V2/V4/Diagnostics/Probe/SelfTest` 等 EXE 都是有界工程工具，会自行退出，不是主程序。

不要把 UI 能启动、能列城市、能预览候选解释为可执行版本。

## 4. 已完成模块与证据

### 4.1 Core 与配置

已实现：

- 严格 JSON 字段、类型和值域校验；
- 配置冻结与计划编译；
- 基础版/有钱版默认方案；
- 城市稳定顺序；
- 项目顺序耗人；
- 恰好 5 人；
- 灰项、人数不足、资金不足、保留金、委任城市跳过；
- 同军团跨城市共享资金；
- 训练零费用，其余四项每人 50；
- 单批次门闩与 fatal/abort 语义。

Root 测试基线：Core `44/44`。

配置入口：`config/default.json`。

### 4.2 V0 目标与冲突门

目录：`src/San9AutoDomestic.Adapter.San9Pk101`

能力：

- 精确路径、版本、大小、哈希、i386 校验；
- 唯一 `KOEI_SAN9WINDOW` 与进程映像路径交叉发现；
- 只以 `QUERY_LIMITED_INFORMATION | VM_READ` 打开；
- 检查冲突进程、模块和代理 DLL；
- 无写入、注入、远程线程和输入模拟 API。

### 4.3 V1 只读结构快照

V1 对 CITY 到 PERSON 固定表做完整 raw A/B 双读，并对动态容器和城市 resident 链做规范化双读。

已确认：

- 玩家主势力；
- 全部军团与直属控制标志；
- 50 城结构；
- 城市所属军团与直属城市；
- 城内武将集合；
- 武将当前统率、武力、智力、政治；
- 人物/容器/链表 ID、owner、环、尾、数量、集合一致性。

V1 只输出 `StructureOnly`，不能伪装成可执行快照。

### 4.4 V2 只读命令可用性与方案预览

已定位五类命令的已知静态门、ready 位、order 位、值域门、费用与候选源顺序。

当前只读排序：

- 巡察：智力；
- 商业、开垦：政治；
- 训练：武力；
- 修筑：统率；
- 同分保留 `city+0xDC` 原始顺序。

重要限制：默认配置中的 `native_best` 当前实际应用的是明确标注的 `StaticStatProxy`，尚不是动态验证过的游戏原生推荐函数结果。

V2 当前仍永久：

- `PlanningReady=false`
- `VerifiedNativeCapability=false`
- `IsActionable=false`
- `CommitAuthorized=false`

Root 测试基线：V2 `25/25`，live 静态锚点 `51/51`。

### 4.5 UI 生命周期

已完成无游戏等待、上线/换代检测、异常留窗、有限重试、日志上限、按钮/Tooltip 代际清理、单实例激活和顶层异常提示。

UI state 测试：`19/19`。

### 4.6 人工单城单命令全生命周期

在专用复制存档上，用户通过游戏原生 UI 手工完成过一条：

`江陵 -> 商业`

只读观察证据：

- 城市 ID：`30`
- 军团：`1`
- 原生 ready 候选：43 人
- 政治前五 ID：`253, 514, 507, 467, 701`
- 商业：`230 -> 286`
- 军团金：`15273 -> 15023`，精确扣除 `250`
- 五人 busy 位全部置 1
- 商业 order 位 `city+0x1E0 bit4` 置 1
- ready 候选：`43 -> 38`

保存并通过游戏原生 UI 重载后，上述状态完整保留。下一旬：

- 商业仍为 `286`
- order 位清零
- 五人 busy 位清零
- 商业重新可执行
- 旬结算使资金独立变化为 `14934`

该证据证明原生命令的实际副作用、持久化和旬复位模型；**不证明助手已经可以提交命令**。

### 4.7 V8 离线单命令事务契约

目录：`prototypes/San9AutoDomestic.V8Transaction`

冻结内容：

- 五类精确命令 descriptor；
- city/corps；
- 原生顺序的恰好五人；
- sequence/deadline；
- process generation/context digest；
- request fingerprint；
- mutation 前 skip；
- mutation 后任何不确定均 `HaltRestart/AbortUncertain`；
- action/cleanup 实际入口的一次性 CAS；
- cleanup 后不可缓存的有序 A/B 验收契约。

结果：`55/55`，另有并发重复 `20/20`。

生产 DLL SHA-256：

`06690240E44448748F3B2A1A7CAA2F9073253B0A32C3AE328DC1DE8B5B47DEEF`

V8 没有被 Core、UI、V5.2 或 native adapter 引用；无 P/Invoke、进程访问或 callback 实现。

### 4.8 V10 离线原生适配数据契约

目录：`native/San9BridgeV10`

冻结内容：

- 五命令全部 native ID、费用、outer task/event、handler、CanExecute、ExecuteUI、factory case；
- 七阶段：BindTarget、OpenOuter、OpenSelector、Clear、NativeFillMax、AcceptInner、AcceptOuter；
- V8 cleanup 数值 `1..6`；
- 176-byte x86 request ABI；
- 跨语言复刻 V8 request fingerprint。

结果：

- internal `45/45`
- independent oracle `202/202`
- PE audit `27/27`

DLL SHA-256：

`8AECD7C80C6BE0E531F5216DC1883E5B181F2F35AC3F24A10E8562C2D8B9C3EC`

V10 只是离线数据契约，不含进程、窗口消息、游戏函数调用、Hook、输入或写入能力。

### 4.9 P0 单命令影子纵切

目录：`prototypes/San9AutoDomestic.SingleCommandSlice`

P0 已把真实 Basic/Wealthy 配置、城市顺序、任务顺序、exact-five、共享资金、训练零费和 skip 语义连成单命令 shadow 流程。

它曾发现并修复一个真实重入问题：provider 在锁内回调 `Stop` 后，旧 run 可被复活并与第二 run 并存。最终 provider 完全锁外，回锁严格核对 nonce/owner/run/ticket/cursor/generation/state。

结果：

- 先运行 V8 `55/55`
- P0 `46/46`
- 独立重复 `20/20`

Library SHA-256：

`F6253F961F4A4F5D5B20298987DBE511FDCB469827AC09C713DC7D24E320EB38`

P0 的 commit 只更新合成内存账本，不会提交游戏。

### 4.10 P1 离线 CommandBroker

目录：`prototypes/San9AutoDomestic.CommandBroker`

已实现：

- current-user-only named mutex 单飞；
- protected/current-SID-only/reparse-free 目录契约；
- checksummed append-only session ledger；
- exclusive/write-through execution journals；
- durable replay identity：`(process session, request fingerprint, stage ordinal)`；
- 只换 `ConsentId` 不能重放；
- `AbortUncertain`、写盘不确定、非终态 Dispose、release timeout/failure 和 restart-required scan 永久锁存 Host；
- release timeout 显式保留 unknown ownership；
- ledger 第 4097 条在写入前拒绝。

首轮审计实际抓到过两次问题：

1. 把终态 journal 改名到旧 glob 外可绕过重放；
2. 当次返回 JournalCorrupt 后，恢复文件可让同一 Host 继续运行。

两者现已修复。

最终结果：

- `57/57`
- 独立复审额外五轮，共 285 次测试执行
- 独立结论：P1=0，P2=0，仅限 offline scope

Library SHA-256：

`0C70EDFE0CE3417734DE684172D051A2A0EB639EE469A729C15130E216FC5419`

P1 仍只有内存 transport，无外部 IPC、进程、Bridge 或 native callback。

## 5. 关键逆向结论

### 5.1 命令 ID 与费用

精确版本内部命令 ID：

| 项目 | ID | 属性 | 原生费用 |
|---|---:|---|---:|
| 巡察 | 0 | 智力 | 50/人 |
| 商业 | 1 | 政治 | 50/人 |
| 开垦 | 2 | 政治 | 50/人 |
| 修筑 | 3 | 统率 | 50/人 |
| 训练 | 5 | 武力 | 0 |

### 5.2 主循环与 controller

- 主消息泵：`0x005C5CE0 -> 0x005CADA0`
- idle slot：`0x604DF4`，原值 `0x434100`
- task tick：`0x434100 -> 0x4345C0 -> 0x47E840`
- scheduler root vptr：`0x607560`
- domestic controller vptr：`0x610BC8`
- controller 专属字段：`+0x30/+0x34/+0x38`
- 当前干净动态 idle state 样本：`root+0x34 = 0x3E9`
- 构造态 `0x3E8` 不能冒充 live idle。

### 5.3 选人 UI

已静态闭合：

- outer task 通过 event `0x3E8` 打开 selector；
- selector vptr：`0x61F0B8`；
- clear：`0x1D52`
- native prefix-max fill：`0x1D51`
- inner accept：`0x1D4D`
- outer accept：`0x0BB9`
- active modal HWND 可经 `0x5CA7D0 -> 0x5CD460` 取 CWnd，并必须核对 vptr 与 HWND。

但没有找到单一、原子化的 “auto-select + accept” 上层入口。嵌套 modal、消息重入、对象销毁和迟到消息仍需动态证明。

### 5.4 城市绑定

V9 结论：精确 EXE 中没有找到经过验证的

`cityId/CCityData* -> live domestic controller target`

setter。

已审查的 `0x7D1/0x7D3` 路线只携带 packed XY，经游戏原生命中测试后才写 `root+0x38`。城市列表路径也没有回写该字段。

目前唯一保留候选是：在已认证主线程 idle、完整 V2 token 和直属控制都一致时，对空的 `root+0x38` 做一次条件交换，待 handler 同步保存目标后再按相同代际条件恢复。该方案：

- 尚未实现；
- 尚未 live 验证；
- 尚未获得业务执行授权；
- factory snapshot 时点和异常恢复仍未知。

这是当前真正阻塞自动执行的核心问题之一。

## 6. V5.2 ping-only 桥现状

目录：

- `native/San9BridgeV52`
- `tools/San9BridgeV52`
- `docs/reverse-engineering-v5.2-live-bootstrap.md`

V5.2 live-opt-in 只允许：

- 用 `WH_GETMESSAGE` 做一次 bootstrap；
- CAS 把 idle slot `0x604DF4` 从 `0x434100` 换为 wrapper；
- wrapper 每帧无条件且恰好一次调用原 idle；
- 最多处理一个 HMAC/nonce/sequence/target/context/deadline 绑定的 ping；
- DLL 固定驻留到游戏进程退出。

V5.2 明确不包含：

- 城市 ID；
- 武将列表；
- 内政命令码；
- 业务函数调用；
- save/city/officer 写入；
- hot unload 或 slot 恢复。

离线结果：controller `97/97`、DLL `98/98`；offline PE `18/18 + 14/14`；live-opt-in 只做过磁盘审计 `27/27 + 19/19`，从未加载或运行。

截至本文档生成时，用户**没有给出所需的明确 live 授权句**。因此不得运行。

所需授权应明确包含：

> 我已保存，同意一次 ping-only 心跳测试，并接受测试后先重启游戏。

即使用户授权，也必须先重新验证：

- 当前进度已保存；
- 唯一窗口/PID/创建代；
- 精确磁盘与 live 映像；
- 冲突进程、模块与代理 DLL；
- slot/app/thread/anchor；
- live DLL 与 controller 是同一次 compile-only 配对。

无论成功、失败、超时还是 cleanup 结果如何，测试后都必须先由用户手动重启游戏。该授权不能扩张为城市绑定或内政提交。

## 7. 当前未完成项与真实阻断

### 7.1 用户可见功能仍未完成

- 方案按钮未开放；
- 助手没有自动提交过一条命令；
- 没有自动遍历全城；
- 没有运行中的停止/故障恢复集成；
- 没有最终成品包。

### 7.2 主线程桥只验证到离线

V5.2 ping 尚未 live。ping 成功最多证明 wrapper/bootstrap/线程路径，不证明任何业务函数可安全调用。

### 7.3 任意城市 target binding 未闭合

没有合法 setter。瞬时 `root+0x38` CAS 仍只是候选，这是当前风险最大、最需要 Claude 重新评估的点。

### 7.4 嵌套 modal 执行未闭合

selector 的 clear/fill/accept 地址已知，但必须证明：

- callback 处于正确游戏线程；
- active HWND 对应精确 selector generation；
- 消息不会迟到后落到复用对象；
- inner accept 返回后 outer task 仍是同一对象；
- outer accept 恰好一次；
- 任一不确定立即 HaltRestart，不能自动重试。

### 7.5 离线安全层尚未接产品

V8、V10、P0、P1 都是互相独立或离线的组件，尚未串到 UI/Core/V5.2。不能因为每层测试通过就声称端到端通过。

### 7.6 Live 前仍缺 OS 级信任锚

P1 已明确留下两个 live 阻断：

- managed 目录验证后，具有祖先 rename/`DELETE_CHILD` 权限的主体仍可能交换目录；需要稳定原生目录 handle/file-id；
- ledger 与全部 journals 的协同删除/回滚无法由同一可变目录证明；需要外部单调信任锚。

## 8. 当前仓库结构与重要文件

优先阅读：

1. `docs/PRD.md`：需求与阶段状态
2. `docs/VERIFICATION.md`：权威验证记录
3. `README.md`：组件边界与启动入口
4. `docs/reverse-engineering-v3-execute.md`：原生命令生命周期
5. `docs/reverse-engineering-v5.2-live-bootstrap.md`：ping-only 桥
6. `docs/reverse-engineering-v7-native-ui.md`：selector/outer UI 路线
7. `docs/reverse-engineering-v9-city-binding.md`：城市绑定 NO-GO 证据
8. `prototypes/San9AutoDomestic.V8Transaction`
9. `native/San9BridgeV10`
10. `prototypes/San9AutoDomestic.SingleCommandSlice`
11. `prototypes/San9AutoDomestic.CommandBroker`

主要产品目录：

- `src/San9AutoDomestic.Core`
- `src/San9AutoDomestic.Adapter.San9Pk101`
- `src/San9AutoDomestic.UI`
- `config/default.json`

逆向静态校验器：

- `tools/re/san9_v3_static.py`
- `tools/re/san9_v5_static.py`
- `tools/re/san9_v6_selection_probe.py`
- `tools/re/san9_v7_static.py`
- `tools/re/san9_v9_city_binding_static.py`
- `tools/re/san9_v10_native_contract_audit.py`

## 9. 当前 Git 检查点

```text
a9fd903 Add audited offline command broker
890b3d1 Add audited single-command shadow contracts
a33c69e Add persistent UI and audited execution foundations
4626cce Build read-only San9 domestic preview foundation
```

本文档生成前工作树 clean；本文档是随后新增的交接件。若已提交，请以当前 `git log` 中最新记录为准。

## 10. 主要构建与验证命令

根项目：

```powershell
cd 'C:\codex files\San9AutoDomestic'
.\build.ps1
```

不要在正式 UI 正运行并锁住发布文件时贸然覆盖 `tools\artifacts\bin`。需要纯验证时优先使用隔离原型构建。

V8：

```powershell
cd 'C:\codex files\San9AutoDomestic\prototypes\San9AutoDomestic.V8Transaction'
.\build-and-test.ps1
```

V10：

```powershell
cd 'C:\codex files\San9AutoDomestic\tools\San9BridgeV10'
.\build.ps1
```

P0：

```powershell
cd 'C:\codex files\San9AutoDomestic\prototypes\San9AutoDomestic.SingleCommandSlice'
.\build-and-test.ps1
```

P1：

```powershell
cd 'C:\codex files\San9AutoDomestic\prototypes\San9AutoDomestic.CommandBroker'
.\build-and-test.ps1
```

V5.2 默认离线构建：

```powershell
cd 'C:\codex files\San9AutoDomestic'
.\tools\San9BridgeV52\build.ps1
```

不要加载或运行 live-opt-in 产物，除非重新完成安全门、取得用户明确授权，并严格限定为一次 ping。

## 11. 绝对不要做的事

- 不要用 Computer Use 操作游戏；
- 不要修改或打补丁到 `San9PK.exe`；
- 不要恢复 `version.dll` 侧载路线；
- 不要与 Easy/Hard/SanIX 或代理 DLL 同时执行；
- 不要直接写 city/person/corps 数值模拟命令；
- 不要直接写三条选人链或全局选择链；
- 不要直接调用 apply 函数；
- 不要把 `0x47E6F0` 当持久订单入队函数；
- 不要在 selector 析构后继续解释旧 task 指针；
- 不要把静态地址锚点通过、离线测试绿或 ping 成功解释为业务执行授权；
- 不要自动重复确认或自动重试不确定命令；
- 不要在没有保存、没有明确授权、无法接受重启时做 live ping；
- 不要无提示终止游戏或替用户自动重启游戏。

## 12. 建议 Claude 重点复核的问题

请 Claude 不要从头重写全部工程，优先回答以下问题：

1. V9 的 transient `root+0x38` CAS 是否存在可证明安全的主线程调用窗口？
2. 是否能从 packed XY/native hit-test 路线构造一个比直接 target CAS 更原生的城市绑定入口？
3. 是否存在尚未识别的 `CCityData*` setter、current-city accessor 或 command factory 参数？
4. selector 嵌套 modal 能否通过已存在的游戏消息队列分阶段驱动，而不产生 reentrant direct-call 风险？
5. outer/inner 对象 generation 应怎样做最小而充分的身份绑定？
6. V8/V10/P0/P1 中哪些约束必须保留，哪些可以在端到端集成时合并，避免继续增加原型层？
7. 如果坚持 native 路线，最小的“单城商业一次”纵切应修改哪些现有文件？
8. 如果 native city binding 无法闭合，是否应该明确转为屏幕/输入自动化 MVP？若转向，如何处理自绘 DirectDraw、焦点、缩放、弹窗和误点风险？该路线会改变当前“无输入模拟 API”的安全边界，必须先征得用户同意。

希望 Claude 给出的不是泛泛建议，而是：

- 一个具体 Go/No-Go 判断；
- 最短纵切的文件级改动清单；
- 每一步需要的新证据；
- 哪一步失败就应停止；
- 是否值得继续当前 native 路线。

## 13. 建议的精简后续路线

### 阶段 A：只验证 ping

- 用户保存进度并明确授权；
- fresh exact-target/conflict/generation gate；
- 只运行一次 V5.2 ping；
- 不携带任何业务数据；
- 无论结果如何，先由用户重启游戏；
- 记录线程、slot、wrapper、ping count 和 cleanup 结果。

### 阶段 B：只做单城单命令

- 新进程、复制存档；
- 固定一座直属城市和商业；
- 只生成一条 exact-five 请求；
- 验证 target bind；
- 打开 outer、selector，clear、native fill max、核对 exact five；
- inner/outer 各确认一次；
- 立即丢弃旧 UI 指针；
- 新 V1/V2 A/B 后置快照核对商业、资金、五人 busy 和 order 位；
- 任一不确定立即停止并要求重启，不自动重试。

### 阶段 C：才接全城与两个方案

- 把 P0 计划逐条喂给已证明的单命令事务；
- 每条命令后重新读完整状态；
- 再接 UI 按钮与停止状态；
- 最后覆盖委任、灰项、少 5 人、资金不足、共享资金、进程退出、冲突模块和重复点击。

## 14. 工期与资源判断

如果城市绑定候选动态验证成功：

- 第一条助手自动命令：约 4–8 个有效开发小时；
- 两个方案、全城市、停止和异常回归：再约 6–10 小时；
- 总计约 10–18 小时，约两个集中工作日。

如果城市绑定路线失败，需要继续逆向，可能再增加 1–2 天，且不能承诺一定成功。

此前开发在安全原型和多轮审计上投入过多，消耗与用户可见产出不成比例。后续应执行以下资源纪律：

- 一条实现线；
- 一个明确里程碑；
- 一次最终独立复审；
- 不再为尚未接入的假设继续建立新协议层；
- 遇到硬阻断立即汇报，不用大量额度掩盖不确定性。

## 15. 当前授权状态

截至 2026-08-09：

- 用户要求的是当前开发情况文档；
- 用户尚未授权本轮 live ping；
- 不应假定 2026-08-07 的 PID、窗口、存档、城市或游戏仍保持原状态；
- 任何后续 live 工作都必须重新发现并验证当前环境；
- 本文档的生成不授权任何进程写入、Hook、输入模拟或业务执行。
