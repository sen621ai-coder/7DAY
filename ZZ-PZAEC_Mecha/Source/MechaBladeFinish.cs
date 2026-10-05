using UnityEngine;

namespace PZAEC.Mecha
{
    // One blade-local finish for source surfaces AND the restored core. The
    // sculpture atlas crosses red armour/gold/white islands at the old cut.
    public static class BladeFinish
    {
        static Material material;
        static readonly Vector3 tip=new Vector3(.23f,.50f,-1.10f);
        static readonly Vector3 axis=(new Vector3(.73f,1.57f,.30f)-tip).normalized;
        static readonly Vector3 originalNormal=new Vector3(.96168816f,-.0582994f,-.267875f);
        static readonly Vector3 normal=(originalNormal-axis*Vector3.Dot(originalNormal,axis)).normalized;
        static readonly Vector3 across=Vector3.Cross(normal,axis);

        public static Vector2 UV(Vector3 point)
        {
            var p=point-tip;float along=Vector3.Dot(p,axis);
            float low=.006f+.012f*along,high=.183f+.018f*along;
            // Depth is deliberately absent: front/back share identical colour.
            return new Vector2(Mathf.Clamp01((Vector3.Dot(p,across)-low)/(high-low)),Mathf.Clamp01(along/2.1f));
        }

        public static Material Material()
        {
            if(material!=null)return material;
            var tex=new Texture2D(256,4,TextureFormat.RGBA32,true,false){name="Mecha blade continuous silver and champagne spine",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,anisoLevel=4};
            var pixels=new Color[256*4];
            for(int y=0;y<4;y++)for(int x=0;x<256;x++){
                float w=x/255f;
                var silver=new Color(.64f,.67f,.70f,1);
                var gold=new Color(.64f,.53f,.32f,1);
                // A restrained, continuous spine inlay; no red patches or
                // baked highlights. Scene lighting supplies the reflections.
                var color=Color.Lerp(gold,silver,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.10f,.16f,w)));
                color=Color.Lerp(color,new Color(.76f,.79f,.81f,1),Mathf.SmoothStep(0,1,Mathf.InverseLerp(.88f,1,w)));
                pixels[y*256+x]=color;
            }
            tex.SetPixels(pixels);tex.Apply(true,false);
            material=new Material(Shader.Find("Standard")){name="Mecha_Complete_BladeFinish",color=Color.white,mainTexture=tex};
            material.SetFloat("_Metallic",.72f);material.SetFloat("_Glossiness",.43f);
            return material;
        }
    }
}
