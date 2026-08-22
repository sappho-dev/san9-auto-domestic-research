# San9PK V9：任意城市原生 target binding 与受控 fallback 边界

状态：**NO-GO。** 精确版本的磁盘 EXE 中，已闭合的原生 `0x7D1/0x7D3` 入口都是“坐标手势 → 命中测试 → `root+0x38`”，没有发现接收 `cityId` 或 `CCityData*` 的合法 setter；内政 root 的 `0x511560` 城市列表路径只进入信息/高亮分支，不绑定 live controller target。`0x01232474` 是玩家点击后“当前操作区域建筑”的观察量，不能反向驱动 controller。

本文只读取 `D:\三国志9\10101749\San9PK.exe` 的磁盘字节，未枚举、打开或写入游戏进程，未调用游戏函数，未发送输入，未使用 Computer Use，也未修改 Core/UI/V5.2/V8 或根构建。

## 1. 版本与结论边界

| 项 | 值 |
|---|---|
| 目标 | `D:\三国志9\10101749\San9PK.exe` |
| SHA-256 | `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028` |
| 架构 | PE32 / I386，ImageBase `0x00400000` |
| V9 工具 | `tools/re/san9_v9_city_binding_static.py` |
| live 访问 | 无 |
| 任意城市合法 setter | **未发现** |
| 业务执行 | **未授权，NO-GO** |

这里的“未发现”是精确哈希下、针对已闭合 controller/UI 调用链的静态结论，不等于对所有可能的间接写、脚本解释器或未识别数据驱动入口作数学意义上的不存在证明。因此它不能反过来成为 live 写入许可。

## 2. 三个原生 event 发射点都只传坐标

场景封装 `0x004345A0` 只做：

```text
mov ecx, [ecx+0x8C]
jmp 0x0047E4E0
```

在整个 `.text` 中，它只有三个 direct call：

| call site | event | payload 证据 | 结论 |
|---|---:|---|---|
| `0x005208AF` | `0x7D1` | `CMainmapView` 先完成视口/输入变换，再把两个 WORD 合成 packed XY，同时传一个标量参数 | 地图点击手势 |
| `0x0051D0CD` | `0x7D3` | 两个 WORD 合成 packed XY，同时传一个标量参数 | 框选/坐标手势 |
| `0x0056391D` | `0x7D1` | 多个普通 View vtable 共用的 packed XY forwarder | 通用视图点击手势 |

三处都没有 `cityId`、`CCityData*` 或“已经命中的对象”参数。root 的 `0x00514370` 又把 `0x7D1` 路由到 `0x00513890`，把 `0x7D3` 路由到 `0x00513B10`；后两者各自通过 hit resolver 得到对象，才写 `root+0x38`。

`0x7D3` 路径的关键序列为：

```text
0x00513BAE  call 0x00508010
0x00513BB5  call 0x0051D440
0x00513BD0  call 0x00418600     ; 从命中集合取对象
0x00513BD6  mov  [esi+0x38],eax ; 绑定 live root target
```

因此，向原生 event 投递城市对象并不是一个已存在的 ABI。即使已知 `cityId -> 0x0124DB58 + id*0x1F0`，也不能把该指针塞进 packed XY 参数冒充合法点击。

## 3. 内政 root 的城市列表路径不是 target setter

root 的城市列表路径是唯一 direct chain：

```text
0x00513E20
  -> 0x0046F300(predicate=0x005107D0)  ; 构造过滤城市列表
  -> 0x00511560                         ; 唯一 direct caller: 0x00513E63
     -> 0x005684D0                      ; CCityListDlg
     -> 0x0041FB00                      ; modal
```

`CCityListDlg` 的精确类型证据：

- runtime name：`CCityListDlg`，descriptor `0x0061DE50`；
- constructor `0x005684D0` 安装 vptr `0x0061DF68`；
- `vtable+0x28 = 0x00578090`（通用 selector event）；
- `vtable+0x80 = 0x0041FB00`（modal）；
- `vtable+0x84 = 0x00577170`（通用 accept）。

modal 返回后的三条相关分支是：

- `0x00511732 -> 0x0050FA10`：打开信息/进一步选择窗口，并把结果写回调用方局部容器；
- `0x005117D6 -> 0x00533580`：逐个城市做地图 visual/highlight；
- `0x0051185B -> 0x00533580`：另一列表分支同样逐个做 visual/highlight。

`0x00533580` 通过对象虚表 `+0x0C` 取 ID，在 `0x01545EE0` 的地图管理器中取 visual，随后只操作 `CMainmapView+0xF4` 的高亮集合。它不写 controller target，也不调用 `0x00513890` 或 `0x005179B0`。

