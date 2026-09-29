# 1.0.13 天空和远景恢复

## 本机原生证据

Constants 的图层定义：NoShadow=8，BackgroundImage=9，HoldingItem=10，RenderInTexture=11，NGUI=12，Terrain=28。1.0.12 排除了 8/9/10；其中 8/9 不是第一人称层。原生 XUiC_CameraWindow 确实排除 9，但基地大屏需要完整背景，不应照搬该过滤。

原生 GPU 诊断显示：活动的 AtmosphereSphere 和 CloudsSphere 位于 layer 9；包围盒半尺寸约 45000 米，使用 Game/SkyboxAtmosphere、Game/SkyboxClouds。此前监控还把世界 farClipPlane 固定为 80 米。

## 修复

FeedCamera.ApplyEnvironment 使用玩家场景掩码，补回 NoShadow/BackgroundImage，排除 HoldingItem/NGUI/RenderInTexture。每次实际绘制更新世界远裁剪与天空背景色，不改游戏全局相机或材质。

FeedCamera.Render 仅在监控需要天空层时使用随该摄像头持有的子相机：天空层 Forward 绘制、far=100000，然后原监控 Deferred 相机在相同 RenderTexture 上清深度绘制场景。长距离仅作用于天空层；finally 恢复场景相机掩码和清屏方式、解绑天空目标并恢复 RenderTexture.active。没有新增常驻天空纹理，没有额外流或区块加载。

## 回归

- 编译与 Test.ps1 离线测试通过。
- 新增真实天空层对照：关闭背景时显示指定清屏色，启用后必须被原生天空球画面替代，检查相机状态恢复。
- 新增 250 米外 NoShadow 和 Terrain 两层目标：旧 80 米裁剪看不到，恢复远裁剪后必须正确回读绿色。
- 保留真实摄像头 12 组前后对照、UV、最后一帧、手持武器过滤及背面文字回归。
- 原生图形最终结果待本次运行后补充。测试不代表客户端满负载下的性能基准；场景仍受游戏区块加载及远景设置限制。
