Shader "Igruha/Circus/BeamDust"
{
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
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.color = input.color;
                o.color.a *= saturate(distance(world, _WorldSpaceCameraPos) - .5);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float radius = length(input.uv * 2 - 1);
                float shape = 1 - smoothstep(.1, 1, radius);
                return half4(input.color.rgb, input.color.a * shape * shape);
            }
            ENDHLSL
        }
    }
}
