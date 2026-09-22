# 自动采矿机事件日志

安装状态：2026-09-22 用户退出游戏后已安装 RuntimeFix 1.27.13，安装 DLL 与候选 SHA256 核对一致，旧 DLL 和 ModInfo 已备份至 .local-tests/apache-research/before-miner-audit-*。下次启动生效；游戏内事件复现仍待验证。

2026-09-22：用户要求排查多台相邻矿机消失。新增 AutoMinerAudit，只观察七种 AutoMiner 资源矿机，不改变损伤、生产、掉落或加载结果。

日志前缀 `[AutoMiner-Audit]`，输出到游戏常规 output_log。房主/服务器日志是判定权威世界操作的主要证据，客户端日志可帮助对照显示与加载差异。

- DamageBlock：受击前累计损伤 damageBefore、请求伤害 requestedDamage、actorId、武器标签及调用路径。请求伤害不等于最终实际扣除值；未知 actorId 不代表没有攻击。
- 同一坐标每五秒最多输出一条损伤记录，中间次数及请求伤害总和在下次损伤或生命周期事件附带输出。没有后续事件时，最后一组汇总不保证落盘。最多缓存1024个坐标，满时清空；退出世界清空。
- OnBlockAdded / Loaded / Unloaded / Removed：标记放置、加载、卸载、移除回调。Removed 本身不等于遭到攻击，必须结合调用链及邻近损伤记录。
- OnBlockDestroyedBy / OnBlockDestroyedByExplosion：记录对应销毁回调和调用链。记录发生在原生方法执行前，不代表操作一定成功完成。
- 所有事件带坐标、区块坐标、方块名称、子物块标志和旋转。多格机器可能产生多条不同位置的记录。
- 不记录库存内容；可用三只背包的现场信息与销毁时间对应，但不能靠此模块追溯安装前事件。

候选 DLL：`.local-tests/apache-research/AEC.T16.RuntimeFix.miner-audit.dll`。游戏运行期间不替换已安装 DLL。下次保存并完整退出后，备份旧 DLL、复制候选并核对 SHA256，再把 RuntimeFix 版本更新到 1.27.13。

编译验证：使用游戏 Mono 引用编译；核对当前程序集的七个目标方法及参数。尚未游戏内复现坍塌/销毁/重新加载事件。
