# 当前待分析问题

## 1. UI 与游戏联动偶发卡住或看似无响应

用户长期观察到：点击助手后，助手与游戏之间的联动有时不继续，表现为等待、无效果、要求重启或看似卡死。现有证据不支持把它简单称为一个线程死锁；过去已经确认过多个不同阶段的故障：

- UI 仅最小化却没有把游戏切回前台，导致游戏主循环在 probe0 窗口内一次也没运行；
- Bridge 已驻留后再次点击，idle slot 被自家 Bridge 占用，统一 discover 状态被显示成误导性的 `EASY_NOT_INSTALLED`；
- Windows 拒绝 `SetForegroundWindow` 时，批次应在 controller 启动前停止；
- 菜单握手、首项 Apply 与批次跨项 reset/rebind 属于不同阶段，必须用会话日志逐段判断。

因此，请把“卡死”视为一个总体验问题，按状态机阶段拆解，不要假设所有现象有同一个根因，也不要仅用离线测试通过数作为结论。

## 2. 最新可复现缺陷：第一项 Apply 成功后错误要求重新绑定

修复前台交接后，最新 Wealthy live 已经证明以下链路成功：

1. fresh V2；
2. 精确游戏窗口前台交接；
3. native `--inspect`；
4. probe0；
5. `WAITING_FOR_USER_MENU_SIGNAL`；
6. 唯一菜单信号；
7. Patrol 请求发布；
8. 原生 `event=1, shadow=1, execute=1, restore=1, apply=1, controller_ack=1`。

随后 controller 立即输出：

```text
BATCH_REBIND_REQUIRED executed=1 skipped=0
result=23
```

这说明当前问题已经不是“未注入”或“没有执行”，而是首项完成后进入第二项 Commerce 前的 reset/ACK、fresh capture、root/city/corps identity 或 idle-start 重绑定条件出现错误。完整脱敏日志见：

- `diagnostics/2026-08-12-focus-handoff-rejected.txt`
- `diagnostics/2026-08-12-wealthy-rebind-after-first-apply.txt`

建议外部分析重点追踪：

- `native/San9BridgeP1EasyPingM2b/src/controller.c` 中 S8 Wealthy 循环、首项完成后的 reset ACK 与 rebind 判断；
- `native/San9BridgeP1EasyPingM2b/src/bridge_dll.c` 中 batch idle handoff、runtime descriptor/request latch 和 root idle 恢复；
- `src/San9AutoDomestic.UI/NativeControllerClient.cs` 对 `BATCH_REBIND_REQUIRED` 与最终 NDJSON 的解析；
- 为什么原生日志报告 `executed=1`，UI 终态解析却显示 `executed=0`。

期望外部分析交付：最可能根因、精确控制流、最小代码补丁、无需实机即可增加的回归用例，以及下一次只跑一次 live 时应观察的字段。外部模型不能实机测试，应明确区分“源码推断”和“已有 live 证据”。

## 3. 已解决但必须保留的前置问题

旧版本只最小化助手，未保证游戏成为前台，造成 probe0 超时。当前源码已经增加 PID、创建时间、路径和 HWND 前后复核，并仅在用户明确确认后调用一次 `ShowWindow` / `SetForegroundWindow`。最新日志证明该修复能到达 WAITING 和首项 Apply，因此不要再把主要分析预算放回旧焦点问题。

## 边界

- 项目使用进程内 Bridge 调用游戏原生内政生命周期，不使用鼠标键盘模拟。
- 目标仅为用户当前原生绑定的城市；全直属城市自动遍历已取消。
- 不包含或分发任何游戏、Easy 或存档二进制。
- 新的 live 运行需要用户另行授权；仓库中的日志是既有运行证据。
