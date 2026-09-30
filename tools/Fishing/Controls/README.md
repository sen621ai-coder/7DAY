# C：钓鱼操作核心

## 当前接入方式：共享契约 v1（2026-09-29 更新）

统一入口为 **`PZAEC.Fishing.Controls.FishingControlsAdapter`**，无参构造，实现 `IFishingControls`。A 可直接以 `new FishingControlsAdapter()` 注入 `SessionDriver`，生命周期为 `Reset(ControlConfig,RodConfig) → Step → Release`。未修改共享契约或 A 的运行时文件。

该入口由 A 的固定模拟 tick 调用，**不要额外套用 ControlTickBuffer，也不要按 RawInputFrame.Sequence 对子步去重**。同一源帧序号可在多个子步出现；A 已经均分鼠标/泄力增量，适配器仅消费本子步分量，保留源序号给 B/F。

- 内部继续复用 FishingControlMapper 的竿目标、卷线/扬竿和鼠标释放逻辑。构造配置复制数值，外部修改不会影响当前会话。
- 共享 `DragAdjustDelta` 为直接的泄力 01 增量；不使用旧核心 DragAxis 的每秒速率语义。
- `RodConfig.AngularSpeedRadiansPerSecond` 限制操作目标变化，溢出鼠标量丢弃，不形成后续动作积压。
- `AutoBackThreshold01` 在线性 pitch 范围内插值；`AutoBackGain` 解释为每秒鼠标轴速度到后退意图的增益；`MaxAutoBack01` 限幅；`AutoBackDecaySeconds` 是满幅衰减时间。范围内的默认值仍待实机调参。
- `MovementRequest` 只输出原生后退输入尚未提供的额外量；ExtraRight 为零。WASD 由游戏原生层保留。AgainstPullScale=`max(MinAgainstPullScale,1-tension/PlayerResistanceNewtons)`，夹在合法范围，线张力为零时不减速。
- PullDirection 是玩家至鱼的世界水平单位方向，由 A 的 MovementBridge 只衰减逆向分量。不要使用旧核心最终 MoveRight/MoveForward 替代这份统一请求，也不要再次乘负载系数。
- FreeLook/Recenter 清空自动后退且不消费鼠标、泄力和抛投；恢复首步丢弃鼠标跳变。InputAllowed=false、CanFish=false、取消和无效数值会返回 Cancel 并进入 Released，直到 Reset；终止快照仅释放、不再次请求取消。
- 原生物理键采样/镜头租约仍归 A。适配器只消费已映射的布尔动作。FeedbackIntensity01 验证范围但不操作相机，实际表现归 D。

验证：20 项操作核心测试 + 12 项统一接口测试，共 **32/32 PASS**。新增测试直接编译使用 A 的 `InputAccumulator` 和 `MovementBridge` 源码，覆盖相同序号多子步、零 tick 累积、边沿、直接泄力增量、逆向分量衰减、自动后退与 WASD 合并、释放/重置、配置和输入防御。引用游戏基础程序集的兼容编译通过。未安装最终 DLL，未声称完成原生按键接管或游戏内验收。

以下为早期独立核心说明，**仅供维护内部实现，不能替代上述共享契约接入方式**。其中关于缺少契约的描述是历史状态，现已由适配器解决。

实现位于 `ZZZ-PZAEC_Fishing/Source/Controls/`。这是无 Unity 依赖的操作模块，不是可独立加载的模组；尚未接入游戏按键、镜头或人物移动。

开发时 `Contracts-v1.md`、A 的运行时骨架均不存在，因此本目录类型为模块内部 DTO，不宣称替代共享契约。A 发布契约后，在本目录增加转换器或由 Runtime 映射即可；不要让其他模块依赖本目录内部实现。

## 已实现

- 鼠标位移映射为弧度制的绝对抬竿/侧压目标；送竿不施加额外负载迟滞。
- 消费 B 提供的真实线张力，不另算鱼的受力；负载降低抬竿和侧压响应。
- 接近抬竿上限后，继续后拉产生有界的自动后退意图。停止后拉后默认在 0.12 秒内退至零；送竿、主动前进、自由观察和菜单立即清除自动后退。
- 对与鱼拉力相反的移动施加负载系数；朝鱼移动或横向移动不被该阻力减速。松线无阻力。
- WASD 意图与自动后退取最大幅度，不相加超速；主动前进优先；对角移动归一化。
- 卷线保持、提竿边沿、泄力调节、取消、自由观察、鼠标重定位、自动后退开关、灵敏度与参数校验。
- 菜单/聊天、失焦、不可控制状态与超长帧释放所有权并清除自动后退。恢复时丢弃首帧位移，保持动作需松开后才能重新触发，防止 UI 点击变成卷线或扬竿。
- `ControlTickBuffer` 保存最新绝对目标及保持状态，将提竿/取消边沿仅消费一次。

