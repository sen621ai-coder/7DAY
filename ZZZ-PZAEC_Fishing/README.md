# PZAEC Fishing — 单人集成测试版 0.1.2

已接入单人本地玩家的钓鱼流程；真实鼠标手感、移动碰撞及完整玩家存档重载仍待实机验收。远程客户端、专用服务器不会启用玩法。网络代码正在并行开发，尚未接入本入口。

## 操作

已准备专用 `FishingTest` 存档时，首次进入会从该存档的 `pzaec-fishing-test-kit.xml` 领取钓竿 2、蚯蚓 200、烤鱼 10。发放限定文件中记录的绝对存档路径；已发数量跟随角色原生保存，背包满时等待空格，正常存档没有该请求文件便不会发放。

关闭游戏后运行 `tools/Fishing/Integration/Install-TestBuild.ps1` 安装已构建的正式模组 DLL，重新启动进入单人存档。配置在 `Config/Fishing/settings.xml`。

1. 制作并装备 `pzaecFishingRodBasic`，携带 `pzaecFishingBaitWorm`（腐肉制作）。朝下瞄准 25 米内、深度至少 0.6 米的水面，左键抛投。有效抛投消耗 1 蚯蚓；无效位置/缺饵不开始，抛投后取消不退饵。
2. 观察漂相，咬实后左键配合向后移动鼠标抬竿。只点按钮不抬竿不能有效中鱼。
3. 右键持续收线，`-` / `=` 调泄力；鼠标侧移侧压、向后抬竿。接近竿角上限后继续后拉可产生受力后退，原生 WASD/碰撞仍生效。
4. 左 Alt 自由观察，左 Ctrl 暂停控竿以挪回鼠标，Esc 取消。切装备、受击、死亡、菜单/失焦、游泳、车辆和世界退出会释放操作。
5. 鱼疲劳并收到近岸后自动收获。鱼获保留重量和结算编号，每条占独立背包格；满包保留待领取结果，空出格子后自动领取，领取前不能再次抛投。

待领取/完成状态使用玩家原生 Buff CVar，与背包进入同一 PlayerDataFile。进行中的鱼战不保存。异常库存写入停止自动重试；完整玩家保存/重载及崩溃一致性未验收，不宣称任意故障下 exactly-once。

## 开发入口

- 多会话总计划：`../tools/Fishing/钓鱼系统并行开发计划.md`
- 公共契约：`../tools/Fishing/Contracts-v1.md`
- 总进度：`../tools/Fishing/STATUS.md`
- A 的最新证据与限制：`../tools/Fishing/Integration/A-runtime-report.md`

唯一公共边界为 `Source/Contracts/`。B–F 在自己的目录实现接口，不直接依赖其他模块的内部 DTO。A 负责工程、Runtime、契约、整合与安装。

## 构建与验证

从 Mods 工作区运行：

```powershell
pwsh -NoProfile -File tools/Fishing/Integration/Build.ps1
dotnet build ZZZ-PZAEC_Fishing/Source/PZAEC.Fishing.csproj -c Release
pwsh -NoProfile -File tools/Fishing/Integration/Inspect-Native.ps1
pwsh -NoProfile -File tools/Fishing/Integration/Test-Contracts.ps1
pwsh -NoProfile -File tools/Fishing/Integration/Test-Modules.ps1
```

两种构建均只输出到 `tools/Fishing/Integration/artifacts/build/`。安装脚本只复制正式 PZAEC.Fishing.dll，并备份已有版本；不要安装探针程序集。移出根目录的 PZAEC.Fishing.dll 并重启即可禁用钓鱼运行时。

原生探针另开隐藏的独立游戏进程及全新 `FishingM0_Isolated` 存档：

```powershell
pwsh -NoProfile -File tools/Fishing/Integration/Start-NativeProbe.ps1
# 根据 artifacts/native-session.json 中的 Log 查看 [FishingM0] RESULT。
pwsh -NoProfile -File tools/Fishing/Integration/Stop-NativeProbe.ps1
```

启动脚本立即返回，不自动等待。探针仍会加载安装目录的整合模组，**隔离的是存档及测试产物，不是全部第三方模组**；当前林业模组需要图形设备，不能使用 `-nographics`。停止脚本核对 PID 与启动时间，只关闭自身测试进程。

## A 提供的运行时

- `LocalFishingRuntime`：装配 B/C/D/E、装备识别、选点扣饵、生命周期、表现和奖励接线。
- `SessionDriver`：固定步长、输入累积、环境插值、事件去重和释放；物理竿尖由 B 计算。
- `NativeWaterQuery`：原生水格/已加载区块查询；水面为体素估计，不能声称匹配水面着色器的波浪。
- `NativeControlLease`、`NativeInputHooks`：仅钓鱼会话持有输入租约。保留原生 Update 和 MoveByInput，局部覆盖镜头输入及移动意图。
- `MovementBridge`：在原生移动轴上合并额外后退，并仅衰减逆拉力分量；不修改玩家 Transform。
- `CatchSettlement`/`NativeCatchRecord`：本地授权会话结算、待领/已领原生保存记录。多人路由尚未接线。

待机与抛投均显示 D 的钓竿。0.1.2 对原生第一人称 FOV 进行握点投影转换，装备采用原生石斧闭合握持姿势；物理竿根仍读取未转换的原生挂点。面板放大并移到左上侧，咬实时增加中央提示。真实手指贴合及左手卷线动作仍需实机复核和完善。

0.1.1 修正正式配置下鱼体力恢复过高、持续遛鱼仍无法进入上岸门槛的问题；保留 450 J 耐力，将受钩恢复功率从 8 W 调为 0.2 W。3 个种子的正式配置回归可在约 92–107 秒完成上岸，这些是固定脚本测试时长，实际操作会有所不同。提竿容许点击前 0.12 秒内的有效抬竿，或点击后 0.2 秒内的抬竿；不扩大咬钩窗口，也不会让仅点按钮自动中鱼。

现在日志记录接受抛投、点击提竿时的鼠标量、状态变化、5 秒一次的受力/体力、关键事件及退出原因。鱼战手感、线漂几何、多人和崩溃恢复仍需验证，详见总进度。
