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
            var vertices=new List<Vector3>();var triangles=new List<int>();var uvs=new List<Vector2>();
            Action<Vector3,Vector3,Vector3,Vector3> face=(a,b,c,normal)=>
            {
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0){var swap=b;b=c;c=swap;}
                Func<Vector3,Vector2> project=v=>Mathf.Abs(normal.y)>=Mathf.Max(Mathf.Abs(normal.x),Mathf.Abs(normal.z))?new Vector2(v.x,v.z):Mathf.Abs(normal.x)>Mathf.Abs(normal.z)?new Vector2(v.z,v.y):new Vector2(v.x,v.y);
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);uvs.Add(project(a));uvs.Add(project(b));uvs.Add(project(c));triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
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
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            var part=new GameObject(name);part.transform.SetParent(root,false);part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=material;return mesh;
        }
        static float DetailHeight(int x,int y,bool grip)
        {
            if(grip)
            {
                int a=(x+y)&15,b=(x-y+64)&15;float groove=(a<2||b<2)?-.32f:.06f;
                return groove+.035f*Mathf.Sin(x*Mathf.PI/4)*Mathf.Sin(y*Mathf.PI/4);
            }
            return .055f*Mathf.Sin(y*Mathf.PI/2)+.025f*Mathf.Sin((x+y)*Mathf.PI/8)+.012f*Mathf.Cos(x*Mathf.PI/16);
        }
        static Texture2D DetailTexture(string name,bool grip,bool normal)
        {
            const int size=64;var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,normal){name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,anisoLevel=4};var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float h=DetailHeight(x,y,grip);
                if(normal)
                {
                    float dx=DetailHeight((x+1)&63,y,grip)-DetailHeight((x+63)&63,y,grip),dy=DetailHeight(x,(y+1)&63,grip)-DetailHeight(x,(y+63)&63,grip);
                    var n=new Vector3(-dx*(grip?1.8f:1.1f),-dy*(grip?1.8f:1.1f),1).normalized;byte r=(byte)Mathf.RoundToInt((n.x*.5f+.5f)*255),g=(byte)Mathf.RoundToInt((n.y*.5f+.5f)*255);
                    pixels[y*size+x]=new Color32(r,g,255,r);
                }
                else
                {
                    int variation=Mathf.RoundToInt(h*(grip?42:75));int fine=((x*37+y*17+x*y*3)&15)-7;byte value=(byte)Mathf.Clamp((grip?188:211)+variation+fine,72,238);
                    pixels[y*size+x]=new Color32(value,value,value,255);
                }
            }
            texture.SetPixels32(pixels);texture.Apply(true,true);return texture;
        }
        static void Finish(Material material,Texture2D albedo,Texture2D normal,float metallic,float smoothness,float tiles=2)
        {
            if(material.HasProperty("_MainTex")){material.SetTexture("_MainTex",albedo);material.SetTextureScale("_MainTex",new Vector2(tiles,tiles));}
            if(material.HasProperty("_BumpMap")){material.SetTexture("_BumpMap",normal);material.SetTextureScale("_BumpMap",new Vector2(tiles,tiles));material.EnableKeyword("_NORMALMAP");}
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",metallic);if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",smoothness);
        }
        public static GameObject Create()
        {
            var root=new GameObject("CargoHub");var owner=root.AddComponent<CargoModelMaterials>();
            // Match the Buster's cool neutral body and blue optical accents. The
            // darker value range keeps the pad grounded without the former olive cast.
            var charcoal=CargoDroneModel.Material(new Color(.06f,.067f,.072f));
            var shell=CargoDroneModel.Material(new Color(.36f,.375f,.38f));
            var steel=CargoDroneModel.Material(new Color(.235f,.25f,.26f));
            var rubber=CargoDroneModel.Material(new Color(.07f,.075f,.08f));
            var signal=CargoDroneModel.Material(new Color(.055f,.25f,.39f));
            var caution=CargoDroneModel.Material(new Color(.57f,.35f,.105f));
            owner.Values=new[]{charcoal,shell,steel,rubber,signal,caution};
            string[] materialNames={"CargoHubCharcoal","CargoHubShell","CargoHubSteel","CargoHubGrip","CargoHubSignal","CargoHubCaution"};for(int i=0;i<owner.Values.Length;i++)owner.Values[i].name=materialNames[i];
            var metalAlbedo=DetailTexture("HubBrushedMetal",false,false);var metalNormal=DetailTexture("HubBrushedMetalNormal",false,true);var gripAlbedo=DetailTexture("HubDiamondGrip",true,false);var gripNormal=DetailTexture("HubDiamondGripNormal",true,true);owner.Textures=new[]{metalAlbedo,metalNormal,gripAlbedo,gripNormal};
            Finish(charcoal,metalAlbedo,metalNormal,.62f,.19f,2.4f);Finish(shell,metalAlbedo,metalNormal,.68f,.31f,2.2f);Finish(steel,metalAlbedo,metalNormal,.76f,.27f,3);Finish(rubber,gripAlbedo,gripNormal,.08f,.13f,3.2f);Finish(signal,metalAlbedo,metalNormal,.5f,.34f,2);Finish(caution,metalAlbedo,metalNormal,.58f,.23f,2);
            signal.EnableKeyword("_EMISSION");signal.SetColor("_EmissionColor",new Color(.018f,.09f,.18f));
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
                    for(int segment=-1;segment<=1;segment++)edge("GuidanceLight",new Vector3(segment*.25f,.937f,.936f),new Vector3(.19f,.012f,.012f),signal);
                    edge("PanelSeam",new Vector3(0,.955f,.814f),new Vector3(1.1f,.002f,.014f),charcoal);
                    edge("VentRecess",new Vector3(0,.48f,.799f),new Vector3(.89f,.30f,.019f),charcoal);
                    for(int slat=0;slat<5;slat++)edge("VentSlat",new Vector3(0,.37f+slat*.055f,.816f),new Vector3(.76f,.021f,.029f),steel);
                    edge("GuideMark",new Vector3(0,.972f,.60f),new Vector3(.28f,.002f,.025f),signal);
                    for(int stripe=-1;stripe<=1;stripe++)edge("CautionMark",new Vector3(stripe*.14f,.956f,.856f),new Vector3(.065f,.002f,.046f),caution);
                }
                // A recessed front console stays below the touchdown surface and rotor envelope.
                Box(p,"ConsoleFrame",new Vector3(0,.73f,-.82f),new Vector3(.54f,.17f,.06f),charcoal);
                Box(p,"ConsoleScreen",new Vector3(-.08f,.74f,-.854f),new Vector3(.28f,.095f,.008f),rubber);
                for(int bar=0;bar<3;bar++)Box(p,"ChargeReadout",new Vector3(-.16f+bar*.075f,.74f,-.86f),new Vector3(.046f,.056f,.004f),signal);
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
