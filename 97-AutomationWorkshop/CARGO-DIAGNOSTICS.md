# 无人机诊断日志（0.11.0）

日志写入运行运输逻辑的房主／服务器的游戏日志。客户端的模型画面不能代替房主记录。默认目录为 `%APPDATA%\7DaysToDie\logs`，使用自定义 `-logfile` 的服务器以实际启动参数为准。

搜索 `[YFCargo][Diag]` 查看新增记录；同时保留 `[YFCargo]` 的原有错误和 `Loading wait` 记录。游戏自身的时间戳仍保留。

## 记录范围

| 事件 | 内容 |
| --- | --- |
| `start / restore / stop` | 诊断版本、世界编号、区块预算、恢复任务数、释放前资源数 |
| `state` | 平台及航班编号、运输阶段、等待原因、位置、电量、货包数、供电、暂停、配置版本、新货与本批货目标、入口坐标 |
| `retarget / route-via / segment / segment-reached` | 新目标、入口生成的完整分段航点、当前航段起终点、抵达位置 |
| `plan-start / plan-reject / plan-ready / plan-failed` | 规划起终点、高度上限、候选编号、检测次数、失败原因和成功航点 |
| `collision` | 障碍方块名／实体编号、包围盒、发生碰撞的航段；区分大型设备和多方块子块 |
| `blocked / retry / return` | 受阻、重试、返航目标、剩余航段、电量及低电量返航标志 |
| `loading-wait / sweep-wait / loading-resumed / plan-wait` | 加载／碰撞检查／规划等待和恢复；区块坐标、驻留、锁定、装饰、初始化状态、预算 |
| `endpoint-wait / endpoint-invalid / endpoint-busy / endpoint-ready` | 矿机或储物箱的加载、身份失效、玩家或机器占用、会话排空、库存事务占用、可用进场坐标 |
| `inventory-applied / save-request / save-wait / save-status` | 事务编号、端点编号、交接包数、库存版本、区块锁定等待及保存结果。`inventory-applied` 本身不等于持久化完成，需结合 `Durable` 和后续阶段 |
| `command / command-applied / command-rejected` | 召回、暂停、更换目标等命令及结果，区分机身和平台入口；自动刷新不逐条记录 |
| `recovery-start / recovery-wait / recovery-settled / recovery-finished` | 存档启动恢复中的待处理事务、区块／会话等待、提交或撤销及完成 |

`id` 的含义随事件变化：`state` 与命令用平台编号，航线事件用航班编号，`endpoint-*` 用端点编号，保存事件用事务编号。同行的 `flight`、`endpoint`、目标编号和坐标用于关联。离线隔离航线测试未挂载正式世界调度器时，`world` 可以为全零。

## 限频与边界

- 航段、返航及规划结果按事件记录，不逐帧记录位置；正常进度每10秒输出一次状态摘要。
- 重复等待和端点信息通常每10秒记录；碰撞与否决候选每秒最多一条同类记录。`suppressed` 表示间隔内省略的重复次数，不是错误数。
- 高频状态抖动最多每半秒记录一次；短时间内多个中间状态可能被合并，保留后续状态。恢复正常也会记录。
- 诊断缓存有上限并随世界实例销毁，不写入库存内容或玩家平台账号。
- 日志用于定位，不会绕过碰撞、跳过库存事务或自动移动货物。此次仅补充诊断，不代表已定位所有真实存档中的卡住原因。

## 卡住时如何提供证据

1. 确认双方启动日志显示 Loaded Mod: YFAutomationWorkshop (0.10.6)。diagnostics=0.10.3 是既有服务端日志格式编号，不是安装版本。
2. 停住后保留现场15至20秒，记下界面状态和大致时间。随后可以正常召回。
3. 提供房主完整的当次游戏日志，尽量包含起飞前后、等待和召回，不只截取最后一行。客户端日志可作为补充。
4. 若游戏崩溃或日志完全停止，同时保留崩溃前尾部，不能仅凭缺少一条结束日志推断货物是否保存。
## 0.10.6 机身交互诊断

客户端搜索 `[YFCargo][Client]`：
- `interact`：16格内的交互键尝试，每架每秒最多一次。结果为 opening、not-aimed、out-of-range（距离及4格门槛）、occluded（遮挡碰撞体、层、距离）、ui-busy。
- `panel-open / request / reply / request-timeout`：面板打开、请求发送、服务器回复及超时，包含 hub 和 request 关联编号。正常自动读取及回复每10秒摘要一次。
- 玩家死亡、游戏失焦或没有有效机身实例时不记录。如果完全无记录，应继续检查机身实例和输入更新，不能视为已通过所有判断。

房主新增 `drone-request` 和机身读取的 `command-rejected`，自动读取每10秒摘要一次。被限频省略的请求编号可能不一一对应。
`entrance-repaired` 表示旧存档悬停在入口正上方且下降路线丢失，已补回下降航点；仍需通过正常碰撞和加载检查。

## 0.11.0 三维寻路

- `search-start`：起点、真实规划目标、室内模式。`route-via` 关联规划代次、强制点类型和顺序。
- `search-stage`：搜索轮次、网格尺寸、三维搜索边界。
- `search-wait`：至多每5秒一次，导航状态、轮次、Open数量、累计节点／查询、计算耗时和墙钟等待。
- `search-finished`：成功或明确失败分类、原因、节点／查询数、累计耗时、输出航点数。
- `navigation-failed`：当前航班规划代次、位置、失败分类。`route-energy`：新路线所需电量、当前电量、保护储备和是否返航。
- `route-migration`：旧入口路线进度不明确，暂停等待重新选入口或召回。
- `state` 增加 `navigation`，状态切换参与限频判定；同一航班的 `collision` 继续给出方块／实体编号和包围盒。

原生搜索总计算上限1500毫秒，每帧软时间片继续受全服2毫秒限制。`SearchBudgetExceeded` 是本次有限搜索未完成，不能解释为没有路；`NoPathInBounds` 也只表示已检查的范围和精度未找到路。`WaitingData` 时边未被判成障碍，数据恢复后继续同一搜索。待数据采用单边保留，优先减少区块申请抖动；按需复用相邻请求窗口，仍受49／128区块硬预算约束。


## 0.11.2 交互与改送

客户端 `interaction-ready` 表示模型交互脚本已取得平台身份并运行；随后 `interact` 记录未瞄准、距离、界面占用、遮挡或opening，`panel-open/request/reply` 确认开窗和服务端响应。输入读取与游戏原生一致，覆盖普通、永久及载具Activate通道。

服务器 `command action=RedirectShipment` 表示用户明确要求把停靠中的旧货改送新目标；此操作不会改变货物数量、货物版本或电量。普通 `SetTarget` 仍不更改旧货目的地。列表中的机器状态牌不再作为设备名。


## 0.11.3 邻近设备取货净空

pickup-clearance 记录来源 endpoint、original 与 selected 取货坐标，或不可用的 hold。pickup-reject 记录候选位置及 model-clearance、child 或实体方块名和坐标。候选高度最多抬升4格；交接通道阻挡和候选机体净空分开检查。未知邻近区块进入受预算限制的预加载，不当作空气。


## 0.11.4 卸货后直返

return-direct 记录当前位置、平台坐标和返航航点（室内、入口、室外）。正常卸货及目标满后的返航不再经过矿机；途中召回与低电量保留 return 原路记录。返回检查点可以保存室内/室外航点，但最后目标仍必须为平台。
