$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$managed=Join-Path (Split-Path $root) '7DaysToDie_Data/Managed'
$all=@(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object FullName)+@(Get-ChildItem "$root/04-AEC-ENDGAME_OVERHAUL" -Filter '*.dll' | ForEach-Object FullName)
$refs=@("$root/0_TFP_Harmony/0Harmony.dll","$root/0_TFP_Harmony/Mono.Cecil.dll","$root/ZZZ-PZAEC_SpawnSafety/PZAEC.SpawnSafety.dll","$root/99-AEC_T16_RuntimeFix/AEC.T16.RuntimeFix.dll","$managed/Assembly-CSharp.dll","$managed/UnityEngine.CoreModule.dll")
foreach($p in ($all+$refs)){try{[void][Reflection.Assembly]::LoadFrom($p)}catch{}}
$framework=@(Get-ChildItem "$PSHOME/ref" -Filter '*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies ($refs+$framework) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using PZAEC.SpawnSafety;
public static class SpawnNativeTests {
 public static bool Permit;public static int Registered;
 public static bool Gate(Entity ignored)=>Permit;
 public static void Register(){Registered++;}
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
 static MethodInfo Target(string type,string method){var t=AccessTools.TypeByName(type);Check(t!=null,type);var m=AccessTools.Method(t,method);Check(m!=null,method);return m;}
 static void Bind(MethodInfo original,string prefix){
  var patch=AccessTools.Method(typeof(Hooks),prefix);var args=original.GetParameters();
  foreach(var p in patch.GetParameters()){
   var type=p.ParameterType.IsByRef?p.ParameterType.GetElementType():p.ParameterType;
   if(p.Name=="__state"||p.Name=="__args"||p.Name=="__originalMethod")continue;
   if(p.Name=="__instance"){Check(!original.IsStatic&&type.IsAssignableFrom(original.DeclaringType),prefix+" instance");continue;}
   if(p.Name=="__result"){Check(type==original.ReturnType,prefix+" result");continue;}
   if(p.Name.StartsWith("___")){var f=AccessTools.Field(original.DeclaringType,p.Name.Substring(3));Check(f!=null&&type==f.FieldType,prefix+" field "+p.Name);continue;}
   int index;Check(p.Name.StartsWith("__")&&int.TryParse(p.Name.Substring(2),out index),prefix+" argument syntax");index=int.Parse(p.Name.Substring(2));
   Check(index<args.Length,prefix+" argument index");var actual=args[index].ParameterType;if(actual.IsByRef)actual=actual.GetElementType();Check(type==actual,prefix+" argument "+index);
  }
 }
 static bool Calls(CodeInstruction c,string name)=>c.operand is MethodInfo m&&m.Name==name;
 static ILGenerator Generator()=>new DynamicMethod("audit",typeof(void),Type.EmptyTypes).GetILGenerator();
 static List<CodeInstruction> Read(MethodInfo m,ILGenerator il){
  var locals=m.GetMethodBody().LocalVariables.Select(v=>il.DeclareLocal(v.LocalType,v.IsPinned)).ToArray();
  var opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
  using(var module=Mono.Cecil.ModuleDefinition.ReadModule(m.Module.FullyQualifiedName)){
   var body=((Mono.Cecil.MethodDefinition)module.LookupToken(m.MetadataToken)).Body;
   var labels=body.Instructions.ToDictionary(i=>i,i=>il.DefineLabel());var result=new List<CodeInstruction>();
   foreach(var i in body.Instructions){
    object operand=i.Operand;
    if(operand is Mono.Cecil.Cil.Instruction b)operand=labels[b];
    else if(operand is Mono.Cecil.Cil.Instruction[] bs)operand=bs.Select(x=>labels[x]).ToArray();
    else if(operand is Mono.Cecil.Cil.VariableDefinition v)operand=locals[v.Index];
    else if(operand is Mono.Cecil.ParameterDefinition p)operand=p.Index+(m.IsStatic?0:1);
    else if(operand is Mono.Cecil.MemberReference member)operand=m.Module.ResolveMember(member.MetadataToken.ToInt32());
    var code=new CodeInstruction(opcodes[i.OpCode.Value],operand);code.labels.Add(labels[i]);
    foreach(var e in body.ExceptionHandlers){
     if(e.TryStart==i)code.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
     if(e.HandlerEnd==i)code.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
    }
    result.Add(code);
   }
   return result;
  }
 }
 static void Labels(List<CodeInstruction> codes){
  var labels=new HashSet<Label>(codes.SelectMany(c=>c.labels));
  foreach(var c in codes){if(c.operand is Label l)Check(labels.Contains(l),"branch target survived");if(c.operand is Label[] ls)foreach(var v in ls)Check(labels.Contains(v),"switch target survived");}
 }
 static void Factory(MethodInfo method,string prefix){
  Bind(method,prefix);var il=Generator();var before=Read(method,il);
  var codes=Hooks.FactoryTranspiler(before,il,method).ToList();Labels(codes);
  int create=codes.FindIndex(c=>Calls(c,"CreateEntity"));int world=codes.FindIndex(c=>Calls(c,"SpawnEntityInWorld"));
  Check(create>=0&&world>create,"factory before registration");
  if(method.Name=="SpawnVanillaPack"){
   Check(Calls(codes[create+1],"FilterMega"),"pack filter directly after factory");
   Check(codes.Skip(create+2).Take(world-create-2).Any(c=>c.opcode==OpCodes.Brtrue||c.opcode==OpCodes.Brtrue_S),"native null skip remains before count");
  }else{
   Check(codes[create+1].opcode==OpCodes.Dup&&Calls(codes[create+2],"AcceptCurrent"),"entity retained for acceptance");
   Check(codes[create+4].opcode==OpCodes.Pop&&codes[create+6].opcode==OpCodes.Ret,"failed entity never reaches native registration");
   Check(codes[create+5].opcode==(method.ReturnType==typeof(bool)?OpCodes.Ldc_I4_0:OpCodes.Ldnull),"native failure contract");
   Check(codes[create+7].labels.Contains((Label)codes[create+3].operand),"success resumes with entity on stack");
   // Execute the actual injected stack/branch sequence, with only acceptance and
   // world registration replaced by probes. Neither path calls Unity internals.
   var dm=new DynamicMethod("guardflow",method.ReturnType,Type.EmptyTypes);var emit=dm.GetILGenerator();
   var resume=emit.DefineLabel();emit.Emit(OpCodes.Ldnull);
   for(int j=create+1;j<=create+6;j++){
    var code=codes[j];
    if(Calls(code,"AcceptCurrent"))emit.Emit(OpCodes.Call,typeof(SpawnNativeTests).GetMethod("Gate"));
    else if(code.operand is Label)emit.Emit(code.opcode,resume);
    else emit.Emit(code.opcode);
   }
   emit.MarkLabel(resume);emit.Emit(OpCodes.Call,typeof(SpawnNativeTests).GetMethod("Register"));
   if(method.ReturnType==typeof(bool)){emit.Emit(OpCodes.Pop);emit.Emit(OpCodes.Ldc_I4_1);}emit.Emit(OpCodes.Ret);
   Permit=false;Registered=0;var failed=dm.Invoke(null,null);Check(Registered==0,"failed guard executes no registration/counter path");
   Check(method.ReturnType==typeof(bool)?failed.Equals(false):failed==null,"executed native failure value");
   Permit=true;dm.Invoke(null,null);Check(Registered==1,"successful guard registers exactly once");
  }
 }
 public static string Run(){
  const string aec="AeclipseCustomZombieSpawner.SpawnDebugPatcher";
  var direct=Target(aec,"TrySpawnEntityByClassIdAtPosition");Bind(direct,"DirectPrefix");
  Check(direct.IsStatic&&direct.ReturnType==typeof(bool),"AEC direct return/argument layout");
  Check(direct.GetMethodBody().LocalVariables[3].LocalType==typeof(object),"AEC reflective prefab is local3");
  var il=Generator();var original=Read(direct,il);int regions=original.Sum(c=>c.blocks.Count);
  var codes=Hooks.DirectTranspiler(original,il).ToList();Labels(codes);Check(codes.Sum(c=>c.blocks.Count)==regions,"AEC exception regions preserved");
  int guard=codes.FindIndex(c=>Calls(c,"AcceptDirect"));Check(guard>0,"AEC guard inserted");Check(codes[guard+2].opcode==OpCodes.Ldc_I4_0&&codes[guard+3].opcode==OpCodes.Ret,"AEC fail returns false before reflected SpawnEntityInWorld");
  Check(codes[guard-3].opcode==OpCodes.Ldloc_3&&codes[guard-2].opcode==OpCodes.Ldarga_S&&codes[guard-1].opcode==OpCodes.Ldarg_2,"entity and coordinate/description references");
  Check(codes[guard+4].labels.Contains((Label)codes[guard+1].operand),"AEC success branch preserved");
  Check(codes.Take(guard).Any(c=>Calls(c,"IsNearTrader")),"original trader guard retained");
  using(var module=Mono.Cecil.ModuleDefinition.ReadModule(direct.Module.FullyQualifiedName)){
   var body=((Mono.Cecil.MethodDefinition)module.LookupToken(direct.MetadataToken)).Body;
   var boundary=body.Instructions.First(i=>i.OpCode.Name=="callvirt"&&i.Operand.ToString().Contains("System.Object::GetType()"));
   Check(!body.ExceptionHandlers.Any(e=>(boundary.Offset>=e.TryStart.Offset&&boundary.Offset<e.TryEnd.Offset)||(boundary.Offset>=e.HandlerStart.Offset&&boundary.Offset<e.HandlerEnd.Offset)),"early return is outside protected exception regions");
   var type=module.Types.First(t=>t.FullName==aec);var enqueue=type.Methods.First(m=>m.Name=="EnqueueFollowersFromLeader").Body.Instructions;
   int call=enqueue.ToList().FindIndex(i=>i.Operand!=null&&i.Operand.ToString().Contains("::TrySpawnFollowerNearLeader("));
   Check(call>=0&&enqueue[call+1].OpCode.Name.StartsWith("brfalse"),"follower failure branch exists");
   var failure=(Mono.Cecil.Cil.Instruction)enqueue[call+1].Operand;
   Check(failure.Next.Next.Operand.ToString().Contains("::Add("),"failed follower retained in pending list");
   Check(enqueue.Any(i=>i.Operand!=null&&i.Operand.ToString().Contains("::PendingFollowersByKey")),"pending follower queue retained");
  }
  Bind(Target(aec,"TrySpawnFollowerNearLeader"),"FollowerPrefix");Bind(Target(aec,"TrySpawnFollowerNearLeader"),"FollowerPostfix");
  Factory(Target("AIDirectorBloodMoonParty","SpawnZombie"),"BloodPrefix");
  Factory(Target("GameEvent.SequenceActions.ActionBaseSpawn","SpawnEntity"),"EventPrefix");
  Factory(Target("AeclipseCustomZombieAI01.MegaHordeRuntime","SpawnVanillaPack"),"MegaPrefix");
  foreach(var name in new[]{"TrySpawnEventBoss","TrySpawnEventBossForHeatmap","SpawnHeatmapEscortZombies","TrySpawnReplacementOnKill","TryApplySpawnRateBonus"})Bind(Target(aec,name),"SourcePrefix");
  Target(aec,"GetFollowerSpawnPositionNearLeader");Target(aec,"IsNearTrader");Target("AIDirectorBloodMoonParty","CalcSpawnPos");
  Bind(Target("AIDirectorBloodMoonParty","CalcSpawnPos"),"BloodPositionPostfix");
  var events=AccessTools.TypeByName("GameEvent.SequenceActions.ActionBaseSpawn");Check(AccessTools.Method(events,"FindValidPosition",new[]{typeof(Vector3).MakeByRefType(),typeof(Vector3),typeof(float),typeof(float),typeof(bool),typeof(float),typeof(bool),typeof(float)})!=null,"native event selector overload");
  Bind(AccessTools.Method(events,"FindValidPosition",new[]{typeof(Vector3).MakeByRefType(),typeof(Vector3),typeof(float),typeof(float),typeof(bool),typeof(float),typeof(bool),typeof(float)}),"EventPositionPostfix");
  // Compose both orders with the existing high-tier class-selector patch.
  var blood=Target("AIDirectorBloodMoonParty","SpawnZombie");var old=AccessTools.Method(AccessTools.TypeByName("AECT16RuntimeFix.BloodMoonSpawnFix"),"Transpiler");Check(old!=null,"existing bloodmoon patch");
  foreach(bool safetyFirst in new[]{false,true}){
   il=Generator();var input=Read(blood,il);
   if(safetyFirst)input=Hooks.FactoryTranspiler(input,il,blood).ToList();
   input=((IEnumerable<CodeInstruction>)old.Invoke(null,new object[]{input})).ToList();
   if(!safetyFirst)input=Hooks.FactoryTranspiler(input,il,blood).ToList();
   Check(input.Count(c=>Calls(c,"AcceptCurrent"))==1,"composed safety guard");
   Check(input.Any(c=>(c.operand as MethodInfo)?.DeclaringType==old.DeclaringType),"existing exact-tier selector retained");Labels(input);
  }
  // Nested/failing scopes restore the previous request and original exception.
  var outer=new Request{Source="outer"};var inner=new Request{Source="inner"};Safety.Current=inner;var error=new Exception("test");Check(ReferenceEquals(Hooks.Restore(error,new Hooks.ScopeState{Previous=outer}),error)&&ReferenceEquals(Safety.Current,outer),"scope finally restores on exception");
  Hooks.Restore(null,null);Check(ReferenceEquals(Safety.Current,outer),"other mod skipping our prefix cannot clear an outer scope");
  var unchanged=new Vector3(10,20,30);outer.LastAccepted=new Vector3(99,99,99);Hooks.FollowerPostfix(true,ref unchanged,null);Check(unchanged.x==10,"skipped follower prefix cannot borrow outer result");Safety.Current=null;
  string description="";bool result=false;Hooks.DirectState saved,nested;
  Safety.Current=new Request{Source="blood",Remaining=0,Next=()=>default(Vector3)};var bloodScope=Safety.Current;
  Check(Hooks.DirectPrefix(default(Vector3),ref description,ref result,out saved),"nested AEC spawn does not inherit exhausted bloodmoon budget");
  Check(Safety.Current.Source=="AEC-direct"&&Safety.Current.Next==null,"nested AEC spawn cannot use bloodmoon distance selector");
  var firstDirect=Safety.Current;firstDirect.Remaining=0;
  Check(Hooks.DirectPrefix(default(Vector3),ref description,ref result,out nested),"reentrant direct spawn gets separate budget");
  Hooks.RestoreDirect(error,nested);Check(ReferenceEquals(Safety.Current,firstDirect),"nested direct restores active parent");
  Check(ReferenceEquals(Hooks.RestoreDirect(error,saved),error)&&ReferenceEquals(Safety.Current,bloodScope),"direct exception restores event context");
  var follower=new Request{Source="follower",Direct=true,Remaining=0};Safety.Current=follower;
  Check(!Hooks.DirectPrefix(default(Vector3),ref description,ref result,out saved)&&!result,"follower exhausted budget still suppresses its own direct retry");
  Hooks.RestoreDirect(null,saved);Check(ReferenceEquals(Safety.Current,follower),"follower shared budget remains owned by follower scope");
  var batch=new Request{Source="batch",Direct=true,PerEntity=true,Remaining=0,Next=()=>default(Vector3)};Safety.Current=batch;
  Check(Hooks.DirectPrefix(default(Vector3),ref description,ref result,out saved)&&Safety.Current.Remaining==12,"each skill batch entity receives its own bounded budget");Hooks.RestoreDirect(null,saved);
  Check(ReferenceEquals(Safety.Current,batch),"skill template restored");Safety.Current=null;
  int retryCalls=0;Safety.Current=new Request{Source="blood-moon",RecoverSelector=true,Surface=true,Fallback=()=>{retryCalls++;return new Vector3(40,30,0);}};
  var site=default(Vector3);bool found=false;Hooks.BloodPositionPostfix(ref site,ref found);Check(found&&site.x==40&&retryCalls==1,"initial blood selector failure gets fallback before entity creation");
  Hooks.BloodPositionPostfix(ref site,ref found);Check(retryCalls==1,"valid original selector output is not replaced");
  Safety.Current.RecoverSelector=false;found=false;Hooks.BloodPositionPostfix(ref site,ref found);Check(!found&&retryCalls==1,"registration callbacks cannot recover unrelated selectors");
  Safety.Current=new Request{Source="event:PZAECDefenseT19W3",RecoverSelector=true,Surface=true,Fallback=()=>new Vector3(48,30,0)};found=false;Hooks.EventPositionPostfix(ref site,ref found);Check(found&&site.x==48,"custom event selector failure recovers before factory");
  Safety.Current=new Request{Bypass=true,Fallback=()=>throw new Exception("vanilla touched")};found=false;Hooks.EventPositionPostfix(ref site,ref found);Check(!found,"ordinary/air event selector left unchanged");Safety.Current=null;
  return "PASS: "+checks+" native IL/signature/branch/patch-composition checks. Unity/Mono in-game hook activation remains a live check.";
 }
}
'@
[SpawnNativeTests]::Run()
