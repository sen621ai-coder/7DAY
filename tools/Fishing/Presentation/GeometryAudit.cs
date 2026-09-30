using System;
using System.Globalization;
using System.Text;
using PZAEC.Fishing.Contracts;
using ContractFishingSimulation=PZAEC.Fishing.Simulation.ContractFishingSimulation;

public static class FishingGeometryAudit
{
    sealed class Water : IWaterQuery
    {
        public WaterSample SampleColumn(Vec3 p,float above,float below)=>new WaterSample{Status=WaterSampleStatus.Valid,Accuracy=SurfaceAccuracy.VoxelEstimate,SurfacePoint=new Vec3(p.X,0,p.Z),Normal=Vec3.Up,BottomY=-3,DepthMeters=3,BottomKnown=true};
        public SegmentHit TraceSolid(Vec3 a,Vec3 b)=>new SegmentHit();
    }
    sealed class RandomSource : IRandomSource
    {uint state=29;public uint NextUInt(){state^=state<<13;state^=state>>17;state^=state<<5;return state;}public float Next01()=>(NextUInt()>>8)/16777216f;}
    sealed class Events : IEventSink {public void Emit(FishingEvent value){} }
    public static string Run()
    {
        var c=new FishingConfig();var water=new Water();var events=new Events();var sim=new ContractFishingSimulation();
        var env=new EnvironmentFrame{CanFish=true,IsGrounded=true,ViewForward=new Vec3(0,0,1),ViewRight=new Vec3(1,0,0),Water=water.SampleColumn(new Vec3(0,0,8),4,16),
            Rod=new RodPose{Root=new Vec3(0,1.2f,0),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0),PitchRadians=.35f}};
        sim.Begin(new SessionStart{SessionId=Guid.NewGuid(),Authority=AuthorityMode.Standalone,CastTarget=new Vec3(0,0,8),InitialLineLengthMeters=6.5f,FishDefinitionId=c.Fish.Id,Seed=29},c,env,new RandomSource(),water,events);
        var trace=new StringBuilder("tick,phase,line_route_deficit_m,rod_chord_excess_m\n");int fight=0;float worstLine=0,worstRod=0,pitch=.35f;
        for(int i=1;i<=1800&&!sim.Current.IsTerminal;i++)
        {
            bool strike=sim.Current.Phase==FishingPhase.BiteWindow;if(strike)pitch=1.0f;
            env.Tick=i;env.TimeSeconds=i/60.0;
            var s=sim.Step(FishingContract.FixedStepSeconds,new ControlIntent{InputSequence=i,RodPitchRadians=pitch,Drag01=.4f,Reel01=.4f,Strike=strike},env,events);
            if(s.Phase!=FishingPhase.Hooked&&s.Phase!=FishingPhase.Fighting&&s.Phase!=FishingPhase.Landing)continue;
            fight++;float route=(s.FloatPosition-s.Rod.Tip).Length+(s.FishPosition-s.FloatPosition).Length;
            float shortage=Math.Max(0,route-s.LineLengthMeters-s.LineExtensionMeters),rod=Math.Max(0,(s.Rod.Tip-s.Rod.Root).Length-c.Rod.LengthMeters);
            worstLine=Math.Max(worstLine,shortage);worstRod=Math.Max(worstRod,rod);
            if(i%30==0)trace.AppendFormat(CultureInfo.InvariantCulture,"{0},{1},{2:F5},{3:F5}\n",i,s.Phase,shortage,rod);
        }
        if(fight==0)throw new Exception("Geometry audit did not reach a hooked fish; cannot claim measured physical consistency.");
        return "B production simulation, flat synthetic water, seed 29; NOT game footage.\nFight snapshots: "+fight+"\nTerminal/latest state: "+sim.Current.Phase+" / "+sim.Current.Failure+
            "\nPeak geometric route deficit (m): "+worstLine.ToString("F5",CultureInfo.InvariantCulture)+"\nPeak physical rod chord excess (m): "+worstRod.ToString("F5",CultureInfo.InvariantCulture)+"\n\n"+trace;
    }
}
