Shader "Igruha/HoleInWall/Show Panel"
{
    Properties
    {
        _BaseColor("Coated canvas", Color) = (.91,.91,.89,1)
        _AccentColor("Printed ink", Color) = (.13,.38,.34,1)
        _LaneCenter("Lane centre", Float) = 0
        _PanelWidth("Width in metres", Float) = 8.64
        _PanelHeight("Height in metres", Float) = 3.6
        _Emblem("Waiting shutter", Float) = 0
        _LaneSymbol("Lane symbol", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
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
            half4 _BaseColor, _AccentColor;
            float _LaneCenter, _PanelWidth, _PanelHeight, _Emblem, _LaneSymbol;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o; }
            float Line(float d,float width) { float aa=max(fwidth(d),.0005); return 1-smoothstep(width-aa,width+aa,abs(d)); }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            half4 Frag(V i):SV_Target
            {
                // World XY stays fixed to a wall travelling only along Z. Both the
                // procedural cutout mesh and the waiting shutter use metre-sized grain.
                float2 p=float2(i.world.x-_LaneCenter,i.world.y);
                float2 cell=float2((p.x+_PanelWidth*.5)/1.44,p.y/1.2);
                float2 local=frac(cell), edge=min(local,1-local)*float2(1.44,1.2);
                float d=min(edge.x,edge.y);
                float seam=1-smoothstep(.009,.024,d);
                float puff=smoothstep(.008,.13,d);
                float grain=sin(p.x*520)*sin(p.y*520);
                grain*=1-saturate(max(fwidth(p.x),fwidth(p.y))*160);
                float height=.007*puff+.0003*grain;
                float3 n=normalize(i.normal);
                float3 dx=ddx(i.world),dy=ddy(i.world);
                float3 r1=cross(dy,n),r2=cross(n,dx);
                float det=dot(dx,r1);
                n=normalize(abs(det)*n-sign(det)*(ddx(height)*r1+ddy(height)*r2));
                half3 albedo=_BaseColor.rgb*(.965+.05*Hash(floor(cell)));
                albedo=lerp(albedo,albedo*.64,seam*.65);
                // Twin stitched seams are subtle at range, tangible at the last approach.
                float stitch=Line(d-.042,.0025)*step(.46,frac((p.x+p.y)*30));
                albedo=lerp(albedo,_BaseColor.rgb*1.12,stitch*.6);
                float border=Line(abs(p.x)-(_PanelWidth*.5-.21),.025);
                float wave=Line(p.y-(.17+.045*sin(p.x*4.36)),.012);
                float header=smoothstep(_PanelHeight-.48,_PanelHeight-.47,p.y);
                albedo=lerp(albedo,_AccentColor.rgb,saturate(border*.7+wave*.35+header*.72));
                if(_Emblem>.5 && abs(i.normal.z)>.5)
                {
                    float field=smoothstep(_PanelWidth*.30,_PanelWidth*.34,abs(p.x));
                    albedo=lerp(albedo,_AccentColor.rgb,field*.82);
                    float2 q=p-float2(0,_PanelHeight*.51);
                    float radius=length(q);
                    float rim=Line(radius-1.05,.025);
                    float disc=1-smoothstep(.98,1.0,radius);
                    albedo=lerp(albedo,_AccentColor.rgb,disc*.95+rim*.8);
                    float icon=0;
                    if(_LaneSymbol<.5) {
                        float2 sun=q-float2(0,.18);
                        icon=1-smoothstep(.29,.31,length(sun));
                        icon+=Line(length(sun)-.46,.045)*pow(saturate(cos(atan2(sun.y,sun.x)*12)),6);
                        icon=max(icon,Line(q.y+.43-.06*sin(q.x*8),.03)*step(abs(q.x),.65));
                    } else if(_LaneSymbol<1.5) {
                        [unroll]for(int j=0;j<3;j++)icon=max(icon,Line(q.y+.35-j*.32-.12*sin(q.x*5),.06)*step(abs(q.x),.72));
                    } else if(_LaneSymbol<2.5) {
                        float2 shell=q+float2(0,.50);float r=length(shell);float a=atan2(shell.x,shell.y);
                        icon=Line(r-.91,.045)*step(abs(a),1.1);
                        icon=max(icon,step(r,.88)*step(abs(a),1.08)*pow(saturate(cos(a*7)),16)*.9);
                        icon=max(icon,Line(q.y+.56,.04)*step(abs(q.x),.25));
                    } else {
                        float petal=length(float2(q.x/.22,(q.y-.12)/.62));icon=1-smoothstep(.92,1,petal);
                        float2 l=float2(q.x*.71+q.y*.71,q.y*.71-q.x*.71);
                        float2 r=float2(q.x*.71-q.y*.71,q.y*.71+q.x*.71);
                        icon=max(icon,1-smoothstep(.92,1,length((l-float2(-.27,.06))/float2(.24,.56))));
                        icon=max(icon,1-smoothstep(.92,1,length((r-float2(.27,.06))/float2(.24,.56))));
                        icon=max(icon,Line(q.y+.55-.07*sin(q.x*6),.03)*step(abs(q.x),.66));
                    }
                    albedo=lerp(albedo,_BaseColor.rgb*1.07,saturate(icon)*disc);
                    float stripe=Line(abs(q.x)-2.35,.025)*step(abs(q.y),.8);
                    albedo=lerp(albedo,_AccentColor.rgb,stripe*.45);
                }
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half shade=saturate(dot(n,light.direction))*lerp(.3,1,light.shadowAttenuation);
                half3 color=albedo*(SampleSH(n)+light.color*shade);
                half3 halfVector=normalize(light.direction+GetWorldSpaceNormalizeViewDir(i.world));
                color+=light.color*pow(saturate(dot(n,halfVector)),48)*.075*puff*light.shadowAttenuation;
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
