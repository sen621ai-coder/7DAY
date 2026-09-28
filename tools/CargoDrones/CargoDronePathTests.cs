using System;
using System.Collections.Generic;
using System.Linq;
using YFAutomation.CargoDrones;
public static class CargoDronePathTests
{
    static int checks;static void Check(bool v,string s){checks++;if(!v)throw new Exception("Path FAIL: "+s);}
    sealed class Space:ICargoAirspace
    {
        public readonly List<CargoBox> Boxes=new List<CargoBox>();public readonly List<CargoPoint> Reached=new List<CargoPoint>();public bool Wait;public int Sweeps;
        public CargoHold Prepare(CargoPoint a,CargoPoint b){return Wait?CargoHold.ChunkLoading:CargoHold.None;}
        public CargoSweep Sweep(CargoPoint a,CargoPoint b){Sweeps++;return Boxes.Any(x=>x.SweptHit(a,b))?CargoSweep.Blocked:CargoSweep.Clear;}
        public void ReachedSegment(CargoPoint p){Reached.Add(p);}
        public void Wall(double x0,double y0,double z0,double x1,double y1,double z1){Boxes.Add(new CargoBox(new CargoPoint(x0,y0,z0),new CargoPoint(x1,y1,z1)));}
    }
    static CargoPathSearch Search(Space s,CargoPoint a,CargoPoint b)
    {var p=new CargoPathSearch(s,a,b,true);for(int i=0;i<20000&&!p.Complete&&!p.Failed;i++)p.Advance();return p;}
    static void Safe(Space s,CargoPoint start,CargoPathSearch p,string name)
    {
        Check(p.Complete,name+" complete status="+p.Status+" nodes="+p.Nodes+" probes="+p.Probes+" cpu="+p.CpuMilliseconds);
        foreach(var q in p.Waypoints){Check(start.Distance(q)<=16.000001&&s.Sweep(start,q)==CargoSweep.Clear,name+" continuous full-body edge");start=q;}
        Check(p.Nodes<=8192&&p.Probes<=16384,name+" bounded work");
    }
    public static int Run()
    {
        checks=0;
        var s=new Space();s.Wall(-20,98,-20,30,99,30);s.Wall(-20,103,-20,30,104,30);
        // U pocket opens behind the drone; moving closer first cannot solve it.
        s.Wall(-3,99,-3,-2,103,7);s.Wall(2,99,-3,3,103,7);s.Wall(-3,99,6,3,103,7);
        var start=new CargoPoint(.5,100.5,3.5);var goal=new CargoPoint(.5,100.5,11.5);var p=Search(s,start,goal);Safe(s,start,p,"U pocket");
        Check(p.Waypoints.Any(q=>q.Z< -3),"U route first moves away from box");
        var quick=new CargoLocalRoute(s,start,goal,124);for(int i=0;i<1000&&!quick.Complete&&!quick.Failed;i++)quick.Advance();Check(quick.Failed,"fixture actually defeats fixed doglegs");
        var motion=new CargoMotion(s,start,goal,600000,returnReserve:180000);motion.RetargetVia(goal,new CargoPoint[0],0);
        for(int i=0;i<20000&&!motion.Arrived;i++){var from=motion.Position;motion.Tick(100);Check(s.Sweep(from,motion.Position)==CargoSweep.Clear,"integrated U flight never clips wall");}
        Check(motion.Arrived&&!motion.EnergyRecall,"integrated indoor A* reaches box with reserve");
        motion.ReturnHome();for(int i=0;i<2000&&!motion.Arrived;i++)motion.Tick(100);Check(motion.Arrived&&motion.Position.Distance(start)<1e-7,"U return follows real outbound path");
        s=new Space();s.Wall(-4,97,-4,16,98,4);s.Wall(-4,103,-4,16,104,4);s.Wall(-4,98,-4,16,103,-3);s.Wall(-4,98,3,16,103,4);s.Wall(4,101,-3,6,103,3);
        start=new CargoPoint(.5,101.5,.5);goal=new CargoPoint(10.5,101.5,.5);p=Search(s,start,goal);Safe(s,start,p,"lower under beam");Check(p.Waypoints.Any(q=>q.Y<100.5),"indoor search can descend");
        s=new Space();s.Wall(-2,98,-2,14,99,4);s.Wall(-2,102,-2,14,103,4);s.Wall(-2,99,-2,14,102,0);s.Wall(-2,99,2,14,102,4);
        start=new CargoPoint(.2,100.2,1);goal=new CargoPoint(10.2,100.2,1);p=Search(s,start,goal);Safe(s,start,p,"two-wide precision corridor");
        s.Wall(4,99,1,5,102,2);p=Search(s,start,goal);Check(!p.Complete&&p.Failed,"one-wide throat cannot pass 1.6 body");
        s=new Space{Wait=true};start=new CargoPoint(0,100,0);goal=new CargoPoint(4,100,0);p=new CargoPathSearch(s,start,goal,true);for(int i=0;i<20;i++)p.Advance();Check(!p.Failed&&!p.Complete&&p.Probes==0&&p.Status==CargoNavigationStatus.WaitingData,"unknown data never closes an edge");s.Wait=false;for(int i=0;i<500&&!p.Complete;i++)p.Advance();Safe(s,start,p,"loading resume");
        s=new Space();s.Wall(3,99,-1,5,102,1);p=Search(s,start,goal);Check(p.Status==CargoNavigationStatus.GoalBlocked,"occupied exact endpoint is explicit");
        p=new CargoPathSearch(new Space(),start,new CargoPoint(90,100,0),true);Check(p.Failed&&p.Status==CargoNavigationStatus.SearchBudgetExceeded,"range cap does not truncate target");
        p=new CargoPathSearch(new Space(),start,goal,true,maxNodes:1);p.Advance();Check(p.Status==CargoNavigationStatus.SearchBudgetExceeded,"budget exhaustion is not no-path");
        s=new Space();s.Wall(15,99,-2,17,103,2);motion=new CargoMotion(s,start,new CargoPoint(30,100,0),600000,returnReserve:180000);motion.RetargetVia(new CargoPoint(30,100,0),new CargoPoint[0],0);
        for(int i=0;i<20000&&!motion.Arrived;i++)motion.Tick(100);Check(motion.Arrived,"16-block sample inside wall is replaced by route to real endpoint");
        // Mid-search snapshots retain semantic entrance constraints, not graph state.
        s=new Space();s.Wall(5,99,-2,7,104,2);start=new CargoPoint(0,100,0);var throat=new CargoPoint(12,100,0);goal=new CargoPoint(18,100,4);
        motion=new CargoMotion(s,start,goal,600000,returnReserve:180000);motion.RetargetVia(goal,new[]{throat},0);
        for(int i=0;i<8;i++)motion.Tick(100);
        var saved=motion.Capture();Check(saved.Navigation.Length==2&&saved.Navigation[0].Point.Distance(throat)<1e-7,"checkpoint retains mandatory entrance while planning");
        motion=new CargoMotion(s,new CargoEnergy(motion.Battery),saved);
        for(int i=0;i<20000&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&s.Reached.Any(q=>q.Distance(throat)<1e-7),"restore still visits entrance before box");
        s=new Space();s.Wall(5,99,-2,7,104,2);motion=new CargoMotion(s,start,goal,600000,returnReserve:180000);motion.RetargetVia(goal,new[]{throat},0);
        for(int i=0;i<8;i++)motion.Tick(100);motion.ReturnHome();
        for(int i=0;i<2000&&!motion.Arrived;i++)motion.Tick(100);
        Check(motion.Arrived&&motion.Position.Distance(start)<1e-7,"recall during search cannot commit stale outbound result");
        motion=new CargoMotion(new Space(),start,goal,600000,returnReserve:180000);motion.RequireMigration();motion.Tick(100);motion.RetryPath();motion.Tick(100);
        Check(motion.Position.Distance(start)<1e-7&&motion.Navigation==CargoNavigationStatus.MigrationRequired,"ambiguous old entrance remains safely suspended across retry");
        motion.ReturnHome();motion.Tick(100);Check(motion.Arrived,"explicit recall resolves migration hold");
        s=new Space();goal=new CargoPoint(4,100,0);s.Wall(3,99,-1,5,102,1);motion=new CargoMotion(s,start,goal,600000,returnReserve:180000);motion.RetargetVia(goal,new CargoPoint[0],0);
        for(int i=0;i<20&&motion.Navigation!=CargoNavigationStatus.GoalBlocked;i++)motion.Tick(100);
        Check(motion.Capture().LastFailure!=null,"static failure captures exact segment for redelivery preflight");motion.ReturnHome();
        Check(!motion.PreflightFailure(s,0),"blocked goal cannot cause repeated empty redelivery flights");
        var failed=motion.Capture();motion=new CargoMotion(s,new CargoEnergy(motion.Battery),failed);s.Boxes.Clear();bool admitted=false;
        for(int i=0;i<500&&!admitted;i++)admitted=motion.PreflightFailure(s,10+i*.1);
        Check(admitted&&motion.Capture().LastFailure==null,"restored failed route reopens after geometry clears");
        var world=Guid.NewGuid();var directory=System.IO.Path.Combine(".local-tests","CargoDrones","PathPersistence-"+world.ToString("N"));System.IO.Directory.CreateDirectory(directory);
        using(var journal=new CargoFileJournal(System.IO.Path.Combine(directory,"cargo.wal"),world))using(var store=new CargoCheckpointStore(directory,world))
        {
            var state=new CargoMissionState(world,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"test",goal,180000,CargoPhase.Docked,CargoHold.None,true,0,0,false,CargoMissionReturnReason.Requested,600000,0,new CargoItem[6],failed);
            store.Save(new[]{state},journal);var loaded=store.Load(journal).Single();
            Check(loaded.Motion.Navigation!=null&&loaded.Motion.Generation==failed.Generation&&loaded.Motion.LastFailure!=null&&loaded.Motion.LastFailure.Goal.Distance(goal)<1e-7,"disk checkpoint round-trip preserves nav generation and failed segment");
        }
        return checks;
    }
}
