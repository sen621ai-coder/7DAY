using UnityEngine;

namespace AECT16RuntimeFix
{
    // Reused cosmetic geometry; triggered only by a confirmed CannonEvent.
    public static class ApacheMuzzleFlash
    {
        public sealed class Effect
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public Light Light;
            public readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
            public float Until;
            public int Shot;
        }
        private static Mesh mesh;
        private static Texture2D texture;
        private static Material material;
        private const float Duration=.06f;
        private static bool EnsureResources()
        {
            if(material!=null)return true;
            var shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Transparent");
            if(shader==null)return false;
            // Soft, tapered white-yellow core and orange edge, no external bundle.
            texture=new Texture2D(32,64,TextureFormat.RGBA32,false);
            texture.name="Apache flash gradient";texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color[32*64];
            for(int y=0;y<64;y++)for(int x=0;x<32;x++){
                float v=y/63f,u=Mathf.Abs(x/31f*2-1);
                float width=(1-v)*(.64f+.18f*Mathf.Sin(v*24));
                float edge=Mathf.Clamp01(1-u/Mathf.Max(.01f,width));
                float alpha=edge*edge*Mathf.Clamp01(v*16)*Mathf.Clamp01((1-v)*3);
                var c=Color.Lerp(new Color(1,.27f,.035f),new Color(1,.97f,.68f),edge*(1-v));c.a=alpha;
                pixels[y*32+x]=c;
            }
            texture.SetPixels(pixels);texture.Apply(false,true);
            material=new Material(shader){name="Apache flash unlit",mainTexture=texture,color=Color.white};
            mesh=new Mesh{name="Apache crossed muzzle flame"};
            mesh.vertices=new[]{
                new Vector3(-.18f,0,0),new Vector3(.18f,0,0),new Vector3(.18f,0,.65f),new Vector3(-.18f,0,.65f),
                new Vector3(0,-.18f,0),new Vector3(0,.18f,0),new Vector3(0,.18f,.65f),new Vector3(0,-.18f,.65f),
                new Vector3(-.12f,-.12f,.065f),new Vector3(.12f,-.12f,.065f),new Vector3(.12f,.12f,.065f),new Vector3(-.12f,.12f,.065f)};
            var uv=new Vector2[12];var indices=new int[36];
            for(int q=0;q<3;q++){
                int v=q*4,t=q*12;uv[v]=new Vector2(0,0);uv[v+1]=new Vector2(1,0);uv[v+2]=new Vector2(1,1);uv[v+3]=new Vector2(0,1);
                // Both sides support the fallback shader as well.
                int[] face={0,1,2,0,2,3,2,1,0,3,2,0};for(int i=0;i<12;i++)indices[t+i]=v+face[i];
            }
            mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();
            return true;
        }
        public static Effect Create(Transform barrel)
        {
            if(barrel==null||!EnsureResources())return null;
            var root=new GameObject("MuzzleFlash");root.hideFlags=HideFlags.DontSave;root.transform.SetParent(barrel,false);
            // OpenMuzzle starts at 1.20 and is .095 long, on the recoil assembly.
            root.transform.localPosition=new Vector3(0,0,1.295f);
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            var light=root.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.63f,.27f);
            light.range=2.5f;light.shadows=LightShadows.None;light.intensity=0;
            root.SetActive(false);
            return new Effect{Root=root,Renderer=renderer,Light=light};
        }
        public static void Trigger(Effect effect)
        {
            if(effect==null||effect.Root==null)return;
            effect.Shot++;effect.Until=Time.time+Duration;
            effect.Root.transform.localRotation=Quaternion.Euler(0,0,(effect.Shot*137)%360);
            float size=.85f+(effect.Shot%4)*.1f;effect.Root.transform.localScale=new Vector3(size,size,size);
            effect.Root.SetActive(true);Update(effect);
        }
        public static void Update(Effect effect)
        {
            if(effect==null||effect.Root==null||!effect.Root.activeSelf)return;
            float fade=Mathf.Clamp01((effect.Until-Time.time)/Duration);
            if(fade<=0){effect.Light.intensity=0;effect.Root.SetActive(false);return;}
            effect.Properties.SetColor("_Color",new Color(1,1,1,fade));effect.Renderer.SetPropertyBlock(effect.Properties);
            effect.Light.intensity=1.6f*fade;
        }
        // All effect roots belong to turrets and are destroyed by their existing cleanup.
        public static void Clear()
        {
            if(material!=null)Object.Destroy(material);if(texture!=null)Object.Destroy(texture);if(mesh!=null)Object.Destroy(mesh);
            material=null;texture=null;mesh=null;
        }
    }
}
