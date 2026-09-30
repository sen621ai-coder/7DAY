# A / M0 技术验证报告

日期：2026-09-29。本报告区分编译、纯逻辑、真实引擎组件探针与完整交互实测。

## 交付结论

共享契约 v1、独立工程、主循环和 A 的原生适配骨架已交付，B–F 可以继续实现接口适配器。尚未安装最终玩法 DLL。

**M0 的自动验证已推进；完整玩家鼠标/移动交互门仍待实机验收，不能把 A 的工程交付写成“全部钓鱼功能完成”。** 这些检查与 M1 的完整玩法装配不同。

## 当前游戏与工具

- 本机目标：`D:/SteamLibrary/steamapps/common/7 Days To Die`。
- 最新实际客户端日志：`C:/Users/Administrator/AppData/Roaming/7DaysToDie/logs/output_log_client__2026-09-29__23-09-19.txt`。
- 游戏日志版本：V 3.2.0 (b10)，Compatibility V 3.2.0。
- Unity：2022.3.62f2 (7670c08855a9)。没有使用旧的 LocalLow Player.log；该文件来自另一份旧游戏。
- Assembly-CSharp 程序集版本为 0.0.0.0，不能用它判断发行版本。
- MVID：`229796d0-95ca-4662-b426-1a6f1f1596ed`。
- Assembly-CSharp SHA256：`CB33A4ADBD9BD25C98255B8DE675B3050EA39C782A515EC97CF943EAC4A480FE`。
- 本机 .NET SDK 8.0.302；net48 工程成功构建。
- PowerShell 自带 Roslyn 备用构建成功，引用本机游戏程序集。
- Unity 2022.3.48f1c1 与 6000.0.23f1c1 在 D:/unity；既有脚本指向 E:/soft/Unity/2022.3.62f2 的路径不存在。不能据此宣称完全没有其他编辑器，但当前已发现路径中没有与游戏完全匹配的编辑器。
- Unity 2022.3.48f1c1 的 Mono 可运行独立契约测试。D 的资产管线需要单独验证生成资产在 2022.3.62f2 的兼容性。

## 原生接口证据

`Inspect-Native.ps1` 使用 Mono.Cecil 读取真实程序集及 IL，输出 `artifacts/native-audit.txt`，16 条结构性检查通过。

| 事项 | 已核验事实 | 限制 |
|---|---|---|
| 水域 | World.GetWater、WaterValue.HasMass/GetMassPercent、Chunk.InProgressUnloading 可用 | WaterUtils.GetWaterLevel 只返回质量阈值后的 0/1，不是世界水面高度，禁止误用 |
| 鼠标 | PlayerMoveController.Update 调用 Unity Input.GetAxis 的 Mouse X/Y | 位移单位是游戏输入轴，不是原始硬件像素；真实物理鼠标手感未测 |
| 视角 | 原生 Update 写 MovementInput.rotation/cameraRotation，再调用 MoveByInput | 租约在 MoveByInput 前恢复本帧由钓竿占用的视角；与其他输入补丁的动态顺序需联调 |
| 移动 | MoveByInput 读取 moveStrafe/moveForward，保留 EffectManager 和角色状态判断 | 真实坡面、碰撞、负重/受伤与运动速度尚未跑完整玩家场景 |
| 原生移动链 | EntityPlayerLocal.Move 转调 Entity.Move | Entity.Move 主要处理运动量；这项 IL 检查本身不证明碰撞实测通过 |
| 攻击隔离 | Update 经 Inventory.Execute 触发主/次动作 | 仅对租约拥有者的 action 0/1 进行拦截，实际持竿/换物品策略等待 M1 接线 |
| 场景坐标 | 游戏使用 Origin.position；组件坐标与绝对坐标需转换 | 模型/手部动画复用尚未验证，D 负责资源阶段 |
| 库存 | Bag.CanTakeItem/AddItem 和 ItemValue.SetMetadata/TryGetMetadata 可用 | 持久事务、满包待领、崩溃去重不在 M0 实现中 |
| 网络 | NetPackage.ProcessPackage 与 NetPackageManager 接口存在 | 不是已完成联机；F 负责封包、身份、速率和重放校验 |

