Shader "Igruha/CryingAngels/KeeperBeam"
{
    Properties
    {
        _BaseColor("Beam tint", Color) = (1,0.79,0.46,0.13)
        _SoftDistance("Depth softness, m", Float) = 1.4
        _NearFade("Camera fade start, m", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            // KeeperBeamCone supplies both triangle windings. Cull one copy.
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 positionOS:TEXCOORD0;
                float along:TEXCOORD1;
                float3 positionWS:TEXCOORD2;
                float4 screenPos:TEXCOORD3;
                half core:TEXCOORD4;
            };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _SoftDistance;
                float _NearFade;
            CBUFFER_END
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.positionOS=i.positionOS.xyz;
                // The generated mesh consists of an apex and far rings; the
                // interpolated step follows the actual configured range.
                o.along=step(0.0001,i.positionOS.z);
                o.screenPos=ComputeScreenPos(o.positionCS);
                o.core=i.color.a;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float radial=max(length(i.positionOS.xy),0.0001);
                float slope=radial/max(i.positionOS.z,0.0001);
                float3 normalOS=normalize(float3(i.positionOS.xy/radial,-slope));
                float3 viewOS=normalize(TransformWorldToObject(GetCameraPositionWS())-i.positionOS);
                // Silhouettes dissolve; the surface facing the viewer reads as a lit volume.
                float edge=smoothstep(0,0.6,abs(dot(normalOS,viewOS)));
                // Light thins with distance from the torch and never draws its far rim.
                float body=smoothstep(0,0.02,i.along)*exp(-i.along*3.4);
                // A camera inside the cone must not get a solid tinted screen:
                // fade close fragments and thin the whole shell while inside.
                float3 camOS=TransformWorldToObject(GetCameraPositionWS());
                float camDist=distance(GetCameraPositionWS(),i.positionWS);
                float near=saturate((camDist-_NearFade)/2.4);
                float camSlope=length(camOS.xy)/max(camOS.z,0.0001);
                float outside=camOS.z<0 ? 1 : smoothstep(slope*0.8,slope*1.2,camSlope);
                float inside=lerp(0.12,1,outside);
                // Soft intersection with floor and statues.
                float2 uv=i.screenPos.xy/i.screenPos.w;
                float sceneEye=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);
                float fragEye=LinearEyeDepth(i.positionCS.z,_ZBufferParams);
                float soft=saturate((sceneEye-fragEye)/_SoftDistance);
                half alpha=_BaseColor.a*i.core*edge*body*near*inside*soft;
                return half4(_BaseColor.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
