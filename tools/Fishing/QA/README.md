# F：网络模块与独立验证

交付日期：2026-09-30。共享契约 v1 未修改。

已交付 F 的网络实现、原生包适配、主机本地连接、输入缓冲、移动检查辅助和可重复自动测试。**尚未启用 A 的多人路由，也未完成真实多人游戏验收。** 交付复核时 A 已在另一会话安装单人测试入口；F 本次没有安装模组根目录 DLL、改动现有存档或重启游戏。

## 运行验证

在 Mods 目录执行：

```powershell
pwsh -NoProfile -File tools/Fishing/QA/Test-Network.ps1
pwsh -NoProfile -File tools/Fishing/QA/Build-Compatibility.ps1
pwsh -NoProfile -File tools/Fishing/QA/Inspect-NativeNetwork.ps1
```

测试、编译产物和日志只写入 `tools/Fishing/QA/artifacts/`。net8 测试不加载 Unity；兼容性编译使用实际安装游戏程序集和 C# 7.3，并把警告视为错误。未调用 A 的最终构建或安装入口。

详细验收状态在 `Validation.md`，A 接入清单在 `../Handoffs/F.md`。

## 代码入口

均在 `ZZZ-PZAEC_Fishing/Source/Networking/`：

| 文件 | 职责 |
|---|---|
| FishingWire.cs | FSH1 有界二进制协议、全部共享快照字段/事件封送、有限值和范围校验 |
| FishingNetwork.cs | IFishingNetwork 实现、权威会话归属、频率/序号/时钟检查、观察者过滤、快照缓存及生命周期 |
| AuthorityInputBuffer.cs | 网络输入只消费一次、短时丢包中和、网络序号与模拟帧序号转换、实测位移检查 |
| NativeFishingTransport.cs | Sender 身份对应的原生 ClientInfo 连接、ToServer/ToClient 包、可靠传输和世界隔离 |
| FishingLoopback.cs | 主机本地玩家走同一协议与校验路径，而非绕过权威入口 |

## 协议和默认限制

- 固定魔数、契约版本、消息类型和单调传输序号；最大 8192 字节，最多 64 个事件。拒绝尾随字节、不完整字段、非有限数和未知枚举。
- 客户端消息仅有 Start 和 Input。Start 只有请求序号和目标点，没有玩家身份字段。Input 没有鱼重、结算 ID、成功标志或奖励规格。
- 服务端回复 StartReply、Snapshot、End。身份来源是 A 在原生连接完成认证/附着后登记的 persistent ID 和 entity ID；不能从包内注册身份。
- 每连接最多一个活动会话；默认最多 64 个已登记连接、客户端最多 128 份会话缓存。连接键包含随机代次，重连使旧代次失效。
- 默认每连接每秒 120 条消息、桶容量 180；无效包也消耗额度。Start 独立每秒 2 次、桶容量 3。实际输入建议合并为每秒 60 条。
- 输入序号每次最多前进 4096；客户端 tick 相对权威 tick 允许最多领先 120、落后 600。客户端时钟不驱动模拟，只用于拒绝逆序采样。
- 单包鼠标轴绝对增量最大 256，移动轴 [-1,1]，泄力增量 [-1,1]，采样间隔 [0,0.25] 秒；服务器输入缓冲再次约束累计量。
- 权威输入缓冲在 250ms 没有新输入时释放卷线、位移和按钮；3 秒无有效输入取消会话。客户端 5 秒收不到权威快照清理场景并发出 SessionEnded。
- 权威快照约每 0.1 秒发送；终态立即发送。只给本人及 A 判定有观察资格的人发送。默认观察过滤关闭，禁止无条件全图广播。
- 事件缓存最多 64 条；溢出明确报错，不能静默丢弃。A 应逐模拟帧 Publish、逐游戏帧 Tick，正常节奏不接近此上限。
- 使用原生可靠包，同一方向使用同一通道。重复/乱序消息不重复应用。关键提竿边沿、终态与事件依赖可靠有序传输，不承诺在任意不可靠 UDP 丢包下补回边沿。

## 调度与接线

所有公开方法、原生接收和回调在游戏主线程执行，不支持任意后台线程调用或在回调里重新注册/删除连接。Start/Stop 按世界生命周期调用；Start 后先 Tick 当前单调时间，再登记连接/发起请求。

服务端每一游戏帧：

