# 历史目录核验报告（2026-10-19）

继续逐批核验与提交。第一阶段“公开来源已穷尽”的表述过早，已撤回。后续公开Git历史、网页存档和可正常翻页的EarsOnMe目录取得了更多真实单集链接。

|频道|已核实收录|当前官方总量|待确认|
|---|---:|---:|---:|
|孤岛车谈|186|212|26|
|疯投圈|149|149|0|
|乱翻书|285|285|0|
|合计|620|646|26|

## 方法与证据
- 正式目录从438条增加至620条，本批新增30条。每一条都取得官方HTTP200页面，核对真实单集ID、频道ID/名称、完整标题、日期和时长；证明见 `history_verification.jsonl`
- 孤岛车谈的关键来源是[公开EarsOnMe频道目录](https://earsonme.com/podcasts/8MZ4OeoY/episodes)。实际云浏览器按页面给出的“下一页”链接走完18页，得到截至2026-10-02的210条标题，86个剩余缺口全部匹配；再打开对应详情，读取实际列出的小宇宙源链接。没有用标题、时间或连续数字猜ID。列表与详情取证见 `history_earsonme.json`
- 疯投圈：公开Podcast2RSS当前60条与2025-01-19历史108条元数据的并集覆盖149条，全部回到官方核实。来源见 `history_public_metadata_sources.json`
- 历史Git榜单56个选定快照、6份官方频道页Wayback快照，以及37份旧榜单存档也提供了真实候选。来源URL、时间和哈希见 `history_rank_snapshots.json`、`history_wayback_snapshots.json`、`history_archived_rank_snapshots.json`。榜单未命中不能证明节目不存在
- 乱翻书原生RSS GUID直接对应官方单集，因此此前能完整导入285条；另外两档的外部RSS GUID不能直接当作小宇宙ID。RSS并非完成工作的必要前提
- 实际官网频道页当前只展示15条，滚到底无加载更多控件，见 `history_website_check.json`。这只是网页UI测试结果，不代表App能力。最后的补齐无需安装App或登录

## 质量与日更保护
原有438个正确ID全部保留，新增182条，无删除、错频道、重复ID或规范化标题。唯一原有元数据校正是疯投圈“咖啡战争2022”：官方标题不含旧序号前缀，日期2022-06-02T07:00:00.942Z、时长4372秒；前后记录在 `history_changes.jsonl`。同秒日期保留原精度，官方毫秒值留在证据中。

14项测试通过。`schema=1`及既有频道字段不变；index.html、update.py、channels.json、每日工作流的Git blob SHA与基线完全一致。每日UTC09:00（日本18:00）追更、新增收藏机制均保留。使用原始更新函数进行真实最新页同步检查，旧历史ID全部保留；详见 `history_recent_sync_check.json`。工作流没有push触发器，不声称执行了新Actions运行。

提交前重新读取远端，使用expected_sha租约更新并回读文件SHA，避免覆盖日更和其他提交。所有工作只处理公开元数据，无音频下载、登录绕过或付费接口。

## 复查与续用
未确认清单在 `history_unresolved.json`，当前26条。搜索历史仍保留在 `history_searches.jsonl`；当前状态优先于阶段一历史计数。

```sh
python -m unittest test_history_verify.py test_daily_compatibility.py -v
python history_validate.py
```
离线历史验证用于此检查点；未来日更新增节目后需要补相应证明才能再次全量通过，它不会参与或阻止既有每日追更。
