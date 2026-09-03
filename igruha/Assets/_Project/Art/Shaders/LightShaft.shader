// Луч прожектора: аддитивный неосвещённый конус с градиентом по вершинному
// цвету.
//
// Почему свой шейдер, а не URP/Particles/Unlit. Именно им шахты и были
// сделаны сначала, и они вышли непрозрачными абажурами: URP выбирает вариант
// шейдера по кейвордам, которые проставляет редактор материала, а не по
// значениям свойств. Из кода можно выставить и _Surface, и _Blend, и
// _SrcBlend/_DstBlend, и включить _SURFACE_TYPE_TRANSPARENT — материал всё
// равно останется непрозрачным. Тридцать строк своего шейдера снимают вопрос
// целиком: смешивание задано в самом проходе и переключать нечего.
//
// Тот же вывод, что по воде (Art/Shaders/StylizedWater.shader): где нужен
// нестандартный проход, свой шейдер честнее подгонки чужого материала.
//
// Луч ничего не загораживает: ZWrite Off, ZTest LEqual, Cull Off, и он не
// пишет ни тени, ни глубину. Гаснет он вершинным цветом в RGB, а не альфой:
// при Blend One One чёрный не добавляет ничего, и это работает одинаково
// везде, тогда как альфа в аддитиве учитывается по-разному.
Shader "Igruha/Light Shaft"
{
    Properties
    {
        _BaseColor ("Цвет луча", Color) = (1, 1, 1, 1)
        _Intensity ("Яркость", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightShaft"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Градиент лежит в RGB вершинного цвета: 1 у прожектора,
                // 0 у нижнего обреза. При Blend One One ноль не добавляет
                // ничего, поэтому луч растворяется, а не кончается ребром.
                half3 beam = _BaseColor.rgb * input.color.rgb * _Intensity;
                return half4(beam, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
