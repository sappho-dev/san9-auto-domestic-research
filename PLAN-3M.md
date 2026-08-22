# 300 万词元执行方案（修订版）

> 取代原交接文档第 9 节与第 10 节。
> 起点：剩余 2,956,151，按 2,950,000 分配。
> 唯一进度指标：跑起来了什么。测试数、文档数、审计轮数都不算进度。

## 诊断依据

离线测试累计约 5,900 项全绿，live 运行 0 次。20 万 Goal 与 5 万 Goal 均以 `budget_limited` 结束且均无 ping。两轮消耗都没有失控，都是精确地花在了各自认定的正确工作上。要改的是排序方式和单位成本，不是纪律。

四项未被识别的结构成本：

1. 交接文档约 11,500 词元，加 PRD/VERIFICATION/v3/v7/v9/v10/manifest 的标准读取集约 30,000。按 60 到 80 次会话放大，开场读取即可吞掉两到三成总预算。
2. 没有 live 观测能力，PersonList 尺寸、handler 布局、候选表格式、vtable 内容全停在静态假设，每个都要单独论证且结论不可靠。
3. 协议层三份实现（Python oracle / C# / C），任何 schema 变动是三倍成本。
4. 原表把 55 万分配给尚未裁决可行性的 H5，而该裁决决定产品定义。

## 三个架构决定

**一、bridge 加载一次，行为由枚举 opcode 驱动。**
bridge 不可热卸载，每验证一个假设重启一次游戏的迭代成本不可接受。opcode 表编译进 DLL（op1 ping、op2 dump handler 区、op3 城市快照、op9 Commerce no-apply、op10 Commerce apply…），共享内存只传索引和有界参数结构，不传任意地址。业务请求走已有 M2a 4096B mailbox，P1Wire 保持冻结不改 schema，不重跑 4,900 项测试。一次游戏会话可连续跑几十个原语。

**二、只读观测优先，录制人类手动执行的原生路径。**
bridge 驻留后先加纯读取 opcode，在 idle safe point 每 tick 转储 root event 状态、`root+0x10` handler 指针与 vptr、`+0x40` 候选表与 `+0x4C` 计数、command 对象、资金与人员行动位。然后让用户在游戏里正常点一次商业。得到原生生命周期的完整轨迹。零写入，风险等同只读，但一次性解决所有 ABI 未知量。

**三、需要断点的问题交给用户跑 x32dbg。**
我出断点清单与记录字段，用户贴回 20 行结果。单个未知量成本从约 40,000 降到约 2,000。首个目标：`0x4C6310`（ExecuteUI）中用户选完五人之后的续点。若存在 `BuildCommandFromSelection(list, n)` 类子函数，可在 safe point 直接调用，整个 shadow vtable 层可删。

## 阶段与预算

| 阶段 | 目标 | 上限 | 结束条件（必须是一次真实运行） |
|---|---|---:|---|
| S0 | 瘦身 | 30,000 | STATE.md ≤ 3,000 词元且能独立支撑新会话开工；旧交接文档移入 archive |
| S1 | probe0 | 120,000 | DLL 驻留游戏进程，共享内存计数器随 idle 递增 |
| S2 | ping-only | 130,000 | 100/100 认证往返，主线程身份全程一致，游戏状态零变化 |
| S3 | 观测层 + 录制 + ABI 闭合 | 300,000 | 一份真实轨迹；PersonList 尺寸与 handler 布局由观测确定 |
| S4 | 城市绑定裁决 | 120,000 | 明确 GO 或 NO-GO；NO-GO 当场改产品定义 |
| S5 | 单城 Commerce | 550,000 | 复制存档完成一次原生商业，apply=1，五人/资金/order bit/存档重载一致 |
| S6 | 其余四命令 | 450,000 | 巡察/开垦/训练/修筑各闭环一次 + 灰项/少人/资金/已执行回归 |
| S7 | 全部直属城市 | 350,000 | 一次完整批次，无输入回退 |
| S8 | UI + 回归 + 发布 | 200,000 | 单 EXE 双方案；Easy 两功能装桥前后与内政后回归；20 轮批次 |
| Reserve | 已证明阻断专用 | 700,000 | 用户逐次批准转入具体阶段 |
| **合计** | | **2,950,000** | |

