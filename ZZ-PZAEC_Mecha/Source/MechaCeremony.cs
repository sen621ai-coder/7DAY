using UnityEngine;
namespace PZAEC.Mecha
{
    // Complete Form choreography and geometric safety, independent of player transfer.
    public static class Ceremony
    {
        public const float EnterSeconds=5, ExitSeconds=3.6f, EnterTransfer=2.9f, ExitTransfer=2.2f, ExitHold=2.8f;
        public static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        public static float Kneel(bool exit,float t)
        {return exit?t<.5f?0:t<1.5f?Ease((t-.5f)/1f):t<2.8f?1:1-Ease((t-2.8f)/.8f):t<1.3f?0:t<2.2f?Ease((t-1.3f)/.9f):t<3.7f?1:1-Ease((t-3.7f)/1.3f);}
        public static float Hatch(bool exit,bool full,float t)
        {
            if(!full)return t<.35f?Ease(t/.35f):1-Ease((t-.35f)/.15f);
            return exit?t<1.5f?0:t<2.2f?Ease((t-1.5f)/.7f):t<2.8f?1:1-Ease((t-2.8f)/.55f):t<2.2f?0:t<2.9f?Ease((t-2.2f)/.7f):t<3.15f?1:1-Ease((t-3.15f)/.55f);
        }
        public static float Equipment(bool exit,float t)
        {return exit?t<.5f?Ease(t/.5f):t<2.8f?1:1-Ease((t-2.8f)/.8f):t<.6f?0:t<1.3f?Ease((t-.6f)/.7f):t<3.7f?1:1-Ease((t-3.7f)/1.3f);}
        public static bool Stable(EntityVehicle v)
        {var rb=v.vehicleRB;return rb!=null&&rb.velocity.sqrMagnitude<=.16f&&Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)>.95f&&!Locomotion.Get(v).HoverOn&&!Locomotion.Get(v).Boost&&(v.GetWheelsOnGround()>0||Weapons.Trace(v,v.position+Vector3.up*.2f,Vector3.down,.9f,out var ground));}
        public static bool ClearCapsule(EntityVehicle v,Entity actor,Vector3 feet)
        {
            var a=feet-Origin.position+Vector3.up*.38f;var b=feet-Origin.position+Vector3.up*1.5f;
            foreach(var c in Physics.OverlapCapsule(a,b,.32f,~0,QueryTriggerInteraction.Ignore))
            {
                if(c==null||c.transform.IsChildOf(v.transform)||(v.vehicleRB!=null&&c.transform.IsChildOf(v.vehicleRB.transform))||(actor!=null&&c.transform.IsChildOf(actor.transform)))continue;
                return false;
            }
            return true;
        }
        public static bool FindExit(EntityVehicle v,Entity actor,out Vector3 point)
        {
            point=Vector3.zero;var rot=Weapons.BodyRotation(v);
            foreach(var local in new[]{new Vector3(-.65f,0,1.9f),new Vector3(0,0,2.1f),new Vector3(-1.55f,0,1.5f),new Vector3(1.55f,0,1.5f)})
            {
                var probe=v.position+rot*local+Vector3.up;
                if(!Weapons.Trace(v,probe,Vector3.down,2f,out var hit))continue;
                var feet=hit.hit.pos+Vector3.up*.08f;if(Mathf.Abs(feet.y-v.position.y)>.65f||!ClearCapsule(v,actor,feet))continue;
                bool flat=true;
                foreach(var offset in new[]{Vector3.left*.25f,Vector3.right*.25f,Vector3.forward*.25f,Vector3.back*.25f})
                    if(!Weapons.Trace(v,feet+offset+Vector3.up*.3f,Vector3.down,.65f,out var edge)||Mathf.Abs(edge.hit.pos.y-feet.y)>.24f){flat=false;break;}
                if(!flat)continue;
                var start=v.position+rot*new Vector3(0,1.0f,.65f);var delta=feet+Vector3.up-start;
                if(Weapons.Trace(v,start,delta.normalized,delta.magnitude,out var wall))continue;
                point=feet;return true;
            }
            return false;
        }
        public static bool Space(EntityVehicle v,Entity actor)
        {
            if(!Stable(v))return false;
            // Conservative clearance for both equipment arcs and the head/cockpit.
            foreach(var local in new[]{new Vector3(-1.8f,0,.2f),new Vector3(2.8f,0,-.1f),new Vector3(0,0,1.4f)})
            {
                var delta=Weapons.BodyRotation(v)*local;var from=v.position+Vector3.up*1.6f;
                if(Weapons.Trace(v,from,delta.normalized,delta.magnitude,out var hit))return false;
                if(!ClearCapsule(v,actor,v.position+delta+Vector3.up*.15f))return false;
            }
            return !Weapons.Trace(v,v.position+Vector3.up*.3f,Vector3.up,3.6f,out var ceiling);
        }
        public static bool NearDoor(EntityVehicle v,Entity actor)
        {
            if(actor==null)return false;var p=Quaternion.Inverse(Weapons.BodyRotation(v))*(actor.position-v.position);
            return Mathf.Abs(p.x)<1.35f&&p.z>-.15f&&p.z<2.45f&&p.y<3.2f&&p.y>-.7f;
        }
        public static void Pose(EntityVehicle v,Model.Rig r,bool exit,bool full,float t,Vector3 observer)
        {
            float k=full?Kneel(exit,t):0,e=full?Equipment(exit,t):0;
            r.Torso.localPosition=r.TorsoBasePosition+Vector3.down*(k*.78f);
            r.Torso.localRotation=r.RestRot[r.Torso]*Quaternion.Euler(k*4,0,0);
            var look=Quaternion.Inverse(Weapons.BodyRotation(v))*(observer-v.position);
            float yaw=Mathf.Clamp(Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg,-35,35);
            float greet=full?Ease(t/.4f)*(1-Ease((t-(exit?.5f:.6f))/.7f)):0;
            r.Head.localRotation=r.RestRot[r.Head]*Quaternion.Euler(k*8,yaw*greet,0);
            r.ShoulderL.localRotation=r.RestRot[r.ShoulderL]*Quaternion.Euler(-8*e,exit?-15*e:0,-18*e);
            r.ShoulderR.localRotation=r.RestRot[r.ShoulderR]*Quaternion.Euler(8*e,0,12*e);
            r.ElbowL.localRotation=r.RestRot[r.ElbowL]*Quaternion.Euler(-8*e,0,0);
            var blade=Vector3.Lerp(new Vector3(.60f,-.67f,.35f),new Vector3(.82f,.08f,-.35f),e).normalized;
            r.HandR.rotation=Quaternion.FromToRotation(r.HandR.TransformDirection(Samurai.BladeTip-Samurai.Grip),r.Mount.TransformDirection(blade))*r.HandR.rotation;
            r.HandL.localRotation=r.RestRot[r.HandL]*Quaternion.Euler(0,-10-(exit?15:0)*e,0);
        }
    }
}
