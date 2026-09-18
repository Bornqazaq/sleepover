Shader "Igruha/HoleInWall/Tessera" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;float2 uv:TEXCOORD0;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;half4 c:COLOR;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
 V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.c=a.c;o.uv=a.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
 half4 frag(V i):SV_Target {Light l=GetMainLight();float edge=min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y));float bevel=smoothstep(0,.10,edge);half3 n=normalize(i.n);half3 c=i.c.rgb*(.83+.17*bevel);c*=SampleSH(n)+l.color*(.3+.7*saturate(dot(n,l.direction)));float glint=pow(saturate(dot(n,normalize(l.direction+GetWorldSpaceNormalizeViewDir(i.w)))),48);return half4(MixFog(c+glint*.11,i.fog),1);}
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
