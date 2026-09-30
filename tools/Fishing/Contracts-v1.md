# 钓鱼共享契约 v1

发布：2026-09-29，A 会话。**v1 已发布，B–F 可以按本文件接入。** 技术与实机验证状态另见 STATUS.md；发布契约不等于完成玩法或实机手感验收。

## 唯一公共边界

以 `ZZZ-PZAEC_Fishing/Source/Contracts/*.cs` 为准，命名空间 `PZAEC.Fishing.Contracts`，版本 `FishingContract.Version = 1`。

| 模块 | 实现接口 | 创建及生命周期 |
|---|---|---|
| B | `IFishingSimulation` | 每场一个实例；Begin → Step/Cancel；Current 返回值快照 |
| C | `IFishingControls` | 每场 Reset(config,rod) → Step → Release |
| D | `IFishingPresentation` | 每场 Begin → Render/OnEvent → Clear；退出时 Dispose |
| E | `IFishingContent` | Load(modDirectory)、Validate、TryGetReward；无库存写入 |
| F | `IFishingNetwork` | Start → SendInput/Publish/Tick → Stop；传输调用 A 的 IAuthoritySessionRouter |
| A | `IWaterQuery`、`ICatchSettlement`、`IAuthoritySessionRouter` | 环境、输入租约、移动桥、生命周期及唯一权威结算入口 |

B/C 已经开始的模块内 DTO 可以保留。请各自在独占目录新增实现上述接口的适配器，**不要让其他模块依赖你的内部 DTO，也不要修改共享 Contracts**。在交接文件写明可实例化类名和构造器；跨模块工厂由 A 最终显式绑定，M0 不自动扫描或启动模块。

## 单位与坐标

- 距离 m、时间 s、质量 kg、力 N、角度 rad；控制量 01 是闭区间 [0,1]。
- `Vec3` 是游戏绝对世界坐标：Y 向上，水平轴跟随游戏 X/Z。不要将其当成 Unity Transform.position。
- 渲染坐标 = 绝对坐标 − `RenderFrame.RenderOrigin`（游戏 `Origin.position`）。每帧更新原点，不缓存上一次偏移。
- 玩家前方向投影至 XZ 后归一化；右方向 `(forward.Z,0,-forward.X)`。不要在模块间自行转换坐标手性。
- 抬竿 pitch 正值向上，侧压 yaw 正值向玩家右侧；RodPose.Forward/Right 表示竿根局部参考基，而不是被鱼拖动的镜头。
- `MouseRightDelta` 为鼠标向右，`MouseBackDelta` 为鼠标向后/屏幕下方向。A 在 native legacy 输入层使用 `-Input.GetAxisRaw("Mouse Y")`；是 Unity 轴单位，不宣称是原始硬件像素。游戏视角反转选项不再二次反转钓竿。

## 输入、时间与调用顺序

1. A 在一次 `PlayerMoveController.Update` 前采样鼠标、游戏移动输入、按钮及环境，并检查菜单/焦点/受击/持竿等资格。
2. `RawInputFrame.Sequence` 单调递增；SampleTimeSeconds 为单调时钟，DurationSeconds 是采样间隔。鼠标与 DragAdjustDelta 是位移，**不得再乘 dt，也不得每个子步重复整个增量**。DragAdjustDelta 为泄力 01 的有符号增量。
3. SessionDriver 的 InputAccumulator 跨无物理 tick 的帧累积；有多个 tick 时均分位移，Cast/Strike/Cancel 边沿只交给第一个 tick。Reel/FreeLook/Recenter 是持续态。
4. 每个固定 1/60 秒 tick：C.Step → B.Step → 保存 MovementRequest 和快照。B 的 tick 从 0 开始，每 Step 加 1；时间仅按 dt 前进。所有查询主线程执行。
5. A 在原生 `EntityPlayerLocal.MoveByInput` 前只应用最后一次 MovementRequest，**每个原生更新一次，不对每个模拟 tick 移动人物**。实际位移通过下一次 EnvironmentFrame 回流；本帧多个模拟子步在上次已模拟环境和当前真实环境之间插值人物位置、竿根和参考方向，避免把一帧位移压进一个子步产生虚假冲击。水域/资格来自最新采样，不能假设人物已按意图成功后退。
6. D 在渲染阶段插值 Previous/Current；OnEvent 单独发送，不因渲染插值重复事件。
7. 单帧最多补算 Session.MaxCatchUpTicks（默认 8，上限16）。超长卡顿丢弃时间债；网络端依靠权威 tick 重同步，不把卡顿当作一次巨大鱼冲刺。

`ControlIntent.Movement` 只包含**额外**后退/侧移，原生 WASD 不重复加入。ExtraForward 正值向前、负值后退。PullDirection 是玩家指向鱼的水平单位方向；AgainstPullScale 只缩减与其相反的移动分量，向鱼前进及横向分量保留。C 根据已有真实张力决定衰减，禁止重算另一套拉力。

开菜单、聊天、失焦或输入资格丢失时取消钓鱼并释放；FreeLook 只释放镜头鼠标而不自动取消，Recenter 临时停止鼠标控竿以挪回鼠标。二者期间都不可累积回到控竿后突然生效的鼠标量。键名在 ControlConfig 中，候选默认值仍须 C 检查冲突。

## 模拟与事件

状态枚举和字段以 Messages.cs 为准。Begin 初始化 Casting/tick0；所有持续时长由 B 管理。CastTarget 必须已经由 A 验证。水不合法、距离超限、障碍和超时有明确终止原因，不能靠鱼穿岸维持流程。

