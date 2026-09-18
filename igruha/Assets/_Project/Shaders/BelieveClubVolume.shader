Shader "Igruha/BelieveOrNot/ClubVolume"
{
    Properties
    {
        _BaseColor("Amber scattering", Color) = (1, .63, .28, .045)
        _LampHeight("Lamp height", Float) = 3.6
        _LampRange("Light range", Float) = 5.1
        _ConeRadius("Floor radius", Float) = 3.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _LampHeight, _ConeRadius, _LampRange;
            CBUFFER_END
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            float Hash(float3 p) { return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453); }
            float Noise(float3 p)
            {
                float3 a=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(a),Hash(a+float3(1,0,0)),f.x),
                                 lerp(Hash(a+float3(0,1,0)),Hash(a+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(Hash(a+float3(0,0,1)),Hash(a+float3(1,0,1)),f.x),
                                 lerp(Hash(a+float3(0,1,1)),Hash(a+float3(1,1,1)),f.x),f.y),f.z);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 origin=GetCameraPositionWS();
                float3 ray=normalize(i.positionWS-origin);
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float depth=SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float3 surface=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                float stop=distance(origin,surface);
                float3 safeRay=sign(ray)*max(abs(ray),.00001);
                float3 lo=(float3(-_ConeRadius,0,-_ConeRadius)-origin)/safeRay;
                float3 hi=(float3(_ConeRadius,_LampHeight,_ConeRadius)-origin)/safeRay;
                float3 a=min(lo,hi),b=max(lo,hi);
                float enter=max(0,max(a.x,max(a.y,a.z)));
                float leave=min(stop,min(b.x,min(b.y,b.z)));
                float stepSize=max(0,leave-enter)/40;
                float density=0;
                [loop] for(int k=0;k<40;k++)
                {
                    float3 p=origin+ray*(enter+(k+.5)*stepSize);
                    float radius=max(.025,(_LampHeight-p.y)/_LampHeight*_ConeRadius);
                    float edge=1-smoothstep(.80,1,length(p.xz)/radius);
                    float3 drift=float3(_Time.y*.023,-_Time.y*.038,_Time.y*.017);
                    float n=Noise(p*3.4+drift)+.4*Noise(p*7.1+drift*1.7);
                    float smoke=.34+1.1*smoothstep(.38,.95,n);
                    float nearLamp=lerp(.38,1.3,saturate(p.y/_LampHeight));
                    float distanceToLamp=distance(p,float3(0,_LampHeight,0));
                    float attenuation=pow(saturate(1-pow(distanceToLamp/_LampRange,4)),2);
                    density+=edge*smoke*nearLamp*attenuation*stepSize;
                }
                float opacity=1-exp(-density*_BaseColor.a);
                return half4(_BaseColor.rgb*opacity,opacity);
            }
            ENDHLSL
        }
    }
}
