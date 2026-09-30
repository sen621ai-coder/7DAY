# B：钓鱼模拟内核与契约适配

2026-09-30。真实感改进及契约测试完成；游戏内视觉、手感、角色碰撞和联网实测未执行。没有安装最终模组 DLL。

## 本轮真实感改进

- 试饵改为每次抛投生成三次不同强度、宽度和间隔的试探，中间留停顿；含饵后平滑转为持续牵引。固定种子仍可回放。
- 先计算鱼带动饵的位置，再由子线牵引浮漂；鱼受到障碍限制时，漂相也随之减弱。浮漂不再带着鱼移动。
- 冲刺力量有起力、峰值和衰减，并有短暂肌肉响应时间。每轮休息时长与逃窜方向不同，疲劳后恢复段延长。
- 鱼在水中以有限转向速度走弧线，侧压会改变受力与路径；取消固定左右交替。
- 绷线时加入实际侧向甩头力，其做功计入体力。在高负载下甩头会逐渐削弱挂钩质量，让后续松线更危险；没有随机瞬间脱钩判定。
- 泄力采用 1.03 倍启动力阈值、0.97 倍停止阈值，并在持续低负载 0.15 s 后停止，避免每 tick 反复启停与卷线互相抢占。

共享契约 v1、默认生产 XML 和其他会话源码未改。新增调节项目前只在 B 内核参数里：转向率 1.8 rad/s、甩头比例 0.13、起力时间 0.22 s、挂钩磨损率 0.015/s 和泄力启停比值。需要开放给玩家时，由 A/E 统一扩展共享配置。水流、鱼嘴钩碰撞、完整绳物理仍不在此轮实现范围。

## A 的接入入口

```csharp
IFishingSimulation simulation = new PZAEC.Fishing.Simulation.ContractFishingSimulation();
simulation.Begin(start, config, environment, random, waterQuery, eventSink);
var state = simulation.Step(FishingContract.FixedStepSeconds, controlIntent, environment, eventSink);
simulation.Cancel(FailureReason.WorldClosed, eventSink);
```

每次抛竿使用新实例。只依赖共享 Contracts 和基础 .NET 类型，不访问 Unity、库存、键鼠或网络。其他会话不要依赖 B 内部 DTO。

- Begin 输出 tick 0 的 Casting 与 Cast 事件；非法目标可能返回终止状态，A 必须检查 Current.IsTerminal。
- Step 严格为契约规定的 1/60 s。可重复使用当前输入序号，但拒绝倒序输入、非有限数和可变步长。
- Cancel 保留 A 给出的受击、死亡等原因；重复终止不产生第二次事件。
- Snapshot 按值返回，配置在开始时映射/复制，外部修改配置不会改动当前会话。
- Landed 只在 Standalone/Server 输出。PredictedClient 可模拟到 Resolved，但没有权威上鱼事件；A 仍须检查 Authority 才能结算。
- Observer 不应运行物理模拟，Begin 会拒绝，应直接展示 F 接收的权威快照。
- 水和地形查询由 A 提供，须在其允许的线程调用。

## 竿姿态、输入与角色移动

B 按共享契约由 EnvironmentFrame.Rod.Root/Forward 和 C 的 pitch/yaw 目标计算未弯曲竿尖，应用角度范围与最大角速度，再由受力求弯曲后的 Snapshot.Rod.Tip。环境提供的是竿根参考，不需要 A 先把 C 的角度重复应用一次。B 不覆盖人物坐标、WASD 或原生速度限制。

RodLoadNewtons 指向鱼，单位 N；C 可投影到水平坐标用于对抗拉力的步速修正。放松鱼线时张力为零，不存在只因按后退键就凭空增加拉力的逻辑。

提竿要同时有 Strike 边沿和有效向上的竿角速度。只按提竿键而不抬竿会判为弱提竿；共享契约没有 WeakStrike 枚举，目前映射为 HookLost/InvalidInput，建议 A 后续增加明确原因供中文提示使用。超过安全速度降低挂钩质量，实际断线仍由几何变化产生的张力决定。

v1 没有单独 Landing 输入，持续卷线在鱼疲劳、进入距离和高度范围且没有冲刺时请求上岸，保持 0.6 s 完成。低竿姿态可能使鱼停在前伸的竿尖外；近岸可抬高竿把鱼带近玩家。

A 已在共享文档中规定多 tick 对真实角色位置、竿根与参考方向插值；B 消费各 tick 的实际环境，不会假设人物按意图继续移动。联合测试验证等价鼠标输入在 30/120 FPS 调度下一致；真实游戏采样、低帧率卡顿和碰撞后的手感仍需 A/F 实机验证。不要重复每帧的鼠标位移。

## 物理与行为模型

