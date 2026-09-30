using System;
using System.Linq;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    // Add a rod pose after native animation, and restore before the next native frame.
    // Rotate the arm bones, not the item socket, so the skinned hands move as well.
    public sealed class NativeArmPose : IDisposable
    {
        readonly Transform[] bones=new Transform[2];
        readonly Quaternion[] original=new Quaternion[2],applied=new Quaternion[2];
        BodyAnimator body;
        bool posing;
        public void Restore()
        {
            if(!posing)return;
            for(int i=0;i<2;i++)if(bones[i]!=null&&Quaternion.Angle(bones[i].localRotation,applied[i])<.01f)
                bones[i].localRotation=original[i];
            posing=false;
        }
        public void Apply(EntityPlayerLocal player,Vector3 reference,Vector3 aim)
        {
            var avatar=player?.emodel?.avatarController as AvatarLocalPlayerController;
            var next=avatar!=null&&avatar.isFPV?avatar.fpsArms:null;
            if(next!=body) {
                Restore();body=next;bones[0]=bones[1]=null;
                if(body!=null) {
                    var animator=body.animator;
                    if(animator!=null&&animator.isHuman) {
                        bones[0]=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                        bones[1]=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    }
                    var root=body.Parts?.BodyObj;
                    var candidates=root!=null?root.GetComponentsInChildren<Transform>(true):new Transform[0];
                    for(int i=0;i<2;i++)if(bones[i]==null) {
                        bones[i]=FindLowerArm(candidates,i==0);
                    }
                    Log.Out("[PZAEC.Fishing] Pole arm bones right="+(bones[0]!=null?bones[0].name:"unavailable")+" left="+(bones[1]!=null?bones[1].name:"unavailable"));
                }
            }
            Apply(bones[0],bones[1],reference,aim);
        }
        public static Transform FindLowerArm(Transform[] candidates,bool right)
        {
            return candidates.FirstOrDefault(t=>{
                string name=t.name.ToLowerInvariant().Replace(" ","");
                string flat=name.Replace("_","").Replace(".","").Replace("-","");
                bool side=flat.Contains(right?"right":"left") || name.StartsWith(right?"r_":"l_") ||
                    name.EndsWith(right?"_r":"_l") || name.EndsWith(right?".r":".l");
                return side&&(flat.Contains("forearm")||flat.Contains("lowerarm"));
            });
        }
        public void Apply(Transform right,Transform left,Vector3 reference,Vector3 aim)
        {
            Restore();bones[0]=right;bones[1]=left;
            if(reference.sqrMagnitude<.001f||aim.sqrMagnitude<.001f)return;
            var delta=Quaternion.FromToRotation(reference,aim);
            delta=Quaternion.RotateTowards(Quaternion.identity,delta,75);
            for(int i=0;i<2;i++)if(bones[i]!=null) {
                original[i]=bones[i].localRotation;
                bones[i].rotation=delta*bones[i].rotation;
                applied[i]=bones[i].localRotation;
            }
            posing=true;
        }
        public void Dispose(){Restore();body=null;bones[0]=bones[1]=null;}
    }
}
