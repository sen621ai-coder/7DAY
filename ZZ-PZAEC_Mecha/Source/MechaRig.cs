using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Mecha
{
    // Offline-authored rigid bindings. All positions are fitted entity-local metres.
    #pragma warning disable 0649 // Populated by JSON deserialization.
    public static class RobotRig
    {
        sealed class Joint { public string name, parent; public float[] position; }
        sealed class Part { public int node, primitive, material, offset, vertices, indices; public string nodeName, joint,role,generatedRepair; }
        sealed class Document { public string sourceSha256; public int sourceParts, triangles; public bool skinned; public int[] sourceNodes; public Joint[] joints; public Part[] parts; }
        static string Role(string joint){return joint.StartsWith("Shoulder")||joint.StartsWith("Elbow")||joint.StartsWith("Hand")?"Arm"+joint[joint.Length-1]:joint;}
        static Vector3 V(float[] p) { return new Vector3(p[0],p[1],p[2]); }
        public static void Build(Transform mount, Material[] materials, int layer,string stem="combat_robot")
        {
            var doc=JsonConvert.DeserializeObject<Document>(File.ReadAllText(System.IO.Path.Combine(Model.Path,stem+"_rig.json")));
            using(var sha=System.Security.Cryptography.SHA256.Create())
            using(var source=File.OpenRead(System.IO.Path.Combine(Model.Path,stem+".glb")))
                if(BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant()!=doc.sourceSha256)
                    throw new InvalidDataException("Mecha rig belongs to a different GLB; run Prepare-Rig.py");
            var joints=new Dictionary<string,Transform>();
            foreach(var j in doc.joints)
            {
                var t=new GameObject("Mecha"+j.name).transform;
                t.SetParent(mount,false); t.localPosition=V(j.position);
                if(!string.IsNullOrEmpty(j.parent)) t.SetParent(joints[j.parent],true);
                joints.Add(j.name,t);
            }
            int triangles=0; var sourceNodes=new HashSet<int>();
            using(var r=new BinaryReader(File.OpenRead(System.IO.Path.Combine(Model.Path,stem+"_rig.bin"))))
            foreach(var p in doc.parts)
            {
                if(p.vertices<=0 || p.indices%3!=0 || p.offset<0 || (long)p.offset+p.vertices*(doc.skinned?48L:32L)+p.indices*4L>r.BaseStream.Length)
                    throw new InvalidDataException("Bad rig geometry "+p.nodeName);
                var parent=doc.skinned?mount:joints[p.joint]; var origin=mount.InverseTransformPoint(parent.position);
                var v=new Vector3[p.vertices]; var n=new Vector3[p.vertices]; var uv=new Vector2[p.vertices]; var ix=new int[p.indices];
                var weights=doc.skinned?new BoneWeight[p.vertices]:null;
                r.BaseStream.Position=p.offset;
                for(int i=0;i<v.Length;i++) { v[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle())-origin; n[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()); uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());
                    if(doc.skinned){int a=r.ReadInt32(),b=r.ReadInt32();float x=r.ReadSingle(),y=r.ReadSingle();if(a<0||b<0||a>=doc.joints.Length||b>=doc.joints.Length||Mathf.Abs(x+y-1)>.001f)throw new InvalidDataException("Invalid skin weights");weights[i]=new BoneWeight{boneIndex0=a,boneIndex1=b,weight0=x,weight1=y};}}
                for(int i=0;i<ix.Length;i++) { ix[i]=r.ReadInt32(); if(ix[i]<0 || ix[i]>=v.Length) throw new InvalidDataException("Rig index out of range"); }
                bool bladeFinish=stem=="samurai_style_gundam_mecha"&&p.role=="SwordBlade";
                if(bladeFinish)for(int i=0;i<v.Length;i++)uv[i]=BladeFinish.UV(v[i]+origin);
                var mesh=new Mesh { name=p.nodeName+"_"+p.joint,indexFormat=IndexFormat.UInt32 };
                mesh.vertices=v; mesh.normals=n; mesh.uv=uv; mesh.triangles=ix; mesh.RecalculateBounds(); mesh.RecalculateTangents();
                var go=new GameObject(mesh.name); go.layer=layer; go.transform.SetParent(parent,false);
                Renderer renderer;
                if(doc.skinned){var bones=new Transform[doc.joints.Length];var bind=new Matrix4x4[bones.Length];for(int j=0;j<bones.Length;j++){bones[j]=joints[doc.joints[j].name];bind[j]=bones[j].worldToLocalMatrix*mount.localToWorldMatrix;}mesh.boneWeights=weights;mesh.bindposes=bind;var skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=bones;skin.rootBone=mount;skin.localBounds=new Bounds(new Vector3(0,1.6f,0),new Vector3(6,6,6));skin.quality=SkinQuality.Bone2;renderer=skin;}
                else{go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();}
                renderer.sharedMaterial=bladeFinish?BladeFinish.Material():materials[p.material];
                if(p.generatedRepair=="recessed-joint-interior-v1"){
                    var liner=new Material(materials[p.material]){name="Mecha_RecessedGraphite",color=new Color(.20f,.22f,.24f)};
                    // The source metallic map otherwise overrides these scalar controls.
                    liner.DisableKeyword("_METALLICGLOSSMAP");liner.SetTexture("_MetallicGlossMap",null);
                    liner.SetFloat("_Metallic",.25f);liner.SetFloat("_Glossiness",.15f);renderer.sharedMaterial=liner;
                }
                renderer.shadowCastingMode=ShadowCastingMode.On;
                var renderPart=go.AddComponent<MechaRenderPart>();renderPart.Role=p.role??Role(p.joint);
                if(p.generatedRepair=="recessed-joint-interior-v1")renderPart.OwnedMaterial=renderer.sharedMaterial;
                triangles+=ix.Length/3; sourceNodes.Add(p.node);
            }
            if((doc.sourceNodes!=null?doc.sourceNodes.Length:sourceNodes.Count)!=doc.sourceParts || triangles!=doc.triangles) throw new InvalidDataException("Incomplete mecha rig");
            Log.Out("[MechaRig] sourceParts="+doc.sourceParts+" rigidParts="+doc.parts.Length+" triangles="+triangles);
        }
    }
}
