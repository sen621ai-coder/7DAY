#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Execute the production fusion implementation with isolated inventory doubles.
# This does not simulate the game's native UI/network/serialization.
$stubs = @'
public class ItemClass {
    public string Name = "gunPZAECHorizonNeedleT16";
    public string GetItemName() { return Name; }
}
public class ItemValue {
    public int type = 1, Meta, Quality = 1;
    public float UseTimes;
    public ItemClass ItemClass = new ItemClass();
    public ItemValue[] Modifications = new ItemValue[1], CosmeticMods = new ItemValue[1];
    private System.Collections.Generic.Dictionary<string, object> data = new System.Collections.Generic.Dictionary<string, object>();
    public bool IsEmpty() { return type == 0; }
    public float MaxUseTimes { get { return (float)(100 * System.Math.Pow(1.05, AECT16RuntimeFix.EquipmentFusion.Rank(this))); } }
    public bool TryGetMetadata<T>(string key, out T value) { object raw; if(data.TryGetValue(key,out raw) && raw is T) {value=(T)raw; return true;} value=default(T); return false; }
    public void SetMetadata(string key, object value) { data[key]=value; }
    public ItemValue Clone() { var v=(ItemValue)MemberwiseClone(); v.data=new System.Collections.Generic.Dictionary<string,object>(data); v.Modifications=(ItemValue[])Modifications.Clone(); v.CosmeticMods=(ItemValue[])CosmeticMods.Clone(); return v; }
}
public class ItemStack {
    public ItemValue itemValue; public int count;
    public ItemStack(ItemValue v, int c) {itemValue=v;count=c;}
    public static ItemStack Empty {get {return new ItemStack(new ItemValue {type=0},0);}}
    public bool IsEmpty() {return count==0 || itemValue.IsEmpty();}
    public ItemStack Clone() {return new ItemStack(itemValue.Clone(),count);}
}
'@
Add-Type -TypeDefinition ((Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/EquipmentFusion.cs') -Raw) + $stubs)
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Near([double]$actual,[double]$expected,$message) { Assert ([Math]::Abs($actual-$expected) -lt 0.00001) $message }
function New-Gear([double]$rank) {
    $v=[ItemValue]::new(); [AECT16RuntimeFix.EquipmentFusion]::SetRank($v,$rank)
    return [ItemStack]::new($v,1)
}
$f=[AECT16RuntimeFix.EquipmentFusion]
$a=New-Gear 0; $b=New-Gear 0; $out=$null
Assert ($f::TryCreate($a,$b,[ref]$out)) 'First fusion rejected'
Near ($f::Rank($out.itemValue)) 1 'First fusion rank'
$running=$out
for($i=0;$i -lt 19;$i++) {
    $next=$null; Assert ($f::TryCreate($running,(New-Gear 0),[ref]$next)) 'Continuous unfused donor rejected'
    $running=$next
}
Near ([Math]::Pow(1.05,$f::Rank($running.itemValue))) 2 'Twenty donors must add 100% base strength'
$a=New-Gear ([Math]::Log(10)/[Math]::Log(1.05)); $b=New-Gear ([Math]::Log(6)/[Math]::Log(1.05))
$a.itemValue.Quality=6; $a.itemValue.UseTimes=200; $a.itemValue.Modifications[0]=[ItemValue]::new()
foreach($reverse in @($false,$true)) {
    $out=$null
    if($reverse) {$ok=$f::TryCreate($b,$a,[ref]$out)} else {$ok=$f::TryCreate($a,$b,[ref]$out)}
    Assert $ok 'Mixed fusion rejected'
    Near ([Math]::Pow(1.05,$f::Rank($out.itemValue))*100) 1030 '1000 + 600 * 5%'
    Assert ($out.itemValue.Quality -eq 6 -and $null -ne $out.itemValue.Modifications[0]) 'Higher input identity lost'
    Near ($out.itemValue.UseTimes/$out.itemValue.MaxUseTimes) .2 'Wear proportion changed'
    $encoded=''; Assert ($out.itemValue.TryGetMetadata[string]($f::RankKey,[ref]$encoded)) 'Fractional metadata missing'
    $restored=[ItemValue]::new(); $restored.SetMetadata($f::RankKey,$encoded)
    Near ($f::Rank($restored)) ($f::Rank($out.itemValue)) 'Round-trip rank lost'
}
$b.itemValue.Modifications[0]=[ItemValue]::new()
Assert ($null -ne $f::Validate($b,$a)) 'Lower input attachment loss allowed'
$legacy=[ItemValue]::new(); $legacy.SetMetadata($f::RankKey,10)
Near ($f::Rank($legacy)) 10 'Legacy integer metadata lost'
foreach($bad in @('NaN','Infinity','-1','1001','invalid')) {$legacy.SetMetadata($f::RankKey,$bad); Near ($f::Rank($legacy)) 0 'Invalid metadata accepted'}
$a=New-Gear 2; $b=New-Gear 2; $a.itemValue.Quality=6
Assert ($f::TryCreate($a,$b,[ref]$out)) 'Equal ranks rejected'
Near ($f::Rank($out.itemValue)) 3 'Equal ranks must retain original formula'
Assert ($out.itemValue.Quality -eq 6) 'Equal rank first-slot rule lost'
$b.itemValue.type=2; Assert ($null -ne $f::Validate($a,$b)) 'Different items allowed'
Assert ($null -ne $f::Validate((New-Gear 1000),(New-Gear 1000))) 'Cap not enforced'
'PASS: production fusion math, 20 unfused donors, slot reversal, attachments, wear, legacy and fractional metadata, invalid inputs, cap.'
