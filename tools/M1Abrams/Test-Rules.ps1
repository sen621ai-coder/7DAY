$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$rules=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1Rules.cs" -Raw
$tests=@'
using System;using PZAEC.M1;
public static class M1RuleTests {
 static int n;static void Check(bool x,string s){n++;if(!x)throw new Exception(s);}
 public static void Run(){
  Check(Rules.Operator(0,false),"solo driver has cannon");Check(!Rules.Operator(0,true),"gunner takes priority");Check(Rules.Operator(1,true),"gunner has cannon");Check(!Rules.Operator(-1,false),"unseated actor denied");Check(!Rules.Operator(2,true),"unsupported seat denied");
  Check(Rules.Recoil(-1)==0&&Rules.Recoil(0)==0,"pre-shot neutral");Check(Math.Abs(Rules.Recoil(.1f)-.25f)<1e-6,"100ms full recoil");Check(Rules.Recoil(.13f)==.25f,"hold");Check(Math.Abs(Rules.Recoil(.6f))<1e-6,"600ms restored");
  for(int i=0;i<=900;i++){float a=i/1000f;float d=Rules.Recoil(a);Check(d>=0&&d<=.250001f,"bounded recoil "+i);if(i>0&&i<=100)Check(d>=Rules.Recoil((i-1)/1000f)-1e-6,"monotonic rearward");if(i>160)Check(d<=Rules.Recoil((i-1)/1000f)+1e-6,"monotonic return");}
  Check(Rules.Recoil(100000)==0,"no accumulated drift");Check(Rules.MinPitch(0)==-6,"front depression");Check(Rules.MinPitch(180)==4&&Rules.MinPitch(-180)==4,"rear deck clearance");Check(Rules.MinPitch(120)>-6&&Rules.MinPitch(120)<4,"continuous rear transition");
  var t=new Rules.Trigger();Check(t.Accept(7,1,true,0)&&t.Active(.2f),"live trigger");Check(!t.Active(.36f),"network lease expiry");Check(!t.Accept(7,1,true,.4f),"duplicate rejected");Check(t.Accept(7,3,false,.4f)&&!t.Active(.4f),"release wins");Check(!t.Accept(7,2,true,.45f)&&!t.Active(.45f),"late fire cannot undo release");Check(t.Accept(8,1,true,.5f),"new operator starts own sequence");t.Stop();Check(!t.Active(.5f),"seat loss stops firing");
  Check(!Rules.Finite(float.NaN)&&!Rules.Finite(float.PositiveInfinity)&&Rules.Finite(1),"nonfinite guard");
  int[] hp={1000000,1500000,2200000,3200000},ap={4000000,8000000,16000000,28000000},he={400000,650000,1000000,1500000};
  for(int i=0;i<4;i++){
   Check(Rules.Specs[i].Health==hp[i]&&Rules.Specs[i].AP==ap[i]&&Rules.Specs[i].HE==he[i],"tier table "+i);
   string name="vehicleM1Abrams"+(i==0?"":"T"+(16+i));Check(Rules.Index(name)==i&&Rules.Index(name+"Placeable")==i,"exact tier aliases");
   for(int r=0;r<4;r++){Check(Rules.Armor(i,r,false)<.81f,"armor never immune");Check(Rules.ProtectedDamage(1000000,i,r,true)>Rules.ProtectedDamage(1000000,i,r,false),"acid weakens armor");Check(Rules.ProtectedDamage(0,i,r,false)==0,"zero does not become damage");}
  }
  Check(Rules.Index("vehicleTruck4x4")==-1&&Rules.Index("vehicleM1AbramsT20")==-1,"unrelated vehicle excluded");
  Check(Rules.HEFalloff(2)==1&&Rules.HEFalloff(5)==.5f&&Rules.HEFalloff(8)==0&&Rules.HEFalloff(9)==0,"HE blast falloff");
  Console.WriteLine("PASS "+n+" actual M1 rule/curve/lease checks");
 }
}
'@
Add-Type -TypeDefinition ($rules+$tests.Replace('using System;using PZAEC.M1;','').Replace('Rules.','PZAEC.M1.Rules.'))
[M1RuleTests]::Run()
