#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot);$game=Split-Path $root
if(Get-Process 7DaysToDie,7DaysToDieServer -ErrorAction SilentlyContinue){throw 'A game/QA process is already running; leave it untouched.'}
$qa=Join-Path $root ('.local-tests/M1NativeQA/run-'+[guid]::NewGuid().ToString('N'));$data=Join-Path $qa 'UserData';$mods=Join-Path $data 'Mods';$harness=Join-Path $mods 'ZZZ-M1NativeQA'
New-Item -ItemType Directory -Force $harness | Out-Null
Copy-Item -LiteralPath (Join-Path $root '0_TFP_Harmony') -Destination $mods -Recurse
# The game ALSO scans its installation Mods folder. Shadow other installed mods
# with empty same-name manifests, entirely inside this disposable UserData.
foreach($dir in Get-ChildItem -LiteralPath $root -Directory){
 if($dir.Name -in @('0_TFP_Harmony','ZZ-PZAEC_M1Abrams') -or !(Test-Path -LiteralPath (Join-Path $dir.FullName 'ModInfo.xml'))){continue}
 [xml]$manifest=Get-Content -LiteralPath (Join-Path $dir.FullName 'ModInfo.xml');if(!$manifest.xml.Name.value){continue}
 $shadow=Join-Path $mods $dir.Name;New-Item -ItemType Directory -Force $shadow|Out-Null
 Copy-Item -LiteralPath (Join-Path $dir.FullName 'ModInfo.xml') -Destination $shadow
}
$target=Join-Path $mods 'ZZ-PZAEC_M1Abrams';New-Item -ItemType Directory $target | Out-Null
foreach($name in @('Config','Resources','ItemIcons','UIAtlases','ModInfo.xml','PZAEC.M1Abrams.dll')){Copy-Item -LiteralPath (Join-Path $root "ZZ-PZAEC_M1Abrams/$name") -Destination $target -Recurse}
# This small isolated world lacks ProjectZ/AEC materials. Supply test-only item
# definitions for those recipe references; live recipes and runtime remain exact.
[xml]$base=Get-Content -LiteralPath (Join-Path $game 'Data/Config/items.xml');[xml]$items=Get-Content -LiteralPath (Join-Path $target 'Config/items.xml');[xml]$recipes=Get-Content -LiteralPath (Join-Path $target 'Config/recipes.xml')
$known=@{};foreach($item in $base.items.item){$known[$item.name]=$true};foreach($item in $items.configs.append.item){$known[$item.name]=$true}
$missing=@($recipes.SelectNodes('//ingredient')|ForEach-Object {$_.name}|Sort-Object -Unique|Where-Object {!$known.ContainsKey($_)})
$stubs=Join-Path $mods '01-M1QAMaterialStubs';New-Item -ItemType Directory -Force (Join-Path $stubs 'Config')|Out-Null
'<xml><Name value="M1QAMaterialStubs"/><DisplayName value="M1 QA material stubs"/><Description value="Isolated test only"/><Author value="QA"/><Version value="0.1.0"/></xml>'|Set-Content (Join-Path $stubs 'ModInfo.xml')
('<configs><append xpath="/items">'+(($missing|ForEach-Object {'<item name="'+$_+'"><property name="Extends" value="resourceForgedSteel"/></item>'})-join '')+'</append></configs>')|Set-Content (Join-Path $stubs 'Config/items.xml')
'<xml><Name value="M1NativeQA"/><DisplayName value="M1 native QA"/><Description value="Isolated test only"/><Author value="QA"/><Version value="0.1.0"/></xml>'|Set-Content (Join-Path $harness 'ModInfo.xml')
$refs=@(Get-ChildItem (Join-Path $game '7DaysToDie_Data/Managed') -Filter '*.dll'|ForEach-Object FullName)+@((Join-Path $root '0_TFP_Harmony/0Harmony.dll'),(Join-Path $target 'PZAEC.M1Abrams.dll'))
$compilerRefs=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object {Join-Path $PSHOME $_};$frameworkRefs=Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll'|ForEach-Object FullName
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($compilerRefs+$frameworkRefs) -TypeDefinition @'
using System;using System.IO;using System.Linq;using Microsoft.CodeAnalysis;using Microsoft.CodeAnalysis.CSharp;
public static class M1NativeCompiler {
 public static void Build(string source,string[] refs,string output){var c=CSharpCompilation.Create("M1.NativeQA",new[]{CSharpSyntaxTree.ParseText(File.ReadAllText(source))},refs.Select(p=>MetadataReference.CreateFromFile(p)),new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));using(var s=new MemoryStream()){var r=c.Emit(s);foreach(var d in r.Diagnostics.Where(d=>d.Severity>=DiagnosticSeverity.Warning))Console.WriteLine(d);if(!r.Success)throw new Exception("QA compile failed");File.WriteAllBytes(output,s.ToArray());}}
}
'@
[M1NativeCompiler]::Build((Join-Path $PSScriptRoot 'NativeQA.cs'),$refs,(Join-Path $harness 'M1.NativeQA.dll'))
[xml]$config=Get-Content -LiteralPath (Join-Path $game 'serverconfig.xml')
$values=@{GameWorld='Navezgane';GameName='M1QA_Isolated';ServerName='M1 Isolated QA';ServerPort='27985';ServerVisibility='0';ServerPassword='M1QALocalOnly';TelnetEnabled='false';WebDashboardEnabled='false';TerminalWindowEnabled='false';EACEnabled='false';UserDataFolder=$data;ServerMaxPlayerCount='1'}
foreach($key in $values.Keys){$node=$config.SelectSingleNode("/ServerSettings/property[@name='$key']");if(!$node){$node=$config.CreateElement('property');$node.SetAttribute('name',$key);$config.DocumentElement.AppendChild($node)|Out-Null};$node.SetAttribute('value',$values[$key])}
$cfg=Join-Path $qa 'serverconfig.xml';$config.Save($cfg);$log=Join-Path $qa 'game.log'
$arguments=@('-batchmode','-dedicated','-crossplatform=None','-serverplatforms=Steam,LAN',('-configfile="'+$cfg+'"'),('-UserDataFolder="'+$data+'"'),'-m1NativeQA','-logfile',('"'+$log+'"'))
$arguments+=('-m1ApacheDll="'+(Join-Path $root '99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll')+'"')
$process=Start-Process -FilePath (Join-Path $game '7DaysToDie.exe') -WorkingDirectory $game -WindowStyle Hidden -PassThru -ArgumentList $arguments
$session=[pscustomobject]@{ProcessId=$process.Id;Started=$process.StartTime.ToString('o');Log=$log;Report=(Join-Path $data 'Saves/Navezgane/M1QA_Isolated/m1-native-report.txt');QaRoot=$qa}
$session|ConvertTo-Json|Set-Content (Join-Path $root '.local-tests/M1NativeQA/session.json');$session|ConvertTo-Json
