using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Surveillance
{
    public sealed class ScreenView : MonoBehaviour
    {
        public WorldBase World;
        public Vector3i Position;
        public bool Retiring;
        public Renderer Surface;
        public TextMesh Status;
        Material instanceMaterial;
        int colliderLayer;
        string colliderTag;
        int cycleChannel;
        float cycleAt;
        static Material frame,off;

        static Material NativeMaterial(string name,Color color)
        {
            var prefab=DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab",false);
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))foreach(var source in renderer.sharedMaterials)
            {
                if(source==null||source.shader==null||!source.shader.isSupported||source.renderQueue>=3000||!source.HasProperty("_MainTex")||!source.HasProperty("_Color"))continue;
                var material=new Material(source){name=name,color=color};material.mainTexture=Texture2D.whiteTexture;
                if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.16f);
                if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",Color.black);return material;
            }
            throw new InvalidOperationException("Supported native monitor material unavailable");
        }
        static void Materials()
        {
            if(frame!=null&&off!=null)return;frame=NativeMaterial("Surveillance charcoal frame",new Color(.055f,.065f,.075f));
            Shader shader=Shader.Find("Unlit/Texture");if(shader==null||!shader.isSupported)shader=Shader.Find("Sprites/Default");
            off=shader!=null&&shader.isSupported?new Material(shader){name="Surveillance display"}:NativeMaterial("Surveillance display",Color.black);
            off.color=Color.black;if(off.HasProperty("_MainTex"))off.mainTexture=Texture2D.blackTexture;
        }
        GameObject Box(string name,Vector3 position,Vector3 scale,Material material,bool collider=false)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.layer=colliderLayer;g.tag=colliderTag;g.transform.SetParent(transform,false);
            g.transform.localPosition=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;
            var c=g.GetComponent<Collider>();if(!collider){c.enabled=false;UnityEngine.Object.DestroyImmediate(c);}return g;
        }
        public void Build()
        {
            var native=DataLoader.LoadAsset<Transform>("@:Entities/Crafting/woodWorkBenchPrefab.prefab",false);var nativeCollider=native.GetComponentInChildren<Collider>(true);
            if(nativeCollider==null)throw new InvalidOperationException("Native collision template missing");
            colliderLayer=nativeCollider.gameObject.layer;colliderTag=nativeCollider.tag;gameObject.layer=colliderLayer;gameObject.tag=colliderTag;
            gameObject.AddComponent<RootTransformRefParent>().RootTransform=transform;Materials();
            var wire=new GameObject("WireOffset");wire.transform.SetParent(transform,false);wire.transform.localPosition=new Vector3(0,.25f,-.12f);
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            // Native MultiBlockDim=4,3,1 occupies local child cells x=-2..1 and y=0..2.
            // Center the physical panel on those cell centers so every rotation stays inside its footprint.
            Box("MonitorBody",new Vector3(-.5f,1f,0),new Vector3(3.96f,2.96f,.18f),frame,true);
            var face=Box("LiveDisplay",new Vector3(-.5f,1f,.101f),new Vector3(3.68f,2.76f,.018f),off);
            Surface=face.GetComponent<Renderer>();Surface.shadowCastingMode=ShadowCastingMode.Off;Surface.receiveShadows=false;
            var label=new GameObject("MonitorStatus");label.transform.SetParent(transform,false);label.transform.localPosition=new Vector3(-.5f,1f,.116f);
            Status=label.AddComponent<TextMesh>();Status.text="请选择摄像头";Status.anchor=TextAnchor.MiddleCenter;Status.alignment=TextAlignment.Center;
            Status.fontSize=64;Status.characterSize=.035f;Status.color=new Color(.68f,.84f,.92f);Status.gameObject.layer=colliderLayer;
            var textRenderer=Status.GetComponent<MeshRenderer>();textRenderer.shadowCastingMode=ShadowCastingMode.Off;textRenderer.receiveShadows=false;
        }
        public void Bind(WorldBase world,Vector3i position)
        {
            World=world;Position=position;Retiring=false;cycleChannel=0;cycleAt=0;
            var rr=GetComponent<RootTransformRefParent>();if(rr!=null)rr.RootTransform=transform;
            if(Surface!=null)
            {
                if(instanceMaterial==null)instanceMaterial=new Material(off){name="Surveillance display "+position};
                Surface.sharedMaterial=instanceMaterial;
            }
            SurveillanceRenderService.Register(this);
        }
        public bool IsScreenOn()
        {
            var te=World?.GetTileEntity(Position) as TileEntityPoweredBlock;return te!=null&&te.IsPowered&&te.IsToggled;
        }
        public Guid SelectedCamera(out string label)
        {
            label="请选择摄像头";var device=SurveillanceClient.At(Position);if(device==null)return Guid.Empty;
            int channel=Mathf.Clamp(device.Selected,0,3);
            if(device.Cycle)
            {
                if(Time.realtimeSinceStartup>=cycleAt){cycleAt=Time.realtimeSinceStartup+5;for(int n=1;n<=4;n++){int c=(cycleChannel+n)%4;if(device.Channels[c]!=Guid.Empty){cycleChannel=c;break;}}}
                channel=cycleChannel;
            }
            var id=device.Channels[channel];label="频道 "+(channel+1);return id;
        }
        public void Show(Texture texture,string text)
        {
            if(instanceMaterial!=null){instanceMaterial.color=texture==null?Color.black:Color.white;instanceMaterial.mainTexture=texture??Texture2D.blackTexture;}
            if(Status!=null)
            {
                Status.text=text??"";Status.characterSize=texture==null?.035f:.022f;
                Status.transform.localPosition=texture==null?new Vector3(-.5f,1f,.116f):new Vector3(-.5f,2.28f,.116f);
                Status.gameObject.SetActive(!string.IsNullOrEmpty(text));
            }
        }
        void OnDisable(){SurveillanceRenderService.Unregister(this);if(!Retiring)Show(null,"");World=null;}
        void OnDestroy(){SurveillanceRenderService.Unregister(this);if(instanceMaterial!=null)UnityEngine.Object.Destroy(instanceMaterial);}
    }
}
