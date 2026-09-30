# 三路分拣箱卸货后无法交互排查

现场反馈：朋友房间中，无人机卸货后，三路分拣箱提示“已锁定”，不能按 E 设置。无法提供房主日志；尚未确认放置者本人是否也打不开。

## 已验证

- 本机 2026-09-29 23:12:08 客户端日志加载自动化工厂 0.11.8，连接朋友房间。没有对应服务器库存事务记录。
- 原生隔离测试现在实际向 `yfAutoRouter` 执行卸货、持久化与事务提交；原先此设备只覆盖卸货规划。WorldScheduler 仍使用普通箱子，保留普通仓库覆盖。
- 210 项通过、0 失败。提交后货运 fence 已释放，原生库存访问会话可交接，交互保护不再拦截。所有者、授权名单、权限锁和 isJammed 均与卸货前一致；所有者仍获授权，库存数量守恒。
- 测试报告：`.local-tests/CargoDrones/NativeQA/run-cef818b3b3a24e9da6bc39b52fc66fce/UserData/Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt`。

## 结论边界

未复现现场故障；本次不修改运行时、箱子权限或玩家存档，不发布修复版本。上述为无真实联网玩家的隔离库存测试，不证明联机会话与客户端权限同步正常。

原生 `TEFeatureLockable.GetActivationText` 的 tooltipLocked 也可能显示给有权限的所有者，不能仅凭“已锁定”判定拒绝原因。`TEFeatureStorage.ShowUI(false)` 使用 ttNoInteractItem；本地权限检查拒绝则播放 Misc/locked。

平台的同队控制权限只适用于货运平台。机器配置 `MachineConfiguration.CanAccess` 仍要求设备所有者或授权名单；同队不会自动获得分拣箱配置权限。后续优先区分所有者与队友访问结果，再检查真实联机中的锁请求、会话关闭和权限同步。
