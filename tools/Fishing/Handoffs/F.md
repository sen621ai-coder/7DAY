# F 会话交接

日期：2026-09-30。F 网络模块与自动验证已交付；真实 M3 联机验收待 A 集成后执行。未修改共享契约、A/B/C/D/E 文件、其他模组、最终 DLL 或现有游戏存档。

## 完成内容

- `FishingNetwork : IFishingNetwork`，构造器注入 A 的 `IAuthoritySessionRouter`、传输接口、观察资格判定和可选限额配置。
- 完整二进制协议及字段校验，可靠原生 ToServer/ToClient 包；身份从已认证的 ClientInfo 登记获取。
- 单玩家单会话、请求重放去重、输入序号/tick/时钟检查、频率限制、连接代次、断线/超时取消。
- 权威快照与事件去重、观察者范围过滤、缓存限额、过期清理；显示插值不会回写物理或触发奖励。
- AuthorityInputBuffer：以服务端节奏消费输入，鼠标增量/提竿边沿只消费一次，250ms 无包释放持续操作；ForPublication 恢复正确网络 ack。
- FishingMovementGuard：辅助核验原生已观测位移，保留原生角色所有权和移动执行路径。
- FishingLoopback/FishingServerTransport：单人与主机本地玩家可走同一权威协议。

## 修改文件范围

- `ZZZ-PZAEC_Fishing/Source/Networking/`：FishingWire.cs、FishingNetwork.cs、AuthorityInputBuffer.cs、NativeFishingTransport.cs、FishingLoopback.cs。
- `tools/Fishing/QA/`：测试工程/脚本、真实模块联调、原生 API 核验、README、Validation 和暂存产物。
- 本交接文件。

没有改 STATUS。交付复核时 A 已同步把 STATUS 更新为单人 0.1.0 安装、F 88 项通过、多人路由未启用；以后续 A 状态为准。

## 实际验证

```powershell
pwsh -NoProfile -File tools/Fishing/QA/Test-Network.ps1
pwsh -NoProfile -File tools/Fishing/QA/Build-Compatibility.ps1
pwsh -NoProfile -File tools/Fishing/QA/Inspect-NativeNetwork.ps1
```

88 项自动检查通过，包含 10,000 随机畸形包、5,000 有效头变异包、0/1/8/32 合成连接负载、真实 A/B/C/E 中鱼流程及断线释放。当前游戏程序集兼容编译通过（警告作为错误）；14 项原生接口/IL 检查通过。没有启动真实主机、远端客户端或专服。

详细结果：`tools/Fishing/QA/Validation.md`；装配和协议说明：`tools/Fishing/QA/README.md`。

## A 必须接入的部分

1. 实现生产 IAuthoritySessionRouter：TryStart 验证真实身份/实体、装备、水面和目标，扣饵后生成新的 Guid 和权威 SessionDriver。每个玩家最多一个。AcceptInput 只入 AuthorityInputBuffer，不能按包推进游戏时间。
2. NativeFishingTransport Bind/Attach/Detach/Dispose 接实际世界、认证玩家附着、断线及退出生命周期。identityResolver 与存档 persistent ID 一致；只从真实 ClientInfo 获取身份。所有回调主线程执行。
3. 每帧按服务器时间 Tick，再 Consume/Advance/Publish；使用 `buffer.ForPublication(driver.Current, events)`，不要把 SessionDriver 内部帧序号直接当成网络 ack。
4. 客户端 StartResult 建立/拒绝本地钓鱼状态；ApplySnapshot 更新 D，SessionEnded 清理 D/释放 C 和原生输入租约。没有首份快照也会超时。
5. 服务器观测真实玩家位移与环境；受击、死亡、切物品、游泳、载具和菜单取消。远端受力移动经现有原生角色所有权和 C 意图适配执行，不能在 F 直接设坐标。
6. 专服使用空 IFishingPresentation；主机本地玩家使用已提供的 Loopback，避免绕过服务器校验。
7. 由 A 从权威 Resolved 生成 CatchResult，调用唯一 ICatchSettlement；复用 A 已交付单人结算，将背包满、存档去重和崩溃恢复纳入多人验证。F 未引入第二个奖励入口。
8. 选择服务端观察距离/资格过滤，整合 D 观察者对象；完成 Validation.md 的四种运行模式及性能验收后，才能标 M3 通过。

## 契约观察与扩展请求

v1 可以完成当前一鱼种协议，不需要修改共享类型。但 NetworkSnapshot 缺少完整 SessionStart/鱼定义及玩家映射，后续多鱼种和第三人称人物绑定需要 A 统一增加服务端会话描述消息或通过原生映射提供给 D。MovementRequest 也不在协议内：当前建议客户端只根据权威快照生成 C 的本地移动意图，服务器校验实际位置；是否需要显式受力移动消息应在实机所有权验证后由 A 定义。

F 使用权威状态加显示插值，不进行客户端鱼战物理回滚。原因是 v1 无模拟恢复/回滚接口，强行跑另一套鱼战会造成错误断线及奖励边界混乱。鼠标视觉预览可以低延迟，最终物理和成功判定始终取服务端状态。需要更激进预测时由 A/B/F 共同定义恢复接口。

## 剩余限制

原生包分派与生命周期仅完成编译/API 核验；实际网络上的鱼获一致性、受力后退、手感与服务器无图形运行尚未实测。不能用 88 项通过替代 M3。全部 F 产物可供 A 集成；F 没有安装玩法 DLL，A 在另一会话已安装单人测试版，该版未启用实际多人路由。