所以该条 `root -> 0x511560` 路径的“打开城市列表 → 选城市”不能静态推导成“把该城市绑定为内政 target”；在这条路径里，已闭合的结果是信息窗口或地图高亮。

`CCityListDlg` 构造器还有三个直接调用点，不能被“信息/高亮”一句话概括；逐函数静态复核结果为：

- `0x00550851` 所在 `0x00550732..0x00550A1E`：modal 后在 `0x00550898 -> 0x00572810` 取得选中列表，`0x005508AD` 取选中对象，随后走 `0x005508CB -> 0x00435840` 及 `CPersonData` 相关对象处理；完整函数没有写内政 live root `+0x38`；
- `0x005561B7` 所在 `0x00556120..0x005562B4`：`0x005561F6 -> 0x00572810` 取得选中列表，`0x00556207` 取对象，`0x00556221` 明确写入结果数组 `0x01A5E6C4 + index*8`，再走 `0x00556228 -> 0x00555C70`；完整函数没有写内政 live root `+0x38`；
- `0x0056EB1E` 位于通用 factory `0x0056EA20..0x0056EF27` 的 case：分配并构造 `CCityListDlg` 后返回新 dialog；该 factory 没有写内政 live root `+0x38`。

因此 V9 只把这些调用点排除为 target setter，不外推它们各自完整的业务语义。

## 4. 没有闭合可逐城使用的 current-selection accessor

### 4.1 `0x01232474` 是当前操作区域建筑观察量

精确 EXE 对 `0x01232474` 的直接绝对引用仍只有：

```text
读：0x0043FF27, 0x00451646, 0x00460936
写：0x00513970
```

唯一写点发生在 `root+0x38` 已由命中解析绑定、且对象通过 `0x00605268` 的 `CChiikiBuildingData` 动态类型检查之后；它不是 `0x00605460` 的 `CCityData` 专属检查。没有 `0x01232474 -> root+0x38` 的反向入口。因此该全局只能作为“当前操作区域建筑”的只读观察量，不能命名成权威 current-city accessor。

### 4.2 `0x0050F430` 不是城市 accessor

该 helper 把当前对象 runtime-cast 成 `CMainmapView`，再调用 `vtable+0xA8`。精确 vtable 解析为：

```text
CMainmapView vptr     = 0x00611308
vtable+0xA8           = 0x00521F10
0x00521F10            = mov eax,[ecx+0x298]; ret
```

`mainmap+0x298` 是 `CSan9MainDlg*`；root 的坐标路径只检查其 `+0x1360` UI/modal 状态。它不是 `CCityData*`，也不是城市选择代际 token。

### 4.3 地图中心/高亮不足以替代 binding

已知城市地图对象坐标，仍缺少以下闭环：

1. 把任意城市原生居中并等待视图代际稳定的 setter；
2. 世界/地图坐标到当前 client packed XY 的可验证 round-trip；
3. 城市必须可见、未被 overlay/单位/其他对象遮挡；
4. hit resolver 返回对象必须与目标 `CCityData*` 完全相同；
5. 居中、命中、菜单创建与 controller generation 必须在同一主线程事务中。

因此“先把地图移到城市，再模拟坐标事件”当前也不是合法的任意城市 accessor。

## 5. 最小受控 fallback：瞬时 `root+0x38` 候选

这一节只是给下一轮安全取证定义边界，**不是实现许可，也不是 live GO**。

### 5.1 为什么它比 UI 坐标路线更窄

若最终确认不存在合法 setter，最小候选只触碰一个已知字段：在游戏主线程 idle callback 内，把精确 live root 的空 target 从 `0` 条件交换为已验证 `CCityData*`，立即走已有原生 query/dispatch，再按对象代际条件恢复。

与“居中 → 坐标换算 → 发 `0x7D1` → hit-test → 菜单”相比，它：

- 不改变地图中心、缩放或高亮；
- 不依赖城市可见性、遮挡和坐标 settle；
- 不把一个批处理城市扩张成多步 UI 状态机；
- 只增加一次精确指针字段的瞬时条件写。

但它直接越过了原生 hit resolver，属于业务对象写入，存在 TOCTOU、对象换代、异常遗留 target 和错误城市提交风险。所谓“更窄”只指状态空间和写集合更小，**不代表当前更安全或已授权**。

### 5.2 进入 CAS 前必须同时成立

每个条件都 fail-closed；任一不成立就跳过该城市，不降级到坐标输入或外部 `WriteProcessMemory`：

