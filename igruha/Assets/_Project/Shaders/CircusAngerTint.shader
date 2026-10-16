Shader "Igruha/Circus/Head anger overlay"
{
    Properties { _BaseMap("Avatar texture",2D)="white" {} _BaseColor("Avatar color",Color)=(1,1,1,1) _Heat("Heat",Range(0,1))=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor; float _Heat;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float2 uv:TEXCOORD1;float mask:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.positionCS=TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz)+normalize(o.normalWS)*.001);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);o.mask=v.color.a;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                half brightness=max(base.r,max(base.g,base.b));
                half skin=smoothstep(.035,.32,brightness);
                Light key=GetMainLight();half lighting=.48+.52*saturate(dot(normalize(i.normalWS),key.direction));
                half3 red=half3(.88,.055,.028)*lighting;
                return half4(red,_Heat*i.mask*.55*skin);
            }
            ENDHLSL
        }
    }
}
