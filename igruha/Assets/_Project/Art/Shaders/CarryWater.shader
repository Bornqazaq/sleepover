Shader "Igruha/Carry Water"
{
    Properties
    {
        _Tint("Water tint", Color) = (.10,.42,.46,1)
        [HideInInspector] _BaseColor("Tilt warning", Color) = (.2,.5,.95,1)
        _Opacity("Body opacity", Range(0,1)) = .52
        _RippleStrength("Ripple strength", Range(0,.3)) = .16
        _Flow("Flow", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _BaseColor;
                float _Opacity, _RippleStrength, _Flow;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float top:TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.top=saturate(v.normalOS.y);
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.positionWS.xz;
                float t=_Time.y;
                float2 ripple=float2(cos(dot(p,float2(26,17))+t*2.1)+.45*cos(p.y*53-t*3.3),
                    sin(dot(p,float2(-19,31))-t*1.8)+.4*sin(p.x*47+t*2.6));
                float3 n=normalize(i.normalWS+float3(ripple.x,0,ripple.y)*_RippleStrength*i.top);
                float3 view=SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                float fresnel=.02+.78*pow(1-saturate(dot(n,view)),4);
                float3 reflected=reflect(-view,n);
                half3 reflection=GlossyEnvironmentReflection(reflected,.13h,1.h);
                // A soft sky fill keeps small water surfaces legible between reflection probes.
                reflection+=lerp(half3(.10,.21,.24),half3(.60,.77,.79),saturate(reflected.y))*.5;
                Light sun=GetMainLight();
                float spec=pow(saturate(dot(n,SafeNormalize(sun.direction+view))),180);
                float caustic=pow(saturate(sin(p.x*24+p.y*11+t)*sin(p.y*29-p.x*8-t*.7)),12);
                half3 col=_Tint.rgb*(.5+SampleSH(n)*.65);
                col=lerp(col,reflection,saturate(fresnel*.8+.42*i.top));
                col+=sun.color*spec*1.4+half3(.30,.46,.42)*caustic*.16*i.top;
                float wave=sin(dot(p,float2(15,9))+t*1.4+.7*sin(p.y*18-t));
                float crest=pow(saturate(wave),26);
                col+=half3(.34,.49,.48)*crest*.32*i.top;
                // Preserve the cart's amber/red tilt warning without painting calm water blue.
                float alarm=saturate((_BaseColor.r-.2)/.75);
                col=lerp(col,_BaseColor.rgb*.65,alarm*.6);
                float pulse=.5+.5*sin(i.positionWS.y*42-t*11);
                col+=half3(.12,.23,.20)*pulse*_Flow;
                float alpha=saturate(_Opacity*(.5+.65*i.top)+fresnel*.7+spec*.3+crest*.12*i.top);
                return half4(col,alpha);
            }
            ENDHLSL
        }
    }
}
