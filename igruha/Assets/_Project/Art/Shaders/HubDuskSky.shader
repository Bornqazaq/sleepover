Shader "Igruha/Hub/Dusk Sky"
{
    Properties { _SunDirection("Sun direction",Vector)=(-1,.04,0,0) }
    SubShader
    {
        Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _SunDirection;
            struct v2f {float4 pos:SV_POSITION;float3 direction:TEXCOORD0;};
            v2f vert(float4 v:POSITION) {v2f o;o.pos=UnityObjectToClipPos(v);o.direction=v.xyz;return o;}
            float hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p)
            {
                float2 c=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash(c),hash(c+float2(1,0)),f.x),lerp(hash(c+float2(0,1)),hash(c+1),f.x),f.y);
            }
            half4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                float west=pow(saturate(dot(normalize(d.xz+float2(.0001,0)),normalize(_SunDirection.xz))),6);
                float3 horizon=float3(.035,.045,.07)+float3(.105,.035,.012)*west;
                float3 color=lerp(horizon,float3(.012,.021,.042),smoothstep(.005,.38,d.y));
                float2 p=d.xz/max(d.y+.45,.12)*2.8;
                float cloud=noise(p)*.65+noise(p*2.03+7)*.35;
                color=lerp(color,float3(.012,.018,.029),smoothstep(.43,.68,cloud)*smoothstep(.02,.16,d.y)*.8);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
