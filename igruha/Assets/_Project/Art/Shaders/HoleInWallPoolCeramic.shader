Shader "Igruha/HoleInWall/Pool Ceramic"
{
    Properties { _BaseColor("Porcelain",Color)=(.64,.81,.75,1) _WaterLevel("Water",Float)=-2.88 _WallSurface("Pool wall",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _WaterLevel, _WallSurface;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o; }
            float2 Hash(float2 p) {return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);}
            half Caustics(float2 uv)
            {
                uv += .26*sin(uv.yx*2.3+float2(_Time.y*.4,-_Time.y*.3));
                float2 cell=floor(uv),local=frac(uv);float first=8,second=8;
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)
                {
                    float2 offset=float2(x,y);float2 h=Hash(cell+offset);
                    float2 p=offset+.5+.36*sin(_Time.y*.8+h*6.283)-local;
                    float d=dot(p,p);if(d<first){second=first;first=d;}else second=min(second,d);
                }
                return pow(saturate(1-(second-first)*8),3);
            }
            half4 Frag(V i):SV_Target
            {
                float2 uv=abs(i.normal.y)>.5?i.world.xz:(abs(i.normal.x)>.5?i.world.zy:i.world.xy);
                float2 tile=uv/.28;
                float2 local=frac(tile);
                float2 edge=min(local,1-local);
                float d=min(edge.x,edge.y);
                float seam=1-smoothstep(.009,.024,d);
                float variation=Hash(floor(tile)).x;
                half3 porcelain=_BaseColor.rgb*(.94+variation*.1);
                // A glazed mosaic frieze belongs to the pool walls, below the coping.
                // It is pigment, never emission; its scale matches the floor ceramic.
                float belowSurface=_WaterLevel-i.world.y;
                if(_WallSurface>.5)
                {
                    float band=step(.12,belowSurface)*step(belowSurface,.96);
                    float diamond=step(abs(local.x-.5)+abs(local.y-.5),.34);
                    float alternate=step(.5,frac((floor(tile.x)+floor(tile.y))*.5));
                    half3 mosaic=lerp(half3(.11,.31,.34),half3(.78,.82,.65),diamond);
                    mosaic=lerp(mosaic,half3(.28,.53,.50),alternate*.35);
                    porcelain=lerp(porcelain,mosaic,band);
                }
                // Narrow, matte grout and a sparse navy lane stripe, not a luminous grid.
                if(abs(i.normal.y)>.5 && abs(frac((i.world.x+21.6)/10.8)-.5)<.012)porcelain=half3(.14,.39,.46);
                half3 albedo=lerp(porcelain,half3(.34,.48,.46),seam*.6);
                // Millimetre bevels and slight glaze waviness catch the light without
                // adding geometry or an emissive grid. Derivatives keep all projections aligned.
                float height=.0028*smoothstep(.009,.07,d);
                height+=.00035*sin(uv.x*34+variation*2)*sin(uv.y*31);
                float3 n=normalize(i.normal),dx=ddx(i.world),dy=ddy(i.world);
                float3 r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);
                n=normalize(abs(det)*n-sign(det)*(ddx(height)*r1+ddy(height)*r2));
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half shade=saturate(dot(n,light.direction))*lerp(.45,1,light.shadowAttenuation);
                half3 color=albedo*(half3(.39,.58,.61)+light.color*shade*.55);
                half3 halfVector=normalize(light.direction+GetWorldSpaceNormalizeViewDir(i.world));
                color+=light.color*pow(saturate(dot(n,halfVector)),80)*.16*(1-seam)*light.shadowAttenuation;
                half caustic=Caustics(i.world.xz*1.7+float2(_Time.y*.045,0));
                color+=half3(.4,.73,.66)*caustic*.13*saturate((_WaterLevel-i.world.y)*2);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
