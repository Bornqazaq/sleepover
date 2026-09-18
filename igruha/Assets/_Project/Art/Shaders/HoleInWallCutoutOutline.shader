Shader "Igruha/HoleInWall/Cutout Outline"
{
    Properties { [MainColor] _BaseColor("Lane ink",Color)=(.03,.42,.68,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; half ink:TEXCOORD0; half fog:TEXCOORD1; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.ink=i.color.r;o.fog=ComputeFogFactor(o.positionCS.z);return o; }
            half4 Frag(V i):SV_Target
            {
                // Stable pigment instead of HDR bloom: cyan stays blue on a cream wall.
                half3 color=lerp(half3(.018,.048,.065),_BaseColor.rgb,saturate(i.ink));
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