RodPose：A 在 EnvironmentFrame.Rod 提供实际竿根 Root 和水平参考基 Forward/Right，Begin 时提供初始 PitchRadians/YawRadians（当前 C 初始 pitch 为 0.35 rad，A 应与其对齐）。每 tick 由 C 输出目标角，B 负责角速度限制、目标竿尖及受力竿尖的计算，并写入 FishingSnapshot.Rod。A 不在 Step 前旋转或重算 Tip，B 不依赖 EnvironmentFrame.Rod.Tip；D 使用快照中的物理竿尖，不回写。SessionDriver.BuildStraightRod 仅保留为独立几何辅助函数，不参与调度路径。此次职责澄清未改变 v1 DTO 或接口签名。

`IWaterQuery.SampleColumn` 返回 Status、SurfacePoint、BottomY、DepthMeters、BottomKnown 和 Accuracy。Unloaded 不等于 Dry；BottomKnown=false 表示采样深度截断，不能当作真实水底。M0 原生适配返回 **VoxelEstimate**：水格与 GetMassPercent 估计逻辑水面，尚未声称与 GPU 水波/曲面完全吻合。D 可以做有界视觉修正，不能把视觉修正回写张力；A 负责后续更精确 native ray 适配。

TraceSolid 返回 Obstructed/Unloaded；M0 使用保守体素查询，复杂块形可能被过度阻挡，M1 需检验鱼线与地形的精确规则。

所有 FishingEvent 的 Sequence 在同一 SessionId 内从 1 开始递增；A 丢弃旧序号和错误 SessionId。会话之间允许重新从 1 开始。事件 Kind/Reason 必须使用共享枚举，内部更细的失败原因可留在模块诊断里。Landed 只可由权威成功状态产生一次，声音和场景事件绝不直接发物品。

## 配置与物品

`FishingConfig` 中的子配置为共享输入类型。构造默认值仅用于探针，未经过真实手感调参；E 负责读取 `Config/Fishing/settings.xml` 并 Validate。文件名不是游戏自动读取的 XML patch，必须由 E 的 Load 主动读取。

固定首版 ID：

- 鱼竿：`pzaecFishingRodBasic`
- 鱼饵：`pzaecFishingBaitWorm`
- 鱼获：`pzaecFishingFishCarp`
- 料理：`pzaecFishingMealGrilled`
- 鱼定义：`carp`

E 必须验证所有数值有限：正质量/长度/刚度、非负阻尼/时间、min≤max、各 01 范围、线强度高于有效泄力范围等。B/C 仍应对收到的配置进行必要防御检查，不能依赖外部 XML 总是正确。新增真实需要的参数由 A 统一增加，不以复制共享 DTO 绕过契约。

## 资源消耗、退出与结算协议

- Idle/预览不扣饵。单人 A 先验证目标/装备并执行模块 Begin；仅当 B 接受且会话仍有效时，在同一主线程回调中扣 1 个饵，再进入持续输入。Begin 被拒绝或缺饵立即释放，不推进模拟、不结算。已接受抛投后取消、断线、离线均不退饵。
- v1 不保存进行中的鱼战；加载或重连丢弃旧会话，输入和画面恢复默认。
- A 从权威 Resolved 状态生成 CatchResult，SessionId 与 SettlementId 在首版一一对应，TerminalTick 为权威 tick。
- 奖励由 E 的 TryGetReward 产生规格，经 A 的唯一 ICatchSettlement 发放。背包满返回 InventoryFull，保留一次待领结果，禁止既丢地面又入包。
- 单人 A 已实现 CatchSettlement，只接受当前已开启的权威会话 Resolved 快照。待领/完成记录写入原生玩家 Buff CVar，GUID/tick 拆为精确的 16-bit 片段，与库存进入同一 PlayerDataFile。一次只保留一条待领鱼获；异常写入停止重试。**完整玩家保存重载及崩溃一致性仍未验收，不宣称任意故障下 exactly-once**。F 不自行建立第二个奖励入口。
- 受击/死亡/换物品/菜单/车辆/游泳/远离/分块丢失/世界退出均有 FailureReason；A 恢复租约与移动请求，D Clear 所有会话对象，C Release 所有输入积累。

## 多人边界

NetworkInput 只携带版本、会话、序号、客户端 tick 及 RawInputFrame；不携带鱼重、成功标志或奖励规格。authenticatedPlayerId 来自原生连接映射，不能从包内自行指定。PlayerEntityId 不能独立证明身份。

F 实现 IFishingNetwork，在初始化时获得 A 的 IAuthoritySessionRouter（可以构造器注入）。A 的路由器负责验证并创建每玩家最多一个权威 SessionDriver。F 负责消息封送、大小/速率/序号限制、版本匹配、快照缓存与断线通知。M0 路由器仅定义接口，A 在 M1/M3 接线。

客户端预测不得结算。快照以 Server tick 和 LastAcceptedInputSequence 做重同步；同一会话输入重复/过期不再次执行；断线后旧会话 ID 永远不能认领新会话奖励。预测修正策略需 F 与 B 交接说明。

## 构建与交接

`pwsh -NoProfile -File tools/Fishing/Integration/Build.ps1` 对真实安装程序集编译到 `tools/Fishing/Integration/artifacts/build/`。工程及最终构建入口由 A 管理。B–F 使用各自的测试产物路径，不向模组根目录写 DLL。

交接路径 `tools/Fishing/Handoffs/B.md` 至 `F.md`。说明契约适配器类型、构造方式、内部 DTO 映射、测试命令及未验证项。共享契约变更请求写进自己的交接，等 A 统一处理；不要自行修改本文件。