## A 的接入顺序

1. 每个本地钓鱼会话创建独立 `FishingControlMapper` 与 `ControlTickBuffer`；切换会话、死亡或退出世界时同时 Reset。
2. 每个渲染输入帧只调用一次 `Sample(frame, load)`，Sequence 严格递增。该函数不能直接在每个物理 tick 重复调用，否则会重复消费鼠标位移。
3. MousePull/MouseSide 是该帧累计的原始位移，不乘 deltaTime、不重复套游戏视角灵敏度；如果 API 给出的量已经按灵敏度缩放，应设置本模块 Sensitivity 为 1。正 Pull 为手向后拉/抬竿，正 Side 为向右；实际游戏输入轴符号由探针确认后转换。
4. MoveRight/MoveForward 是玩家本地水平输入，正 Forward 为前进。Load.PullRight/PullForward 为角色到鱼的水平向量，不能传相机俯仰后的三维向量长度。张力单位 N。
5. 用明确的钓鱼状态决定 SessionActive 和 CanControl。CanControl=false 表示会话应取消的情况，如受击、死亡、换物品、进入载具、游泳。菜单/聊天门控负责立即释放输入；若按 M1 规则要求开菜单直接取消，Runtime 同时结束会话。
6. 输入门控应在当帧应用，不等待模拟 tick。OwnsRodInput=false 时释放本模块拥有的原生输入覆盖，保留正常游戏移动/视角；绝不可将零移动意图强行写入角色导致不能走路。
7. OwnsRodInput=true 时阻止钓鱼动作同时触发武器攻击；根据 SuppressLook 控制鼠标是否仍驱动原生镜头。FreeLook 允许看周围，Recenter 仅松开竿控制并保持视角。
8. 调用 buffer.Publish(intent)。每个固定模拟 tick 调用 Consume，将绝对竿角、卷线状态及 DragFraction 映射到共享契约。先处理取消，再处理提竿等边沿。首次有效提竿须由 B 判定是否挂钩。
9. 按游戏移动更新频率将意图交给角色原生移动入口，保留碰撞、坡度和现有受伤/负重减速。MoveRight/MoveForward 已施加一次钓鱼负载，MovementLoadMultiplier 仅供诊断，禁止再乘一次。也不要既在 Update 又在 FixedUpdate 执行移动。
10. 人物真实位移应反馈给 B 下一 tick 的环境。不要直接修改 Transform，也不要用本模块的移动意图冒充实际位移。
11. 模拟 catch-up tick 可复用绝对目标和保持状态，边沿只消费一次；游戏暂停不应在后台继续推进钓鱼。长帧超过默认 0.25 秒时本模块释放输入，Runtime 应限制追赶步数。

输入建议为：鼠标控竿，WASD 走位，单独的保持动作负责卷线、自由观察和鼠标重定位；泄力使用 +/- 动作。尚未选定实际物理键，必须由 A 的输入探针检查 ProjectZ/AEC 和原生键位后绑定并允许配置。本模块不硬编码未经验证的 Unity/GameInput API。

镜头反馈强度及实际镜头表现由 D/Runtime 提供，C 不写相机。B 的竿变形才是物理反馈，本模块输出的是玩家目标角，不应拿目标角覆盖 B 的实际竿形。

## 验证

在 Mods 下执行：

```powershell
pwsh -NoProfile -File tools/Fishing/Controls/Test-Controls.ps1
pwsh -NoProfile -File tools/Fishing/Controls/Build-Compatibility.ps1
```

2026-09-29：20 项测试全部通过；在 30/60/144/240 Hz 输入样本验证位移映射，在 30/60/144 Hz 验证后退释放。兼容编译使用当前游戏 mscorlib/System/System.Core，C# 7.3，警告视为错误。

测试产物只写本工具目录 artifacts；不生成或覆盖模组根目录游戏 DLL。SDK 测试编译结果不代表游戏程序集加载、Harmony 接管或人物碰撞已验证。

待实机：现有键位冲突、鼠标轴单位/符号、不同 DPI 的舒适性、镜头与攻击隔离、撞墙/台阶/岸边实际移动、受伤负重叠加、暂停/菜单退出、联机角色所有权及实际拉扯手感。
