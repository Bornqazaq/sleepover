Shader "Igruha/Infection/Atmosphere"
{
 Properties { _BaseMap("Puff",2D)="white"{} [HDR]_BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Name "Atmosphere"
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;half fog:TEXCOORD1; };
   Varyings vert(Attributes v)
   {
    Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
    o.color=v.color*_BaseColor;o.fog=ComputeFogFactor(o.positionCS.z);return o;
   }
   half4 frag(Varyings i):SV_Target
   {
    half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*i.color;
    c.rgb=MixFog(c.rgb,i.fog);return c;
   }
   ENDHLSL
  }
 }
}
