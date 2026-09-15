using System.Collections.Generic;
using UnityEngine;
namespace SakuraPreview
{
    public sealed class SakuraPose
    {
        readonly Transform root;
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        readonly Dictionary<string,Quaternion> rest=new Dictionary<string,Quaternion>();
        public SakuraPose(Transform model)
        {
            root=model;
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {bones[t.name]=t;rest[t.name]=t.localRotation;}
        }
        public void Apply(float speed,float phase,float gestureUntil)
        {
            foreach(var pair in bones)if(pair.Value!=root)pair.Value.localRotation=rest[pair.Key];
            float walk=Mathf.Clamp01(speed/.7f), swing=Mathf.Sin(phase)*25*walk;
            Rotate("arm_stretch.l",root.forward,48);
            Rotate("arm_stretch.r",root.forward,-48);
            Rotate("thigh_stretch.l",root.right,swing);
            Rotate("thigh_stretch.r",root.right,-swing);
            Rotate("leg_stretch.l",root.right,Mathf.Max(0,-Mathf.Sin(phase))*30*walk);
            Rotate("leg_stretch.r",root.right,Mathf.Max(0,Mathf.Sin(phase))*30*walk);
            Rotate("arm_stretch.l",root.right,-swing*.65f);
            Rotate("arm_stretch.r",root.right,swing*.65f);
            Rotate("spine_03.x",root.right,Mathf.Sin(phase*.55f)*1.2f);
            if(Time.realtimeSinceStartup<gestureUntil)
            {
                Rotate("arm_stretch.r",root.forward,100);
                Rotate("forearm_stretch.r",root.forward,70);
                Rotate("hand.r",root.forward,Mathf.Sin(Time.realtimeSinceStartup*10)*15);
                Rotate("head.x",root.right,Mathf.Sin(Time.realtimeSinceStartup*4)*4);
            }
        }
        void Rotate(string name,Vector3 axis,float angle)
        {Transform t;if(bones.TryGetValue(name,out t))t.rotation=Quaternion.AngleAxis(angle,axis)*t.rotation;}
    }
}

