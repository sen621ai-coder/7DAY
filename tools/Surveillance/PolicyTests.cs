using System;
using System.Collections.Generic;
using PZAEC.Surveillance;

public static class SurveillancePolicyTests
{
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static void RunSchedule(int fps,bool focus)
    {
        var clocks=new List<FeedClock>();for(int i=0;i<4;i++)clocks.Add(new FeedClock());
        var counts=new int[4];var gaps=new double[4];
        for(int frame=0;frame<fps*30;frame++)
        {
            double now=(double)frame/fps;int chosen=RenderPolicy.Pick(clocks,now);
            if(chosen<0)continue;
            var clock=clocks[chosen];if(clock.LastSuccess>=0)gaps[chosen]=Math.Max(gaps[chosen],now-clock.LastSuccess);
            clock.Success(now,RenderPolicy.Rate(4,chosen==0,focus,0));counts[chosen]++;
            Assert(RenderPolicy.Pick(new[]{clock},now)<0,"A rendered stream cannot catch up repeatedly in one frame");
        }
        for(int i=0;i<4;i++)
        {
            double minimum=fps>=30?(focus&&i==0?7.5:4.2):3;
            Assert(counts[i]/30d>=minimum,"Stream starved at "+fps+" FPS: "+i+" count="+counts[i]);
            Assert(gaps[i]<=.3+1d/fps,"Excessive frame gap at "+fps+" FPS");
        }
        Console.WriteLine("PASS schedule "+fps+" FPS focus="+focus+" counts="+string.Join(",",counts));
    }
    public static void Run()
    {
        Assert(RenderPolicy.MaxActive==4&&RenderPolicy.MaxIdle==4,"Stream limits");
        Assert(RenderPolicy.Width(2)==768&&RenderPolicy.Width(1)==512&&RenderPolicy.Width(0)==384,"Resolution tiers");
        RunSchedule(60,true);RunSchedule(30,true);RunSchedule(30,false);RunSchedule(15,false);
        var early=new FeedClock{Due=10};var late=new FeedClock{Due=1};
        Assert(RenderPolicy.Pick(new[]{early,late},2)==1,"Head-of-line waiting blocks ready feed");
        var broken=new FeedClock();broken.Failure(0);Assert(RenderPolicy.Pick(new[]{broken},.05)<0,"Failure backoff missing");
        broken.Failure(.1);Assert(Math.Abs(broken.Due-.35)<.00001,"Second retry delay");
        broken.Failure(.35);Assert(Math.Abs(broken.Due-.85)<.00001,"Third retry delay");
        Assert(!broken.Fresh(0),"Uninitialized texture considered valid");broken.Success(1,5);
        Assert(broken.Fresh(1.49)&&!broken.Fresh(1.501),"Stale frame boundary");
        var mixed=new List<FeedClock>{new FeedClock(),new FeedClock(),new FeedClock(),new FeedClock()};int[] served=new int[4];
        for(int frame=0;frame<1800;frame++)
        {
            double now=frame/60d;
            if(frame%6==0)mixed[0]=new FeedClock{Due=now}; // Rapidly changing channel.
            int i=RenderPolicy.Pick(mixed,now);if(i<0)continue;
            if(i==1)mixed[i].Failure(now);else{mixed[i].Success(now,5);served[i]++;}
        }
        Assert(served[2]>130&&served[3]>130,"Channel churn or failures starve healthy peers");
        var gate=new ViewGate();Assert(!gate.Update(9,true,0),"9m enters without 8m activation");
        Assert(gate.Update(7,true,.1)&&gate.Update(9,true,.2),"Distance hysteresis");
        Assert(gate.Update(9,false,.3)&&!gate.Update(9,false,.401),"Visibility grace");
        Assert(gate.Update(7,true,.402),"Wake waits unnecessarily");Assert(!gate.Update(11,true,.403),"Range exit");
        Assert(RenderPolicy.Tier(460,2)==2&&RenderPolicy.Tier(410,1)==1,"Resolution threshold flapping");
        var quality=new QualityGate();Assert(quality.Update(2,0)==2,"Initial quality");
        Assert(quality.Update(0,.1)==2&&quality.Update(0,1)==2,"Immediate downgrade");
        Assert(quality.Update(0,1.2)==0,"Delayed downgrade missing");
        Assert(quality.Update(2,1.3)==0&&quality.Update(2,2)==0&&quality.Update(2,3.3)==2,"Resize cooldown");
        Assert(RenderPolicy.Rate(4,true,true,3)==5&&RenderPolicy.Width(0)==384,"Pressure safety floor");
        Console.WriteLine("PASS deadlines, retries, stale frames, channel churn, visibility hysteresis, quality cooldown");
    }
}
