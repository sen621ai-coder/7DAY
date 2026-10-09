using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace PZAEC.FlyingSword
{
    public static class SwordPresentation
    {
        sealed class RiderPose
        {
            public Transform Root,Hips,Chest;public Vector3 RootLocalPosition,HipPosition;public Quaternion RootLocal,ChestInput,ChestApplied;
            public Animator Animator;public Transform[] Bones;public Quaternion[] Rest;public float FootYaw;public bool Riding,ChestWritten;
            public Transform[] Upper;public Quaternion[] UpperRest;
        }
        static readonly Dictionary<int,RiderPose> poses=new Dictionary<int,RiderPose>();
        static readonly string[] legs={"LeftUpLeg","LeftLeg","LeftFoot","RightUpLeg","RightLeg","RightFoot"};
        static readonly string[] upper={"Spine","Spine1","Spine2","Neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand"};
        static GUIStyle style;
        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(EntityPlayerLocal),"OnGUI"),postfix:new HarmonyMethod(typeof(SwordPresentation),nameof(HUD)));
            foreach(var type in new[]{typeof(AvatarSDCSController),typeof(AvatarMultiBodyController)})
                h.Patch(AccessTools.Method(type,"Update"),postfix:new HarmonyMethod(typeof(SwordPresentation),nameof(AnimationState)));
        }
        public static Animator BodyAnimator(EntityPlayer p)
        {var avatar=p.emodel?.avatarController;var multi=avatar as AvatarCharacterController;return multi?.CharacterBody?.Animator??avatar?.GetAnimator();}
        static Transform Bone(Transform root,string name){return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);}
        static void AnimationState(AvatarController __instance)
        {
            var p=__instance.Entity as EntityPlayer;if(p==null||!(p.AttachedToEntity is EntityJuque))return;
            var multi=__instance as AvatarMultiBodyController;
            if(multi!=null){foreach(var body in multi.BodyAnimators)PrepareAnimator(p,body.Animator);}else PrepareAnimator(p,__instance.GetAnimator());
        }
        public static void PrepareAnimator(EntityPlayer p,Animator a)
        {
            if(a==null)return;bool empty=SwordRules.Deployed(p.inventory.holdingItemItemValue)>0;
            int hold=empty?0:p.inventory.holdingItem.HoldType.Value;
            int carry=hold>=0&&hold<AnimationDelayData.AnimationDelay.Length?AnimationDelayData.AnimationDelay[hold].Carry:0;
            foreach(var parameter in a.parameters){switch(parameter.name){
                case "InAir":case "IsMoving":case "IsCrouching":case "IsClimbing":a.SetBool(parameter.nameHash,false);break;
                case "Forward":case "Strafe":case "VerticalSpeed":a.SetFloat(parameter.nameHash,0);break;
                case "VehiclePose":a.SetInteger(parameter.nameHash,0);break;
                case "WeaponHoldType":a.SetInteger(parameter.nameHash,hold);break;
                case "WeaponCarry":a.SetInteger(parameter.nameHash,carry);break;
                case "IsAiming":a.SetBool(parameter.nameHash,!empty&&p.AimingGun);break;
                case "YLook":a.SetFloat(parameter.nameHash,p.inventory.IsHoldingGun()?Mathf.Clamp(-Mathf.DeltaAngle(0,p.rotation.x)/90,-1,1):0);break;
            }}
        }
        public static void Pose(EntityPlayer p)
        {
            // A late driver runs after native avatar IK. EntityAlive and local camera hooks
            // both arrive here; neither applies rotations a second time in the same frame.
            if(!(p.AttachedToEntity is EntityJuque)&&!poses.ContainsKey(p.entityId)&&SwordRules.Deployed(p.inventory?.holdingItemItemValue)==0)return;
            var driver=p.GetComponent<JuquePoseDriver>();if(driver==null)driver=p.gameObject.AddComponent<JuquePoseDriver>();driver.Player=p;
        }
        static RiderPose Create(Animator animator)
        {
            var root=animator.transform;var result=new RiderPose{Root=root,RootLocal=root.localRotation,RootLocalPosition=root.localPosition,Animator=animator,Bones=new Transform[6],Rest=new Quaternion[6],Hips=Bone(root,"Hips"),Chest=Bone(root,"Spine2")};
            // Sample the game's own neutral pose, then restore the live animation exactly.
            // This avoids caching a walk stride, seated pose, or bind-pose T stance.
            var transforms=root.GetComponentsInChildren<Transform>(true);var pos=transforms.Select(t=>t.localPosition).ToArray();var rot=transforms.Select(t=>t.localRotation).ToArray();var scale=transforms.Select(t=>t.localScale).ToArray();
            var clip=animator.runtimeAnimatorController?.animationClips.FirstOrDefault(c=>c.name.ToLowerInvariant().Contains("unarmed_idle"));
            if(clip!=null)clip.SampleAnimation(root.gameObject,0);
            result.Upper=upper.Select(n=>Bone(root,n)).ToArray();result.UpperRest=result.Upper.Select(t=>t==null?Quaternion.identity:t.localRotation).ToArray();
            for(int i=0;i<6;i++){result.Bones[i]=Bone(root,legs[i]);if(result.Bones[i]!=null)result.Rest[i]=result.Bones[i].localRotation;}
            if(result.Hips!=null)result.HipPosition=result.Hips.localPosition;
            for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=pos[i];transforms[i].localRotation=rot[i];transforms[i].localScale=scale[i];}
            return result;
        }
        public static void ApplyPose(EntityPlayer p,float dt)
        {
            var held=p.inventory?.GetHoldingItemTransform();var sword=held!=null?held.Find("JuqueVisual"):null;
            if(sword!=null)sword.GetComponent<Renderer>().enabled=SwordRules.Deployed(p.inventory.holdingItemItemValue)==0;
            var animator=BodyAnimator(p);if(animator==null)return;
            var v=p.AttachedToEntity as EntityJuque;
            RiderPose pose;if(!poses.TryGetValue(p.entityId,out pose)||pose.Animator!=animator){if(v==null)return;pose=Create(animator);poses[p.entityId]=pose;}
            if(v==null){if(pose.Riding){pose.Root.localRotation=pose.RootLocal;pose.Root.localPosition=pose.RootLocalPosition;pose.Riding=false;}return;}
            var visual=v.vehicleRB?.transform.Find("JuqueVisual");if(visual==null||pose.Hips==null||pose.Bones.Any(b=>b==null))return;
            bool gun=p.inventory.IsHoldingGun();var root=pose.Root;float aim=gun?Mathf.Clamp(Mathf.DeltaAngle(v.rotation.y,p.rotation.y),-135,135):0;
            if(!pose.Riding){pose.FootYaw=Mathf.Clamp(aim,-90,90);pose.Riding=true;}
            // This is a bounded front-facing interval, not a wrapping heading.
            // Do not turn through the prohibited rear arc when crossing +/-135.
            pose.FootYaw=Mathf.MoveTowards(pose.FootYaw,Mathf.Clamp(aim,-90,90),Mathf.Clamp(dt,0,.1f)*360);
            var deck=visual.rotation;var facing=deck*Quaternion.Euler(0,pose.FootYaw,0);var up=deck*Vector3.up;
            root.SetPositionAndRotation(visual.position+up*.025f,facing);
            pose.Hips.localPosition=pose.HipPosition+Vector3.down*.08f;
            pose.Hips.localRotation=Quaternion.Euler((v.Boost?8:2)*v.Throttle,0,0);
            if(pose.Chest!=null){var input=pose.Chest.localRotation;if(pose.ChestWritten&&Quaternion.Angle(input,pose.ChestApplied)<.01f)input=pose.ChestInput;pose.ChestInput=input;pose.ChestApplied=Quaternion.Euler(0,Mathf.Clamp(aim-pose.FootYaw,-50,50),0)*input;
                // Native gun aim clips stop short of the sword's steep downward
                // firing envelope. Add a small torso bend, keeping both hands and
                // the weapon in their native animation hierarchy (including recoil).
                float bend=p.inventory.IsHoldingGun()?Mathf.Max(0,-Mathf.DeltaAngle(0,p.rotation.x)-55)*.8f:0;
                if(bend>0){var parent=pose.Chest.parent.rotation;var right=Quaternion.Euler(0,p.rotation.y,0)*Vector3.right;pose.ChestApplied=Quaternion.Inverse(parent)*Quaternion.AngleAxis(bend,right)*parent*pose.ChestApplied;}
                pose.Chest.localRotation=pose.ChestApplied;pose.ChestWritten=true;}
            for(int i=0;i<6;i++)pose.Bones[i].localRotation=pose.Rest[i];
            if(!gun){for(int i=0;i<pose.Upper.Length;i++)if(pose.Upper[i]!=null)pose.Upper[i].localRotation=pose.UpperRest[i];pose.ChestWritten=false;
                for(int side=0;side<2;side++){int start=side==0?6:10;var arm=pose.Upper[start];var forearm=pose.Upper[start+1];var hand=pose.Upper[start+2];if(arm==null||forearm==null||hand==null)continue;
                    var target=root.TransformPoint(new Vector3(side==0?-.30f:.30f,.92f,.04f));var orientation=hand.rotation;SolveLeg(arm,forearm,hand,target,root.forward+root.right*(side==0?-.3f:.3f));hand.rotation=orientation;}
            }
            for(int side=0;side<2;side++){
                var offset=Quaternion.Euler(0,pose.FootYaw,0)*new Vector3(side==0?-.055f:.055f,0,side==0?.19f:-.19f);
                offset.x=Mathf.Clamp(offset.x,-.05f,.05f);offset.z-=.12f;
                var sole=visual.position+deck*offset+up*.025f;
                var foot=pose.Bones[side*3+2];var toe=Bone(foot,side==0?"LeftToeBase":"RightToeBase");float height=toe!=null?Mathf.Abs(toe.localPosition.y):.09f;
                var ankle=sole+up*height-facing*Vector3.forward*.045f;
                SolveLeg(pose.Bones[side*3],pose.Bones[side*3+1],foot,ankle,facing*Vector3.forward);
                foot.rotation=facing;
            }
        }
        static void SolveLeg(Transform upper,Transform lower,Transform foot,Vector3 target,Vector3 bend)
        {
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,foot.position);var delta=target-upper.position;float d=Mathf.Clamp(delta.magnitude,.01f,a+b-.005f);var axis=delta.normalized;
            var pole=Vector3.ProjectOnPlane(bend,axis).normalized;if(pole.sqrMagnitude<.1f)pole=Vector3.ProjectOnPlane(Vector3.forward,axis).normalized;
            float x=(a*a-b*b+d*d)/(2*d),y=Mathf.Sqrt(Mathf.Max(0,a*a-x*x));var knee=upper.position+axis*x+pole*y;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,knee-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,target-lower.position)*lower.rotation;
        }
        public static void Clear(){poses.Clear();}
        static void HUD(EntityPlayerLocal __instance)
        {
            var p=__instance;if(!SwordControls.Ready(p))return;var v=p.AttachedToEntity as EntityJuque;var item=p.inventory.holdingItemItemValue;
            if(v!=null&&v.OwnerSlot>=0&&v.OwnerSlot<p.inventory.SlotCount)item=p.inventory.GetItem(v.OwnerSlot).itemValue;
            if(!SwordRules.IsSword(item))return;if(style==null)style=new GUIStyle(GUI.skin.label){fontSize=18,alignment=TextAnchor.MiddleCenter};
            var old=GUI.color;try{float energy=SwordRules.Energy(item);GUI.color=new Color(.55f,1,.93f);string state=v!=null?(energy<=0?"灵力耗尽 · 缓降中":"御剑 · E 下剑回收"):SwordRules.Deployed(item)>0?"已御出 · 靠近后 E 上剑 / G 收回":"巨阙剑 · G 御出";
                var charge=SwordRuntime.State(p).ChargeAt;string text=state+"   灵力 "+energy.ToString("0")+" / "+SwordRules.Capacity[SwordRules.Tier(item)].ToString("0");
                if(charge>=0)text+="   蓄力 "+Mathf.RoundToInt(SwordRules.Charge(Time.time-charge)*100)+"%";
                GUI.Label(new Rect(Screen.width*.2f,Screen.height*.79f,Screen.width*.6f,36),text,style);
                float width=240;var bar=new Rect((Screen.width-width)/2,Screen.height*.79f+36,width,4);GUI.color=new Color(.08f,.16f,.17f,.8f);GUI.DrawTexture(bar,Texture2D.whiteTexture);bar.width*=Mathf.Clamp01(energy/SwordRules.Capacity[SwordRules.Tier(item)]);GUI.color=new Color(.35f,.95f,.85f);GUI.DrawTexture(bar,Texture2D.whiteTexture);
            }finally{GUI.color=old;}
        }
    }
    [DefaultExecutionOrder(10000)]
    public sealed class JuquePoseDriver:MonoBehaviour
    {
        public EntityPlayer Player;
        void LateUpdate(){if(Player!=null)SwordPresentation.ApplyPose(Player,Time.deltaTime);}
    }
}
