Shader "Igruha/CarryItem/ConstructionSky"
{
    Properties
    {
        _MainTex("Cloud panorama", 2D) = "gray" {}
        _Exposure("Exposure", Float) = 1.1
        _Rotation("Rotation", Float) = 118
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Exposure; float _Rotation;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.direction=TransformObjectToWorldDir(v.positionOS.xyz); return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float3 ray=normalize(i.direction);
                float2 uv=float2(frac(atan2(ray.z,ray.x)/(2*PI)+.5+_Rotation/360),1-acos(clamp(ray.y,-1,1))/PI);
                half3 sky=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv).rgb;
                // Feather the authored longitude join; calm polar caps avoid a pinched texture pole.
                float seam=1-smoothstep(0,.018,min(uv.x,1-uv.x));
                half3 opposite=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,float2(1-uv.x,uv.y)).rgb;
                sky=lerp(sky,(sky+opposite)*.5,seam);
                sky=lerp(sky,half3(.085,.17,.30),smoothstep(.985,1,ray.y));
                sky=lerp(sky,half3(.15,.21,.29),smoothstep(.98,1,-ray.y));
                return half4(sky*_Exposure,1);
            }
            ENDHLSL
        }
    }
}
