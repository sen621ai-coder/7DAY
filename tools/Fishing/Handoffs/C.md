# C 会话交接 — 2026-09-29

## 最新状态：统一接口适配已完成

契约 v1 发布后新增 `ZZZ-PZAEC_Fishing/Source/Controls/FishingControlsAdapter.cs`，实现 **`PZAEC.Fishing.Contracts.IFishingControls`**。

可实例化类型：**`PZAEC.Fishing.Controls.FishingControlsAdapter`**；无参构造，A 在模块工厂/SessionDriver 构造处注入 `new FishingControlsAdapter()` 即可。不改共享契约、不要求 B/D/E/F 依赖 C 的内部 DTO。

关键适配：Step 按 A 固定 tick 消费已经均分的位移，同一源输入序号允许多子步；不再使用 C 的 render-to-tick buffer；共享 DragAdjustDelta 直接加到 Drag01；Movement 只输出额外后退量并通过 A 的方向分量阻力桥施加负载，避免 WASD/阻力重复；共享配置映射和数值验证、角速度上限、取消/Release/Reset 都已接通。

新增测试 `tools/Fishing/Controls/ContractAdapterTests.cs`；测试工程引用真实 Contracts、InputAccumulator/SessionDriver 及 MovementBridge。已更新 Controls.Tests.csproj、Build-Compatibility.ps1 和 README。源文件只写 C 所属范围。

实际执行：

- `pwsh -NoProfile -File tools/Fishing/Controls/Test-Controls.ps1`：原 20 项 + 新 12 项 = **32/32 PASS**。
- `pwsh -NoProfile -File tools/Fishing/Controls/Build-Compatibility.ps1`：Controls + 真实 Contracts 引用当前游戏基础程序集编译，**PASS**，C# 7.3、warnings-as-errors。

剩余由 A 整合验收：在工厂中选择上述实现、原生键位/菜单/镜头租约、实际角色碰撞/减速叠加和实机手感；D 提供镜头反馈设置。尚未构建/安装最终模组 DLL，也未在运行的游戏中验证。**“缺统一接口适配”这一阻塞已解除；游戏内完成状态仍不能由这 32 项独立测试推出。**

下文为此前交接记录，关于契约不存在和待新增适配器的描述现已过时，以本节为准。

状态：独立操作核心已实现并验证；共享契约接入与游戏内验收等待 A。不能将此状态写为 C 的全部实机验收完成。

## 前置条件现状

启动及测试结束时只发现总计划，未发现 `tools/Fishing/Contracts-v1.md`、共享 Contracts 或 Runtime 骨架。为推进用户“完成 C”的请求，先在 C 独占范围实现纯操作逻辑；没有修改总计划、工程入口、共享契约、其他模组或安装最终 DLL。

## 修改文件

- `ZZZ-PZAEC_Fishing/Source/Controls/FishingControlTypes.cs`：内部设置、输入帧、张力快照与输出意图。
- `ZZZ-PZAEC_Fishing/Source/Controls/FishingControlMapper.cs`：鼠标映射、受载操作、自动后退及方向移动、生命周期门控。
- `ZZZ-PZAEC_Fishing/Source/Controls/ControlTickBuffer.cs`：渲染帧到模拟 tick 的绝对目标/边沿桥接。
- `tools/Fishing/Controls/ControlTests.cs`、`Controls.Tests.csproj`、`Test-Controls.ps1`：20 项行为测试。
- `tools/Fishing/Controls/Build-Compatibility.ps1`：引用当前游戏基础程序集的隔离兼容编译。
- `tools/Fishing/Controls/README.md`：接入步骤、坐标单位、输入释放规则及待验项。
- `tools/Fishing/Controls/.gitignore`：隔离测试产物。
- 本交接文件。

## 模块接口与责任

`FishingControlMapper.Sample(ControlFrame, ControlLoad) → ControlIntent`，每个渲染帧调用一次；绝对目标可跨物理 tick 保持，鼠标位移不可重复使用。

`ControlTickBuffer.Publish/Consume/Reset` 保留尚未消费的提竿与取消边沿。停止控竿会清除已排队的提竿事件；取消优先。

本模块只读取张力/拉力方向，不实现鱼战物理、不生成物品、不直接移动角色或相机。所有目标角均为弧度、张力为 N；本地 Forward 为玩家前方、Right 为右方。正 MousePull 为抬竿。

人物移动输出已包含负载系数，A 必须在原生角色移动系统中只应用一次并保留现有速度/碰撞限制。输入所有权释放应立即执行，不能等下一模拟 tick。

## 验证结果

- `pwsh -NoProfile -File tools/Fishing/Controls/Test-Controls.ps1`：20/20 PASS。
- `pwsh -NoProfile -File tools/Fishing/Controls/Build-Compatibility.ps1`：PASS，当前游戏基础程序集、C# 7.3、warnings-as-errors。
- 首次测试工程相对源码路径多写一级导致编译失败，已修正并完整重跑成功。
- 未执行：游戏加载、Harmony/原生输入接管、实际碰撞移动、游戏内手感、联机。没有安装测试 DLL。

## 给 A 的接入请求

1. 发布真实契约后映射 C 内部 DTO，或通知 C 增加转换器，不把这些内部类型直接复制成另一个共享契约。
2. 提供逐帧鼠标位移、按键保持/边沿、聚焦/菜单/聊天状态，确认鼠标轴实际单位与符号。
3. 提供 B 的张力、绷紧状态及玩家到鱼的本地水平拉力方向。
4. 通过游戏原生移动入口消费移动意图，并在取消、菜单及死亡等路径释放覆盖、Reset mapper/buffer。
5. 与 D 协调自由观察和镜头反馈设置，检查物理键位冲突后完成绑定。
6. 按 README 的待验清单执行实机验收；需要调参时集中交给 E，默认参数仅为待调初值。

本会话没有把 A 缺失的适配工作伪装成已完成。只要契约与入口可用，剩余工作是类型映射、输入绑定和实际移动验收。
