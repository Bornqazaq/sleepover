Shader "Igruha/CarryItem/CloudSea"
{
    Properties
    {
        _Density("Density", 3D) = "white" {}
        _SunColor("Sunlit vapour", Color) = (1,.965,.89,1)
        _ShadeColor("Blue shadow", Color) = (.39,.60,.8,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_Density); SAMPLER(sampler_Density);
            CBUFFER_START(UnityPerMaterial)
            half4 _SunColor, _ShadeColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            { Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); return o; }
            float Density(float3 p)
            {
                float3 uv = p * .006 + float3(_Time.y * .00012, 0, _Time.y * .00006);
                float broad = SAMPLE_TEXTURE3D_LOD(_Density,sampler_Density,uv,0).r;
                float detail = SAMPLE_TEXTURE3D_LOD(_Density,sampler_Density,uv*3.7+float3(.31,.47,.19),0).r;
                float top = -51 + broad * 37 + detail * 5;
                float vertical = smoothstep(-71,-59,p.y) * (1-smoothstep(top-12,top,p.y));
                float islands = smoothstep(.43,.62,broad + (detail-.5)*.32);
                float opening = smoothstep(28,62,length(p.xz));
                float boundary = 1-smoothstep(260,335,max(abs(p.x),abs(p.z)));
                return vertical * islands * opening * boundary * .15;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 origin=GetCameraPositionWS(); float3 ray=normalize(i.positionWS-origin);
                float3 ro=TransformWorldToObject(origin), rd=mul((float3x3)unity_WorldToObject,ray);
                rd=sign(rd)*max(abs(rd),.000001);
                float3 t0=(-.5-ro)/rd,t1=(.5-ro)/rd;
                float3 nearT=min(t0,t1),farT=max(t0,t1);
                float start=max(0,max(nearT.x,max(nearT.y,nearT.z)));
                float end=min(farT.x,min(farT.y,farT.z));
                float2 uv=i.positionCS.xy/_ScaledScreenParams.xy;
                float raw=SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                #endif
                float3 opaque=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
                end=min(end,length(opaque-origin));
                if(end<=start)return 0;
                const int steps=56; float stepLength=(end-start)/steps;
                // Stable world-space sampling avoids crawling edges; no animated screen noise.
                float distance=start+stepLength*.5; half transmission=1; half3 color=0;
                [loop] for(int n=0;n<steps;n++)
                {
                    float3 p=origin+ray*distance; float density=Density(p);
                    if(density>.001)
                    {
                        float shade=exp(-Density(p+float3(-2,3,-1))*26-Density(p+float3(-5,8,-3))*42);
                        half3 light=lerp(_ShadeColor.rgb,_SunColor.rgb,saturate(shade*.91+.09));
                        float alpha=1-exp(-density*stepLength);
                        color+=transmission*alpha*light; transmission*=1-alpha;
                        if(transmission<.015)break;
                    }
                    distance+=stepLength;
                }
                return half4(color,1-transmission);
            }
            ENDHLSL
        }
    }
}
