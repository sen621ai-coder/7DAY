using UnityEngine;
namespace PZAEC.Mecha
{
    // Complete Form choreography and geometric safety, independent of player transfer.
    public static class Ceremony
    {
        public const float EnterSeconds=7, ExitSeconds=6, EnterTransfer=4.2f, ExitTransfer=3.65f, ExitHold=4.1f;
        public const float EnterKneelStart=1.65f,EnterKneelEnd=2.55f,ExitKneelStart=1.1f,ExitKneelEnd=2;
        public const float EnterOpen=3.1f,ExitOpen=2.65f,EnterClose=4.3f,ExitClose=4.1f;
        public static float KneelEnd(bool exit){return exit?ExitKneelEnd:EnterKneelEnd;}
        public static float OpenAt(bool exit){return exit?ExitOpen:EnterOpen;}
        public static float CloseAt(bool exit){return exit?ExitClose:EnterClose;}
        public static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
        public static float Kneel(bool exit,float t)
        {
            float start=exit?ExitKneelStart:EnterKneelStart,end=KneelEnd(exit),rise=exit?4.95f:5.25f,stand=exit?5.75f:6.6f;
            if(t<start)return 0;if(t<end)return Ease((t-start)/(end-start));
            if(t<end+.28f)return 1+.045f*Mathf.Sin((t-end)/.28f*Mathf.PI);
            if(t<rise)return 1;return 1-Ease((t-rise)/(stand-rise));
        }
        public static float Hatch(bool exit,bool full,float t)
        {
            if(!full)return t<.35f?Ease(t/.35f):1-Ease((t-.35f)/.15f);
            float open=OpenAt(exit),close=CloseAt(exit),openDuration=exit?.7f:.75f;
            return t<open?0:t<open+openDuration?Ease((t-open)/openDuration):t<close?1:1-Ease((t-close)/.65f);
        }
        public static float Equipment(bool exit,float t)
        {float rise=exit?4.95f:5.25f,stand=exit?5.75f:6.6f;return t<.45f?0:t<1.1f?Ease((t-.45f)/.65f):t<rise?1:1-Ease((t-rise)/(stand-rise));}
        public static Vector3 FootTarget(Model.Rig r,bool exit,float t,int side,Vector3 home)
        {
            // Place one foot while the other supports the body, then lower on planted soles.
            float a=exit?.45f:.65f,b=exit?.75f:1.05f,c=exit?1.05f:1.5f;
            float progress=side==0?Mathf.Clamp01((t-a)/(b-a)):Mathf.Clamp01((t-b)/(c-b));
            return home+r.Mount.forward*(side==0?-.40f:.20f)*Ease(progress)+r.Mount.right*(side==0?-.04f:.04f)*Ease(progress)+r.Mount.up*(Mathf.Sin(progress*Mathf.PI)*.09f);
        }
        public static void SwordTarget(bool exit,float t,out Vector3 grip,out Vector3 direction,out Vector3 normal)
        {
            float e=Equipment(exit,t);grip=Vector3.Lerp(new Vector3(1.22f,2.12f,.60f),new Vector3(1.25f,2.30f,.60f),e);
            direction=Vector3.Slerp(new Vector3(.08f,-.79f,.60f),new Vector3(.10f,-.15f,.98f),e).normalized;normal=Vector3.right;
        }
        public static bool Stable(EntityVehicle v)
        {var rb=v.vehicleRB;return rb!=null&&rb.velocity.sqrMagnitude<=.16f&&Vector3.Dot(rb.rotation*Vector3.up,Vector3.up)>.95f&&!Locomotion.Get(v).HoverOn&&!Locomotion.Get(v).Boost&&GroundSupport.IsGrounded(v)&&!Traversal.Active(v);}
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
            float brace=Ease((t-(exit?.45f:.8f))/.7f)*(1-Ease((t-(exit?5.75f:6.6f))/.4f));
            r.Torso.localPosition=r.TorsoBasePosition+new Vector3(.055f*brace,-k*.72f-.045f*brace,.055f*brace);
            r.Torso.localRotation=r.RestRot[r.Torso]*Quaternion.Euler(k*10,0,-brace*2);
            var look=Quaternion.Inverse(Weapons.BodyRotation(v))*(observer-v.position);
            float yaw=Mathf.Clamp(Mathf.Atan2(look.x,look.z)*Mathf.Rad2Deg,-35,35);
            float greet=full?Ease(t/.4f)*(1-Ease((t-(exit?.5f:.6f))/.7f)):0;
            r.Head.localRotation=r.RestRot[r.Head]*Quaternion.Euler(k*8,yaw*greet,0);
            r.ShoulderL.localRotation=r.RestRot[r.ShoulderL]*Quaternion.Euler(-8*e,exit?-15*e:0,-18*e);
            r.ShoulderR.localRotation=r.RestRot[r.ShoulderR]*Quaternion.Euler(8*e,0,12*e);
            r.ElbowL.localRotation=r.RestRot[r.ElbowL]*Quaternion.Euler(-8*e,0,0);
            // The final sword target is resolved after the planted foot IK.
            r.HandL.localRotation=r.RestRot[r.HandL]*Quaternion.Euler(0,-10-(exit?15:0)*e,0);
        }
    }
}
