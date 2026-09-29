# 1.0.7 画面方向、视野策略和背面文字

## 原因与修改

- 原视频面是 Unity Cube，其实际绘制面的 V 坐标方向与摄像头纹理不一致。最终实现复制已在原生游戏 GPU 测试中能绘制的 Cube 网格；逐面动态翻转实验证实，翻转法线朝 -Z 的面会恢复四角正确方向，翻转 +Z 面不会改变画面。最终只对 -Z 面翻转 V，并保留其余已有 UV 处理。物理屏幕正面仍朝 +Z，状态文字和观看判定沿用此方向。不再使用在本机 D3D11 中未显示的自建四边形。
- 旧视野门控在离开视野 0.2 秒后返回待机，再进入时将 FeedClock.LastSuccess 置为 -1，等待首帧。现在支持视口边缘预热、0.85 秒离开迟滞、离屏最多一路 2 Hz；暂停旧帧明确标注，重入先调度新帧。计算投影与焦点时使用实际 Mesh.bounds，适配 Cube 的缩放尺寸。
- 原状态文字由字体材质绘制，可能从屏幕背面透过机身。ScreenView.FaceViewer 在本地玩家 LateUpdate 按显示面正反面控制 StatusRenderer；图形 QA 可以传入测试相机分别验证。

## 已完成验证

- `pwsh -NoProfile -File tools/Surveillance/Build.ps1`：真实游戏程序集编译成功，DLL 与 ModInfo.xml 均为 1.0.7。
- `pwsh -NoProfile -File tools/Surveillance/Test.ps1`：调度、视野迟滞、标记、放置、设置、预览回收、状态同步测试全部通过。
- `pwsh -NoProfile -File tools/Surveillance/Build-VisualQA.ps1 -OutputDirectory .local-tests/Surveillance/VisualQA-1.0.7-compile`：四角彩色纹理、正面状态文字、背面文字差分测试程序编译成功。
- 原生 D3D11 隔离测试证实游戏实际回退到 `Sprites/Default` 视频材质。自建四边形在该环境下未绘制，日志分别为 `.local-tests/Surveillance/NativeSmoke/run-1a2d66f199694eb081726f9b6b85081b/game.log` 和 `.local-tests/Surveillance/NativeSmoke/run-d3d9d9b302874d56a075e6e8050d48fc/game.log`。该方案已撤回；最终版本使用此前原生 GPU 测试通过的 Cube 网格。
- 实验日志 `.local-tests/Surveillance/NativeSmoke/run-d0dac8f6029f48cd9371009102c98770/game.log`：基线四角为蓝/黄/红/绿，翻转 +Z 面无变化；翻转 -Z 面后依次为红/绿/蓝/黄。
- 最终原生测试日志 `.local-tests/Surveillance/NativeSmoke/run-02a97fff27e64decadb30276471253af/game.log` 包含 `VISUAL PASS`：四角四色纹理方向正确；正面“待机”文字变化 21 个像素，背面变化 0 个像素；黑屏、连续红绿视频帧和 4×3 屏幕根碰撞体通过。原生图形后端为 D3D11。
- 红框模块原先读取了 Cube 的另一面 UV，可能与已校正的视频错位。现从实际可见的 -Z 法线三角面读取 UV，并把红框画在物理 +Z 外表面。最终原生测试日志 `.local-tests/Surveillance/NativeSmoke/run-add1aa8bebf94eeda68dfdd65ca8709e/game.log` 包含 `VISUAL PASS`：四色四角正确；指定左下象限的红框像素数为 `94,0,0,0`；正面文字变化 21 像素、背面 0 像素。黑屏、连续红绿帧与根碰撞体再次通过。
- 最终编译 DLL 的 SHA256 为 `9161E41D266545666FA55685F994FDC25926090EFEF849A3389C82A5550DFBFC`，与通过最终原生测试的暂存 DLL 一致。测试进程已退出。

## 验证边界

图形测试使用独立 dedicated 世界、真实 D3D11 渲染和手动测试摄像机。视野切换的离屏预热与暂停策略已由离线门控/调度测试覆盖；尚未模拟联机客户端玩家完整转头与多屏动态切换。隔离测试没有接触用户存档。
