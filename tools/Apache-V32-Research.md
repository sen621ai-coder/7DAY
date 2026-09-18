# AH-64 阿帕奇：V3.2 适配研究

日期：2026-09-18。范围：静态研究、原版资源下载、编译诊断与本机程序集检查；没有安装阿帕奇、修改现有飞控或启动游戏验证。

## 结论

“保留阿帕奇模型与导弹设定，沿用 MD-500 操作”在架构上可行。飞控复用把握较高，武器需要真正的 V3.2 代码适配，不能仅复制 XML。完整模型加载、导弹碰撞和多人同步尚未实机验证。

推荐新增独立的阿帕奇载具，保留 MD-500。保留作者资源包与阿帕奇专用模型节点配置；把现有飞控提取为仅对白名单直升机启用的共享控制器；按原有导弹参数实现 V3.2 专用武器适配。仅新增阿帕奇的模型不能自动继承飞控：当前 Applies 精确匹配 vehicleMD500。

## 固定研究版本与证据

- 阿帕奇仓库：https://github.com/Zilox135/AH-64-Apache-Helicopter
- 阿帕奇提交：`8eff6b144783b947957a607b26a68ef954c562c3`，ModInfo 版本 `1.7.0`。
- 武器前置仓库：https://github.com/closerex/Closer_ex-7D2D-mods
- 武器仓库提交：`be6199f7ba59404961fd6b0090dc7b7bc9aeb82f`。
- 研究文件：`.local-tests/apache-research/`；树清单、原 XML/源码、模型包、前置 DLL 均在此隔离目录。
- 编译检查：`Compile-Probe.ps1`；结果：`compile-results.json`。只调用 Roslyn 编译诊断，引用本机游戏与前置 DLL 的元数据，没有执行第三方程序集或输出安装版 DLL。
- 编译探针按每个 csproj 的 Compile 清单取源码，依赖使用上游发布 DLL，因此这不是全套前置重新构建成功的证据。

## 模型与存储

作者 Git 树中的实际模组文件总计 15,328,313 字节，约 **14.62 MiB**，不含仓库 README/LICENSE。资源包 `Resources/ApacheHelicopterPrefab.unity3d` 为 14,971,698 字节，约 **14.28 MiB**。

资源包头为 UnityFS，Unity 构建版本为 `2022.3.29f1`。这只能确认打包格式，不能证明在本机 Unity 中所有材质、脚本与粒子均可加载。

XML 引用表明同一个包提供机体 prefab、旋翼相关节点、起停/飞行/开火/爆炸声音与 BigExplosion 特效。不要为了精简直接移除声音或粒子资源；若要分拆，需另行解析和重打包。

四个武器前置目录中的 DLL、ModInfo 和 Config/Resources/UIAtlases 文件按树清单估计约 0.16 MiB，不含源码/PDB/Quartz，也不是完整安装包体积。复杂性主要来自跨版本补丁，而非这些 DLL 的大小。

相较当前 MD-500 的 13.40 MiB，阿帕奇本体仅多约 1.22 MiB。两者同时保留则需新增约 14.62 MiB，再加适配代码/必要依赖。

## 飞控复用依据

两者都使用 `EntityVGyroCopter`，均配置 motor0/motor1，主旋翼 rpmMax=8，以及 force0 至 force5。当前 MD-500 控制器拦截 EntityVehicle.FixedUpdateForces 并按刚体加速度施力，不依赖 MD-500 网格形状。

保留以下操作语义：Space 上升、C 下降、W/S 水平前进/制动/倒退、A/D 转向、Shift 加速、松键速度稳定。保留无油/无驾驶员/浸水时失去主动升力与原生联机物理授权。

阿帕奇必须保留自己的座位、轮组、旋翼路径、灯光、碰撞体配置。其主旋翼路径为 `Origin/TopPropellerJoint`，MD-500 为 `M/Origin/TopPropellerJoint`，所以不能直接整份覆盖 vehicles.xml。原版速度参数分别为 `20,9,25,9` 与经过补丁后的 MD-500 参数；复用操作不意味着自动复制速度、耐久或容量。

已执行 `tools/Test-MD500FlightControls.ps1`：71 项数学/控制器断言通过；本机 V3.2 IL 的三处地面输入、一处速度油耗注入点及模拟先于网络发送的顺序检查通过。这不是阿帕奇试飞结果。

## 可保留的导弹设定

配置位置：`Config/vehicles.xml` 的 WeaponLeft / WeaponRight，`Config/items.xml` 的 ammoMissile。

