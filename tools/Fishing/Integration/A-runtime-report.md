# A 单人运行时接入 — 2026-09-30

本轮把 A/B/C/D/E 装配为单人本地权威入口，尚不代表完成真人交互验收或多人玩法。

## 实现

- ModApi 注册更新/世界退出/游戏关闭，仅本地主机创建运行时，远程客户端及 dedicated 不启动。
- LocalFishingRuntime 实例化 B/C/D/E；装备鱼竿后按配置抛投，用原生绝对视线查水面/障碍/深度；Begin 成功才扣一份鱼饵。抛投按钮不作为同帧提竿重放。
- NativeBindings 解析 KeyCode；原生移动桥保留碰撞/速度，只加入额外后退并衰减逆拉力方向。竿根来自实际位置/眼高和抛投方向，每帧应用 Origin 偏移。原生占位模型按会话隐藏/恢复，正式手部动画仍待 D。
- 清理覆盖切物品/槽位、实际伤害回调及血量下降、死亡、菜单/失焦/暂停、游泳、车辆、世界/玩家更换、超距和终态。钓鱼采样异常不直接中断原生 PlayerMoveController.Update。
- CatchSettlement 验证当前授权 session、权威 Resolved、身份/重量/tick 和 E 奖励规格；重复事件不重复发奖，满包保留一条待领鱼获并阻止新抛投覆盖。
- NativeCatchRecord 使用原生 CVar。IL 确认 PlayerDataFile.FromPlayer 克隆背包并序列化 Buffs，ToPlayer 恢复二者。进行中的鱼战不保存。
- 原生测试发现 Bag.AddItem 会忽略不同结算元数据而堆叠鱼获，改用 Bag.SetSlots 更新指定空格并通知背包变化，保留每条鱼的重量/编号。
- 修复探针脚本误写的 PowerShell 比较符，进程核验现在使用 -lt / -gt。
- F 在本轮期间新增 Networking。NativeFishingPacket.Write 通过 BinaryWriter 的 byte[]/offset/count 重载写入，避免 net48 的 Span 重载解析错误。A 尚未启用网络传输/路由器。

## 验证

- A 骨架 25 项、C 操作 32 项通过。
- A/B/C/E 联合 18 项通过，包含实际 C/B 钓鱼至上岸与 E 奖励规格。上岸用例降低耐力并关闭恢复以缩短时长，不代表默认难度已调好。
- 结算/选点 21 项通过：满包、重建服务后领取、重复事件、身份/重量篡改、未知 session、预测客户端、未知写入结果停止重试、浅水/遮挡/干地/未加载/距离/朝向。
- 原生复测和安装结果在末尾追加。探针使用隔离存档和 inactive 玩家夹具，不伪称真人输入验收。

## 尚需实机验收

1. 背包和快捷栏分别放蚯蚓，确认有效抛投扣 1、无效位置/缺饵不扣、取消不退饵。
2. 完成咬钩/中鱼/冲刺/上岸，检查过早/过迟/空提竿和强拉断线。
3. 靠墙遛鱼，比较原生 S 与鼠标后退，检查不会穿墙、加倍 WASD 或放开后持续倒退。
4. 每个状态打开菜单、Alt 切出、切物品、受击、上下车辆，确认镜头/移动/模型恢复。
5. 满包上岸，保存退出再进入，空出一格只领一次；再保存重进不重复。

原生完整玩家保存重载和崩溃一致性未验收，不宣称任意故障下 exactly-once。线漂约束、精确水面、正式手部动作和多人路由/预测也未验收。

## 最终结果

2026-09-30 后续按用户要求，新建正常存档 `C:/Users/Administrator/AppData/Roaming/7DaysToDie/Saves/Navezgane/FishingTest`。由独立游戏进程原生生成、保存并正常退出后复制，未修改 GUA2。存档中的 pzaec-fishing-test-kit.xml 指定首次本地角色进入时发放钓竿 2、蚯蚓 200、烤鱼 10；TestEquipmentDelivery 按条目保存已发数量，空间不足等待空格。角色尚未首次进入，装备是待发放状态。该更新 SDK 构建 0 警告/0 错误，安装 DLL SHA256 为 `BA20EC8A593A0F6046F6050D56AF5CE8EFD30AAAC386A4D53BCC16B6309C2B91`；之前 DLL 已由安装脚本备份。

2026-09-30 原生复测 **27/27 通过**，日志 `artifacts/native-469311d4f2ba4e4da9801fecf74af8e3/game.log`；已验证当前游戏引擎能加载 D 的实际资源并清理，未验收画面像素或真人操作。首轮 `native-c304bcbfbf184647b42ec34fb14774fa` 发现鱼获被堆叠，修复后复测通过。两次独立测试进程均已停止。

额外重跑 F 的 `QA/Test-Network.ps1`：88 项通过（包含纯逻辑网络→真实模块的路由测试），不等于真实联机验收。两种构建均通过。已用 Install-TestBuild.ps1 安装正式模组 DLL，校验 SHA256：`F4EEF0F74B0DE865288202D69CBD8F7A087E8B18275A374ABBAD9D6A4B64663A`。安装时游戏未运行，下次启动单人存档生效。
