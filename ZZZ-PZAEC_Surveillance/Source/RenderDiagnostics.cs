using System;
using System.Globalization;
using System.IO;

namespace PZAEC.Surveillance
{
    // Allocated only when recording is enabled. No formatting or disk IO in the sample path.
    public static class RenderDiagnostics
    {
        struct Sample
        {
            public double Time,Duration,Age,Wake;
            public Guid Id;
            public int Kind,Width,Hz;
        }
        const int Capacity=65536;
        static Sample[] ring;
        static int count,next;
        static long overwritten;
        public static bool HasData=>count>0;
        public static void Record(double time,int kind,Guid id,double duration,double age,double wake,int width,int hz)
        {
            if(!SurveillanceRenderService.Diagnostics)return;
            if(ring==null)ring=new Sample[Capacity];
            ring[next]=new Sample{Time=time,Kind=kind,Id=id,Duration=duration,Age=age,Wake=wake,Width=width,Hz=hz};
            next=(next+1)%Capacity;if(count<Capacity)count++;else overwritten++;
        }
        public static void Export(string path)
        {
            if(count==0)return;
            using(var writer=new StreamWriter(path))
            {
                writer.WriteLine("# event: 0=game_frame, 1=successful_render, 2=failed_render; overwritten="+overwritten);
                writer.WriteLine("seconds,event,camera,durationMs,previousFrameAgeMs,wakeToFirstFrameMs,width,targetHz");
                int first=(next-count+Capacity)%Capacity;
                for(int i=0;i<count;i++)
                {
                    var s=ring[(first+i)%Capacity];writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F6},{1},{2},{3:F4},{4:F4},{5:F4},{6},{7}",s.Time,s.Kind,s.Id,s.Duration,s.Age,s.Wake,s.Width,s.Hz));
                }
            }
            count=next=0;overwritten=0;ring=null;
        }
    }
}
