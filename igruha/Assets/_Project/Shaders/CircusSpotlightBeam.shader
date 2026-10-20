Shader "Igruha/Circus/SpotlightBeam"
{
    Properties
    {
        _BaseColor("Haze colour", Color) = (1,.85,.6,1)
        _Density("Haze density", Range(0,.2)) = .055
        _Radius("Cone end radius", Float) = 4
        _Length("Cone length", Float) = 20
        _SourceRadius("Lens radius", Float) = .15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Name "StageHaze"
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Density, _Radius, _Length, _SourceRadius;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 rayWS = normalize(input.positionWS - _WorldSpaceCameraPos);
                float3 origin = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 ray = mul((float3x3)unity_WorldToObject, rayWS);
                float3 invRay = rcp(ray + 1e-7);
                float3 t0 = (float3(-_Radius,-_Radius,0) - origin) * invRay;
                float3 t1 = (float3(_Radius,_Radius,_Length) - origin) * invRay;
                float3 nearT = min(t0,t1), farT = max(t0,t1);
                float entry = max(0, max(nearT.x,max(nearT.y,nearT.z)));
                float exit = min(farT.x,min(farT.y,farT.z));
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float viewZ = max(.001, -mul((float3x3)UNITY_MATRIX_V,rayWS).z);
                exit = min(exit, depth / viewZ);
                if (exit <= entry) return 0;
                const int samples = 24;
                float stepSize = (exit-entry) / samples;
                float jitter = frac(52.9829189 * frac(dot(input.positionCS.xy,float2(.06711056,.00583715))));
                float sum = 0;
                [unroll] for(int i=0;i<samples;i++)
                {
                    float3 p = origin + ray * (entry+(i+jitter)*stepSize);
                    float along = saturate(p.z / _Length);
                    float radius = lerp(_SourceRadius,_Radius,along);
                    float radial = saturate(1-length(p.xy)/max(.01,radius));
                    float3 world = TransformObjectToWorld(p);
                    // Slow wisps break up the shaft without opaque fog cards.
                    float drift = .8 + .2*sin(dot(world,float3(.65,.42,.37)) + _Time.y*.18)
                        * sin(dot(world,float3(-.23,.31,.52)) - _Time.y*.13);
                    float density = smoothstep(0,.32,radial) * smoothstep(0,.025,along)
                        * (1-smoothstep(.78,1,along)) * drift / (1 + p.z*p.z*.022);
                    sum += density;
                }
                float opacity = 1-exp(-sum*stepSize*_Density);
                // A broad lens-facing glow; no opaque shells or intersecting cards.
                return half4(_BaseColor.rgb*opacity,0);
            }
            ENDHLSL
        }
    }
}
