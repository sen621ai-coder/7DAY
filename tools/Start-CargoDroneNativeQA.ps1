#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameRoot,[string]$RuntimeDll,[switch]$Headless,[string]$ReadbackDirectory,[switch]$InventoryOnly,[switch]$WorldScheduler,[switch]$CrashAfterNativeSave,[string]$ResumeCrashDirectory,[ValidateSet('yfAutoForestry','AutoMinerIron','AutoMinerLead','AutoMinerCoal','AutoMinerNitrate','AutoMinerClay','AutoMinerShale','AutoMinerBrass')][string]$SourceBlock='yfAutoForestry',[switch]$BindingAudit)
$ErrorActionPreference='Stop'
$modRoot=Split-Path $PSScriptRoot
$qaBase=Join-Path $modRoot '.local-tests/CargoDrones/NativeQA'
if($CrashAfterNativeSave -and $ResumeCrashDirectory){throw 'Crash preparation and restart are separate runs.'}
if($WorldScheduler -and ($CrashAfterNativeSave -or $ResumeCrashDirectory)){throw 'World scheduler and crash QA are separate scenarios.'}
if($ResumeCrashDirectory){
    $resumeRoot=[IO.Path]::GetFullPath($ResumeCrashDirectory)
    $qaPrefix=[IO.Path]::GetFullPath($qaBase).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resumeRoot.StartsWith($qaPrefix,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath (Join-Path $resumeRoot 'crash-killed.json'))){throw 'Restart requires a killed isolated QA run inside NativeQA.'}
    $resumeSave=Join-Path $resumeRoot 'UserData/Saves/Navezgane/CargoDroneQA_Isolated'
    if(-not(Test-Path -LiteralPath (Join-Path $resumeSave 'cargo-crash-ready.txt')) -or -not(Test-Path -LiteralPath (Join-Path $resumeSave 'cargo-native-inventory.wal'))){throw 'Missing prepared crash evidence.'}
}
if($ReadbackDirectory){
    $readbackPath=[IO.Path]::GetFullPath($ReadbackDirectory)
    $qaPrefix=[IO.Path]::GetFullPath($qaBase).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $readbackPath.StartsWith($qaPrefix,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath (Join-Path $readbackPath 'expected.snapshot'))){throw 'Readback must reference an isolated QA region fixture.'}
}
if(Test-Path -LiteralPath (Join-Path $qaBase 'session.json')){
    $previousSession=Get-Content -LiteralPath (Join-Path $qaBase 'session.json') -Raw | ConvertFrom-Json
    $previousProcess=Get-Process -Id $previousSession.ProcessId -ErrorAction SilentlyContinue
    if($previousProcess -and $previousProcess.ProcessName -eq '7DaysToDie' -and [Math]::Abs(($previousProcess.StartTime-([datetime]$previousSession.Started)).TotalSeconds) -lt 1){throw 'The previous isolated QA process is still running; stop that exact process before starting another.'}
}
$qaRoot=Join-Path $qaBase ('run-'+[guid]::NewGuid().ToString('N'))
$userData=Join-Path $qaRoot 'UserData'
if($ResumeCrashDirectory){
    New-Item -ItemType Directory -Force $userData | Out-Null
    Copy-Item -LiteralPath (Join-Path $resumeRoot 'UserData/Saves') -Destination $userData -Recurse
    $oldReport=Join-Path $userData 'Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt'
    if(Test-Path -LiteralPath $oldReport){Remove-Item -LiteralPath $oldReport}
}
$modDirectory=Join-Path $userData 'Mods/98-CargoDroneQA'
if(-not $RuntimeDll){$RuntimeDll=Join-Path $modRoot '.local-tests/CargoDrones/YF.Automation.dll'}
if(-not(Test-Path -LiteralPath $RuntimeDll)){throw 'Build the staged runtime before starting native QA.'}
New-Item -ItemType Directory -Force $modDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $modRoot '0_TFP_Harmony') -Destination (Join-Path $userData 'Mods') -Recurse -Force
$automationDirectory=Join-Path $userData 'Mods/97-AutomationWorkshop'
New-Item -ItemType Directory -Force $automationDirectory | Out-Null
foreach($item in @('Config','Resources','ModInfo.xml')){Copy-Item -LiteralPath (Join-Path $modRoot ('97-AutomationWorkshop/'+$item)) -Destination $automationDirectory -Recurse -Force}
Copy-Item -LiteralPath $RuntimeDll -Destination (Join-Path $automationDirectory 'YF.Automation.dll') -Force
'<xml><Name value="CargoDroneQA"/><DisplayName value="Isolated Cargo Drone Native QA"/><Version value="0.1.0"/></xml>' | Set-Content (Join-Path $modDirectory 'ModInfo.xml')
$managed=Join-Path $GameRoot '7DaysToDie_Data/Managed'
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll') | ForEach-Object { Join-Path $PSHOME $_ }
$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class CargoQACompiler {
 public static void Build(string source,string[] refs,string output) {
  var compilation=CSharpCompilation.Create("CargoDrone.NativeQA",new[]{CSharpSyntaxTree.ParseText(File.ReadAllText(source))},refs.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  using(var stream=new MemoryStream()){var result=compilation.Emit(stream);foreach(var d in result.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);if(!result.Success)throw new Exception("Native QA compilation failed");File.WriteAllBytes(output,stream.ToArray());}
 }
}
'@
$refs=@(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName)+@((Join-Path $modRoot '0_TFP_Harmony/0Harmony.dll'),[IO.Path]::GetFullPath($RuntimeDll))
[CargoQACompiler]::Build((Join-Path $PSScriptRoot 'CargoDrones/CargoDroneNativeQA.cs'),$refs,(Join-Path $modDirectory 'CargoDrone.NativeQA.dll'))
[xml]$config=Get-Content -LiteralPath (Join-Path $GameRoot 'serverconfig.xml')
$values=@{GameWorld='Navezgane';GameName='CargoDroneQA_Isolated';ServerName='Cargo Drone Isolated QA';ServerPort='27983';ServerVisibility='0';ServerPassword='CargoQALocalOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$userData;ServerMaxPlayerCount='1';}
foreach($key in $values.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(-not $node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$values[$key])}
$configPath=Join-Path $qaRoot 'serverconfig.xml';$config.Save($configPath)
$log=Join-Path $qaRoot ('game-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$qaArguments=@('-batchmode','-dedicated','-crossplatform=None','-serverplatforms=Steam,LAN',('-configfile="'+$configPath+'"'),('-UserDataFolder="'+$userData+'"'),'-yfCargoDroneNativeQA','-logfile',('"'+$log+'"'))
# The existing forestry mod requires native shader materials during block loading.
# Use a hidden graphics-capable batch process by default, not NullGfxDevice.
if($Headless){$qaArguments+='-nographics'}
if($InventoryOnly){$qaArguments+='-yfCargoInventoryOnly'}
if($WorldScheduler){$qaArguments+='-yfCargoInventoryOnly';$qaArguments+='-yfCargoWorldScheduler'}
$qaArguments+=('-yfCargoSourceBlock='+$SourceBlock)
if($BindingAudit){if(-not $WorldScheduler){throw 'Binding audit requires -WorldScheduler.'};$qaArguments+='-yfCargoBindingAudit'}
if($CrashAfterNativeSave){$qaArguments+='-yfCargoInventoryOnly';$qaArguments+='-yfCargoCrashAfterNativeSave'}
if($ResumeCrashDirectory){$qaArguments+='-yfCargoInventoryOnly';$qaArguments+='-yfCargoResumeCrash'}
if($ReadbackDirectory){$qaArguments+=('-yfCargoReadback="'+$readbackPath+'"')}
$process=Start-Process -FilePath (Join-Path $GameRoot '7DaysToDie.exe') -WorkingDirectory $GameRoot -WindowStyle Hidden -PassThru -ArgumentList $qaArguments
[pscustomobject]@{ProcessId=$process.Id;Started=$process.StartTime.ToString('o');Log=$log;Report=(Join-Path $userData 'Saves/Navezgane/CargoDroneQA_Isolated/cargo-native-report.txt');QaRoot=$qaRoot;CrashAfterNativeSave=[bool]$CrashAfterNativeSave} | ConvertTo-Json | Set-Content (Join-Path $qaBase 'session.json')
Get-Content (Join-Path $qaBase 'session.json')
