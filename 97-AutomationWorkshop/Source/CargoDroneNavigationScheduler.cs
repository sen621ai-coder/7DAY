using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace YFAutomation.CargoDrones
{
    // Cooperative budget shared by all native searches, including isolated QA.
    internal static class CargoNavigationScheduler
    {
        sealed class Request { public int Seen,Served;public long Order; }
        static readonly Dictionary<object,Request> requests=new Dictionary<object,Request>();
        static readonly HashSet<object> selected=new HashSet<object>();
        static int frame=-1,probes;static long order;static double spent;
        internal static bool Begin(object search)
        {
            int now=Time.frameCount;
            if(frame!=now)
            {
                frame=now;spent=0;probes=0;selected.Clear();
                foreach(var key in requests.Where(p=>now-p.Value.Seen>2).Select(p=>p.Key).ToArray())requests.Remove(key);
                foreach(var key in requests.OrderBy(p=>p.Value.Served).ThenBy(p=>p.Value.Order).Take(2).Select(p=>p.Key))selected.Add(key);
            }
            Request r;if(!requests.TryGetValue(search,out r))
            {if(requests.Count>=32)return false;r=new Request{Order=order++,Served=now-1};requests.Add(search,r);if(selected.Count<2)selected.Add(search);}
            r.Seen=now;if(!selected.Contains(search)||spent>=2||probes>=8||r.Served==now)return false;
            r.Served=now;return true;
        }
        internal static void Remove(object search){if(search==null)return;requests.Remove(search);selected.Remove(search);}
        internal static void End(double ms,int work){spent+=ms;probes+=work;}
        internal static void Clear(){requests.Clear();selected.Clear();frame=-1;}
    }
}
