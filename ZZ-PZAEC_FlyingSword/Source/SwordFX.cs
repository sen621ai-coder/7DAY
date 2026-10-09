using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordFX
    {
        sealed class Effect{public GameObject Go;public int Actor;public byte Kind;public Vector3 A,B;public float Start,Value;public SwordCrescent Crescent;public float[] Stops;}
        static readonly List<Effect> effects=new List<Effect>();

        static readonly Dictionary<int,float> charges=new Dictionary<int,float>();
        public static void Clear(){foreach(var e in effects)if(e.Go!=null)Object.Destroy(e.Go);effects.Clear();charges.Clear();SwordAttackVisuals.Clear();SwordPresentation.Clear();}
        public static void Receive(World w,NetPackagePZAECJuqueEvent ev)
        {
            var actor=w.GetEntity(ev.Actor) as EntityPlayer;if(ev.Kind==3){charges[ev.Actor]=Time.time;SwordAttackVisuals.Charge(actor);return;}if(ev.Kind==1||ev.Kind==2||ev.Kind==5||ev.Kind==7){charges.Remove(ev.Actor);SwordAttackVisuals.Cancel(actor);}if(ev.Kind==7){SwordAttackVisuals.Release(actor);return;}if(ev.Kind==8){SwordAttackVisuals.Burst(ev.A-Origin.position);SwordAttackVisuals.Sound(ev.A-Origin.position,2);return;}
            if(ev.Kind!=1&&ev.Kind!=2||SwordMod.Settings.Effects<=0)return;
            var e=new Effect{Actor=ev.Actor,Kind=ev.Kind,A=ev.A,B=ev.B,Start=Time.time,Value=ev.Value};
            if(e.Kind==2){e.Go=SwordModel.Visual(null,3);e.Go.transform.position=e.A-Origin.position;}
            else {e.Go=new GameObject("Juque crescent");e.Crescent=e.Go.AddComponent<SwordCrescent>();e.Crescent.Init(e.B,e.Value);e.Stops=new float[33];var right=Vector3.Cross(Vector3.up,e.B).normalized;for(int i=0;i<33;i++){e.Stops[i]=Mathf.Lerp(35,45,e.Value);var from=e.A+right*(i/32f-.5f)*Mathf.Lerp(1,8,e.Value);if(Voxel.Raycast(w,new Ray(from,e.B),e.Stops[i],-538750997,8,0)&&Voxel.voxelRayHitInfo.hit.blockValue.type!=0)e.Stops[i]=Vector3.Distance(from,Voxel.voxelRayHitInfo.hit.pos);}}
            effects.Add(e);
        }
        public static void Tick(World w)
        {
            if(w.GetPrimaryPlayer()==null)return;
            foreach(var entity in w.Entities.list)if(entity is EntityPlayer p){var held=p.inventory?.GetHoldingItemTransform();if(held==null)continue;var model=held.Find("JuqueVisual");if(model==null)continue;if(SwordAttackVisuals.Valid(p))SwordAttackVisuals.For(p);float start;float charge=charges.TryGetValue(p.entityId,out start)?SwordRules.Charge(Time.time-start):0;if(!SwordRules.IsSword(p.inventory.holdingItemItemValue)||p.IsDead())charge=0;var block=new MaterialPropertyBlock();block.SetColor("_EmissionColor",new Color(.12f,.6f,1)*charge*2*SwordMod.Settings.Effects);model.GetComponent<Renderer>().SetPropertyBlock(block);}
            for(int i=effects.Count-1;i>=0;i--){var e=effects[i];float age=Time.time-e.Start;float duration=e.Kind==2?.7f:Mathf.Lerp(35,45,e.Value)/50;
                if(age>=duration||e.Go==null){if(e.Go!=null)Object.Destroy(e.Go);effects.RemoveAt(i);continue;}
                if(e.Kind==2){var p=w.GetEntity(e.Actor);if(p==null)continue;float t=Mathf.SmoothStep(0,1,age/duration);var target=p.position+Vector3.up*1.1f;var side=Vector3.Cross(Vector3.up,e.B).normalized;var control=(e.A+target)*.5f+side*1.4f+Vector3.up*.75f;e.Go.transform.position=((1-t)*(1-t)*e.A+2*(1-t)*t*control+t*t*target)-Origin.position;e.Go.transform.localScale=Vector3.one*Mathf.Lerp(3,.15f,t);e.Go.transform.rotation=Quaternion.LookRotation((target-e.A).sqrMagnitude>.01f?target-e.A:Vector3.forward)*Quaternion.Euler(0,0,360*t);}
                else {float range=Mathf.Lerp(35,45,e.Value);e.Crescent.Shape(e.A-Origin.position,age*50,e.Stops,Mathf.Clamp01((range-age*50)/3));}
            }
            foreach(var entity in w.Entities.list){var v=entity as EntityJuque;if(v==null||v.vehicleRB==null)continue;
                var visual=v.vehicleRB.transform.Find("JuqueVisual");if(visual!=null){float lean=v.GetAttached(0)!=null?(v.Boost?9:4)*v.Throttle:Mathf.Sin(Time.time*1.7f)*1.5f;visual.localRotation=Quaternion.Euler(lean,0,-v.Turn*12);var primary=w.GetPrimaryPlayer();SwordFlightAura.For(v).Step(v.GetAttached(0)!=null,v.Boost,primary.AttachedToEntity==v&&primary.bFirstPersonView,SwordMod.Settings.Effects,Time.time,visual.localRotation,v.isEntityRemote?(Vector3?)null:v.vehicleRB.velocity);}
            }
        }
    }
}
