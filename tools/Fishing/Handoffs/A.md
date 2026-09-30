# A 会话交接

2026-09-30 更新：A 已完成单人运行时接线并安装 0.1.0 测试版；两种构建通过，SDK 0 警告/0 错误。A 骨架 25、C 操作 32、真实模块联合 18、结算/选点 21、原生组件探针 27 项通过。最新报告 `../Integration/A-runtime-report.md`，状态 `../STATUS.md`。完整玩家鼠标/移动/存档重载仍未实机验收。

## B–F 现在可以继续

- 读取 `tools/Fishing/Contracts-v1.md` 和 `Source/Contracts/*.cs`。
- B：`ContractFishingSimulation` 已交付并通过联合验证。竿姿态职责已统一：A 插值实际竿根/参考基，B 根据 C 目标角计算未弯曲和受力竿尖，A 不再于 Step 前计算 Tip。
- C：`FishingControlsAdapter` 已交付，32 项操作测试及 A/B/C/E 联合验证通过。原生 WASD 已由游戏读取，输出只含额外移动，由 Native MovementBridge 应用一次受力衰减。游戏内初始竿 pitch 应与 C 的 0.35 rad 对齐。
- D：Unity 模块引用已加入工程。实现 `IFishingPresentation`；复杂相机扰动可以保持为可选回调，M0 没有强行接管相机。当前缺少匹配 2022.3.62f2 的编辑器路径，2022.3.48f1c1 资产必须实测后再宣称兼容。
- E：物品 ID 与你的首轮交接一致，配置路径已固定，读取和验证实现 `IFishingContent`。物理默认参数尚需调参。
- F：本轮工作区已出现 Networking/QA，88 项逻辑测试通过。A 为 net48 修复 NativeFishingPacket.Write 的重载解析（BinaryWriter + byte[]/offset/count）。本轮只启用 LocalFishingRuntime；网络路由与多人原生库存权威接线后续整合，勿绕过 A 的结算入口。

## 本会话修改范围

只修改本项目的 Contracts、Runtime、工程、ModInfo、根 README、Integration 工具、总计划关联文档及本交接。其他模块由对应会话维护；整体编译可能包含同时新增的模块，但不表示 A 已验收这些模块。

## 构建和生命周期约定

- `Build.ps1` 默认暂存，支持 `-CoreOnly` 的独立契约测试及 `-ProbeOnly` 的 A 原生探针。
- `ModApi` 已启用本地主机单人入口，远程客户端/dedicated 不启动。正式 DLL 已安装，探针 DLL 仅在隔离目录。
- SessionDriver 只负责模块调度，不发奖励或移动原生角色。
- NativeInputHooks 由单人运行时创建，仅验证后的钓鱼会话持有租约；已接持竿/槽位、伤害回调、血量、世界/玩家切换、焦点/菜单/暂停、死亡、游泳和车辆取消。
- NativeControlLease 在 MoveByInput 的 finally 恢复临时输入轴，保留原生移动流程已读到的本帧请求。异常取消清空额外后退，且不锁死全局输入。
- NativeWaterQuery 的 SurfaceAccuracy=VoxelEstimate；体素障碍查询保守，对复杂形状需要后续细化。

## 尚未宣称完成

单人原生入口、输入绑定、持竿/受击/切图检查、有效抛投扣饵、上岸结算及满包待领已实现。待领/完成状态存在原生玩家 CVar；已验证 Buff 二进制序列化，完整玩家保存/重载仍待实测。原生堆叠忽略鱼获元数据，故发奖采用空背包格 + SetSlots；玩家手动堆叠后元数据行为不作为历史结算依据，去重以保存记录为准。

运行中正式玩家的鼠标接管、真实走位/碰撞手感、正式持竿动作、奖励存档事务、网络整合。独立测试通过不能替代这些游戏内验收。M0 的交互实测门仍在 STATUS 单独列出，不应隐藏在“编译成功”下面。
