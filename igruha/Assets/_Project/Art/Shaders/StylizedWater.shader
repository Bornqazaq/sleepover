// Стилизованная вода под URP: волны Герстнера по вершинам, аналитические
// нормали, цвет по глубине, пена у кромки и преломление дна.
//
// Почему свой шейдер, а не URP/Lit с прозрачностью. Именно так вода и была
// сделана до этого: плоская плита Lit со Smoothness 0.92 и альфой 0.45.
// В кадре она читалась крашеным полом, а не водой, потому что у неё не было
// НИ ОДНОГО признака воды — ни движения, ни бликов, которые едут, ни разницы
// мелкого и глубокого, ни линии уреза у бортика. Всё это даёт не материал,
// а шейдер.
//
// Нормали считаются аналитически из тех же синусов, что двигают вершины,
// а не берутся из normal map. Отсюда нет ни одной текстуры: блик едет ровно
// по той волне, которая под ним, и на плоском стиле проекта это читается
// чище тайлящейся карты.
//
// ТРЕБУЕТ Depth и Opaque texture. В PC_RPAsset обе включены
// (m_RequireDepthTexture: 1, m_RequireOpaqueTexture: 1) — на них держатся
// глубина, пена и преломление. В Mobile_RPAsset они выключены: там вода
// выродится в ровный цвет без пены, но не сломается.
//
// Волны — только вид. Амплитуда 0.08 м против 2.88 м от воды до платформы:
// ни KillZone_Water, ни проверка погружения в HoleInWallAudio по плоскому
// WaterSurfaceY о них знать не обязаны.
Shader "Igruha/Stylized Water"
{
    Properties
    {
        [Header(Cvet)]
        _ShallowColor ("Мелко", Color) = (0.25, 0.78, 0.80, 1)
        _DeepColor ("Глубоко", Color) = (0.03, 0.22, 0.34, 1)
        _DepthRange ("На скольких метрах уходит в глубокий цвет", Float) = 2.5
        _Opacity ("Непрозрачность на глубине", Range(0, 1)) = 0.92

        [Header(Pena u kromki)]
        _FoamColor ("Цвет пены", Color) = (0.85, 0.97, 1.0, 1)
        _FoamDepth ("Ширина полосы пены, м", Float) = 0.55
        _FoamCutoff ("Порог пены", Range(0, 1)) = 0.35
        _FoamSpeed ("Скорость дрожания пены", Float) = 1.6

        [Header(Volny Gerstnera)]
        _WaveA ("Волна A (напр. xy, крутизна z, длина w)", Vector) = (1, 0.35, 0.18, 7)
        _WaveB ("Волна B", Vector) = (-0.6, 1, 0.14, 4.5)
        _WaveC ("Волна C", Vector) = (0.9, -0.7, 0.09, 2.6)
        _WaveSpeed ("Скорость волн", Float) = 0.55
        _WaveHeight ("Общая высота волн, м", Float) = 0.08

        [Header(Ryab)]
        _RippleScale ("Частота ряби", Float) = 2.2
        _RippleSpeed ("Скорость ряби", Float) = 1.1
        _RippleStrength ("Сила ряби в нормали", Float) = 0.35

        [Header(Blik i otrazhenie)]
        _SpecColorTint ("Цвет блика", Color) = (1, 1, 1, 1)
        _SpecPower ("Резкость блика", Float) = 220
        _SpecStrength ("Сила блика", Float) = 1.4
        _FresnelPower ("Резкость френеля", Float) = 4
        _FresnelStrength ("Сила френеля", Range(0, 1)) = 0.28

        [Header(Prelomlenie)]
        _RefractionStrength ("Сила преломления, м экрана", Float) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 200

        Pass
        {
            Name "StylizedWaterForward"
            Tags { "LightMode" = "UniversalForward" }

            // Blend Off, а не альфа-смешение: цвет под водой шейдер берёт сам
            // из _CameraOpaqueTexture и смешивает по глубине. Смешивать
            // повторно железом значило бы применить прозрачность дважды —
            // мелкая вода становилась бы прозрачнее, чем задумано.
            Blend Off
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DepthRange;
                float _Opacity;

                float4 _FoamColor;
                float _FoamDepth;
                float _FoamCutoff;
                float _FoamSpeed;

                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float _WaveSpeed;
                float _WaveHeight;

                float _RippleScale;
                float _RippleSpeed;
                float _RippleStrength;

                float4 _SpecColorTint;
                float _SpecPower;
                float _SpecStrength;
                float _FresnelPower;
                float _FresnelStrength;

                float _RefractionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Одна волна Герстнера. Кроме смещения возвращает и производные —
            // из них собирается нормаль. Считать нормаль конечными разностями
            // по соседним вершинам нельзя: сетка воды 0.4 м, а рябь мельче,
            // и разностная нормаль её просто не увидит.
            void GerstnerWave(float4 wave, float3 basePos, float time,
                              inout float3 offset, inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z;
                float wavelength = max(0.01, wave.w);
                float k = TWO_PI / wavelength;
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, basePos.xz) - c * time);
                float a = steepness / k;

                float sinF = sin(f);
                float cosF = cos(f);

                tangent += float3(-d.x * d.x * steepness * sinF,
                                   d.x * steepness * cosF,
                                  -d.x * d.y * steepness * sinF);

                binormal += float3(-d.x * d.y * steepness * sinF,
                                    d.y * steepness * cosF,
                                   -d.y * d.y * steepness * sinF);

                offset += float3(d.x * a * cosF, a * sinF, d.y * a * cosF);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float time = _Time.y * _WaveSpeed;

                float3 offset = float3(0, 0, 0);
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);

                GerstnerWave(_WaveA, positionWS, time, offset, tangent, binormal);
                GerstnerWave(_WaveB, positionWS, time, offset, tangent, binormal);
                GerstnerWave(_WaveC, positionWS, time, offset, tangent, binormal);

                // Берётся только вертикальная составляющая. Горизонтальный
                // сдвиг к гребню — половина смысла Герстнера, но на краю
                // сетки он оторвал бы воду от бортика бассейна и открыл щель.
                // Форму гребня держат нормали, а они считаются по полным
                // производным, поэтому потеря почти не видна.
                positionWS.y += offset.y * _WaveHeight;

                output.positionWS = positionWS;
                output.normalWS = normalize(cross(binormal, tangent));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            // Мелкая рябь поверх волн: тот же приём, только нормаль правится
            // производной синуса напрямую, без смещения вершин.
            float3 ApplyRipples(float3 normalWS, float3 positionWS)
            {
                float2 p = positionWS.xz * _RippleScale;
                float t = _Time.y * _RippleSpeed;

                float dx = cos(p.x + t) * cos(p.y * 1.3 - t * 0.8)
                         + cos(p.x * 1.7 - t * 1.4) * 0.6;
                float dz = -sin(p.x + t) * sin(p.y * 1.3 - t * 0.8) * 1.3
                         + cos(p.y * 2.1 + t * 0.9) * 0.6;

                normalWS.xz += float2(dx, dz) * _RippleStrength;
                return normalize(normalWS);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / max(0.0001, input.screenPos.w);

                float3 normalWS = ApplyRipples(normalize(input.normalWS), input.positionWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Глубина воды = сколько сцены стоит ПОД этим пикселем
                // поверхности. Отсюда и цвет, и пена: у бортика и у опор
                // платформ разница мала, на середине бассейна велика.
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float waterDepth = max(0.0, sceneEye - surfaceEye);

                // Преломление: дно уводится нормалью волны. Смещение гасится
                // на мелком — иначе у самой кромки в выборку попадает то, что
                // стоит ПЕРЕД водой, и берег «протекает» внутрь бассейна.
                float depthFade = saturate(waterDepth / max(0.0001, _DepthRange));
                float2 refractUV = screenUV + normalWS.xz * _RefractionStrength * depthFade;

                float refractedRaw = SampleSceneDepth(refractUV);
                float refractedEye = LinearEyeDepth(refractedRaw, _ZBufferParams);
                if (refractedEye < surfaceEye)
                {
                    refractUV = screenUV;
                }

                float3 sceneColor = SampleSceneColor(refractUV);

                float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFade);
                float coverage = saturate(depthFade * _Opacity);
                float3 color = lerp(sceneColor, waterColor, coverage);

                // Пена у кромки: полоса там, где под водой почти сразу дно
                // или опора. Дрожит синусом, чтобы не выглядеть наклейкой.
                float foamBand = 1.0 - saturate(waterDepth / max(0.0001, _FoamDepth));
                float foamWobble = sin(input.positionWS.x * 3.1 + _Time.y * _FoamSpeed)
                                 * cos(input.positionWS.z * 2.7 - _Time.y * _FoamSpeed * 0.8);
                float foam = smoothstep(_FoamCutoff, 1.0, foamBand + foamWobble * 0.18);
                color = lerp(color, _FoamColor.rgb, foam);

                // Блик по главному свету — по нормали волны, поэтому едет
                // вместе с ней. Ради него вся аналитика нормалей и делалась.
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDir)), max(1.0, _SpecPower));
                color += _SpecColorTint.rgb * mainLight.color * (spec * _SpecStrength);

                // Френель: у горизонта вода светлеет, под ногами прозрачнее.
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                color = lerp(color, _ShallowColor.rgb, fresnel * _FresnelStrength);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
