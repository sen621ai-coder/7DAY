# 大屏放置后消失：1.0.10 根因与验证

## 用户复现

`Player-2026-09-29_08-35-44.780.log` 确认本地测试档 CargoM1Test_20260928 加载 1.0.9。地面 (1183,86,1130)、贴墙 (1172,86,1125) 两次放置均先有 12/12 占位，约 0.023 / 0.048 秒后进入 falling 并删除根格。这不是客户端旧版、工具隐藏模型或服务器版本不一致。

## 根因：两套结构模式冲突

在原存档只读副本运行旧版 1.0.9，日志：`.local-tests/Surveillance/NativeSmoke/run-3366866062fc4b7a824445f19be40da7/game.log`。

- 已等待根格及所有相邻区块完成原生稳定性初始化。
- (1183,85,1130) 是真实 concreteShapes:cube，稳定性 15。
- 新屏全部 12 格成功写入，稳定性全部为 15。
- World.AddFallingBlock 调用栈来自 MultiBlockManager.UpdateOversizedStability，随后剩余 0/12。

3.2 b10 的 OversizedBlockUtils.GetLocalStabilityBounds 将 OversizedBounds 变成底部一格高的支撑检测区域，收缩边界后以方块中心判断支撑。旧屏边界 (-0.5,1.5,-0.4),(4,3,0.2) 是薄板尺寸，深度及偏移使支撑区域不能覆盖地板方块中心；壁挂也不应依靠这套只检查底部的逻辑。

1.0.10 移除 OversizedBounds，保留 MultiBlockDim=4,3,1 和 StabilitySupport=true，交由真实 12 格的原生结构计算。屏体几何、碰撞体、朝向和位置未改动。没有使用 StabilityIgnore、强制稳定值、吞掉正常坍塌或自动重建方块。

旧 multiblocks.7dt 可保存 oversized 标志。原生加载器逐类别重建登记；窄范围 Harmony 前缀仅跳过本模组屏幕的废弃 oversized 类别，其他类别照常加载。Initialize 完成后标记登记数据待保存；不操作世界方块或供电 Tile。

## 撤回旧测试的承重结论

1.0.6–1.0.9 的隔离占位检查不能作为结构稳定性验收：

1. 仅添加 ChunkObserver，没有原生玩家。ChunkManager.task_Lighting 的处理额度为 Players.Count * 2，区块一直 StopStabilityCalculation=true。
2. 支撑材料使用 concreteShapes 而不是实际方块 concreteShapes:cube，解析为空气。旧测试没有断言支撑存在。
3. 悬空负对照未坍塌时不应该降格成“引擎限制”，应判定测试无效。

新测试加入原生测试玩家、等待所有相关区块就绪、校验每格支撑存在且有稳定性，要求悬空负对照必须坍塌。四方向地面与无底板壁挂经原生 Block.PlaceBlock 放置，再回收预览、切换电线高亮、创建供电实体并开关，最后拆支撑检查正常坍塌。存档副本测试覆盖原坐标及已有两块屏幕。

## 验证记录

- 1.0.10 已原位编译；tools/Surveillance/Test.ps1 离线回归通过。
- 第一次修正版副本测试在原生 Physics.BakeMesh / 区块网格分配时内存不足退出，未计为通过。已缩小隔离观察范围并关闭测试动态远景，不改变结构稳定性。
- 原存档副本通过：`.local-tests/Surveillance/NativeSmoke/run-434f57fc3ace4838b6f69f13267d47ab/game.log`。地面原位置四朝向和贴墙原位置各保留 12/12 格；两块已有屏幕的全部 24 格和供电 Tile 保留。日志确认旧 oversized 登记迁移执行。
- 正式 DLL 与上述副本加载 DLL 的 SHA-256 相同：`6F64884654D27A4A20E643DA62BF7AB196FF94BEC84BF111213925E887362108`。
- 四方向与工具回归通过：`.local-tests/Surveillance/NativeSmoke/run-0f1c0b38b2514784b52e7cdea86cf7bc/game.log`。四块完整背墙壁挂及四块底部支撑屏，96 格在放置、预览回收、电线高亮和供电开关后全部保留；悬空屏剩余 0/12；分别拆掉壁挂背墙和落地屏底部支撑后，两屏按原生机制坍塌，最终 PLACEMENT PASS。
- 构建数千格支撑会塞满原生 BlockPlacedAt 的 200 项队列，后续检查可能被丢弃。最后的测试把搭建支撑与正式放屏分阶段，等待稳定性队列和坍塌队列清空才开始；前一轮未排空队列的悬空对照结果不计为通过。

运行入口：

```powershell
pwsh -NoProfile -File tools/Surveillance/Start-NativeSmoke.ps1 -PlacementQA
pwsh -NoProfile -File tools/Surveillance/Start-NativeSmoke.ps1 -PlacementQA -PlacementSource SavedPlacementGameQA.cs -SaveSource .local-tests/Surveillance/PlayerSaveSnapshot-20260929
```

第二条仅对副本中固定的报告坐标有效；脚本再次复制副本到唯一 QA 目录，不写原存档。每次只允许一个游戏进程。自动测试覆盖原生放置和工具相关调用，不等同于真人鼠标/快捷栏完整输入回放。