原始详细 API/IL 调查保留在 `artifacts/api-*.txt`，不作为需要提交的大型源码文件。

## 骨架行为验证

命令：`pwsh -NoProfile -File tools/Fishing/Integration/Test-Contracts.ps1`。

最终结果：**25/25 PASS**，输出 `artifacts/contracts/result.txt`。测试运行真实 Contracts/SessionDriver/MovementBridge 代码，模块使用测试替身，不冒充真实鱼战实现。

覆盖：随机种子回放、鼠标跨帧与跨 tick 守恒、边沿不重放、重复/NaN 输入拒绝、逆拉力减速、向鱼和横向移动保留、鼠标额外后退、无会话不干涉原生轴、抬竿目标坐标、固定步长、长卡顿限制、人物/竿根在子步间插值、事件去重、专服跳过 Render、取消/菜单/死亡/异常释放与 Dispose。

编译：PowerShell 暂存构建通过；SDK `dotnet build ... -c Release` 通过，0 警告、0 错误。整体工程会纳入其他会话已写入的源码，这仅证明当时可一起编译，不替代其他会话自己的交付验收。

## 真实引擎探针

使用 `Start-NativeProbe.ps1` 创建独立的 `FishingM0_Isolated` 存档；测试程序集仅位于该次运行的 UserData/Mods。原有游戏进程和正式存档没有被停止或改写。

注意：游戏也扫描安装目录 Mods，因此加载的是现有整合环境；隔离的是存档及测试产物，不能称为只有钓鱼的纯净模组环境。

第一轮成功日志：`artifacts/native-dc1bd7e128f541a386bdd4b1c727f84f/game.log`，15/15 PASS。

检查内容：真实 World/Chunk 的水量查询、临时三层水柱高度/深度、未加载区块、截断水柱、鱼线穿水不误判为实体；真实 EntityPlayerLocal/MovementInput 组件上的租约视角恢复、受阻后退轴、单次应用及释放；Unity LineRenderer 创建与坐标转换；实际 ItemValue 元数据内存往返和独立 Bag 插入。

玩家组件刻意保持 inactive，未运行完整玩家 Awake/相机/UI/碰撞流程。这是原生组件探针，**不是伪装成真人操作测试**。创建鱼线组件也不等同于完成渲染像素或水面观感验证。

最终一轮日志：`artifacts/native-cfa05cd309c24a6e91d621b03296a729/game.log`，**17/17 PASS**，在首轮基础上补验了真实 Harmony 对 MoveByInput 的安装和新版租约释放时恢复移动轴。已关闭自身测试进程；原先的客户端进程 PID 15336 保持运行。最终运行并未验证 interactive-player / visual-pixels / save-roundtrip，结果日志也明确标注。

## 调试中遇到的问题

- 首次原生探针的启动条件未激活，已增加仅对隔离 GameName 生效的备用门控。
- `-nographics` 下，既有林业模组因 `No supported native workstation material for forestry` 使 blocks.xml 加载失败。改为隐藏的带图形能力 batch 进程后，完整加载并通过探针；没有修改林业模组源码或资源。
- SDK 项目缺 Audio/AssetBundle 和 LogLibrary 引用，已补齐统一 Unity 模块、游戏 firstpass 与日志引用，重建通过。
- B 提出多 tick 锚点突变风险，A 已补充环境插值与目标竿尖转换，并加入独立回归检查。

## 后续必须完成的门

1. M0 交互：正式角色实际持竿，物理鼠标接管/自由观察/释放、WASD+自动后退，验证原生碰撞与现有减速、菜单/受击/换物品恢复。
2. M1：B/C/D/E 工厂装配、装备识别、抛投扣饵、状态机终止与上岸奖励服务。
3. M2：水面精细高度、竿/线/漂画面、手部动画、低光表现与真人手感。
4. M3：F 权威路由接线、客户端/主机/专服、断线重连、持久奖励去重及性能。

A 未用占位场景或编译结果替代这些验收。B–F 的接口开发可继续，不需要等待真人手感测试。
