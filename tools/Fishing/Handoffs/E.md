# E 内容会话交接 — 2026-09-29

状态：**契约 v1 内容实现、独立构建测试和离线整合检查完成，可供 A 接入。** 游戏内制作/食用/鱼获与正式视觉验收未执行，不代表整个钓鱼玩法或 M2/M3 已完成。

## A 接入点

```csharp
IFishingContent content = new PZAEC.Fishing.Content.FishingContent();
FishingConfig config = content.Load(modDirectory);
// 将 config 传给 B/C/D；权威成功终态才允许调用 TryGetReward。
```

无参构造器；配置路径使用 FishingContract.ConfigFile：`Config/Fishing/settings.xml`。所有字段与 Contracts/Configuration.cs 对应，没有新增共享类型或修改契约。未知字段、缺字段、版本不符、非法值、反向区间和冲突键位明确失败。

奖励：carp → pzaecFishingFishCarp × 1，FishMassKg保留用于结果显示。仅在成功加载后允许查询；需要非空 SessionId/SettlementId/PlayerPersistentId、非负 TerminalTick、匹配鱼定义和当前配置鱼重。**权威成功判定、会话与结算 ID 绑定、持久去重、背包满重试都是 A 的职责；TryGetReward 不承担这些。** 不在未结算结果仍存在时热重载鱼重。

## 固定 ID 与玩法

- pzaecFishingRodBasic：手工制作，木材20＋锻铁2＋废聚合物10＋布5。
- pzaecFishingBaitWorm：腐肉1制作虫饵5；A 在接受有效抛投时扣1，无效抛投不扣，已接受后取消不退。
- pzaecFishingFishCarp：仅鱼获结算，不能直接吃，无配方生产。
- pzaecFishingMealGrilled：篝火＋烤架，鲤鱼1制作烤鱼1；基础食物10/治疗5/体力加成10，使用现有食物 buff 流程。

最终遵循 A 的 ID。启动时提出的 BaitDough / GrilledCarp 临时 ID 已移除。所有新物品禁售，入门配方没有书籍/技能锁。

## 修改文件

- ZZZ-PZAEC_Fishing/Config/items.xml：4种独立物品，鱼竿无继承近战动作。
- ZZZ-PZAEC_Fishing/Config/recipes.xml：3个配方。
- ZZZ-PZAEC_Fishing/Config/Localization.csv：4个名称＋4段说明，中英双语。
- ZZZ-PZAEC_Fishing/Config/Fishing/settings.xml：全部共享配置字段。
- ZZZ-PZAEC_Fishing/Source/Content/FishingContent.cs：IFishingContent、严格加载/验证、只读奖励映射。
- tools/Fishing/Content/：隔离测试工程、测试源码、构建脚本、配置合并验证、README与产物忽略规则。
- 本交接。

未编辑 A 工程/入口/契约、B/C/D/F源码、其他模组或最终安装 DLL。

## 已执行验证

命令：`pwsh -NoProfile -File tools/Fishing/Content/Test-Content.ps1`

- net8.0、net48 测试工程均编译成功，0错误、0警告。
- 两个运行时各通过106项内容检查。
- 模拟合并工作区30个模组的3649条 items/recipes/materials/buffs/blocks 补丁，4个物品和3个配方通过唯一性、引用、中文/英文文本和可用材料验证；已有补丁未命中目标为0。
- 结果：tools/Fishing/Content/artifacts/config-check.json；测试产物只位于 E 的 artifacts。

不是原生游戏配置加载或联机实测。没有启动、重启游戏或安装最终 DLL。

## 待 A/B/C/D 联调

1. A 显式绑定本实现，完成扣饵、配置失败时停止启动及唯一结算事务。原生加载后确认制作/食用与整合包实际行为。
2. B 对初始值做手感与稳定性调参，E 再更新配置。当前值与 A 探针基线一致，不能称已验证拉力手感。
3. D 正式模型/图标未接入：目前石矛网格/原版图标和袋状鱼获是占位。D 确定路径后由 E 更新 XML 引用。
4. C/A 默认键位及自定义绑定需实机测试；E 只检查钓鱼操作之间的冲突。
5. 首版固定3 kg，警觉性以试饵/咬钩等待时长表示；随机体重范围和独立警觉参数不在 v1 契约中。后续由 A 增补契约，再由 E/B 接入，避免无效配置。

完整参数语义和运行步骤见 tools/Fishing/Content/README.md。
