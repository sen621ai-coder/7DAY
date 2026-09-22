using System;
using System.Collections.Generic;
using UnityEngine;

namespace YFAutomation.CargoDrones
{
    // Formal hub origin is its block bottom; the legacy QA crate used a raised origin.
    public static class CargoHubModel
    {
        public const float LandingHeight=1.25f; // Rest-pose collision bottom clears the one-block solid base.
        public const float DeckHeight=.9706f; // Buster landed minimum Y is -0.2294026.

        static void Box(Transform root,string name,Vector3 position,Vector3 size,Material material,float yaw=0)
        {
            CargoDroneModel.Part(root,name,position,size,material).localRotation=Quaternion.Euler(0,yaw,0);
        }
        static Vector3[] Ring(float half,float corner,float y)
        {
            return new[]{new Vector3(-half+corner,y,-half),new Vector3(half-corner,y,-half),new Vector3(half,y,-half+corner),new Vector3(half,y,half-corner),new Vector3(half-corner,y,half),new Vector3(-half+corner,y,half),new Vector3(-half,y,half-corner),new Vector3(-half,y,-half+corner)};
        }
        static Mesh Plate(Transform root,string name,float half,float corner,float bottom,float top,float bevel,Material material)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            Action<Vector3,Vector3,Vector3,Vector3> face=(a,b,c,normal)=>
            {
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var swap=b;b=c;c=swap;}
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            };
            var low=Ring(half,corner,bottom);var middle=Ring(half,corner,top-bevel);var high=Ring(half-bevel,corner*.9f,top);
            for(int i=0;i<8;i++)
            {
                int j=(i+1)%8;var outward=(low[i]+low[j])*.5f;outward.y=0;
                face(new Vector3(0,top,0),high[i],high[j],Vector3.up);
                face(new Vector3(0,bottom,0),low[i],low[j],Vector3.down);
                face(low[i],low[j],middle[j],outward);face(low[i],middle[j],middle[i],outward);
                face(middle[i],middle[j],high[j],outward+Vector3.up);face(middle[i],high[j],high[i],outward+Vector3.up);
            }
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var part=new GameObject(name);part.transform.SetParent(root,false);part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=material;return mesh;
        }
        public static GameObject Create()
        {
            var root=new GameObject("CargoHub");var owner=root.AddComponent<CargoModelMaterials>();
            var charcoal=CargoDroneModel.Material(new Color(.055f,.065f,.06f));
            var shell=CargoDroneModel.Material(new Color(.48f,.50f,.46f));
            var steel=CargoDroneModel.Material(new Color(.24f,.27f,.25f));
            var rubber=CargoDroneModel.Material(new Color(.09f,.105f,.095f));
            var green=CargoDroneModel.Material(new Color(.26f,.64f,.10f));
            var caution=CargoDroneModel.Material(new Color(.63f,.43f,.12f));
            owner.Values=new[]{charcoal,shell,steel,rubber,green,caution};
            foreach(var mat in owner.Values){if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",.24f);}
            rubber.SetFloat("_Metallic",0);green.EnableKeyword("_EMISSION");green.SetColor("_EmissionColor",new Color(.10f,.30f,.025f));
            var temporaryMeshes=new List<Mesh>();
            try
            {
                var p=root.transform;
                temporaryMeshes.Add(Plate(p,"LowerFrame",.92f,.22f,.055f,.20f,.035f,charcoal));
                temporaryMeshes.Add(Plate(p,"PowerHousing",.79f,.20f,.20f,.80f,.035f,steel));
                temporaryMeshes.Add(Plate(p,"DeckUnderlay",.97f,.25f,.78f,.89f,.025f,charcoal));
                temporaryMeshes.Add(Plate(p,"ArmorDeck",.95f,.24f,.875f,.951f,.025f,shell));
                temporaryMeshes.Add(Plate(p,"LandingGrip",.70f,.20f,.95f,DeckHeight,.008f,rubber));
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                {
                    Box(p,"Foot",new Vector3(x*.69f,.045f,z*.69f),new Vector3(.31f,.09f,.31f),rubber);
                    Box(p,"CornerColumn",new Vector3(x*.65f,.48f,z*.65f),new Vector3(.13f,.60f,.13f),charcoal);
                    Box(p,"CornerArmor",new Vector3(x*.71f,.51f,z*.71f),new Vector3(.15f,.46f,.055f),shell,-x*z*45);
                    var screw=CargoDroneModel.Part(p,"DeckBolt",new Vector3(x*.74f,.954f,z*.61f),new Vector3(.045f,.007f,.045f),steel,PrimitiveType.Cylinder);
                    Box(p,"BoltSlot",new Vector3(x*.74f,.9615f,z*.61f),new Vector3(.022f,.002f,.004f),charcoal,45);
                }
                // Segmented guidance lights, access-panel seams and recessed ventilation.
                for(int side=0;side<4;side++)
                {
                    var rotation=Quaternion.Euler(0,side*90,0);
                    Action<string,Vector3,Vector3,Material> edge=(name,at,size,mat)=>Box(p,name,rotation*at,size,mat,side*90);
                    edge("LightRecess",new Vector3(0,.932f,.924f),new Vector3(.82f,.025f,.018f),charcoal);
                    for(int segment=-1;segment<=1;segment++)edge("GuidanceLight",new Vector3(segment*.25f,.937f,.936f),new Vector3(.19f,.012f,.012f),green);
                    edge("PanelSeam",new Vector3(0,.955f,.814f),new Vector3(1.1f,.002f,.014f),charcoal);
                    edge("VentRecess",new Vector3(0,.48f,.799f),new Vector3(.89f,.30f,.019f),charcoal);
                    for(int slat=0;slat<5;slat++)edge("VentSlat",new Vector3(0,.37f+slat*.055f,.816f),new Vector3(.76f,.021f,.029f),steel);
                    edge("GuideMark",new Vector3(0,.972f,.60f),new Vector3(.28f,.002f,.025f),green);
                    for(int stripe=-1;stripe<=1;stripe++)edge("CautionMark",new Vector3(stripe*.14f,.956f,.856f),new Vector3(.065f,.002f,.046f),caution);
                }
                // A recessed front console stays below the touchdown surface and rotor envelope.
                Box(p,"ConsoleFrame",new Vector3(0,.73f,-.82f),new Vector3(.54f,.17f,.06f),charcoal);
                Box(p,"ConsoleScreen",new Vector3(-.08f,.74f,-.854f),new Vector3(.28f,.095f,.008f),rubber);
                for(int bar=0;bar<3;bar++)Box(p,"ChargeReadout",new Vector3(-.16f+bar*.075f,.74f,-.86f),new Vector3(.046f,.056f,.004f),green);
                Box(p,"ConsoleSwitch",new Vector3(.16f,.74f,-.857f),new Vector3(.055f,.055f,.014f),caution);
                // Merge fixed parts by material: six renderers instead of one per bolt/slat.
                var filters=root.GetComponentsInChildren<MeshFilter>();var combined=new List<Mesh>();owner.Meshes=new Mesh[owner.Values.Length];
                for(int i=0;i<owner.Values.Length;i++)
                {
                    var entries=new List<CombineInstance>();foreach(var filter in filters)if(filter.GetComponent<Renderer>().sharedMaterial==owner.Values[i])entries.Add(new CombineInstance{mesh=filter.sharedMesh,transform=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                    var mesh=new Mesh{name="CargoHubSurface"+i};owner.Meshes[i]=mesh;mesh.CombineMeshes(entries.ToArray(),true,true);mesh.UploadMeshData(true);combined.Add(mesh);
                    var surface=new GameObject("HubSurface"+i);surface.transform.SetParent(p,false);surface.AddComponent<MeshFilter>().sharedMesh=mesh;surface.AddComponent<MeshRenderer>().sharedMaterial=owner.Values[i];
                }
                foreach(var filter in filters){filter.gameObject.SetActive(false);UnityEngine.Object.Destroy(filter.gameObject);}
                var anchor=new GameObject("LandingAnchor").transform;anchor.SetParent(p,false);anchor.localPosition=new Vector3(0,LandingHeight,0);
                return root;
            }
            catch{UnityEngine.Object.Destroy(root);throw;}
            finally{foreach(var mesh in temporaryMeshes)UnityEngine.Object.Destroy(mesh);}
        }
    }
}
