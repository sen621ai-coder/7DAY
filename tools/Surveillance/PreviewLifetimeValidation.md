# 1.0.6 放置后切换工具消失：进展与验证范围

## 用户现象

大屏外观正常；放下后切换至接线工具、尚未点击就消失，用户报告占位也不存在。用户此前不能退出，2026-09-28 深夜确认已退出，随后完成隔离测试。

## 确认的缺陷

本机游戏 IL：

- ItemActionConnectPower.StartHolding 调用 WireManager.ToggleAllWirePulse(true)；此路径处理电线高亮，没有删除屏幕方块的调用。
- ItemClassBlock.CreateMesh 给模型加一层空 GameObject，CloneModel 放在其子层。
- GameObjectPool.DestroyObject 清理整个传入对象子树的运行时材质，然后销毁传入对象。
- 1.0.5 BeforePoolDestroy 仅 GetComponent<ScreenView>() 检查最外层；对原生预览外层容器无效。内部屏幕的 frame/off 材质与模板及其他屏幕共享，因而会被错误销毁。

1.0.6 修复：GetComponentsInChildren<ScreenView>(true)，仅解绑每个待回收监控子树上的材质引用，并标记该实例 Retiring；其他物体仍走原生清理。

## 已验证

- Build.ps1 引用真实游戏程序集成功，输出 DLL 与 ModInfo 均更新至 1.0.6。
- Test-PreviewLifetime.ps1 执行生产清理函数，使用对象树 API 替身覆盖直接/两层容器、激活/未激活子树，模拟原生后续材质销毁。
- 将测试指向 HEAD 的 1.0.5 源码，失败：Placed screen material destroyed by preview cleanup。
- 同一测试使用修复源码通过，已放置屏幕的材质存活，非监控渲染器仍正常清理。
- 原有离线回归通过。
- PlacementGameQA.cs 已对真实游戏程序集编译通过，并于 2026-09-29 00:01 完成原生隔离测试，最终记录 `PLACEMENT PASS`。
- 1.0.5 DLL 备份在 `.local-tests/Surveillance/pre-preview-lifetime-1.0.5/`。

## 未确认与下一步

共享材质回收本身不会删除世界 BlockValue，不能据此声称“占位消失”已经解决。1.0.6 加入事件日志 `[Surveillance] Screen lifecycle`，区分 placed、root-removing、child-removing、falling，记录 client/server、根坐标、当前占位数和 Tile 类型；不拦截删除或坍塌。

已运行 `pwsh -File tools/Surveillance/Start-NativeSmoke.ps1 -PlacementQA`，使用独立 QA 存档，保留完整图形初始化。依次通过：

1. 四个方向分别放置壁挂屏，等待 3 秒稳定性处理：全部 48 格保留，子格均指向正确主格。
2. 用原生 CloneModel 创建模型与带外层容器的预览，通过 GameObjectPool.DestroyObject 回收预览，并调用接线工具 StartHolding 所使用的 ToggleAllWirePulse(true)。等待 3 秒后，占位、激活模型、启用的碰撞体以及非空材质均保留。
3. 调用原生 ItemActionConnectPower.GetPoweredBlock 为四块屏创建供电 Tile，切换 IsToggled，关闭电线高亮并等待 8 秒。全部 48 格及四个供电 Tile 保留。

首次测试过早要求刚放置的屏幕已有供电 Tile，失败于 `powered tile missing`。复核本机游戏 IL 后确认：原生 BlockPoweredLight 同样按需创建供电 Tile，ItemActionConnectPower.GetPoweredBlock 包含创建、InitializePowerData 和 AddTileEntity 流程。因此修正的是测试顺序，没有为此修改生产代码。

通过日志：`.local-tests/Surveillance/NativeSmoke/run-30fa2d4fa3d146febbac026156984449/game.log`。

测试使用生产 1.0.6 DLL，SHA256：`3F2497465194F2D38821CBE14EB6479C22FB5C52F218DEE877547ADEA2E268C2`。离线 Test.ps1 亦再次全部通过。测试进程已退出。

验证边界：这是独立 dedicated 世界中的实际方块操作与原生 Unity 模型/回收 API 测试，模型由测试显式实例化；没有模拟联机图形客户端的完整玩家输入，也没有实际连接发电机验证供电网络。此次没有复现 BlockValue 被删除。若用户联机环境仍出现占位消失，可从新增生命周期日志区分移除与坍塌事件，并核对双方版本。

目前没有操作运行中的游戏或用户存档，也没有通过增加稳定性、强制重建丢失方块等方式掩盖未确认原因。
