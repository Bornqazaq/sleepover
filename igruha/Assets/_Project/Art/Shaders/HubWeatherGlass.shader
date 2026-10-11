Shader "Igruha/Hub/Weather Glass"
{
    Properties { _Tint("Tint",Color)=(.36,.49,.55,.045) _Streak("Particle edge fade",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint; float _Streak;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
            Varyings vert(Attributes i) {Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
            half4 frag(Varyings i):SV_Target
            {
                half4 color=_Tint*i.color;
                if(_Streak>.5)color.a*=smoothstep(0,.18,i.uv.x)*(1-smoothstep(.82,1,i.uv.x))*sin(saturate(i.uv.y)*3.14159);
                return color;
            }
            ENDHLSL
        }
    }
}
