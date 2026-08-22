# San9PKEasy 1.1.0.5 静态 ownership manifest 与冲突审计

状态：**P0 静态兼容档案 PASS；不授权 live 注入、进程读取或游戏写入。**

本轮只读取磁盘上的三个精确 PE 文件，没有启动游戏或 Easy，没有枚举、打开或读取任何进程，也没有修改目标二进制。机器可读真源为 [easy-compatibility-manifest.json](easy-compatibility-manifest.json)，本文只解释其证据和门禁语义。

## 1. 精确档案

| 文件 | 大小 | SHA-256 | PE |
|---|---:|---|---|
| `San9PK.exe` | 2,636,800 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` | I386 / PE32 / ImageBase `0x00400000` |
| `San9PKEasy.exe` | 24,576 | `CDACA1477EDB5A3BD79BDA8540E19837FC3F9170965D972E8E48FB24CAE21C07` | I386 / PE32 / ImageBase `0x00400000` |
| `Easy.dll` | 32,768 | `E8BA3A603F6B0E7AF8A246DA0FE5CBDBA86AD9B77C5A507DD86159EF6C74A3F0` | I386 / PE32 / preferred ImageBase `0x10000000` |

任何大小、哈希、Machine、PE kind 或 preferred ImageBase 漂移都在反汇编前拒绝。preferred ImageBase 只用于生成静态对照字节；运行兼容门必须以实际加载基址计算 `rel32`，不得假设 Easy 恒在 `0x10000000`。

## 2. `WriteProcessMemory` 完整性闭包

加载器只从 `KERNEL32.dll` 导入一个 `WriteProcessMemory`，IAT VA/RVA 为 `0x0040301C / 0x0000301C`。线性反汇编中引用该 IAT 的指令恰好五条：

| IAT 引用 | 语义 | 下游 WPM call 数 |
|---|---|---:|
| `0x00401787` | 直接写临时远程分配区中的 `Easy.dll` 路径 | 1 |
| `0x00401B47` | 把 WPM 取入 `EDI`，安装辅助状态 | 4 |
| `0x00401CE3` | 把 WPM 取入 `EDI`，安装代码重定向 | 32 |
| `0x00402351` | 把 WPM 取入 `EDI`，更新“小兵培养”成对状态 | 2 |
| `0x00402438` | 把 WPM 取入 `EDI`，卸载时恢复重定向和游戏侧辅助点 | 37 |

总计 `1 + 4 + 32 + 2 + 37 = 76` 个静态 WPM callsite，全部由校验器逐地址闭合：

- 第一个调用只写 `VirtualAllocEx` 得到的临时 DLL 路径区，加载后释放，不是持久 ownership 点。
- 安装辅助四次分别写 Easy HWND 槽、AI 反击蛮族分支和两个军团兵粮常量。
- 安装代码重定向三十二次对应下节全部记录。
- 运行期两次只能更新成对的小兵培养阈值。
- 卸载三十七次恢复 32 个重定向和 5 个游戏模块辅助点；Easy 模块内 HWND 槽没有恢复调用，因此 loader 退出/卸钩必须按 PRD 视为 epoch 变化并要求游戏重启。

这证明唯一持久物理 ownership 集合为 **32 个代码重定向 + 6 个辅助写点 = 38 点**；没有发现需要修订 PRD 计数的新写点。

## 3. 32 个代码重定向

`Easy.dll` RVA `0x1000` 的初始化器连续写入一张位于 RVA `0x6390` 的表：一个 `0x12345678` sentinel，加 25 个 Easy 内部函数指针。加载器从已重定位的模块中读取该表，因此 manifest 记录 `Easy target RVA`，运行字节由 `actual Easy base + RVA` 计算。

下表“安装字节”只是在 preferred base `0x10000000` 下的静态对照；机器校验以指令类型、目标 RVA 和实际基址为准。x86-32 `rel32` 按 EIP 的模 `2^32` 加法编码，不能错误地要求数学差值先落入 signed-int32；自测覆盖实际基址 `0x90000000` 与 `0xF0000000`。

| ID | 游戏 VA | 类型 | 长度 | 原字节 | preferred-base 安装字节 | Easy target RVA | 静态用途分类 |
|---|---|---|---:|---|---|---|---|
| 00 | `0x004E6270` | JMP rel32 | 5 | `E92B52FEFF` | `E99BAEB10F` | `0x1110` | 多人探索：清选中表 |
| 01 | `0x00485DB6` | CALL rel32 | 5 | `E845FCFFFF` | `E865B3B70F` | `0x1120` | 多人探索：逐人执行 |
| 02 | `0x0049575D` | CALL rel32 | 5 | `E89E02FFFF` | `E8BEB9B60F` | `0x1120` | 多人探索：逐人执行 |
| 03 | `0x004EC414` | CALL rel32 | 5 | `E8673F0800` | `E8A74DB10F` | `0x11C0` | 军师推荐：selector wrapper |
| 04 | `0x004ED0AE` | CALL rel32 | 5 | `E8CD320800` | `E83D41B10F` | `0x11F0` | 军师推荐：selector wrapper |
| 05 | `0x004CF7BB` | CALL rel32 | 5 | `E8C00B0A00` | `E8601AB30F` | `0x1220` | 军师推荐：selector wrapper |
| 06 | `0x004EDFAB` | CALL rel32 | 5 | `E8D0230800` | `E8A032B10F` | `0x1250` | 军师推荐：selector wrapper |
| 07 | `0x004E122B` | CALL rel32 | 5 | `E850F10800` | `E85000B20F` | `0x1280` | 军师推荐：selector wrapper |
| 08 | `0x004D134B` | CALL rel32 | 5 | `E830F00900` | `E860FFB20F` | `0x12B0` | 军师推荐：selector wrapper |
| 09 | `0x004DB3EB` | CALL rel32 | 5 | `E8904F0900` | `E8F05EB20F` | `0x12E0` | 军师推荐：selector wrapper |
| 10 | `0x004DFCAB` | CALL rel32 | 5 | `E8D0060900` | `E86016B20F` | `0x1310` | 军师推荐：selector wrapper |
| 11 | `0x004EC3D8` | CALL rel32 | 5 | `E8C37AFAFF` | `E80350B10F` | `0x13E0` | 军师推荐：候选收集 |
| 12 | `0x004ED075` | CALL rel32 | 5 | `E8967EFAFF` | `E87643B10F` | `0x13F0` | 军师推荐：候选收集 |
| 13 | `0x004D1EA7` | CALL rel32 | 5 | `E804CBFBFF` | `E864F5B20F` | `0x1410` | 军师推荐：候选收集 |
| 14 | `0x004D16DD` | CALL rel32 | 5 | `E8CED2FBFF` | `E82EFDB20F` | `0x1410` | 军师推荐：候选收集 |
| 15 | `0x004DBE67` | CALL rel32 | 5 | `E88449FBFF` | `E8C455B20F` | `0x1430` | 军师推荐：候选收集 |
| 16 | `0x004DB77D` | CALL rel32 | 5 | `E86E50FBFF` | `E8AE5CB20F` | `0x1430` | 军师推荐：候选收集 |
| 17 | `0x004E06EA` | CALL rel32 | 5 | `E8D11FFBFF` | `E8610DB20F` | `0x1450` | 军师推荐：候选收集 |
| 18 | `0x004E003D` | CALL rel32 | 5 | `E87E26FBFF` | `E80E14B20F` | `0x1450` | 军师推荐：候选收集 |
| 19 | `0x004D0DFA` | CALL rel32 | 5 | `E821D6FBFF` | `E87106B30F` | `0x1470` | 军师推荐：候选收集 |
| 20 | `0x004D068D` | CALL rel32 | 5 | `E88EDDFBFF` | `E8DE0DB30F` | `0x1470` | 军师推荐：候选收集 |
| 21 | `0x004D016D` | CALL rel32 | 5 | `E83ED4FBFF` | `E81E13B30F` | `0x1490` | 军师推荐：候选收集 |
| 22 | `0x004CFB0D` | CALL rel32 | 5 | `E89EDAFBFF` | `E87E19B30F` | `0x1490` | 军师推荐：候选收集 |
| 23 | `0x004EE94D` | CALL rel32 | 5 | `E8FE78FAFF` | `E85E2BB10F` | `0x14B0` | 军师推荐：候选收集 |
| 24 | `0x004EE34D` | CALL rel32 | 5 | `E8FE7EFAFF` | `E85E31B10F` | `0x14B0` | 军师推荐：候选收集 |
| 25 | `0x004E11F2` | CALL rel32 | 5 | `E8D91FFBFF` | `E8D902B20F` | `0x14D0` | 军师推荐：候选收集 |
| 26 | `0x005CAE0E` | CALL rel32 | 5 | `E8DDFEFFFF` | `E86D67A30F` | `0x1580` | 快捷键：窗口消息过滤 |
| 27 | `0x0043308C` | CALL rel32 | 5 | `E86FCAFEFF` | `E88FE5BC0F` | `0x1620` | 快捷键：modal 返回覆盖 |
| 28 | `0x00510579` | CALL rel32 | 5 | `E882F5F0FF` | `E8C210AF0F` | `0x1640` | 快捷键：modal 返回覆盖 |
| 29 | `0x0052FA45` | CALL rel32 | 5 | `E896E5F2FF` | `E8661CAD0F` | `0x16B0` | 部队阵型外观：索引映射 |
| 30 | `0x0043DA3D` | CALL rel32 + NOP | 6 | `FF150CF35F00` | `E85E3CBC0F90` | `0x16A0` | 最大军团兵粮调用替换 |
| 31 | `0x0045DDA1` | CALL rel32 + NOP | 6 | `FF150CF35F00` | `E82A39BA0F90` | `0x16D0` | 最大设施伤兵调用替换 |

计数是精确的 `1 JMP + 29 CALL + 2 CALL/NOP`。每个原字节同时来自加载器恢复表和精确 `San9PK.exe`，32/32 全部一致；目标 RVA 同时来自 Easy 初始化表和加载器安装块的栈偏移。

“静态用途分类”用于冲突理解，不构成可供新桥调用的 ABI。Easy 内部函数依赖寄存器、栈、全局变量和硬编码续点，新桥不得把它们当稳定 API。

## 4. 六个辅助物理写点

| ID | 目标 | 原始状态 | Easy 状态 | 约束 |
|---|---|---|---|---|
| `aux-child-training-a` | game VA `0x0040CAEF` | `32` | `32` 或 `00` | 必须与 B 成对 |
| `aux-child-training-b` | game VA `0x0040BF25` | `32` | `32` 或 `00` | 只允许 `32/32`、`00/00` |
| `aux-max-corps-food-a` | game VA `0x0043DA06` | `40420F00`（1,000,000） | `80969800`（10,000,000） | 固定四字节小端值 |
| `aux-max-corps-food-b` | game VA `0x0045AC96` | `40420F00` | `80969800` | 固定四字节小端值 |
| `aux-ai-counter-barbarians` | game VA `0x004B06B1` | `E4` | `00` | 改写短分支位移 |
| `aux-easy-game-hwnd-slot` | actual Easy base + RVA `0x6450` | `00000000` | 精确绑定的游戏 HWND 小端值 | 不能解释为 game VA |

小兵培养两点是一个逻辑原子；单点字节各自合法但组合为 `32/00` 或 `00/32` 仍必须拒绝。完整 installed 模式可为 `32/32` 或 `00/00`，但 loader 卸载路径固定恢复 `32/32`，所以完整 original 模式只允许 `32/32`；original redirects 与 `00/00` 是残留/混合态，必须拒绝。该 pair 以及 Easy HWND 必须进入批次 context digest。

## 5. 页面保护 ownership

`VirtualProtectEx` IAT 只有两个引用：

- `0x00401B34`：game VA `0x0061C000`、长度 `0x1000`，设为 `PAGE_READWRITE (0x04)`；
- `0x00402772`：同页恢复为 `PAGE_READONLY (0x02)`。

该转换不是第 39 个物理写点。Easy 的军师推荐 wrapper 会临时替换该页中的文本/全局指针，所以页面可写性本身属于 Easy 的运行状态。新桥不得依赖 Easy 留下的 RW 权限；离线 runtime snapshot 校验也把该页保护状态纳入 whole-hook 一致性。

## 6. 与当前 idle 三锚的冲突图

| 新桥静态候选 | VA | 与 38 个 Easy 写点直接重叠 | 与 `0x0061C000..0x0061CFFF` 重叠 |
|---|---|---|---|
| app idle slot | `0x00604DF4` | 否 | 否 |
| original idle function | `0x00434100` | 否 | 否 |
| idle caller return | `0x005C5D11` | 否 | 否 |

结论仅为“没有直接同址冲突”。它不证明调用顺序、线程、重入、页面竞争、Easy epoch 或功能行为已经兼容；这些仍需后续独立门禁和实机回归。

## 7. 校验器与自测

生成并校验：

```powershell
python tools/re/san9_easy_manifest.py generate --output docs/easy-compatibility-manifest.json
python tools/re/san9_easy_manifest.py validate
python tools/re/san9_easy_manifest_selftest.py
```

当前结果：

```text
PASS exact 32 redirects + 6 auxiliaries; WPM/VirtualProtect closure complete
PASS 277/277 offline tests; process access=0; writes to targets=0
```

277 项覆盖：

- 三件套精确身份和确定性重建；
- 32 个 redirect 原字节、目标 RVA 的逐项 manifest 单字节变异；
- 6 个辅助点逐项 manifest 单字节变异；
- 37 个游戏侧 ownership 锚、39 个 loader 写入闭包锚、27 个 Easy 初始化表/槽锚的单字节文件变异；
- preferred、普通 relocated 及 `0x90000000`/`0xF0000000` 高位 Easy base 下的模 `2^32` `rel32`；
- `0xFFFFA000` 伪造基址导致完整 Easy `SizeOfImage=0x9000` 越过 UInt32 的拒绝反例，并校验所有 Easy target RVA/slot 均在镜像内；
- 38 点逐点坏字节、逐点缺失、未知额外点、混合安装态；
- 小兵培养 split pair、original redirects + 残留 `00/00`、错误 HWND、错误 Easy base、错误页保护。
- 非字符串基址、字节和页保护等畸形 JSON 类型。
- 截断的 `rel32` 字节和解析到错误 Easy target RVA 的完整长度反例。
- 纯函数直调时对同形篡改 `target_va` 的 manifest 认证反例。

`validate-runtime-snapshot` 只验证调用方提供的离线 JSON，不读取进程。纯函数入口会先对 manifest 做三件套确定性重建认证，再解释 snapshot；同形篡改的 manifest 也会 fail-closed。它能区分完整 original 与完整 installed；生产执行门即使复用该纯函数，也只能放行 `installed`，original、partial、mixed 和 unknown 必须拒绝。

## 8. 冻结产物

| 产物 | SHA-256 |
|---|---|
| `docs/easy-compatibility-manifest.json` | `72E1B0A89D1C0070798F3080025A9540B8A9B457A8B19AFAABEFB71399190CDE` |
| `tools/re/san9_easy_manifest.py` | `7B8504BE5EE6E8E0A46D96C2A52EBADFE7ECAE1FF80E6A3DA6DD712925862D9B` |
| `tools/re/san9_easy_manifest_selftest.py` | `79FEDA80F7239FFFE3F2593FA04D75986227825F8AD611710610FA7B0A97AD70` |

本产物完成的是 P0 ownership 和静态冲突门，不是 Easy 功能回归、live 共存证明或一键内政执行授权。
