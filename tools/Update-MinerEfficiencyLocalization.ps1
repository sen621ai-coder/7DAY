$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$path=Join-Path $root '98-AECxProjectZ_Tweaks/Config/Localization.csv'
# Preserve the existing mixed-encoding file byte-for-byte while appending new keys.
$columns=(Get-Content -LiteralPath $path -TotalCount 1).Split(',')
$values=@{
 yfApiaryForagingDesc='每批基础产出5份蜂蜜，育蜂箱翻倍至10份。有足够原料时每批消耗10份菊花、黄花或棉花，周期48游戏小时；原料不足时自动从环境采蜜，不消耗原料，速度减半（96游戏小时）。采蜜器使上述周期减半。6个产出格，每格存一批。'
 yfAutoMinerEfficientDesc='基础产出：每500汽油获得1包6000资源，折合每1000汽油12000资源。3列2行共6格；未装资源打包器每格3包，装入后每格6包。配件增产与加速效果保留。'
 yfAutoForestryEfficientDesc='无需树苗的汽油自动林场。默认燃料倍率下每批消耗1000汽油，普通产出3包，装资源打包器产出6包；每包12000木头。3列2行共6格，每格最多3包，装打包器后每格最多6包。全满暂停，取走后继续；备件加速效果保留。'
}
$existing=Get-Content -Raw -LiteralPath $path
$rows=foreach($key in $values.Keys){
 if($existing.Contains('"'+$key+'"')){continue}
 $row=[ordered]@{}; foreach($column in $columns){$row[$column]=''}
 $row.Key=$key; $row.File='blocks'; $row.Type='Block'; $row.NoTranslate='false'
 $row.english=$values[$key]; $row.schinese=$values[$key]
 [pscustomobject]$row
}
if($rows){$csv=@($rows|ConvertTo-Csv -NoTypeInformation); [IO.File]::AppendAllText($path,"`r`n"+($csv[1..($csv.Count-1)] -join "`r`n")+"`r`n",[Text.UTF8Encoding]::new($false))}