1. `network.Tick(monotonicNow)`：更新时钟、检查超时及发送上一批快照。
2. 接收包后 `router.AcceptInput` 只调用该玩家的 `AuthorityInputBuffer.Push`，不能每收到一包就推进物理。
3. 对每个仍有效的会话，调用 `buffer.Consume(now, dt)`，把结果交给 `SessionDriver.Advance(dt, frame, authoritativeEnvironment)`。dt 来自服务端真实帧时长；固定步长由 A/B 实现。
4. 使用 `buffer.ForPublication(driver.Current, events)` 将模拟内部帧序号转换回已消费的网络输入序号，再交给 `network.Publish`。Publish 自动覆盖 LastAcceptedInputSequence 为真实接收值。
5. 结算只由 A 从权威 Resolved 生成 CatchResult 并调用 ICatchSettlement；F 不提供领奖包、不接收客户端 CatchResult、不写库存。

收到取消输入或输入失效时，F 会调用 A.CancelPlayer；A 应立即从自己的活动会话列表移除该会话，因此后续不再 Publish 已关闭的 session。环境取消或 B 自然终态则通过终态 Publish 通知 F；A 不要在 AcceptInput 的回调中重入 Publish。

发送鱼战的授权环境仍须 A 检查：目标点、水域、鱼竿/鱼饵、受击/死亡/菜单/车辆/游泳、玩家位置、竿根和镜头方向。F 的 FishingMovementGuard 可辅助检查真实已接收位置，但不是完整反作弊或玩家网络移动系统；实际最大速度须包含游戏允许的效果。检测到瞬移应取消本次鱼战，合法传送也先取消，再 Reset。

## 客户端校正与观察者

F 采用权威状态加显示插值，不实现另一套客户端鱼战物理：TryGetSnapshots 提供 Previous/Current，A 将其交给 D 的 RenderFrame。当前游戏没有经过验证的客户端物理回滚接口，故不做未经验证的预测重放。可本地预览控竿目标，但不能把显示坐标或插值张力写回 B。

新快照立即成为逻辑权威状态。重复事件按会话内序号去重；过期 tick、倒退 ack、终态后的旧状态拒绝。插值只能影响显示，不能触发本地断线或结算。断线、超时、观察资格丢失及世界退出由 SessionEnded 提醒 A 释放控制/清理 D。

v1 的 NetworkSnapshot 不含观察者所需的完整 SessionStart（鱼定义、种子、玩家身份）或 MovementRequest。首版只有 carp：A 可以根据快照和已知静态配置建立只读 D 场景；若后续要正确显示多鱼种、第三人称持竿身份及把服务端受力移动反馈到远端玩家，应由 A 统一扩展契约，或由 A 的玩家同步适配使用现有原生映射。F 不擅自添加契约字段，也不把完整人物位置从客户端输入包传来。

对远端玩家的受力移动，不能把 B 的鱼战设为客户端权威。A 需要把已认证的服务端张力快照交给 C 的客户端移动意图，再经原生角色所有权路径应用，服务端使用真实位置和位移检查；这一部分属于 A/C/F 联调，尚未实机通过。

## 原生与主机装配

- `NativeFishingTransport(world, identityResolver)`：identityResolver 使用真实 ClientInfo 的平台/跨平台身份，必须与 A 存档身份一致。F 不猜平台优先级。
- `FishingNetwork(router, nativeTransport, interestPredicate)`：服务端 Start(Server)，再 native.Bind(network)。认证且附着后由 A 调用 native.Attach(client)，退出调用 Detach。已经在线的玩家在绑定时也需要登记。
- 远端客户端使用同一个 native 实例作为传输，Start(PredictedClient)，绑定 network。引擎 ToClient 包仅在 remote world 分派给 ReceiveFromServer。
- 带本地玩家的主机：native 外包一层 FishingServerTransport，将其传给服务端 network；native.Bind 指向服务端 network。再创建 FishingLoopback(server, multiplex)、一个本地 client network（Start(PredictedClient)），并 link.Attach(localClient, trustedLocalPlayerId, entityId)。纯单人也可用相同本地协议路径，服务器模式为 Standalone。
- 专服只创建服务端，无 D、无本地 client、无摄像机。A 的服务器 SessionDriver 可以使用不创建任何 Unity 资源的 IFishingPresentation 空实现。
- 结束世界时 Dispose loopback，然后 Dispose native（内含 network.Stop）；解除 A 自己安装的生命周期委托。F 不自动安装 ModApi/Harmony 钩子。

## 不属于本轮完成证明的内容

测试里的 Router、Water 和 NoGraphics 是测试替身；B/C/E/SessionDriver 使用实际源码。不能把主机/远端逻辑一致性测试等同于启动了真实主机或专用服务器。A 已报告单人结算与存档相关测试，但其多人权威结算接线仍需单独验证。本轮只证明客户端不能通过 F 协议指定或直接触发奖励，不证明联机下库存持久去重、背包满待领或崩溃恢复。
