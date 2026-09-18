# M1 构建与验证

运行目录为游戏 `Mods`，使用 PowerShell 7。模型源固定为 `F:/game/m1_abrams.glb`，读取前核对 SHA-256；源文件变化必须重新审核部件分组，不能直接沿用编号。

工具需要 Python 3.12（NumPy、Pillow）及 Blender 4.5。本机便携 Blender 在 `.local-tests/M1Tools/blender-4.5.0-windows-x64/blender.exe`，不属于分发模组。编译器来自 PowerShell 的 Roslyn，引用本机 `7DaysToDie_Data/Managed` 和 `0_TFP_Harmony/0Harmony.dll`。

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