与原表的差异：ping 从 40 万压到 25 万（probe0 拆出后不再是一次性大爆炸）；新增 30 万观测层，从原 H3 的 75 万里挪；城市绑定从末位 55 万前移为 12 万裁决点，实现留在 S7；储备从 20 万提到 70 万（历史上每次估算均 100% 烧穿）；UI 从 35 万降到 20 万（引擎跑通后只是接线）。

## 执行规则

1. **文档总配额**：剩余项目全程 ≤ 60,000 词元，只允许写进 STATE.md（覆盖式）和每阶段一份 ≤ 200 行 NOTES。禁止新建 v11/v12 逆向文档，禁止再写交接文档。原第 9.1 条的 20% 配额作废。
2. **开场只读 STATE.md**。其他文件 grep 定位后读片段，禁止全文读 PRD/VERIFICATION/v 系列。
3. **不新增第二份协议实现**。C# 侧不再实现任何 wire 编解码；进程发现与身份绑定的唯一真源在 C 侧；UI 通过 `controller.exe --inspect` 读 stdout JSON。
4. **断点优先于静态推断**。每个未知 ABI 事实先问能否用一个断点两分钟看到，能就交给用户。
5. **审计上限 2 轮**。第二次复审不通过直接上真机，让实跑当审计。S1–S3 不设独立审计线程；审计只在 S5（不可逆写入）和 S8（发布）恢复。
6. **40% 止损**。阶段用到 40% 仍无一次真实运行，立刻砍到最小可运行片。
7. **禁止同阶段并行两条技术路线。**
8. **授权三级**：只读 inspect（无需单独授权）；进程内驻留 + 只读 opcode（覆盖 probe0、ping、观测录制、no-apply vptr CAS 时序，一次授权）；业务写入 apply（单独授权）。

## 成功顺序

```
probe0 > live ping > 观测轨迹 > 城市绑定裁决 > 一次 Commerce apply
       > 五命令 > 全城 > UI
```

上一层没跑通，下一层一律不扩。

## S1 最小修改清单（文件级）

工作面仅限 `native/San9BridgeP1EasyPingM2b`。

- `src/controller.c`：`main()` 增加 `--inspect`（只读，输出结构化 JSON）与 `--probe0 --confirm <确认词>` 两条路径。所有绑定量自动发现，不接受命令行传 PID、DLL 路径或地址。
- 新增 `src/discover.c` / `include/discover.h`：唯一游戏与唯一 Easy loader 发现、路径版本大小 SHA 校验、PID/创建时间/HWND/main TID 绑定、Easy 实际基址与 32+6 快照、canonical binding frame 编码。该文件不 include 任何 install 头，编译期保证 inspect 无副作用。
- `src/bridge_dll.c`：probe0 路径下 wrapper 只做三件事，调原 idle 恰好一次、自增共享内存计数器、写一次 wrapper 自检计数（改过哪些寄存器/DF/FPU 状态）。
- `audit_pe.py`：import allowlist 扩到 live 最小集（kernel32 mapping/进程枚举、user32 SetWindowsHookExW/FindWindowW/GetWindowThreadProcessId、advapi32 SID、psapi 或 toolhelp）。每条新增 import 在 STATE.md 里写一行理由。
- `build.ps1`：不变，继续只跑 offline.exe。

预算估计 120,000，其中实现约 70,000，离线恶意参数矩阵约 20,000，live 调试余量 30,000。

## 第一次 live 的验收与回滚

授权文本（probe0 与 ping 同批）：

> 授权本批次 P1 进程内驻留测试；只做主线程计数与认证 ping，不执行任何内政，不写入任何游戏业务字段；接受 bridge 固定驻留至游戏重启，并会在测试结束后重启游戏。

执行前：用户保存进度并使用复制存档；游戏与精确 Easy 已启动稳定；无 Hard/SanIX/未知修改器；记录前台 HWND 与鼠标位置。

验收：bootstrap 只发生一次；slot CAS 只成功一次；probe0 计数器单调递增且原 idle 每次恰好一次；100 个 request/response 全部认证且连续；main TID、caller、app/vtable、Easy digest 全程一致；无业务字段变化、无城市人员变化、无输入 API 调用；鼠标位置与前台窗口不变。

失败处理：输出明确状态，不自动重试。回滚边界是重启游戏，没有第二条。

