# Standalone test harness only: isolate Unity's scheduler initialization, retaining
# the game's actual ItemStack/ItemValue cloning and equality implementation.
param([string]$Directory=(Join-Path $env:TEMP 'YFAutomation-Harness'),[string]$RuntimeDll,[string]$MonoPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Add-Type -Path (Join-Path $root '0_TFP_Harmony/Mono.Cecil.dll')
$game=(Resolve-Path (Join-Path $root '../7DaysToDie_Data/Managed')).Path
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new();$resolver.AddSearchDirectory($game)
$reader=[Mono.Cecil.ReaderParameters]::new();$reader.AssemblyResolver=$resolver
$m=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $game 'Assembly-CSharp.dll'),$reader)
$t=$m.Types|Where-Object Name -eq 'ThreadManager'
foreach($method in $t.Methods|Where-Object Name -in @('.cctor','IsMainThread')){
 $method.Body=[Mono.Cecil.Cil.MethodBody]::new($method)
 $il=$method.Body.GetILProcessor()
 if($method.Name -eq 'IsMainThread'){$il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))}
 $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
}
New-Item -ItemType Directory -Force $Directory | Out-Null
$m.Write((Join-Path $Directory 'Assembly-CSharp.dll'));$m.Dispose()
$runtime=if($RuntimeDll){(Resolve-Path $RuntimeDll).Path}else{Join-Path $root '97-AutomationWorkshop/YF.Automation.dll'}
& (Join-Path $PSScriptRoot 'Build-AutomationSmoke.ps1') -RuntimeDll $runtime -Output (Join-Path $Directory 'AutomationSmoke.exe')
Copy-Item $runtime -Destination $Directory -Force
$env:MONO_PATH=$Directory+';'+$game+';'+(Join-Path $root '0_TFP_Harmony')
if(-not $MonoPath){$MonoPath=(Get-Command mono -ErrorAction SilentlyContinue).Source}
if(-not $MonoPath -or -not (Test-Path -LiteralPath $MonoPath)){throw 'Specify -MonoPath pointing to a Unity Mono runtime.'}
& $MonoPath (Join-Path $Directory 'AutomationSmoke.exe')
if($LASTEXITCODE -ne 0){throw 'Automation inventory harness failed'}