1. 状态：Casting、Settling、Waiting、Nibbling、BiteWindow、Hooked、Fighting、Landing、Resolved，以及取消、断线、脱钩。
2. 吃口：随机点动、送漂、走漂或黑漂；试饵阶段施加脉冲牵引，含饵阶段持续牵引。浮漂用质量、浮力回复和阻尼响应，挂钩窗口与吃口同步。
3. 拉力：未伸展的线不产生压缩力。杆与线先作为串联弹簧；达到杆弯上限后只剩线的柔度。径向速度阻尼与隐式响应分母抑制高刚度爆炸。
4. 泄力：超过阈值才出线，有最大放线速度和线杯容量，不能无条件截平任意冲击。卷线受负载与输入比例影响。
5. 鱼：冲刺、恢复、侧跑、再次冲刺和近岸反扑。消耗取决于游动力与物理功，休息可恢复；不是倒计时保证体力耗尽。
6. 失败：瞬时超限、持续超载、持续松线；挂钩质量影响松线容忍。水不可用、挡线、超距离、锚点跳变和超时均明确终止。
7. 水域：检查水柱、底/面余量和鱼体扫掠。遇岸/实体停在最后有效位置并转向。鱼线碰实体保守取消，不实现复杂绕障/缠线。
8. 每场事件序号单调，消费后清除；终止、成功和取消均解除竿负载。

这是简化玩法物理，不是全杆有限元、完整柔性绳或流体模拟。吃口施力轮廓由鱼行为状态驱动；第一轮没有独立鱼嘴/钩碰撞、波浪流场、鱼体体积碰撞或完整三维线碰撞。

## 配置映射

- Rod：长度、刚度、最大弯曲量、角度限制和角速度直接计算；杆/线阻尼按串联柔度权重平方合成为径向阻尼。
- Line：刚度、断线力、泄力、出线、卷线及容量直接映射。损伤增量为 max(0,T/BreakForce - DamageStartFraction) × DamagePerSecond × dt；超过 BreakForce 立即断线。
- Float：质量、浮力、阻尼直接映射。FloatPosition 为中心，静态中心偏移为 (0.5-RestSubmerged01) × HeightMeters；D 保持一致。
- Hook：窗口、松线时间、初始质量、提竿速度上下限生效。
- Fish：质量、力量、阻力、速度目标、体力、恢复、冲刺/恢复时长、等待与试饵范围、近岸距离和上岸条件生效。
- Session：水深、抛投距离、玩家距离和超时生效。
- SessionStart 初始线长 0 表示自动按几何给线；正值须在范围内，过短会在中鱼后立即受载。建议 A 默认使用 0。

尚未暴露到共享配置的初版默认值：抛投 0.45 s、沉稳 0.65 s、吃口牵引 0.42 N、最短线长 0.7 m、子线目标深度 0.6 m、水面/底余量 0.15 m、松线阈值 0.12 N、上岸保持 0.6 s、上岸高度差上限 3 m、锚点跳变阈值 35 m/s、有限鱼速保护。卷线停转力暂取断线力。需要时由 A 增补契约、E 集中提供配置，不另建生产配置文件。

BottomKnown=false 时以确认采样水柱末端作为保守模拟边界，不声称那里是真实地面。未加载和干地均不允许鱼进入；所在水域卸载结束会话。原生水查询精度与成本仍待实测。

## 验证和产物

```powershell
pwsh -NoProfile -File tools/Fishing/Simulation/Test-Simulation.ps1
pwsh -NoProfile -File tools/Fishing/Simulation/Build-Compatibility.ps1
pwsh -NoProfile -File tools/Fishing/Simulation/Test-IntegratedModules.ps1
```

36/36 模拟与契约测试 PASS；14/14 A/B/C/E 联合检查 PASS（实际模块及生产 XML，复用 A 的只读测试源码，构建产物全部在 B 目录）；兼容编译 PASS，C# 7.3，引用当前游戏 mscorlib/System/System.Core，警告作为错误。

覆盖提竿时机、四类吃口、种子回放、松线零力、不同速度后拉、泄力、瞬时/持续断线、脱钩、三轮爆发、近岸反扑、体力、侧压、水域/地形、取消去重、输入校验、配置复制、30/60/120/240 Hz 稳定性、30/144 FPS 固定调度、上岸一次性、契约及预测端权威事件抑制。

夹具后拉 1 m 时：1 m/s 缓拉峰值约 26.81 N，8 m/s 猛拉约 50.88 N。只是测试配置的物理解释性证据，不代表实机平衡参数。

结果：artifacts/test-results.txt；联合结果：artifacts/modules/results.txt；30 s 轨迹：artifacts/seed-29-trace.csv（含冲刺包络、甩头和出线速度）；吃口轨迹：artifacts/bite-motion-trace.csv；隔离兼容 DLL：artifacts/compatibility/。产物与 obj 被局部 .gitignore 忽略。

## 尚未验证与 F 的边界

- 未执行游戏加载、鼠标手感、人物碰撞、镜头/模型、真实水面或联网测试。
- 同一运行时、种子与输入可重放；不承诺跨 CPU/运行时浮点逐位一致。
- v1 无状态还原/回滚接口；F 第一轮宜用权威快照插值。显示快照没有 RNG 和全部计时器，不能当完整存档还原。
- Snapshot 和事件消费有分配，真实性能未测；水域/障碍每 tick 多次查询，A/F 应测并发钓鱼成本。
- A 保留终止快照并结算；B 不写库存、不重发奖励，不声明进程崩溃下 exactly-once。
