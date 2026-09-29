# 监控大屏放置后坍塌调查（1.0.9）

## 实机证据

用户当前连接的远端服务器“森”，客户端加载 1.0.8。日志 `C:\Users\nxvan\AppData\Roaming\7DaysToDie\BacktraceLogs\Player-2026-09-29_04-13-17.478.log` 中：

- 12:22:37，`-1617,56,-1149` 大屏放置，`occupied=12/12`；0.054 秒后 `root-removing`，`occupied=0/12`，紧接着生成 `fallingBlock`。同一位置 12:22:51 再次发生。
- 12:25:11，`-1626,56,-1137` 放置后约 0.144 秒同样移除并生成 `fallingBlock`。

因此是世界方块真实坍塌，并非仅模型隐藏或切换工具销毁预览。此前隔离测试给屏幕全部 12 格背后建了混凝土墙，未覆盖用户截图中的屋顶立屏。

## 机制与修改

原配置显式设置 `StabilitySupport=false`。游戏 3.2 b10 的 `Block.CanFallBelow` 与 `ChannelCalculator.BlockPlacedAt` 对此值的处理表明：下排屏幕格不提供上排的竖向支撑，只有地板而没有背墙时，上排会坍塌并带走整块多方块屏幕。

1.0.9 改为 `StabilitySupport=true`，让屏幕各格参与正常稳定性传递。未使用 `StabilityIgnore=true`：它会让完全悬空方块跳过正常坍塌。`IsTerrainDecoration` 不是稳定性控制项，未改动。

## 验证

- `Build.ps1 -OutputPath .local-tests/Surveillance/1.0.9-build/PZAEC.Surveillance.dll`：通过；游戏退出后已将该 DLL 安装至正式模组根目录。正式 DLL 与隔离测试中加载的 DLL SHA-256 均为 `321CFF001784CDB69DB92DC6983C97FADC5D6F64AC985054BA6D3F71FCABCBE8`。
- `Test.ps1`：通过，新增 XML 断言要求屏幕提供竖向支撑。
- `PlacementGameQA.cs` 已扩展：四个朝向各测试完整背墙壁挂及只有底部地板的立屏。经原生 `Block.PlaceBlock` 放置，稳定性等待后检查所有 96 个有支撑屏幕的占位，再执行预览回收、工具高亮和供电开关路径。测试程序集编译通过。
- 首两次原生隔离运行中，8 块有支撑屏幕的 96 格占位均通过各阶段检查。另在清空四周的空气包络内放置一块屏幕，立即确认 12/12 格占位；稳定性结算 14 秒后，它仍完全悬空保留。这是原生多方块稳定性的已观察限制，不作为本次有支撑放置修复的验收条件。游戏在最终旋转确定前调用 `CanPlaceBlockAt`，直接按旋转限制悬空会误挡合法贴墙放置。
- 最终原生隔离验收：`pwsh -NoProfile -File tools/Surveillance/Start-NativeSmoke.ps1 -PlacementQA` 通过。日志 `.local-tests/Surveillance/NativeSmoke/run-a772247b2b5c491899e2490bc908db82/game.log` 确认加载 1.0.9；原生放置并等待稳定性结算后 8 块屏幕的 96 格均保留；回收预览及电线高亮后仍保留；供电 Tile 与开关操作后仍保留，最终记录 `PLACEMENT PASS`。悬空屏 `occupied=12/12` 仅作为观察项。

测试调用原生 `Block.PlaceBlock`、预览销毁及接线高亮方法，没有模拟图形客户端玩家实际切换快捷栏；远端服务器“森”仍需安装同版后在用户原存档核验。

当前连接的是远端服务器，房主与客户端必须统一更新至 1.0.9 并完整重启；旧版服务器仍可能按旧稳定性配置坍塌。