1. 精确 PID 创建代、EXE SHA、模块/image hash、conflict gate、session nonce 全部通过；
2. 当前代码正在经过已认证的游戏主线程回调，禁止工作线程和跨线程窗口消息直接写；
3. root 由本代 scene/scheduler 重新追得，`[root] == 0x00610BC8`、`root+0x10 == root`，且 `root+0x34` 与同一进程代刚取得的只读 idle 基线一致；当前干净动态样本为 `0x3E9`，`0x3E8` 仅是构造器初始值，不能硬编码成 live idle 常量；`0x0047E420(root)` 的语义动态取证一致；
4. root generation、scene generation、turn/旬、日期、玩家势力、直属控制集均与 V2 token 相同；
5. `root+0x38 == 0`，无 modal、无已挂 child task、无 command singleflight、无停止/重载请求；
6. `cityId` 经 `0x00452F70` 重取，结果落在精确城市表、对齐正确、`[city] == 0x00605938`，且 city/corps/force/委任/直属控制关系全部通过；
7. V2 完整 A/B/A token 通过：同一城市、同一命令、资源/资金、设施值、ready officers、前五人身份与顺序、组合校验结果均未变化；
8. 原生 CanExecute 针对同一 command descriptor 返回可执行，灰项仍只允许跳过。

这里的“CAS”必须是进程内、主线程上的对齐 32 位 compare-exchange `0 -> exact city` 语义；静态候选不授权任何外部进程写法。

### 5.3 提交、恢复和验收契约

候选事务必须满足：

```text
fresh snapshot + full token
  -> CAS(root+0x38, expected=0, desired=exact CCityData*)
  -> 再验 root/scene/city/token
  -> 原生 factory/query
  -> 原生 event dispatch
  -> 验 handler exact vptr 且 handler+0x60 == exact CCityData*
  -> conditional CAS(root+0x38, expected=exact city, desired=0)
  -> 从新可信快照验收
```

约束：

- factory/attach 的同步窗口必须用动态证据证明；不能因静态看到 handler snapshot 就假定可以提前清空 target；
- 恢复只能做 `exact city -> 0` 的条件交换，并且 root generation 未变；若字段已被游戏改成其他值，绝不能盲写 `0`；
- 从 CAS 成功开始，任何异常、停止、重载、modal/scene/root 换代都进入条件恢复；
- 无法证明恢复成功时立即 `HaltRestart`，不处理下一城、不重试命令；
- post-submit 必须从新快照观察 busy/order/resource/fund/facility 或人员状态的预期变化；不接受旧缓存或仅以函数返回值判成功；
- 一次事务只允许一个城市、一个命令、一次 dispatch；禁止自动重放，避免双扣资金或重复下令。

### 5.4 仍缺的最小动态证据

所有阶段都必须使用复制存档、单城单命令、fresh explicit authorization，并在阶段后重启游戏；本轮没有执行：

1. **只读时序取证**：证明 live root getter、idle/state、root/scene generation、`root+0x38` 生命周期及原生手动点击时的绑定/清空顺序；
2. **CAS 往返探针**：在主线程 idle 中只做 `0 -> city -> 0`，不 dispatch，证明条件恢复、停止/异常清理和全局状态无变化；
3. **factory snapshot 探针**：一次命令中证明 exact handler vptr、`handler+0x60 == city`，并确定可安全恢复 target 的最早指令边界；
4. **故障注入**：逐点注入 root generation 改变、modal 出现、target 被游戏改写、停止/重载、attach 失败，证明不会清错字段、投错城市或重复提交；
5. **提交验收**：只允许一次真实命令，保存前后新快照并人工核对城市、五人、资金、设施/命令状态；之后重启复核；
6. **多城前门槛**：单城路径连续通过后，才评估直属城市迭代；军团委任城市必须始终被 control token 排除。

如果改走 UI 坐标路线，则还要额外闭合“原生居中 setter、视图 settle token、坐标 round-trip、遮挡/重叠 hit identity、菜单目标 identity”。在这些证据出现前，它并不比瞬时单字段候选更可控。

## 6. 最终判定

| 能力 | 判定 |
|---|---|
| 精确 EXE 离线静态锚点 | GO |
| `cityId -> exact CCityData*` lookup | GO（只读 lookup） |
| 原生 click/box hit → `root+0x38` | GO（只说明玩家 UI 路径） |
| 原生 event 直接接收 cityId/object | **NO-GO / 不存在已验证 ABI** |
| CCityListDlg 绑定 controller target | **NO-GO** |
| 可逐城复用的 authoritative UI selection accessor | **NO-GO** |
| transient `root+0x38` fallback | **静态候选；未授权，需上述动态证据** |
| 一键内政业务 dispatch | **NO-GO** |

运行复核：

```powershell
python tools/re/san9_v9_city_binding_static.py
python tools/re/san9_v9_city_binding_static.py --json
```

工具没有 `--pid`、live、写入、调用或输入开关；SHA 不符即 fail-closed，只向 stdout 输出。
