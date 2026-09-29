Shader "Igruha/Mosquitoes/NightSky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION)
            {
                v2f o; o.pos=UnityObjectToClipPos(vertex);
                #if UNITY_REVERSED_Z
                o.pos.z=0;
                #else
                o.pos.z=o.pos.w;
                #endif
                o.direction=vertex.xyz; return o;
            }
            float hash(float3 p) { return frac(sin(dot(p,float3(12.9898,78.233,37.719)))*43758.5453); }
            half4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                float3 c=lerp(float3(.006,.012,.030),float3(.0015,.003,.012),smoothstep(-.08,.7,d.y));
                float m=dot(d,normalize(float3(1,.72,.35)));
                float halo=pow(saturate(m),180)*.10;
                float moon=smoothstep(.99948,.99955,m);
                c+=halo*float3(.45,.65,1)+moon*float3(.75,.83,.97);
                float3 cell=floor(d*420);
                float star=step(.9992,hash(cell))*smoothstep(.25,.55,d.y);
                c+=star*.35;
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
