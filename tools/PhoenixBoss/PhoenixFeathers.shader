Shader "YF/PhoenixFeathers" {
 Properties { _MainTex("Color",2D)="white"{} _EmissionMap("Glow",2D)="black"{} _Cutoff("Alpha Cutoff",Range(0,1))=.3 }
 SubShader {
  Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
  Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "UnityCG.cginc"
   sampler2D _MainTex,_EmissionMap; float4 _MainTex_ST; float _Cutoff;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_FOG_COORDS(1)};
   v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);UNITY_TRANSFER_FOG(o,o.pos);return o;}
   fixed4 frag(v2f i):SV_Target {fixed4 c=tex2D(_MainTex,i.uv);clip(c.a-_Cutoff);c.rgb+=tex2D(_EmissionMap,i.uv).rgb*.6;UNITY_APPLY_FOG(i.fogCoord,c);return c;}
   ENDCG
  }
 }
}
