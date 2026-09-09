Shader "Igruha/CryingAngels/GalleryMist"
{
    Properties
    {
        _BaseColor("Mist tint", Color) = (0.22,0.33,0.46,0.1)
        _Drift("Drift speed", Float) = 0.025
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
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
                float _Drift;
            CBUFFER_END
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
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
                float edge=saturate(1-dot(p,p)); edge*=edge;
                float n=Noise(input.uv*6+float2(_Time.y*_Drift,0));
                n=0.65*n+0.35*Noise(input.uv*13-float2(0,_Time.y*_Drift));
                return half4(_BaseColor.rgb*input.color.rgb,_BaseColor.a*input.color.a*edge*smoothstep(.20,.82,n));
            }
            ENDHLSL
        }
    }
}
