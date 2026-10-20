Shader "Igruha/UI/CircusDuelComic"
{
    Properties
    {
        [PerRendererData] _MainTex ("First portrait", 2D) = "black" {}
        _SecondTex ("Second portrait", 2D) = "black" {}
        _Cue ("Cue", Float) = 1
        _Age ("Shot age", Float) = 0
        _Elapsed ("Show age", Float) = 0
        _Aspect ("Aspect", Float) = 1.77778
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            sampler2D _MainTex, _SecondTex;
            float _Cue, _Age, _Elapsed, _Aspect;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            float hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }
            float jag(float y) { float n=y*14; return lerp(hash(floor(n)),hash(floor(n)+1),frac(n))-.5; }
            float4 portrait(float2 uv, float second)
            {
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 0;
                return second > .5 ? tex2D(_SecondTex,uv) : tex2D(_MainTex,uv);
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 uv=i.uv;
                float split=step(2.5,_Cue);
                float boundary=.5+(uv.y-.5)*.055+jag(uv.y)*.032;
                float second=split>0 ? step(boundary,uv.x) : step(1.5,_Cue);
                float direction=second>0 ? -1 : 1;
                float3 hot=lerp(float3(1,.23,.035),float3(.06,.56,1),second);
                float3 pale=lerp(float3(1,.85,.38),float3(.6,.95,1),second);
                float2 local=uv;
                if(split>0) local.x += second>0 ? -.25 : .25;
                // Preserve the portrait's aspect even on ultrawide windows.
                local.x=(local.x-.5)*(_Aspect/1.777778)+.5;
                float2 p=float2((local.x-.5)*_Aspect,uv.y-.53);
                float diagonal=uv.x*direction+uv.y*.45;
                float broad=pow(saturate(1-abs(p.y-p.x*.36)*1.5),3);
                float3 color=lerp(float3(.022,.018,.035),hot*.28,broad);
                float angle=atan2(p.y,p.x);
                float radial=frac(angle*31.0+sin(angle*17)*.9);
                float rush=pow(saturate(1-_Age/.25),2);
                float streak=step(.95,radial)*smoothstep(.22,.95,length(p));
                color += hot * streak * (.15+rush*.7);
                float slash=pow(saturate(1-abs(frac(diagonal*4-_Elapsed*.055)-.5)*12),2);
                color += hot * slash * .14;
                float dots=step(.82,length(frac(uv*float2(180,100))-.5)*1.5);
                color *= 1-dots*.16;
                float4 actor=portrait(local,second);
                float rim=portrait(local+float2(.0025,0),second).a + portrait(local-float2(.0025,0),second).a;
                color += pale*saturate(rim-actor.a*2)*.7;
                color=lerp(color,actor.rgb,actor.a);
                // An irregular rising flame/ice wake under the entering nickname.
                float x=uv.x*20;
                float flame=.17+.035*sin(x*1.7+_Elapsed*2.1)+.02*sin(x*3.8-_Elapsed*3.7);
                float band=exp(-abs(uv.y-(.16+uv.x*direction*.025))*42);
                float tongues=saturate((flame-uv.y)*18)*smoothstep(.065,.17,uv.y);
                color += hot*(band*.7+tongues*.23);
                float cell=floor(uv.x*55);
                float sy=frac(uv.y*1.5-_Elapsed*(.06+hash(cell)*.07)+hash(cell+3));
                float spark=pow(saturate(1-abs(frac(uv.x*55)-.5)*16),2)*pow(saturate(1-abs(sy-.4)*100),2);
                color += pale*spark*.7;
                if(split>0)
                {
                    float d=abs(uv.x-boundary)*_Aspect;
                    color=lerp(color,hot,saturate(1-d/.025)*.5);
                    color=lerp(color,float3(1,1,1),1-smoothstep(.002,.005,d));
                }
                float edge=step(uv.y,.045+uv.x*.012)+step(.955+uv.x*.012,uv.y);
                color=lerp(color,float3(.008,.009,.016),saturate(edge));
                float impact=pow(saturate(1-_Age/.095),3)*.42;
                color=lerp(color,pale,impact);
                return float4(color,i.color.a);
            }
            ENDHLSL
        }
    }
}
