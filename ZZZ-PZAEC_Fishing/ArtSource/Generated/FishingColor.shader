Shader "PZAEC/FishingColor" {
Properties { _Color ("Tint", Color) = (1,1,1,1) }
SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Pass {
Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct input { float4 vertex:POSITION; fixed4 color:COLOR; };
struct output { float4 vertex:SV_POSITION; fixed4 color:COLOR; };
fixed4 _Color;
output vert(input v) { output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; return o; }
fixed4 frag(output i):SV_Target { return i.color; }
ENDCG
} } }