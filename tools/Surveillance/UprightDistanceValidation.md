# 监控远距低清与摄像头转正验证（1.0.8）

## 用户反馈与原因

- 距监控屏约数十米时显示“画面已暂停”：旧的观看门槛只有进入 8 米、退出 10 米；远处即使屏幕仍在视野里，也停止更新。
- 实际摄像头画面侧转 90°：原生 `MotionSensorController.GetCameraTransform()` 返回传感器 Cone，原版预览摄像机作为其子物体继承完整世界旋转。Cone 随方块/模型产生侧滚时，复制此旋转的视频相机也会倾斜。此前四色静态贴图测试只能校验屏幕 UV，不能验证摄像头采集姿态。

## 修复

- `ViewGate` 改为 48 米接入、52 米退出，视野边缘保活 0.85 秒；距离屏幕超过 12 米且可见时，用 384×288、目标 2 Hz 的低清画面。每客户端仍最多同时绘制 4 路不同摄像头，同源共用纹理。低清帧按 2 Hz 的周期判断新鲜度，避免正常帧被误报为暂停。
- `FeedCamera.Follow` 使用 Cone 的 `forward` 保留瞄准方向，以世界 `up` 校正侧滚；近乎竖直的瞄准方向使用 Cone 的局部方向回退，避免零向量。

## 检查结果

- `pwsh -NoProfile -File tools/Surveillance/Build.ps1`：通过，正式 DLL 已写入模组根目录。
- `pwsh -NoProfile -File tools/Surveillance/Test.ps1`：通过。混合 4 路调度测试在 30 FPS 下，1 路近处焦点约 8.9 Hz、3 路远处各 2 Hz；检查 48/52 米距离回差、384×288 档、2 Hz 帧新鲜度、最多 4 路。
- `pwsh -NoProfile -File tools/Surveillance/Start-NativeSmoke.ps1 -VisualQA`：原生 D3D11 隔离世界通过。以侧滚 90° 的 Cone 拍摄世界上、下两个彩色标记，摄像头 RenderTexture 回读为红上 224、蓝下 224、反向均为 0；贴到监控屏再回读为红上 80、蓝下 80、反向均为 0。日志包含 `PASS upright world landmarks through camera RenderTexture and monitor` 和 `VISUAL PASS`；屏幕背面状态文字像素差为 0。游戏日志：`.local-tests/Surveillance/NativeSmoke/run-bd44b6f7d5f1459088e7cb55f7e2bc4c/game.log`。
- 正式 DLL SHA-256：`E3AC8A76D0095604EB180ECBC32BC8CAE93C063DFE20E1A006193867ED57F3CE`。

此隔离测试直接覆盖画面方向，但没有以真实玩家在原存档中走远、转身并观察实际帧率；该体验仍需用户重启客户端后在原场景验收。
