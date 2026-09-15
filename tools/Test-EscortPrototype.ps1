#Requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Test-ModelTintFix.ps1')
Add-Type -ReferencedAssemblies ($references + $frameworkReferences) -TypeDefinition @'
using System;
using System.Collections.Generic;
using AECT16RuntimeFix;
public static class EscortPrototypeRegression {
    static int checks;
    static void Check(bool ok,string text) { checks++; if(!ok) throw new Exception(text); }
    static Dictionary<int,double> Position(double distance) { return new Dictionary<int,double>{{1,distance}}; }
    static EscortPrototype.Run Start() {
        var r=new EscortPrototype.Run(300); Check(r.Join(1,true),"owner admission");
        Check(!r.Join(2,false),"foreign admission"); Check(r.Start(),"start");
        Check(!r.Join(3,true),"late join"); return r;
    }
    public static string Run() {
        foreach(double length in new[]{299,401,double.NaN,double.PositiveInfinity}) {
            bool rejected=false; try { new EscortPrototype.Run(length); } catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"route length validation");
        }
        var r=Start(); r.Tick(1,Position(31)); Check(r.Distance==0,"outside movement radius");
        r.Tick(1,Position(30)); Check(r.Distance==1,"inclusive movement radius");
        for(int wave=0;wave<3;wave++) {
            for(int t=0;t<120;t++) r.Tick(1,Position(0));
            Check(r.AtCheckpoint && r.Distance==100*(wave+1),"checkpoint clamping");
            Check(!r.StartAmbush(new[]{100,100}),"duplicate spawn IDs");
            int id=100+wave;
            Check(r.StartAmbush(new[]{id}),"start ambush");
            double distance=r.Distance; r.Tick(1,Position(0)); Check(r.Distance==distance,"cannot skip wave");
            Check(!r.Killed(id,false),"unconfirmed death");
            Check(!r.Killed(id+500,true),"foreign kill");
            Check(r.Killed(id,true),"authoritative death"); Check(!r.Killed(id,true),"dedup death");
        }
        Check(r.State==EscortPrototype.Phase.Succeeded,"three checkpoints win");
        Check(r.RewardEligible(1,true,true,200),"inclusive reward radius");
        Check(!r.RewardEligible(1,true,true,200.01),"outside reward radius");
        Check(!r.RewardEligible(1,false,true,0),"dead cannot claim");
        Check(!r.RewardEligible(1,true,false,0),"left party cannot claim");
        Check(!r.RewardEligible(99,true,true,0),"unregistered cannot claim");
        r.Damage(100,false); Check(r.State==EscortPrototype.Phase.Succeeded,"terminal state immutable");
        var idle=Start(); for(int i=0;i<59;i++)idle.Tick(1,Position(201));
        Check(!idle.Terminal,"59s grace"); idle.Tick(1,Position(201)); Check(idle.Failure=="abandoned","60s abandon");
        var hurt=Start(); hurt.Damage(100,true); Check(hurt.Health==100,"player friendly fire blocked");
        hurt.Damage(50,false); Check(!hurt.Repair(false),"repair requires kit");
        Check(hurt.Repair(true) && hurt.Health==70,"repair amount"); Check(!hurt.Repair(true),"repair cooldown");
        for(int n=0;n<2;n++) { for(int i=0;i<30;i++)hurt.Tick(1,Position(31)); hurt.Damage(20,false); Check(hurt.Repair(true),"subsequent repair"); }
        for(int i=0;i<30;i++)hurt.Tick(1,Position(31)); hurt.Damage(20,false); Check(!hurt.Repair(true),"repair cap");
        hurt.Damage(100,false); Check(hurt.Failure=="transport_destroyed","transport death");
        var missing=Start(); for(int i=0;i<100;i++)missing.Tick(1,Position(0));
        missing.StartAmbush(new[]{-25}); missing.MissingEnemy(-25); Check(missing.Failure=="enemy_missing","missing is not killed");
        var timeout=Start(); for(int i=0;i<900;i++)timeout.Tick(1,Position(31)); Check(timeout.Failure=="timeout","timeout");
        var participation=Start(); for(int i=0;i<100;i++)participation.Tick(1,Position(0));
        Check(Math.Abs(participation.Members[1].ActiveSeconds-100)<.001,"participation accumulation");
        participation.Members[1].ActiveSeconds=0;
        for(int wave=0;wave<3;wave++) { while(!participation.AtCheckpoint)participation.Tick(1,Position(0)); participation.StartAmbush(new[]{200+wave}); participation.Killed(200+wave,true); }
        participation.Members[1].ActiveSeconds=participation.Elapsed*.599;
        Check(!participation.RewardEligible(1,true,true,0),"under 60 percent");
        participation.Members[1].ActiveSeconds=participation.Elapsed*.6;
        Check(participation.RewardEligible(1,true,true,0),"exactly 60 percent");
        return "PASS: "+checks+" T16 escort model assertions. No game movement, combat, rewards or multiplayer are exercised.";
    }
}
'@
[EscortPrototypeRegression]::Run()
