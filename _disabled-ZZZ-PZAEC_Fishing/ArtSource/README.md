# 钓鱼表现资源源文件

资源由本项目原创程序化建模脚本生成，无第三方贴图、网格或录音。维护源码在 `tools/Fishing/Presentation/FishingAssetBuild.cs` 和 `FishingRealismAssets.cs`，生成的 Unity 原生资源保留在本目录 `Generated/`。

重建：在 Mods 根目录运行 `pwsh -NoProfile -File tools/Fishing/Presentation/Build-Assets.ps1`。默认编辑器为本机 `D:/unity/2022.3.48f1c1/Editor/Unity.exe`。工具使用自己的 `artifacts/UnityProject`，只复制验证通过的资源包，不生成或安装游戏 DLL。

## 资源约定

| 资源 | 路径/轴向 | 结构 |
|---|---|---|
| 竿 | `assets/fishing/fishingrod.prefab`，Z 向前，米制，总长 2.7m | 13 根 RodBone00–12，LineExit 在末端骨骼，GripRight/GripLeft 为手部适配锚点；含卷线器、线杯、导环 |
| 漂 | `assets/fishing/fishingfloat.prefab`，Y 向上，几何中心为原点 | 约 0.27m 长，橙白目数、细尾、底部穿线环；运行时按配置尺寸缩放，浸没量由模拟决定；Waterline 子标记仅是建模参考零点 |
| 鱼 | `assets/fishing/fishingfish.prefab`，Z 向头，Y 向背 | 约 0.47m 长，6 根 FishBone00–05，蒙皮主体、尾鳍、背鳍、腹鳍、胸鳍和眼睛；Mouth 为嘴部锚点 |
| 声音 | `assets/fishing/reeldrag.wav`、`watersplash.wav`、`linesnap.wav` | 原创合成音色：泄力棘轮、水花、断线；开发音效，尚非实录品质 |

骨骼通过渲染快照驱动，不包含独立重复模拟或预定“咬钩成功”动画。鱼尾可改变摆幅/频率，竿骨可按真实竿尖位置拟合曲线。骨骼路径不由美术修改而不告知运行时适配。

## 质量与限制

当前是原创低面数功能美术，具有可复用蒙皮和锚点，比原始几何占位更完整，但尚未通过正式拟真资产验收。没有玩家手臂模型或绑定到原版手部骨骼的持竿动画；两个握把锚点只提供对接位置，不能当作已完成的手部动作。

2026-09-30 已细化：512×1024 鱼体色彩/鳞片法线、56圈×48边平滑鱼身、曲面鳍膜和立体鳍条、鳃缝/嘴唇/口腔/触须；碳纤维竿、陶瓷导环、绕线线杯和可旋转导线架。蒙皮顶点合计3186；静态细节已按刚性父节点和材质合并，鱼模型渲染器数检查小于32。仍为原创程序化材质，没有使用扫描素材。Unity 编辑器渲染只用于检查网格、方向和材质；游戏水体遮挡、雾、第一人称裁剪和低光可读性仍需 A 安装后实测。

Unity 2022.3.48f1c1 构建与重新加载已验证；实际游戏 Unity 版本的资源包加载兼容性需在游戏内验证。不得以编辑器成功替代游戏兼容性结论。
