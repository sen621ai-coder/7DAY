# M1 构建与验证

运行目录为游戏 `Mods`，使用 PowerShell 7。模型源固定为 `F:/game/m1_abrams.glb`，读取前核对 SHA-256；源文件变化必须重新审核部件分组，不能直接沿用编号。

工具需要 Python 3.12（NumPy、Pillow）及 Blender 4.5。本机便携 Blender 在 `.local-tests/M1Tools/blender-4.5.0-windows-x64/blender.exe`，不属于分发模组。编译器来自 PowerShell 的 Roslyn，引用本机 `7DaysToDie_Data/Managed` 和 `0_TFP_Harmony/0Harmony.dll`。

副武器 0.4.0：`export_secondary_runtime.py` 从已确认的 `Generated/SecondaryWeapons/M1Abrams-SecondaryWeapons.blend` 导出独立 `M1Secondary.meshbin`、挂点和颜色图集；不重写主模型。`secondary_config.py` 幂等生成弹药、配方、解锁和说明。`Test-All.ps1` 包含副武器数值、协议、模型及配置检查。

原生测试用 `pwsh -File tools/M1Abrams/Start-NativeQA.ps1`：没有其他游戏进程时启动隐藏的独立 Navezgane 专服，使用 `.local-tests/M1NativeQA/` 下的新存档和模组副本。安装目录的其他模组用相同 Name 的空清单在测试 UserData 内遮蔽，避免原生游戏继续扫描安装目录而重复/混合加载；缺失的整合材料使用测试占位物品。测试会自行退出，路径与 PID 记录在 session.json，结果在该测试存档的 `m1-native-report.txt`。这不是完整整合包、真实双客户端或视觉实战验收。

```powershell
$m1Python = 'C:/Users/nxvan/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$m1Blender = '.local-tests/M1Tools/blender-4.5.0-windows-x64/blender.exe'
& $m1Python tools/M1Abrams/prepare.py
& $m1Python tools/M1Abrams/configure.py
& $m1Blender -b --python-exit-code 1 --python tools/M1Abrams/build_blender.py
& tools/M1Abrams/Test-All.ps1 -Python $m1Python
& $m1Blender -b --python-exit-code 1 --python tools/M1Abrams/validate_blender.py
```

`configure.py` 重新生成配置/声音；手工修改配置后应同步修改生成脚本。`build_blender.py` 生成可编辑工程、FBX、运行时网格、锚点、贴图与预览。`Build.ps1` 先完成编译再原子替换 DLL，失败时保留旧 DLL；运行中的游戏须重启才能加载新版本。

主要交付：`Generated/M1Abrams.blend`、`M1Abrams.fbx`、`M1-textured.png`、`M1-fire-pose.png`；资源最终输出到 `ZZ-PZAEC_M1Abrams/Resources`。Blender 图片是离线模型预览，不是游戏截图，开火姿态图也不代表游戏最终粒子效果。

`Test-All.ps1` 隔离运行 C# 测试并编译实际源码。网络/库存测试使用替身对象，未运行 Unity 场景或专用服务器。`validate_blender.py` 进行 432 组离散姿态的精确三角形相交检查，不能证明任意连续姿态、悬挂或网络状态。测试结果 JSON 在 `Generated`。安装方法与实机清单见模组 `README.md`。
