Shader "Igruha/Circus/Winner Ghost"
{
    Properties
    {
        _BaseMap("Character texture", 2D) = "white" {}
        _BaseColor("Spirit tint", Color) = (.65,1,.93,.6)
        _Body("Fade legs", Float) = 1
        _Wave("Waves", Float) = .008
        _VertexTint("Vertex tint", Float) = 0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor;
            float _Body, _Wave, _VertexTint;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 world:TEXCOORD1; float2 uv:TEXCOORD2; float height:TEXCOORD3; float4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.height=v.positionOS.y;
                float wave=sin(v.positionOS.y*11 + _Time.y*3.2 + v.positionOS.z*5)*_Wave;
                v.positionOS.x += wave;
                v.positionOS.z += cos(v.positionOS.y*9 + _Time.y*2.7)*_Wave;
                o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
                o.color=v.color; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 source=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
                half3 pale=lerp(source*.45+.28,half3(.72,1,.95),.7);
                half rim=pow(1-saturate(abs(dot(normalize(i.normalWS),normalize(GetWorldSpaceViewDir(i.world))))),2);
                half fade=lerp(1,smoothstep(.38,.54,i.height),saturate(_Body));
                half3 rgb=lerp(_BaseColor.rgb,pale+rim*half3(.12,.4,.45),saturate(_Body));
                half alpha=_BaseColor.a*fade*lerp(1,.75+rim*.25,saturate(_Body));
                return half4(rgb,alpha)*lerp(half4(1,1,1,1),i.color,saturate(_VertexTint));
            }
            ENDHLSL
        }
    }
}
