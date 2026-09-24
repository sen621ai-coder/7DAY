using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Per-world, server-only, bounded diagnostic history. No inventory payloads
    // or player platform IDs; hub/flight/endpoint IDs correlate the records.
    public sealed class CargoDiagnostics
    {
        sealed class Entry { public float At;public int Skipped;public string State; }
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        readonly Guid world;
        public CargoDiagnostics(Guid world){this.world=world;}
        public void Write(string kind,Guid id,string detail,float interval=10,string state=null)
        {
            try
            {
                float now=Time.realtimeSinceStartup;string key=kind+":"+id;
                Entry e;
                if(entries.TryGetValue(key,out e)&&((e.State==state&&now-e.At<interval)||(kind=="state"&&now-e.At<.5f))){e.Skipped++;return;}
                if(e==null){if(entries.Count>=512)entries.Clear();entries[key]=e=new Entry();}
                string skipped=e.Skipped>0?" suppressed="+e.Skipped:"";
                e.At=now;e.State=state;e.Skipped=0;
                detail=(detail??"").Replace('\r',' ').Replace('\n',' ');if(detail.Length>6000)detail=detail.Substring(0,6000)+"...";
                Log.Out("[YFCargo][Diag] world="+world+" event="+kind+" id="+id+skipped+" "+detail);
            }
            catch { /* A logging failure may not change flight or inventory state. */ }
        }
        public static string ChunkState(World world,CargoPosition p)
        {
            var chunk=world.GetChunkFromWorldPos(p.X,p.Z) as Chunk;
            return "at="+p.X+","+p.Y+","+p.Z+" chunk="+(p.X>>4)+","+(p.Z>>4)+" resident="+(chunk!=null)+" locked="+(chunk!=null&&chunk.IsLocked)+" decoration="+(chunk!=null&&chunk.NeedsDecoration)+" initialized="+(chunk!=null&&chunk.IsInitialized);
        }
        public void Status(CargoHubStatus s,int budget)
        {
            string state=s.Flight+"/"+s.Phase+"/"+s.Hold+"/"+s.Packages+"/"+s.Configuration.Revision+"/"+s.Message;
            Write("state",s.Configuration.HubId,"flight="+s.Flight+" phase="+s.Phase+" hold="+s.Hold+" pos="+CargoTrace.Point(s.Position)+" battery="+s.Battery+" cargo="+s.Packages+" paused="+s.Configuration.Paused+" powered="+s.Powered+" revision="+s.Configuration.Revision+" budget="+budget+" target="+Binding(s.Configuration.Target)+" shipmentTarget="+Binding(s.ShipmentTarget)+" entrance="+(s.Configuration.Entrance.HasValue?CargoHubUI.Coordinates(s.Configuration.Entrance.Value):"none")+" message="+s.Message,10,state);
        }
        static string Binding(CargoBinding b){return b==null?"none":b.EndpointId+"@"+CargoHubUI.Coordinates(b.Position);}
    }
}
