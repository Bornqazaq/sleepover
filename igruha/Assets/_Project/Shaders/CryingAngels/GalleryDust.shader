Shader "Igruha/CryingAngels/GalleryDust"
{
    Properties { _BaseColor("Dust tint", Color) = (1,0.86,0.62,0.55) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.uv*2-1;
                float d=dot(p,p);
                // A soft mote: bright pin-point, faint halo, nothing at the quad edge.
                float mote=saturate(1-d); mote*=mote;
                float spark=saturate(1-d*6);
                half alpha=_BaseColor.a*input.color.a*(mote*0.35+spark);
                return half4(_BaseColor.rgb*input.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
