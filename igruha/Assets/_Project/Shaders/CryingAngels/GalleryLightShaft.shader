Shader "Igruha/CryingAngels/LightShaft"
{
    Properties { _BaseColor("Moonbeam", Color) = (0.24,0.50,0.88,0.065) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float across=pow(saturate(1-abs(i.uv.x*2-1)),2);
                float along=smoothstep(0,.05,i.uv.y)*(1-smoothstep(.65,1,i.uv.y));
                return half4(_BaseColor.rgb,_BaseColor.a*across*along);
            }
            ENDHLSL
        }
    }
}
