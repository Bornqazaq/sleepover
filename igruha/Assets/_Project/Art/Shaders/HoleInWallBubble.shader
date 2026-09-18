Shader "Igruha/HoleInWall/Bubble"
{
    Properties { _WaterLevel("Water",Float)=-2.88 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float _WaterLevel; CBUFFER_END
            struct A {float4 vertex:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float y:TEXCOORD1;};
            V Vert(A v){V o;float3 p=TransformObjectToWorld(v.vertex.xyz);o.vertex=TransformWorldToHClip(p);o.uv=v.uv;o.color=v.color;o.y=p.y;return o;}
            half4 Frag(V i):SV_Target
            {
                clip(_WaterLevel-i.y);float2 p=i.uv*2-1;float r=length(p);
                float rim=exp(-pow((r-.76)*14,2));float glint=exp(-65*dot(p-float2(-.3,.4),p-float2(-.3,.4)));
                return half4(lerp(half3(.39,.72,.77),half3(.9,1,1),glint),i.color.a*(rim*.5+glint*.7));
            }
            ENDHLSL
        }
    }
}
