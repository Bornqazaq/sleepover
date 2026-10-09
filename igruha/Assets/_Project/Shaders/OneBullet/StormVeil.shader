Shader "Igruha/OneBullet/StormVeil"
{
    Properties { _BaseColor ("Sand", Color) = (.75,.52,.25,.5) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS: POSITION; float2 uv: TEXCOORD0; };
            struct Varyings { float4 positionCS: SV_POSITION; float2 uv: TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            Varyings vert(Attributes v)
            { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            half4 frag(Varyings i): SV_Target
            {
                float2 p=i.uv*float2(4,6)+float2(-_Time.y*.8,_Time.y*.1);
                float n=noise(p)*.65+noise(p*2.7)*.35;
                float fade=smoothstep(0,.12,i.uv.x)*smoothstep(0,.12,1-i.uv.x)*smoothstep(0,.15,1-i.uv.y);
                return half4(_BaseColor.rgb*(.85+n*.3),_BaseColor.a*fade*(.25+n*.75));
            }
            ENDHLSL
        }
    }
}
