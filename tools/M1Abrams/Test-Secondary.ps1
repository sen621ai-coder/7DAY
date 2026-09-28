$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$rules=Get-Content "$root/ZZ-PZAEC_M1Abrams/Source/M1SecondaryRules.cs" -Raw
$tests=@'
namespace PZAEC.M1 {
public static class SecondaryTests {
 static int n;static void Check(bool ok,string label){n++;if(!ok)throw new System.Exception(label);}
 static void Near(float a,float b,string label)=>Check(System.Math.Abs(a-b)<.002f,label);
 public static void Run(){
  float[] distances={0,120,160,200,201};float[] expected={1,1,.8f,.6f,0};for(int i=0;i<5;i++)Near(SecondaryRules.Falloff(distances[i]),expected[i],"range boundary "+distances[i]);
  for(int seat=-1;seat<3;seat++)for(int mode=0;mode<4;mode++)foreach(bool gunner in new[]{false,true})Check(SecondaryRules.Allowed(seat,gunner,(byte)mode)==(seat==0?(mode<3&&(!gunner||mode==1)):seat==1?(mode==0||mode==2):false),"seat permissions");
  var g=new SecondaryRules.Gun{Rounds=100};
  for(int shot=0;shot<50;shot++){float now=shot*.101f;g.Tick(now,.101f);Check(g.CanShoot(now),"cold gun legal shot "+shot);g.Fired(now);Check(!g.CanShoot(now),"no duplicate same-frame shot");}
  Check(g.Rounds==50&&g.Overheated,"50 rounds overheat without deleting belt remainder");Near(g.Heat,100,"full heat");
  float last=g.LastShot;g.Tick(last+.4f,.4f);Near(g.Heat,100,"cooling delay");g.Tick(last+.6f,.2f);Near(g.Heat,98,"only cool time after delay boundary");g.Tick(last+3.49f,2.89f);Check(g.Overheated,"no early overheat unlock");g.Tick(last+3.51f,.02f);Check(!g.Overheated,"unlock below forty");
  g=new SecondaryRules.Gun{Loading=true,ReloadUntil=4};g.Tick(3.99f,3.99f);Check(g.Rounds==0&&!g.CanShoot(3.99f),"no rounds during belt load");g.Tick(4,.01f);Check(g.Rounds==100&&!g.Loading,"belt gives exactly 100 rounds");g.Rounds=63;g.Tick(5,1);Check(g.Rounds==63,"completed load cannot refill later");
  Near(SecondaryRules.Spread(0,true),.2625f,"stabilizer cold spread");Near(SecondaryRules.Spread(100,true),.9f,"stabilizer hot spread");
  Check(SecondaryRules.Tube(12,0,0,1)==1,"fallback to other ready tube");Check(SecondaryRules.Tube(12,13,0,2)==-1,"both cooling");Check(SecondaryRules.Tube(12,13,1,12)==0,"ready tube not blocked by preferred tube");
  Check(SecondaryRules.Remaining(12,1)==11000&&SecondaryRules.Remaining(1,12)==0,"remaining time persistence");
  var input=new SecondaryRules.InputLease();Check(input.Accept(10,1,true,true,0,2),"first input");Check(!input.Firing(.3f),"entering while held requires release");Check(!input.Accept(10,1,false,false,.1f,2),"duplicate packet rejected");Check(!input.Accept(10,0,false,false,.1f,2),"old packet rejected");
  input.Accept(10,2,false,true,.2f,2);input.Accept(10,3,true,true,.3f,2);Check(input.Firing(.3f),"fresh press after switch delay");Check(!input.Firing(.66f),"timeout stops fire");input.Stop();input.Accept(10,4,true,true,.7f,2);Check(!input.Firing(.7f),"UI/permission loss needs release");
  input.Accept(10,5,false,true,.8f,2);input.Accept(10,6,true,true,.9f,1);Check(!input.Firing(1.2f),"mode switch while held cannot fire");input.Accept(11,1,true,true,1.3f,1);Check(!input.Firing(1.6f),"seat handoff cannot inherit trigger");
  for(int tier=0;tier<4;tier++){Check(SecondaryRules.AADamage[tier]>SecondaryRules.MGDamage[tier]*10,"missile burst dominates MG burst");Check(SecondaryRules.AADamage[tier]*2>0,"dual salvo avoids integer overflow");}
  System.Console.WriteLine("PASS "+n+" secondary heat, ammo, range, input authority and timer checks");
 }
}}
'@
Add-Type -TypeDefinition ($rules+$tests)
[PZAEC.M1.SecondaryTests]::Run()