| 项目 | 原版设置 |
|---|---|
| 操作席位 | 两组均为 seat=0（驾驶员） |
| 武器槽 | 左 slot=0，右 slot=1 |
| 发射节点 | HornWeapon/WeaponRoot/Rocket01、Rocket02 |
| 类型 | ParticleWeapon,VehicleWeapon |
| 每次粒子发射数量 | burstCount=2 |
| 一轮重复次数 | burstRepeat=3 |
| 重复间隔 | 0.15 秒 |
| 重装间隔 | 0.5 秒 |
| 弹药 | ammoMissile |
| 触发爆炸 | 碰撞触发；粒子死亡不触发 |
| 爆炸伤害配置 | 实体 250、方块 50 |
| 爆炸范围配置 | 实体 10、方块 5 |

这些是配置基础值，不是技能、护甲和服务器规则作用后的实测伤害。代码每个 burst step 扣 1 发弹药并发射 burstCount 个粒子：保留原版规则时，粒子数与耗弹数不是 1:1。实现时必须明确保留这一行为还是另行调整，不能误把 burstCount 当成耗弹数。

检查到的配置没有 rotator/目标锁定字段，也没有机炮武器条目；按固定朝向导弹理解，不应宣称有自动追踪能力。轨迹、散布与寿命还可能由包内粒子系统决定，不能只从 ammoMissile 的 ProjectileVelocity 推断实飞导弹速度。

模型与配置保留 `AH-64_Apache_Helicopter` 模组标识最直接；若重命名或搬移，必须同步修改 `@modfolder(AH-64_Apache_Helicopter)` 路径。新增弹药/音效/导航等标识应考虑前缀，避免通用名 ammoMissile、Helicopter、missile_fire 与其他模组冲突。

## V3.2 实际不兼容点

| 原前置 | 编译错误数量 | 已确认的问题 |
|---|---:|---|
| CustomParticleLoader | 3 | ExplosionServer / ExplosionClient 原九参数调用失效 |
| CustomPlayerActionManager | 4 | SaveControls、BtnDefaults_OnOnPressed、actionTabGroups 变化，以及 RefreshValue 参数变化 |
| CustomParticleLoaderMultiExplosion | 1 | ExplosionServer 九参数调用失效 |
| VehicleWeapon | 4 | DamageMultiplier 构造、字典 .Dict、AttackHitInfo.hitPosition 变化 |

共 12 条编译错误；部分 VehicleWeapon 错误属于阿帕奇没有用到的射线/循环武器，但会阻断原项目整体重编译。12 不是适配总工作量，也不是仅有 12 个运行时问题。

本机 ExplosionServer 为八参数（从 worldPos 开始），ExplosionClient 也为八参数；旧接口的 clrIdx 已不存在。原 CustomParticleLoader 的 `explode_Prefix` 仍请求 `_clrIdx`，其 transpiler 又使用旧参数索引并按固定指令数量替换。这些需要重新核对 IL 与网络包，不能简单删除调用处的一个参数。

上游武器框架本身有开火状态网络包、远端粒子随机种子同步、驾驶员背包扣弹等行为。重写时必须实现对应流程并验证伤害只结算一次，不可把视觉特效当成伤害同步。

阿帕奇实体 XML 使用旧 `LootListAlive` 和平铺 `Explosion.*`；本机原版使用 `LootList` 与 `property class="Explosion"`，应按现行格式适配并验证。已检查顶层 XML 的 XPath 在本机原版配置中全部有匹配，但这不代表经过 ProjectZ/AEC 修改后的最终配置也相同，且不证明新增节点的旧字段有效。

Quartz 未做版本兼容性验证。原依赖链借助它改善按键设置界面；如果自行实现符合 V3.2 的专用输入/设置方案，可重新评估是否还需要它，而不是直接认定可无代价删除。

## 实施路线建议

1. 先做“阿帕奇模型 + 共享 MD-500 飞控”的隔离试验。模型加载、材质、旋翼、座位、碰撞、上下机、存读档和油耗通过后，才继续接武器。
2. 用户目标仅要求原模型与开火设定，建议做仅作用于阿帕奇的 V3.2 导弹适配：保留左右节点、连发节奏、弹药和原效果资源；实现专用输入、碰撞判定、扣弹、原生爆炸伤害与网络同步。此路线减少对全局旧 UI/爆炸补丁的依赖，但仍需开发和实机验证，不承诺比修复旧框架更省总工时。
3. 如果要求与作者武器框架所有附加功能完全一致，则修复并逐项验证四个前置与 Quartz；不要为了单架阿帕奇顺带迁移无关武器功能。
4. 最后验证单机与专服：两侧单独/一起开火、空弹、连按、切座、上下机中断、贴近机身碰撞、远端可见效果、一次命中一次伤害、断线重连、保存重载及现有其他载具不受影响。

评估：飞控接入低至中等工作量；模型兼容需实测；导弹与联机适配中等以上，是主要工作。当前没有发现架构上的不可行因素，但尚不能标记为“V3.2 可直接使用”。

原资源涉及模型原作者，仓库许可和 Nexus 资产声明也需分别对待；本次仅用于本地研究，若未来发布整合包应再核对资源再分发范围。
