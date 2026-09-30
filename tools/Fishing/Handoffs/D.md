# D：钓鱼场景与视听表现交接

2026-09-30。**第二轮真实感改进已交付，可由 A 整合；原版玩家手部与游戏内拟真验收尚未完成。** 最新明细及实测物理差值见 `../Presentation/Realism-update.md`。

## 实例化与依赖

实现类：`PZAEC.Fishing.Presentation.FishingPresentation : IFishingPresentation`。

构造：`FishingPresentation(string modDirectory, bool dedicatedServer=false, PresentationOptions options=null, Action<UnityEngine.Vector3> cameraFeedback=null, Action<string> diagnosticLog=null)`。

按照契约 Begin → Render/OnEvent → Clear，世界退出必须 Dispose。每个会话独立实例，多会话共享同一路径资源包。Render 必须主线程，传入实际 RenderOrigin 和 IsLocalPlayer/IsDedicatedServer。

可选相机回调提供“本帧欧拉角偏移，度”，不可逐帧累加。默认上限 0.35 度再乘 Controls.FeedbackIntensity01，可关闭；A 不接回调则无镜头扰动，不影响其他效果。RightGrip/LeftGrip 提供握持锚点，未对原版玩家骨骼施加任何操作。

工程需要 CoreModule、AssetBundleModule、AudioModule、IMGUIModule。A 的交接已确认添加 Unity 引用；D 不修改工程。只在 D 的 artifacts 编译验证，没有生成模组根目录游戏 DLL，也没有安装/重启游戏。

## 已交付

- `Source/Presentation/PresentationMath.cs`：参考基与 pitch/yaw 到竿方向、端点约束竿曲线、按有效长度下垂的鱼线、原点转换、帧及事件校验。
- `Source/Presentation/PresentationMotion.cs`：鱼尾相位积分与平滑幅度、按实际回收长度转动的摇轮/导线架、出线时独立旋转的线杯，避免快照重复或更正引起跳动。
- `Source/Presentation/FishingPresentation.cs`：竿/鱼骨骼、浮漂姿态、鱼线、8 组涟漪/水滴池、4 个普通音效声道、泄力声、断线/上岸尾音、可选镜头反馈及调试 HUD。
- `Source/Presentation/PresentationAssets.cs`：按路径引用计数共用资源，避免第二观察者重复加载失败或退出时误卸载他人资源。
- `Resources/fishing-presentation.unity3d`：三套 prefab、纹理材质、自带鱼线/泡沫 shader、三种原创合成音效。
- `ArtSource/Generated/`：Unity 原生 mesh/material/prefab/texture/audio 源文件；`ArtSource/README.md` 说明轴向、尺寸、锚点和来源。
- `tools/Fishing/Presentation/`：原创生成脚本、真实游戏程序集编译检查、独立数学测试、Unity 实际资源渲染与生命周期验证、接入 README、Previews。

Begin 不触碰 Unity 场景，专服不创建对象。无效快照隐藏场景、有效后恢复，错误资源有 LastError 与诊断回调。Clear 归零镜头、停止循环音并释放场景；终结事件的独立尾音最多 4 个、约 0.7 秒内自毁，Dispose 立即清除尾音。

## 验证

1. `pwsh -NoProfile -File tools/Fishing/Presentation/Test-Math.ps1`：26 项真实生产源码测试通过，包括漂底环共同连接、线长矛盾诊断、刚性握把、鱼尾连续性、卷线/出线分离、大坐标、跨会话事件去重及非法快照。
2. `pwsh -NoProfile -File tools/Fishing/Presentation/Compile.ps1`：Presentation + 当前真实 Contracts v1 对安装目录程序集编译通过。产物只在 D/artifacts。
3. `pwsh -NoProfile -File tools/Fishing/Presentation/Build-Assets.ps1`：Unity 2022.3.48f1c1 构建、重载资源包、3 个 prefab、材质、蒙皮检查通过；重复构建也作为验证项。
4. `pwsh -NoProfile -File tools/Fishing/Presentation/Test-Unity.ps1`：22 项 Unity 检查通过，新增主线/子线共同穿过漂环、子线接到鱼嘴及细化鱼模型网格合批检查。1000 次 Render 不增长场景对象。编辑器计时只代表无摄像机绘制的 CPU 调用，不能替代游戏性能结果。
5. `pwsh -NoProfile -File tools/Fishing/Presentation/Audit-Geometry.ps1`：用 B 当前真实模拟、种子29、平面水域跑30秒，获得1417个中鱼快照；物理几何矛盾已量化，见下文。该命令不改B源码。

过程发现并修复：重复构建时未完整保存鱼蒙皮；重复加减世界原点导致线端点偏移；双面鳍共用反向三角形导致法线抵消发黑。验证日志和最新计时在 D/artifacts/UnityProject/Build/。

## A/B 需要处理的联调问题

1. **浮漂与中鱼后主线的共同约束**：D 第二轮已经渲染竿尖→漂底环→鱼嘴的两段线，加入物理 LineExtension 后按比例分配松弛。路径比可用长度长时，以绷直段连接并输出 `LineRouteExcessMeters`、日志及debug HUD；不回写B或假造张力。这只修复了可见连接，不能替代物理约束。B实际快照审计（以漂/鱼中心计算）测得最大差值 **0.31079m**，需要B按过漂路径计算张力和长度，D再复验。
2. **竿弯长度**：握把现在保持刚性，竿身使用柔性段曲线，但D仍以B的Tip为终点。实际B审计测得Tip到Root最大超出配置竿长 **0.17192m**；D输出 `RodChordExcessMeters`，没有通过挪动竿尖掩盖。需要B定长/弯曲几何约束。
3. **水面精度**：FloatPosition 是中心。涟漪通过中心 + (Submerged01−0.5)×Float.Height 估计逻辑水面；完全出水/没入时该值是截断估计，真实水波贴合需 A 水查询精度提升。
4. **手部与镜头**：原版持竿/扬竿/收线动作尚未接入。现有锚点能给 A 的持物/IK 层定位，但不能替代正式手部动画；玩家摄像机回调同样需 A 在正确阶段接入。

## 未验证与资产质量

当前原创程序化资产已细化为平滑鱼身、鳞片法线、嘴唇/口腔/触须/鳃缝、带立体鳍条的曲面鱼鳍、碳纤维竿、陶瓷导环、绕线线杯与导线架。合计3186个蒙皮顶点，静态细节按骨骼父节点与材质合批。仍非扫描写实美术，音效仍是合成音色，原版玩家手部动作未接入。

构建编辑器为本机 `D:/unity/2022.3.48f1c1/Editor/Unity.exe`，游戏是 A 核验的 2022.3.62f2，实际游戏加载兼容性尚未验证。编辑器场景用透明平面代替水体，Previews 中白天/低光图均非游戏截图。

待 A 集成后：实际第一人称裁剪、GPU 水体遮挡、夜晚 3/10/20m 浮漂可读性、手部动作、联机观察、全生命周期退出和 1/4/8 人性能。详细清单在 Presentation/README.md。
