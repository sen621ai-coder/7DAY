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
        Renderer statusRenderer;
        Texture shownTexture;
        string shownText;
        bool shown;
        ScreenTargetOverlay targetOverlay;
        bool overlayFailed;
        public Renderer MarkerRenderer=>targetOverlay==null?null:targetOverlay.Renderer;
        public Renderer StatusRenderer
        {get{if(statusRenderer==null&&Status!=null)statusRenderer=Status.GetComponent<Renderer>();return statusRenderer;}}
        Material instanceMaterial;
        int colliderLayer;
        string colliderTag;
        int cycleChannel;
        float cycleAt;
        static Material frame,off;
        static Mesh displayMesh;
        static Texture2D frameTexture;
        static Font statusFont;
        const float FaceZ=ScreenLayout.FaceZ,LabelZ=ScreenLayout.LabelZ;

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
            if(frame!=null&&off!=null)return;
            Shader shader=Shader.Find("Unlit/Texture");if(shader==null||!shader.isSupported)shader=Shader.Find("Sprites/Default");
            off=shader!=null&&shader.isSupported?new Material(shader){name="Surveillance display"}:NativeMaterial("Surveillance display",Color.black);
            if(off.HasProperty("_Color"))off.color=Color.white;off.mainTexture=Texture2D.blackTexture;
            // Encode charcoal in the texture: native block tint updates must never whiten the cabinet.
            frameTexture=new Texture2D(1,1,TextureFormat.RGBA32,false){name="Surveillance charcoal"};
            frameTexture.SetPixel(0,0,new Color(.055f,.065f,.075f));frameTexture.Apply(false,true);
            frame=new Material(off){name="Surveillance charcoal frame",mainTexture=frameTexture};
            Log.Out("[Surveillance] Monitor shader="+off.shader.name+"; graphics="+SystemInfo.graphicsDeviceType);
        }
        static Mesh DisplayMesh(Mesh original)
        {
            if(displayMesh!=null)return displayMesh;
            // Keep Unity's tested cube geometry. The native renderer samples the
            // cube's -Z triangle set on the visible panel; its V is inverted.
            displayMesh=UnityEngine.Object.Instantiate(original);displayMesh.name="Surveillance display cube";
            var positions=displayMesh.vertices;var normals=displayMesh.normals;var uv=displayMesh.uv;
            bool front=false;
            for(int i=0;i<positions.Length;i++)
            {
                if(normals[i].z>.9f){uv[i]=new Vector2(positions[i].x+.5f,positions[i].y+.5f);front=true;}
                else if(normals[i].z<-.9f)uv[i].y=1f-uv[i].y;
            }
            if(!front)throw new InvalidOperationException("Display cube has no front UVs");
            displayMesh.uv=uv;return displayMesh;
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
            gameObject.AddComponent<RootTransformRefParent>().RootTransform=transform;
            // A root collider is also how the native model pool obtains accurate selection bounds.
            var hit=gameObject.AddComponent<BoxCollider>();hit.center=new Vector3(ScreenLayout.X,ScreenLayout.Y,ScreenLayout.Z);hit.size=new Vector3(ScreenLayout.Width,ScreenLayout.Height,ScreenLayout.Depth);
            var wire=new GameObject("WireOffset");wire.transform.SetParent(transform,false);wire.transform.localPosition=new Vector3(0,.25f,-.29f);
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            Materials();
            // ModelOffset.x cancels native even-width pivot correction. Root Y is the bottom of the cell.
            // Back sits 1 cm inside the rear cell boundary; a full-block wall can touch it.
            Box("MonitorBody",hit.center,hit.size,frame);
            var face=Box("LiveDisplay",new Vector3(-.5f,1.5f,FaceZ),new Vector3(3.68f,2.76f,.018f),off);
            var filter=face.GetComponent<MeshFilter>();filter.sharedMesh=DisplayMesh(filter.sharedMesh);
            Surface=face.GetComponent<Renderer>();Surface.shadowCastingMode=ShadowCastingMode.Off;Surface.receiveShadows=false;
            var label=new GameObject("MonitorStatus");label.transform.SetParent(transform,false);label.transform.localPosition=new Vector3(-.5f,1.5f,LabelZ);
            label.transform.localRotation=Quaternion.Euler(0,180,0);
            Status=label.AddComponent<TextMesh>();Status.text="请选择摄像头";Status.anchor=TextAnchor.MiddleCenter;Status.alignment=TextAlignment.Center;
            if(statusFont==null)statusFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","Arial"},64);
            Status.font=statusFont;
            Status.fontSize=64;Status.characterSize=.035f;Status.color=new Color(.68f,.84f,.92f);Status.gameObject.layer=colliderLayer;
            var textRenderer=Status.GetComponent<MeshRenderer>();textRenderer.shadowCastingMode=ShadowCastingMode.Off;textRenderer.receiveShadows=false;
            if(statusFont!=null)textRenderer.sharedMaterial=statusFont.material;
        }
        public void Bind(WorldBase world,Vector3i position)
        {
            World=world;Position=position;Retiring=false;cycleChannel=0;cycleAt=0;shown=false;
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
            var id=device.Channels[channel];label=channelLabels[channel];return id;
        }
        static readonly string[] channelLabels={"频道 1","频道 2","频道 3","频道 4"};
        public void Show(Texture texture,string text)
        {
            if(texture==null)targetOverlay?.Hide();
            if(shown&&shownTexture==texture&&shownText==text)return;
            bool textureChanged=!shown||shownTexture!=texture;
            shown=true;shownTexture=texture;shownText=text;
            if(textureChanged&&instanceMaterial!=null){if(instanceMaterial.HasProperty("_Color"))instanceMaterial.color=Color.white;instanceMaterial.mainTexture=texture??Texture2D.blackTexture;}
            if(Status!=null)
            {
                Status.text=text??"";Status.characterSize=texture==null?.035f:.022f;
                Status.transform.localPosition=texture==null?new Vector3(-.5f,1.5f,LabelZ):new Vector3(-.5f,2.78f,LabelZ);
                Status.gameObject.SetActive(!string.IsNullOrEmpty(text));
            }
        }
        // The game's font shader can draw through world geometry. Keep status text
        // visible only from the physical front of this wall-mounted monitor.
        public void FaceViewer(Camera viewer)
        {
            var label=StatusRenderer;
            if(viewer==null||Surface==null||label==null)return;
            var face=Surface.transform;
            label.enabled=Vector3.Dot(face.forward,viewer.transform.position-face.position)>0;
        }
        void LateUpdate()
        {
            var player=GameManager.Instance?.World?.GetPrimaryPlayer();
            FaceViewer(player==null?null:player.playerCamera);
        }
        public void ShowMarkers(TargetMarkerDetector detector,int width,int height)
        {
            if(detector==null||detector.Boxes.Count==0){targetOverlay?.Hide();return;}
            if(overlayFailed||Surface==null)return;
            try
            {
                if(targetOverlay==null)targetOverlay=new ScreenTargetOverlay(Surface);
                targetOverlay.Show(detector,width,height);
            }
            catch(Exception e){overlayFailed=true;targetOverlay?.Hide();Log.Warning("[Surveillance] Target overlay unavailable: "+e.Message);}
        }
        void OnDisable(){SurveillanceRenderService.Unregister(this);targetOverlay?.Hide();if(!Retiring)Show(null,"");World=null;}
        void OnDestroy(){SurveillanceRenderService.Unregister(this);targetOverlay?.Dispose();if(instanceMaterial!=null)UnityEngine.Object.Destroy(instanceMaterial);}
    }
}
