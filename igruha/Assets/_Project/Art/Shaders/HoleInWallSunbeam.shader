Shader "Igruha/HoleInWall/Sunbeam" {
 Properties {_Color("Sunlight",Color)=(1,.85,.52,.09) _Surface("Projected glass",Float)=0}
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent"}
 Pass { Blend SrcAlpha One ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial) half4 _Color;float _Surface; CBUFFER_END
 struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 w:TEXCOORD1;};
 V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.uv=a.uv;return o;}
 half4 frag(V i):SV_Target {float feather=pow(saturate(sin(i.uv.x*PI)),2)*smoothstep(0,.12,i.uv.y)*smoothstep(1,.65,i.uv.y);if(_Surface>.5){float2 p=float2(i.w.x*.78+i.w.z*.40,i.w.z*.67-i.w.x*.18);float2 grid=abs(frac(p)-.5);feather*=smoothstep(.10,.16,grid.x)*smoothstep(.07,.12,grid.y);}return half4(_Color.rgb,_Color.a*feather);}
 ENDHLSL
 }}
}
