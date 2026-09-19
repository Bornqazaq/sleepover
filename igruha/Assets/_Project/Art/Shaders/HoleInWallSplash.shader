Shader "Igruha/HoleInWall/Splash"
{
    Properties
    {
        _Droplet("Rounded spray instead of a water sheet", Float) = 0
        _WaterLevel("Spray clipping plane", Float) = -1000
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float3 positionWS:TEXCOORD1; half fog:TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
                float _Droplet;
                float _WaterLevel;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.uv=input.uv;
                output.color=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half alpha=input.color.a;
                half3 color=input.color.rgb;
                if (_Droplet > .5)
                {
                    clip(input.positionWS.y - _WaterLevel);
                    float2 p=input.uv*2-1;
                    float radius=dot(p,p);
                    alpha*=1-smoothstep(.50,1.0,radius);
                    float highlight=exp(-18*dot(p-float2(-.24,.28),p-float2(-.24,.28)));
                    color=lerp(color*.7,half3(.92,1,1),highlight*.85+saturate(p.y)*.25);
                }
                else
                {
                    // Feather the contact and lip: never a hard polygon or an opaque white disc.
                    alpha*=pow(saturate(sin(input.uv.y*3.14159265)),.55);
                }
                return half4(MixFog(color,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
