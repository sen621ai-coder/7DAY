# 0.11.0 无人机寻路验证

日期：2026-09-28。运行时 SHA-256：`f040b20d0379bf4fd1b1fb3a86a458c74f03996ec5f4931be19d29cb16685020`。

## 已通过

- `pwsh -File tools/Test-CargoDroneCore.ps1`：6019核心、483寻路、78调度检查。含实盘检查点导航扩展、失败航段重送前复查、存档恢复与召回中断搜索。
- `pwsh -File tools/Test-CargoDroneNavigationScheduler.ps1`：4任务公平轮转、注销释放、2份搜索／8次查询／2毫秒协作式预算。
- `python tools/Test-AutomationWorkshop.py`：配置、配方、UI引用和入口航标属性。
- 原生入口及多隔墙：153项通过，0失败。报告：`.local-tests/CargoDrones/NativeQA/run-b43a1c0257f54f2db6f7463f06d6b945/UserData/Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt`。
- 原生货运调度及持久化：232项通过，0失败。报告：`.local-tests/CargoDrones/NativeQA/run-c0e4af7d498949d9b7358a32e13647b8/UserData/Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt`。
- 原生测试后最后的入口配置失效清理调用，由核心回归验证；导航算法、碰撞与序列化逻辑与通过原生测试的版本相同。

## 压测后调整

初次原生100毫秒总预算在约149节点／905次查询时耗尽；提高原生上限至1500毫秒，仍保持单帧软预算和节点／查询硬预算。复用相邻区块窗口后，测试房间一次成功搜索约294节点、2615次查询、285毫秒。首轮室外远目标绕行跳过升高优先的问题也已修复；原生绕墙检查通过。

## 范围

房间为带顶板、入口竖井和交错隔墙的明确近似场景，未取得用户原存档的完整几何。没有联机客户端或肉眼按E交互验收；需要双方更新后现场复测。四任务公平性为离线时钟验证。

额外旧库存独立脚本因本机未提供Unity Mono可执行路径未运行完成；本次库存验证来自已通过的原生调度／持久化测试，不把该脚本记为通过。隔离服务器的Steam登录超时不影响本地测试报告。

旧DLL备份位于 `.local-tests/CargoDrones/Navigation-0.11.0/YF.Automation.before.dll`。发行包包含整个运行所需mod目录，不包含编译中间文件和测试存档。
