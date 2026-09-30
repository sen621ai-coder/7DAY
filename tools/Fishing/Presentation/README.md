# D 表现层：构建、接入与验证

## 已实现的入口

```csharp
var presentation = new PZAEC.Fishing.Presentation.FishingPresentation(
    modDirectory,
    dedicatedServer: isDedicatedServer,
    options: new PZAEC.Fishing.Presentation.PresentationOptions(),
    cameraFeedback: optionalEulerDegreesCallback,
    diagnosticLog: optionalLogCallback);
```

实现真实 `IFishingPresentation` v1。实例只能由游戏主线程调用，观察者每个钓鱼会话一个实例。

- `Begin` 保存会话，不初始化 Unity 对象；`Render` 第一次非专服帧才加载资源。
- `RenderFrame.RenderOrigin` 每帧读取。竿/漂/鱼按绝对坐标转换；鱼线直接使用已转换的场景端点，避免大世界二次加减原点导致线与竿尖分离。
- 竿骨骼根据共享 Rod.Root/Tip/Forward 拟合，竿尖不回写；漂位置和朝向直接来自快照，没有第二套假吃口动画。
- 鱼骨骼按实际速度和冲刺状态摆动；鱼线到可见鱼嘴或浮漂，不改变模拟的钩或物理位置。
- 几何鱼线现在分为主线与子线，共同连接漂底环，子线接到鱼嘴；卷线器到各导环也有连续细线。按 LineLength + LineExtension 分配松弛；不足以容纳全部端点时显示绷直路径，并通过 LineRouteExcessMeters/日志/HUD 明确报告差值，不改模拟。
- 物理仍是v1直接弹簧，B还需统一过漂路径约束；D修复可见连接不代表物理问题已解决。实际B审计最大线长差0.31079m、竿端点超长0.17192m。`Audit-Geometry.ps1`可复现。
- 鱼尾以积分相位连续摆动，胸鳍随游速收拢，鳃/嘴有轻微动作。摇轮按回收米数转动，出线只旋转线杯；重复帧与异常快照更正不重复转动。`ReelHandTarget` 是随摇轮运动的左手抓握目标，供A后续接原版手部。
- 8 组复用涟漪/小水滴，4 个常规音效声道；事件唯一序号去重，队列上限 32。
- 声音在客户端以空间音播放，不调用游戏 AI 噪声系统。3 个音色均为原创合成开发音效，尚未经过实录品质验收。
- 镜头反馈只通过可选回调输出**欧拉角偏移，单位度**；默认上限 0.35 度，再乘配置强度。D 从不改摄像机 Transform。A 必须把此值视为本帧偏移而非累加量，并在正确镜头阶段应用；不接回调则无镜头扰动。
- `RightGrip`/`LeftGrip` 返回已创建的世界空间握持锚点；用于 A 的手部/IK 对接，不代表原版玩家动作已经接入。
- 调试 HUD 只在 `ShowDebug && IsLocalPlayer` 时显示，默认关闭。音量、声音、镜头反馈可通过 `PresentationOptions` 调节。
- `Clear` 立即停循环音、禁用并释放游戏表现根节点、归零镜头偏移。断线/上岸终结事件在清理前立即播放独立短音尾，最长约 0.7 秒后自动销毁；上限 4 个。`Dispose` 在世界退出时立即删除所有尾音并释放共享资源包。A 在世界退出必须调用 Dispose，不能只 Clear。
- 多个观察者以引用计数共用同一路径的资源包；一个会话退出不会卸载其他会话正在使用的资源。
- 资源加载错误记录 `LastError`，停止该会话表现，模拟和库存不受影响。A 应接日志回调，避免用户只见无画面而没有诊断。

## A 工程需要的引用

除 CoreModule 外，添加安装目录中的 `UnityEngine.AssetBundleModule.dll`、`UnityEngine.AudioModule.dll` 和 `UnityEngine.IMGUIModule.dll`，均 `Private=false`。D 的编译脚本引用安装目录真实程序集且只输出到自身 artifacts，不改 A 工程或最终 DLL。

## 命令

按顺序在 Mods 根目录执行：

1. `pwsh -NoProfile -File tools/Fishing/Presentation/Test-Math.ps1`
2. `pwsh -NoProfile -File tools/Fishing/Presentation/Compile.ps1`
3. `pwsh -NoProfile -File tools/Fishing/Presentation/Build-Assets.ps1`
4. `pwsh -NoProfile -File tools/Fishing/Presentation/Test-Unity.ps1`
5. `pwsh -NoProfile -File tools/Fishing/Presentation/Audit-Geometry.ps1`
6. `pwsh -NoProfile -File tools/Fishing/Presentation/Encode-Preview.ps1`

仅重新输出预览可用 `Test-Unity.ps1 -PreviewOnly`。`Previews/fishing-motion.mp4` 是6秒的编辑器动态预览，已烧录“scripted snapshots / not in-game”标记，不能作为实机录像。

Unity 项目在 `artifacts/UnityProject`，日志位于 `Logs/`，图片和验收报告位于 `Build/`。这些临时文件被 tools/Fishing/.gitignore 排除；交付预览另存到本目录 `Previews/`。资源源文件在模组 `ArtSource/Generated/`，客户端资源包在 `Resources/fishing-presentation.unity3d`。

运行 Unity 构建/验证须串行，避免同项目被两个编辑器打开。`Build-Assets.ps1` 返回成功后才可验证；无新验收戳则不复制失败资源到模组目录。

## 游戏内验收仍需进行

当前编辑器的水面是展示用平面，不是游戏 GPU 水体。需要 A 完成可安装主循环后验证：

| 场景 | 要检查的结果 |
|---|---|
| 白天，1920×1080，漂距 3/10/20m | 目数能否读清、鱼线锯齿、漂与真实水面是否贴合；不无限放大漂 |
| 黄昏/夜晚，同距离 | 橙白标记可读性与照明，不能整根鱼竿莫名发光 |
| 试饵/黑漂/扬竿 | 漂位置来自模拟，动作和挂钩窗口一致，无额外随机动画 |
| 冲刺/送竿/泄力 | 竿尖连接不断裂、弯曲不突跳、泄力声与放线同步 |
| 断线/上岸/取消 | 线和场景立即收回、尾音有限、HUD/镜头归零、无持续声音 |
| 大坐标/原点切换 | 模型位置与鱼线端点一致，原点改变后旧涟漪不漂移 |
| 第一人称/他人观察 | 不遮住视线、不穿手、手部动作对接正确、远端不影响本地镜头 |
| 重复进入/退出 30 次 | 资源包/材质/音频对象不增长，退出世界后尾音也归零 |
| 多人 1/4/8 场同时进行 | 主线程耗时、GC 分配和绘制批次记录，并按结果决定优化目标 |

尚未提供正式玩家持竿/扬竿/收线动画；握持锚点、竿/鱼蒙皮是对接基础。正式美术和真实游戏水材质下的效果不得以编辑器检查通过替代。

当前场景预览和资产图均明确属于编辑器测试。游戏版本由 A 核验为 Unity 2022.3.62f2；资源构建编辑器为 2022.3.48f1c1。资源已在构建编辑器重载成功，跨补丁版本的游戏加载仍待实测。
