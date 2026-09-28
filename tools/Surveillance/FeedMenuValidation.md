# 1.0.5 视频与菜单修复验证

日期：2026-09-28。

## 原生 API 证据

- `XUiC_Radial.SetCurrentBlockData` 使用 `ui_game_symbol_{0}` 格式化 command.icon；此前传入完整键，出现重复前缀。
- `MotionSensorController.GetCameraTransform` 返回 Cone；初始化中执行 Cone.gameObject.SetActive(false)。旧相机挂在该节点下，activeInHierarchy 随之关闭。
- `XUiC_CameraWindow.CreateCamera` 使用 DeferredShading、SolidColor 并屏蔽 layer 8；新 FeedCamera 与此保持一致。
- 原 IMGUI 菜单只修改 Cursor/bAllowPlayerInput，没有进入原生模态窗口管理。现在使用 GUIWindow 子类，设置 playerUI/alwaysUsesMouseCursor/isInputActive，并通过 GUIWindowManager.Open(window,true) 打开；Esc、原生关闭回调和手动关闭均释放输入。

## 已通过

- Build.ps1：引用本机游戏程序集编译成功。
- Test.ps1：调度、目标框、存档/协议以及屏幕放置/激活路由回归通过，新增轮盘图标前缀检查。
- Start-NativeSmoke.ps1 -VisualQA：独立 UserData/Saves，图形模式启动真实游戏；不修改用户存档、不停止用户进程。游戏同时会发现安装目录的其他 mods，本测试仅用隔离目录覆盖监控模组并加入 QA 插件。
- FeedCamera：父参考锥体保持关闭，视频相机独立激活且自动渲染禁用；两次 Camera.Render 的 GPU 回读分别为红、绿，位置与旋转跟随关闭的参考节点。
- 实例化真实 ScreenView 模型并调用 Bind/Show，GPU 回读验证无信号为黑、绑定纹理显示红色、纹理更新后显示绿色；根碰撞体覆盖屏幕。实际 shader 为 Sprites/Default。

日志：`.local-tests/Surveillance/NativeSmoke/run-881cc390c4e3419d84c7ac57d18b170b/game.log`，结尾包含 `[SurveillanceQA] VISUAL PASS`。

备份：`.local-tests/Surveillance/pre-feed-menu-fix-1.0.4/PZAEC.Surveillance.dll`。

## 验证范围

GPU 验证使用可控测试帧，未模拟玩家完整联机绑定、实体区域加载或鼠标操作。重启客户端后仍需确认：设置图标可见、菜单期间视角不随鼠标转动、Esc 恢复操作、附近已加载且有电的摄像头持续显示实际场景。未加载区域仍显示“监控区域未加载”，不会强制加载远处区块。
