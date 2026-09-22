# Buster Drone 外观接入

用户于 2026-09-22 指定本地 `buster_drone.glb`，替代此前待获取的 Oakraven 模型。原始文件包含作者 LaVADraGoN、来源 URL 和 CC BY 4.0 信息；发行资源内保留 `ATTRIBUTION.md` 和 `provenance.json`。

## 资源与构建

`Build-BusterModel.py --source <GLB路径>` 使用 NumPy 和 Pillow 离线转换，输出到 `97-AutomationWorkshop/Resources/CargoDrone`。不要求安装 Unity Editor，不在游戏内解释 GLB，也不附带第三方模组逻辑或脚本。原始 GLB 不复制进发行包。当前输入去掉展示地台后为 38 个网格、32,714 个三角面、88 个变换节点、2 个涡轮叶片枢轴；贴图上限 2048，保留颜色、法线、金属/光滑度、遮蔽及机身发光。

`buster.yfmesh` 为小端版本 1：魔数 YFBD、节点数、网格数、整体缩放/朝向/中心；随后是每个节点的名称、父节点索引、网格索引、停靠/飞行 TRS、叶片旋转轴，以及各网格的顶点/法线/切线/UV/三角形索引/材质索引。输入转换拒绝不支持的蒙皮、非线性插值、剪切、非零默认变形等。运行时只读取此固定资源，检查版本、计数、层级、索引和数值。

## 运行表现

- `CargoDroneModel.Drone()` 现在创建 Buster 模型；不再生成旧四旋翼几何体。
- `CargoBusterRig` 在两种机械姿态间用 1.5 秒切换；原始 25 秒展示片段不直接循环。父级根运动/摇摆冻结，由原有 `CargoDroneVisual` 负责位置插值、朝向及世界原点偏移。眼球保持静态，不播放原始瞳孔变形。
- 双叶片按各自局部枢轴转动，不沿用旧四旋翼的节点命名或统一 Y 轴假设。移除展示机身倾斜后，额外校正两侧完整涡轮总成，使飞行姿态的叶片平面保持水平。
- 去掉旧外观的六个橙色立方体货包；六格库存及界面状态仍来自既有权威快照，`CargoCount` 保留货包数。本次未给原模型额外添加货箱。
- 所有外观不带 Collider、Rigidbody、原生 EntityDrone 或独立库存。货物搬运、半径、区块租约和存档机制没有改动。
- 全姿态包围范围在离线阶段按 41 个姿态、24 个旋转角度计算，再统一缩放。径向上界约 0.74、高度半径 0.5216，留在原有服务端水平半径 0.8、垂直半径 0.6 内。原生 QA 另外覆盖各朝向/姿态/旋翼组合。
- Mesh、Material 和 Texture 通过引用计数在所有实例间共享；最后一架销毁后释放。每架只保留自己的变换和动画状态。

## 验证和交付范围

使用当前游戏 Mono/Unity 程序集编译到 `.local-tests/CargoDrones/YF.Automation.dll`。原生 QA 用独立 UserData 和 `CargoDroneQA_Isolated` 存档，渲染飞行/停靠 PNG，检查实际材质、网格共享、运动包围范围，并跑真实世界调度搬运。

本次外观接入不解除现有真实库存的 QA 启用限制，不代表正式停机坪放置流程或真实多客户端同步已验收。资源与源码位于工作树，测试 DLL 未安装到玩家实际游戏目录。

最终原生测试于 2026-09-22 完成：107 项通过、0 失败。模型几何覆盖 24 × 21 × 12 种航向/姿态/叶片角度组合，检查共享 Mesh/Material 和最后实例销毁后的释放，同时完成实际库存搬运、离线释放/恢复与返航。飞行和停靠预览已人工查看。报告位于 `.local-tests/CargoDrones/NativeQA/run-433cef2b47c645b696a986f8e48f07c4/UserData/Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt`；测试进程正常退出。
