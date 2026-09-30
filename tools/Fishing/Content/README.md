# Fishing 内容模块（E）

## 接入

实例化 `new PZAEC.Fishing.Content.FishingContent()`，通过 `IFishingContent.Load(modDirectory)` 读取模组下 `Config/Fishing/settings.xml`。配置完整覆盖契约 v1 的每个字段，不隐式回退到探针默认值。缺文件、未知字段、重复节点、非有限数、非法范围或冲突键位会抛出异常；A 应记录错误并停止启动钓鱼。

`Validate(FishingConfig, out error)` 同样检查外部传入的可变 DTO。先加载并验证，再把配置交给 B/C/D；不要在活动鱼战或存在待领结果时热重载内容。加载失败会撤销该内容实例的奖励查询资格。

`TryGetReward` 只把合法鱼获映射为 `RewardSpec`，不会扣饵、发物品、写存档或去重。A 必须先确认权威会话处于成功终态，并通过唯一结算入口处理重复调用和背包满。首版只接受配置中的 `carp` 及当前配置固定鱼重（容差0.001 kg），返回 `pzaecFishingFishCarp × 1`。鱼重不保存到可堆叠物品，不影响物品数量。

每次权威接受抛投扣 `pzaecFishingBaitWorm × 1`，次数常量为 `FishingContent.BaitPerAcceptedCast`。准备、无效抛投不扣饵；已接受抛投的失败或取消不退饵，均由 A 执行。

## 首版物品与配方

| 物品 | ID | 获取/用途 |
|---|---|---|
| 入门岸钓竿 | pzaecFishingRodBasic | 手工：木材20、锻铁2、废聚合物10、布5；30秒 |
| 虫饵 | pzaecFishingBaitWorm | 手工：腐肉1 → 虫饵5；5秒 |
| 鲤鱼 | pzaecFishingFishCarp | 权威鱼获结算；不能直接食用，无制作配方 |
| 烤鲤鱼 | pzaecFishingMealGrilled | 篝火＋烤架：鲤鱼1 → 烤鱼1；20秒 |

烤鱼沿用当前原版烤肉的食物处理流程：基础食物10、治疗5、最大体力加成10，并兼容已有农夫套装食物加成。数值仍需当前 ProjectZ + AEC 游戏内验证；不会重写其全局食物 buff。

入门配方没有技能/书籍锁，先保证可测试。新物品均不能卖给商人，避免未调平衡的无限赚钱路径。后续成长路线与更多鱼种另行调参。

图标暂时分别复用原版石矛、腐肉、生肉、烤肉。鱼竿暂用原版石矛网格但没有继承攻击动作；A 通过持有物品 ID 接管输入，D 接入正式持竿模型时由 E 修改配置引用。背包/落地的鱼仍是原版袋状占位资源，不能以此宣称真实视觉已完成。

## 参数解释与边界

- Rod：长度、刚度、阻尼、挠度上限、抬竿/侧压范围和角速度。
- Line：线杯容量、弹性和阻尼、断线力、损伤起始比例、每秒损伤、泄力区间、收线/出线速度。默认断线90 N、泄力5–65 N，初始泄力由 Controls.InitialDrag01 插值得到35 N。
- Float：质量、浮力弹性系数、阻尼、高度和静态浸没比例；浮力系数单位为 N/m，不是恒定浮力 N。
- Hook：提竿窗口、松线脱钩时间、有效/安全提竿速度、初始挂钩质量。
- Fish：首版固定3 kg鲤鱼，巡游9 N、爆发50 N、体力450 J、恢复8 W；吃口等待和试探时间范围、近岸反扑及上岸阈值。
- Controls：鼠标灵敏度、负载响应、自动后退、阻力比例、镜头反馈与默认键位。默认按契约，不代表已经验证与玩家自定义键位无冲突。
- Session：最小水深、抛投/玩家距离、超时和追帧限制（1–16）。

每个数值有范围校验并验证跨字段关系；非法配置明确拒绝。允许键名：A–Z、Alpha0–Alpha9、Mouse0–Mouse6、左右 Alt/Control/Shift、Space、Escape、Equals、Minus、Tab、BackQuote、Comma、Period、Slash、Semicolon、LeftBracket、RightBracket。Cast/Strike 可共用按键，其余操作键不可互相重叠。游戏原生 WASD、背包等冲突仍需 A/C 实测。

初始数值保留 A 契约探针基线，避免在没有 B 实测数据时制造另一套受力平衡，不能宣称手感已调好。B/C 调参建议由 E 更新配置。随机体重区间、独立警觉参数尚未进入契约；v1 用固定体重和 Nibble/BiteWait 时长，扩展时由 A 统一增补，E 不私藏无消费者参数。

## 验证

在 Mods 目录执行 `pwsh -NoProfile -File tools/Fishing/Content/Test-Content.ps1`。

测试工程仅编译 Contracts + Content 到本目录 artifacts，不运行整模组构建、不写安装 DLL。分别对 net8.0 和 net48 执行106项检查，包含加载、区域格式、缺失/未知配置、每个浮点字段 NaN、空配置节点、顺序约束、XML DTD 拒绝、键位冲突、失败重载、奖励资格、可变 DTO 隔离。

Test-Config.ps1 离线按工作区模组目录排序，模拟 items/recipes/materials/buffs/blocks 的 XPath 补丁，验证物品唯一性、配方材料、烤架/篝火、材质、食物 buff 和中英文本。报告位于 artifacts/config-check.json。这不等同原生 XML loader；不覆盖用户目录其他模组，不验证 prefab/图标资源加载。

待集成实测：原生 XML 日志、制作列表、烤架制作、食用加成与 ProjectZ 饥饿系统、物品外观、扣饵、背包满和重复结算、多人同步。
