using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AECT16RuntimeFix
{
    public static class AutoForestryPresentation
    {
        public static void Build(GameObject root,string assets,Material[] materials,Material metal,Material dark)
        {
            var lamp=AutoForestryMachinery.Box(root.transform,"WorkLamp",new Vector3(-2.7f,1.85f,-1.85f),new Vector3(.40f,.09f,.18f),metal);
            AutoForestryMachinery.Box(root.transform,"LampSupport",new Vector3(-2.7f,1.45f,-1.75f),new Vector3(.04f,.8f,.04f),dark);
            var light=lamp.AddComponent<Light>(); light.type=LightType.Point; light.color=new Color(1f,.82f,.57f);
            light.range=3.5f; light.intensity=.8f; light.shadows=LightShadows.None; light.enabled=false;
            var dustObject=new GameObject("Sawdust"); dustObject.transform.SetParent(root.transform,false);
            dustObject.transform.localPosition=new Vector3(-2.7f,1.05f,-2.16f);
            dustObject.transform.localRotation=Quaternion.Euler(-45,0,0);
            var dust=dustObject.AddComponent<ParticleSystem>();
            dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=dust.main; main.playOnAwake=false; main.loop=true; main.maxParticles=24;
            main.startLifetime=.45f;main.startSpeed=.65f;main.startSize=.018f;
            main.startColor=new Color(.51f,.32f,.13f,1); main.gravityModifier=.45f;
            main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var emission=dust.emission; emission.rateOverTime=12;
            var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=18;shape.radius=.025f;
            var particles=dust.GetComponent<ParticleSystemRenderer>();particles.renderMode=ParticleSystemRenderMode.Mesh;
            // Reuse a built-in cube mesh for tiny wood chips, avoiding a shader-dependent billboard.
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            particles.mesh=cube.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.DestroyImmediate(cube);
            particles.sharedMaterial=materials[4];particles.shadowCastingMode=ShadowCastingMode.Off;
            particles.receiveShadows=false;
            var near=new List<Renderer>(); var far=new List<Renderer>();
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
            {
                near.Add(r);
                string n=r.name;
                if(n=="Foundation"||n=="ControlCabinet"||n=="ControlScreen"||n=="SawTable"||n=="TableLeg"||n=="FeedCover"||n=="FeedCoverSide"||n=="OutputPallet"||n=="WoodworkCoarse"
                    ||n=="ForestrySpeed"||n=="ForestryPacker"||n=="ForestrySiren"||n.StartsWith("Timber")
                    ||n.StartsWith("RawStockLog")||n=="RawStockCrib"||n=="RawStockStop") far.Add(r);
            }
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(assets,"sawmill-lod.meshbin"))))
            {
                if(new string(reader.ReadChars(4))!="YFF1"||reader.ReadInt32()!=materials.Length) throw new InvalidDataException("Invalid forestry LOD header");
                int parts=reader.ReadInt32();if(parts<1||parts>32)throw new InvalidDataException("Invalid LOD parts");
                for(int p=0;p<parts;p++)
                {
                    int m=reader.ReadInt32(),count=reader.ReadInt32(),k=reader.ReadInt32();
                    if(m<0||m>=materials.Length||count<3||count>200000||k<3||k>600000||k%3!=0) throw new InvalidDataException("Invalid LOD dimensions");
                    var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var indices=new int[k];
                    for(int i=0;i<count;i++)
                    {
                        v[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                        n[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                        uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
                        var a=v[i];if(float.IsNaN(a.x+a.y+a.z)||Math.Abs(a.x)>5||a.y<0||a.y>4||Math.Abs(a.z)>3)throw new InvalidDataException("LOD outside footprint");
                    }
                    for(int i=0;i<k;i++){indices[i]=reader.ReadInt32();if(indices[i]<0||indices[i]>=count)throw new InvalidDataException("Invalid LOD index");}
                    var mesh=new Mesh{name="ForestryDistant"+p,indexFormat=IndexFormat.UInt32};
                    mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();mesh.RecalculateTangents();
                    var obj=new GameObject("SawmillDistantPart"+p);obj.transform.SetParent(root.transform,false);
                    obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var r=obj.AddComponent<MeshRenderer>();r.sharedMaterial=materials[m];r.shadowCastingMode=ShadowCastingMode.Off;far.Add(r);
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing LOD data");
            }
            var lod=root.AddComponent<LODGroup>();
            lod.SetLODs(new[]{new LOD(.20f,near.ToArray()){fadeTransitionWidth=.18f},new LOD(.008f,far.ToArray()){fadeTransitionWidth=.18f}});
            lod.RecalculateBounds();lod.fadeMode=LODFadeMode.CrossFade;lod.animateCrossFading=false;
        }
    }
}
