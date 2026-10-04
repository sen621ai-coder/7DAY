using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PZAEC.Mecha;
public static class EquipmentOwnershipQA
{
    static string folder;
    static string Role(SkinnedMeshRenderer s){return s.GetComponent<MechaRenderPart>().Role;}
    static Vector3[][] Bake(SkinnedMeshRenderer[] skins)
    {
        var result=new Vector3[skins.Length][];var mesh=new Mesh();
        for(int i=0;i<skins.Length;i++){skins[i].BakeMesh(mesh);result[i]=mesh.vertices;for(int j=0;j<result[i].Length;j++)result[i][j]=skins[i].transform.TransformPoint(result[i][j]);}
        UnityEngine.Object.DestroyImmediate(mesh);return result;
    }
    static void Capture(Camera camera,Model.Rig rig,SkinnedMeshRenderer[] skins,string name,Vector3 view,bool colors,string only=null)
    {
        var objects=new List<GameObject>();var materials=new List<Material>();var enabled=skins.Select(s=>s.enabled).ToArray();var helpers=rig.Mount.GetComponentsInChildren<MeshRenderer>(true);var helperEnabled=helpers.Select(s=>s.enabled).ToArray();if(only!=null)foreach(var helper in helpers)helper.enabled=false;
        try{
            for(int i=0;i<skins.Length;i++){
                var skin=skins[i];skin.enabled=false;string role=Role(skin);if(only!=null&&!role.StartsWith(only))continue;
                var mesh=new Mesh();skin.BakeMesh(mesh);var go=new GameObject("Inspection baked real mesh");objects.Add(go);go.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);go.transform.localScale=skin.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;
                Material material;
                if(colors){material=new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));material.color=role.StartsWith("Sword")?new Color(1,.65f,.08f):role=="WingL"?new Color(.1f,.8f,1):role=="WingR"?new Color(.9f,.15f,.75f):role=="Backpack"?new Color(.2f,.9f,.35f):new Color(.48f,.52f,.58f);}
                else material=new Material(skin.sharedMaterial);
                materials.Add(material);go.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
            var focus=rig.Mount.position+Vector3.up*(only=="Leg"?.70f:1.5f);camera.transform.position=focus+rig.Mount.rotation*view;camera.transform.LookAt(focus);camera.Render();
            var rt=camera.targetTexture;var old=RenderTexture.active;RenderTexture.active=rt;var png=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);png.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);RenderTexture.active=old;
        }finally{for(int i=0;i<helpers.Length;i++)helpers[i].enabled=helperEnabled[i];for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];foreach(var go in objects){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}foreach(var mat in materials)UnityEngine.Object.DestroyImmediate(mat);}
    }
    public static string[] Run(World world,EntityVehicle v,Model.Rig rig,Camera camera,string output)
    {
        folder=Path.Combine(output,"fresh-rig-inspection");Directory.CreateDirectory(folder);var results=new List<string>();
        var skins=rig.Mount.GetComponentsInChildren<SkinnedMeshRenderer>(true);var rb=v.vehicleRB;var position=rb.position;var rotation=rb.rotation;
        var oldRT=camera.targetTexture;var oldBg=camera.backgroundColor;var oldFov=camera.fieldOfView;var rt=new RenderTexture(960,720,24);camera.targetTexture=rt;camera.backgroundColor=new Color(.045f,.06f,.085f);camera.fieldOfView=37;
        Action<string,bool> check=(n,ok)=>results.Add((ok?"PASS ":"FAIL ")+"FRESH RIG "+n);
        try{
            Boarding.Clear();Gait.Clear();Samurai.Stop(v);var m=Locomotion.Get(v);m.Grounded=true;m.HoverOn=m.Boost=false;m.WingBlend=m.Blend=0;m.FlightMode=Flight.Phase.Ground;
            // Independently reviewed source-face landmarks in the plate marked by
            // the user. Joint independence alone does not prove semantic ownership.
            foreach(var point in new[]{new Vector3(.5578825f,1.4157172f,.05812766f),new Vector3(.53124976f,1.250988f,-.1441368f),new Vector3(.454651f,1.053064f,-.33873782f),new Vector3(.3898234f,.9103601f,-.4306492f)}){
                float nearest=float.MaxValue;string actual=null;
                foreach(var skin in skins){var landmarkVertices=skin.sharedMesh.vertices;var ix=skin.sharedMesh.triangles;
                    for(int i=0;i<ix.Length;i+=3){float d=Vector3.Distance((landmarkVertices[ix[i]]+landmarkVertices[ix[i+1]]+landmarkVertices[ix[i+2]])/3,point);if(d<nearest){nearest=d;actual=Role(skin);}}
                }
                check("source-reviewed lower fin face is bound to WingR at "+point+" actual="+actual,nearest<.00001f&&actual=="WingR");
            }
            rig.ResetPose();var baseline=Bake(skins);
            var hubs=skins.Where(s=>s.name.StartsWith("Complete_KneeHub")).ToArray();
            check("two closed knee mechanisms loaded",hubs.Length==2);
            var soleL=rig.FootL.position;var soleR=rig.FootR.position;
            foreach(float drop in new[]{0f,.20f,.40f,.55f}){
                rig.ResetPose();rig.Torso.position-=rig.Mount.up*drop;
                Gait.Solve(rig,0,soleL,rig.Mount.up);Gait.Solve(rig,1,soleR,rig.Mount.up);
                var hubPoints=Bake(hubs);float error=0;
                for(int h=0;h<hubs.Length;h++){
                    var bone=hubs[h].bones[hubs[h].sharedMesh.boneWeights[0].boneIndex0];
                    // The axle must stay centred on the actual animated hinge.
                    var local=hubPoints[h].Select(p=>bone.InverseTransformPoint(p)).ToArray();
                    var min=local.Aggregate(Vector3.Min);var max=local.Aggregate(Vector3.Max);
                    error=Mathf.Max(error,((min+max)*.5f).magnitude);
                }
                check("knee hubs remain centred through bilateral IK drop="+drop+" error="+error,error<.001f);
                Capture(camera,rig,skins,"knees-front-"+drop.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),new Vector3(0,.1f,3.4f),false,"Leg");
                Capture(camera,rig,skins,"knees-side-"+drop.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),new Vector3(2.4f,.1f,2.6f),false,"Leg");
            }
            rig.ResetPose();
            var changes=new List<object>();
            foreach(string part in new[]{"Sword","WingL","WingR","HipL","HipR"}){
                rig.ResetPose();var joint=part=="Sword"?rig.Sword:part=="WingL"?rig.WingL:part=="WingR"?rig.WingR:part=="HipL"?rig.HipL:rig.HipR;
                joint.localRotation=rig.RestRot[joint]*Quaternion.Euler(31,67,43);if(part=="Sword")joint.localPosition+=Vector3.right*.8f;
                var changed=Bake(skins);float leaked=0,moved=0;int affected=0;
                for(int i=0;i<skins.Length;i++){
                    string role=Role(skins[i]);bool expected=part=="Sword"?role.StartsWith("Sword"):part.StartsWith("Wing")?role==part:role=="Leg"+part.Substring(3);
                    float delta=0;for(int j=0;j<changed[i].Length;j++)delta=Mathf.Max(delta,Vector3.Distance(changed[i][j],baseline[i][j]));
                    changes.Add(new {perturb=part,role=role,maxVertexMotion=delta});if(expected){moved=Mathf.Max(moved,delta);affected+=changed[i].Length;}else leaked=Mathf.Max(leaked,delta);
                }
                check(part+" isolated motion cannot drag other equipment, leak="+leaked+" moved="+moved+" vertices="+affected,leaked<.001f&&moved>.1f);
                if(part=="Sword"){Capture(camera,rig,skins,"isolated-sword-moved",new Vector3(-4,1,6),true);Capture(camera,rig,skins,"isolated-sword-moved-rear",new Vector3(4,1,-6),true);}
            }
            rig.ResetPose();
            var views=new[]{new Vector3(0,.2f,6),new Vector3(6,.2f,0),new Vector3(0,.2f,-6),new Vector3(-6,.2f,0)};
            for(int i=0;i<views.Length;i++){
                Capture(camera,rig,skins,"ownership-"+i,views[i],true);Capture(camera,rig,skins,"sword-only-"+i,views[i],false,"Sword");Capture(camera,rig,skins,"wings-only-"+i,views[i],false,"Wing");
            }
            var rear=skins.Where(s=>Role(s)=="Backpack"||Role(s).StartsWith("Wing")).ToArray();var restLocal=new Vector3[rear.Length][];var owner=new Transform[rear.Length][];var mesh=new Mesh();int vertices=0;
            for(int i=0;i<rear.Length;i++){rear[i].BakeMesh(mesh);restLocal[i]=mesh.vertices;var weights=rear[i].sharedMesh.boneWeights;owner[i]=weights.Select(w=>rear[i].bones[w.boneIndex0]).ToArray();for(int j=0;j<restLocal[i].Length;j++)restLocal[i][j]=owner[i][j].InverseTransformPoint(rear[i].transform.TransformPoint(restLocal[i][j]));vertices+=restLocal[i].Length;}
            float maxRigidError=0;int samples=0;var curve=new List<object>();
            for(int frame=0;frame<240;frame++){
                float t=frame/30f;float yaw=75*Mathf.Sin(t*1.7f);rb.rotation=Quaternion.Euler(0,yaw,0);rb.position=position+new Vector3(Mathf.Sin(t)*2,0,Mathf.Sin(t*.7f)*3);v.SetPosition(rb.position+Origin.position);Gait.Update(world,v,rig,1f/30);
                if(frame%6==0){float error=0;for(int i=0;i<rear.Length;i++){rear[i].BakeMesh(mesh);var points=mesh.vertices;for(int j=0;j<points.Length;j++)error=Mathf.Max(error,Vector3.Distance(restLocal[i][j],owner[i][j].InverseTransformPoint(rear[i].transform.TransformPoint(points[j]))));}maxRigidError=Mathf.Max(maxRigidError,error);samples+=vertices;curve.Add(new{frame,yaw,error});}
                Capture(camera,rig,skins,"turn-colored-"+frame.ToString("D3"),new Vector3(0,1,-6),true);
                if(frame%2==0)Capture(camera,rig,skins,"turn-textured-"+(frame/2).ToString("D3"),new Vector3(3,.8f,-6),false);
            }
            check("all back/wing vertices retain bone-local positions during repeated forward/reverse steering; max="+maxRigidError+" samples="+samples,maxRigidError<.001f&&samples>100000);
            UnityEngine.Object.DestroyImmediate(mesh);File.WriteAllText(Path.Combine(folder,"metrics.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{isolations=changes,turn=curve,maxRigidError,samples,rearVertices=vertices},Newtonsoft.Json.Formatting.Indented));
        }finally{rb.position=position;rb.rotation=rotation;v.SetPosition(position+Origin.position);Gait.Clear();Samurai.Stop(v);rig.ResetPose();camera.targetTexture=oldRT;camera.backgroundColor=oldBg;camera.fieldOfView=oldFov;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        return results.ToArray();
    }
}
