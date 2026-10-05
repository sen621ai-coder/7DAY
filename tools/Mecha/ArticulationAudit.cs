using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Mecha;
// QA only: record rendered production poses without changing the runtime model.
public static class ArticulationAudit
{
    static readonly List<object> poses=new List<object>();
    static float[] Matrix(Matrix4x4 m){return new[]{m.m00,m.m01,m.m02,m.m03,m.m10,m.m11,m.m12,m.m13,m.m20,m.m21,m.m22,m.m23};}
    public static void Sample(Model.Rig r,string action,int frame)
    {
        if(r==null||r.Sword==null)return;
        var skin=r.Mount.GetComponentInChildren<SkinnedMeshRenderer>();if(skin==null)return;
        poses.Add(new {action,frame,root=Point(r.Mount.InverseTransformPoint(SwordMotion.Root(r))),tip=Point(r.Mount.InverseTransformPoint(SwordMotion.Tip(r))),ground=r.GroundY,bones=skin.bones.Select(b=>Matrix(r.Mount.worldToLocalMatrix*b.localToWorldMatrix)).ToArray()});
    }
    static float[] Point(Vector3 p){return new[]{p.x,p.y,p.z};}
    public static void Save(string folder){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"articulation-poses.json"),Newtonsoft.Json.JsonConvert.SerializeObject(poses));}
}
