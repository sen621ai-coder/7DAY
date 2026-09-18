$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$motion=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/ForestryMotion.cs') -Raw
Add-Type -TypeDefinition ($motion+@'
namespace AECT16RuntimeFix {
 public static class ForestryMotionChecks {
  static void Check(bool b,string name){if(!b)throw new System.Exception(name);}
  public static void Run(){
   float cycle=ForestryMotion.Cycle;
   for(int tick=0;tick<=20000;tick++)for(int board=0;board<2;board++){
    float travel=tick*cycle/20000f;
    var raw=ForestryMotion.Slice(travel,board,0);
    var left=ForestryMotion.Slice(travel,board,1);var right=ForestryMotion.Slice(travel,board,2);
    Check(System.Math.Abs(left.length-right.length)<.0001,"Cut halves must have equal lengths");
    Check(raw.length+left.length<=ForestryMotion.BoardLength+.0001,"Cut conserves board length");
    foreach(var s in new[]{raw,left,right}){
     Check(s.length>=0&&s.length<=ForestryMotion.BoardLength+.0001,"Segment length bounds");
     if(s.length>.0001)Check(s.x-s.length*.5>=ForestryMotion.Entry-.0001&&s.x+s.length*.5<=ForestryMotion.Exit+.0001,"Visible timber outside deck");
    }
    if(raw.length>.0001)Check(raw.x+raw.length*.5<=ForestryMotion.Cut+.0001,"Uncut wood crossed blade");
    if(left.length>.0001){
     Check(left.x-left.length*.5>=ForestryMotion.Cut-.0001,"Split wood before cut");
     Check(right.z-left.z-.095f>=.035f,"Cut halves intersect blade thickness");
    }
    var periodic=ForestryMotion.Slice(travel+cycle,board,0);
    Check(System.Math.Abs(raw.length-periodic.length)<.00001,"Cycle not periodic");
   }
   // At wrap, this board is hidden inside a cover at both ends, never visibly repositioned.
   for(int p=0;p<3;p++){
    Check(ForestryMotion.Slice(0,0,p).length==0,"Cycle begins empty");
    Check(ForestryMotion.Slice(cycle-.00001f,0,p).length<.00002,"Cycle ends empty");
   }
   float metres=.1f;
   Check(System.Math.Abs(-ForestryMotion.RollerDegrees(metres)*System.Math.PI/180*ForestryMotion.RollerRadius-metres)<.000001,"Roller surface must advance with wood");
   Check(System.Math.Abs(ForestryMotion.DriveDegrees(720)*.11f-720*.14f)<.0001,"Belt surface speed mismatch");
   float speed=0;
   for(int i=0;i<100;i++){float next=ForestryMotion.Approach(speed,1.35f,.02f);Check(next>=speed&&next<=1.35f,"Acceleration overshoot");speed=next;}
   Check(speed==1.35f,"Never reached running speed");
   for(int i=0;i<100;i++){float next=ForestryMotion.Approach(speed,0,.02f);Check(next<=speed&&next>=0,"Braking overshoot");speed=next;}
   Check(speed==0,"Never stopped");
  }
 }
}
'@)
[AECT16RuntimeFix.ForestryMotionChecks]::Run()
# Construction regressions: dormant prefab children must participate in batching.
$wood=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/AutoForestryWoodworking.cs') -Raw
if(!$wood.Contains('group.GetComponentsInChildren<MeshRenderer>(true)')){throw 'Inactive woodworking geometry omitted'}
$machine=Get-Content (Join-Path $root '99-AEC_T16_RuntimeFix/Source/AutoForestryMachinery.cs') -Raw
$match=[regex]::Match($machine,'foreach\(float x in new\[\]\{([^}]+)\}\)\s*Cylinder\(p,"FeedRoller"')
if(!$match.Success){throw 'Cannot inspect roller coordinates'}
foreach($coordinate in $match.Groups[1].Value.Split(',')){
    $x=[float]::Parse($coordinate.Trim().TrimEnd('f'),[cultureinfo]::InvariantCulture)
    if([Math]::Abs($x-[AECT16RuntimeFix.ForestryMotion]::Cut)-.065 -le .34){throw 'Roller enters blade envelope'}
}
Write-Output 'PASS: 40,002 cycle samples; cut bounds/conservation; hidden reset; roller direction; pulley ratio; smooth start/stop; inactive batching; blade clearance.'
