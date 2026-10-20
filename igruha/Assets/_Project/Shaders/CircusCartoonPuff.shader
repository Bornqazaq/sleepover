Shader "Igruha/Circus/Cartoon steam"
{
    Properties { _BaseColor("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _BaseColor; CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color*_BaseColor;return o;}
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;float a=atan2(p.y,p.x);float radius=length(p);
                float edge=.80+.085*sin(a*5)+.045*cos(a*3);
                float alpha=(1-smoothstep(edge-.17,edge,radius))*i.color.a;
                return half4(i.color.rgb*(.82+.18*(1-radius)),alpha);
            }
            ENDHLSL
        }
    }
}
