using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Материалы «Дырки в стене»: один источник на всю игру — арену строит
    /// редактор, стену и силуэты собирает рантайм, и поверхности у них общие.
    ///
    /// ⚠️ <c>CreatePrimitive</c> вешает встроенный Default-Material, шейдер
    /// которого не из URP и в сборку не попадает: в билде объект стал бы
    /// фиолетовым, хотя в редакторе выглядит нормально (STATE 3.9). Поэтому
    /// материал берётся только отсюда.
    ///
    /// Материалы кэшируются по цвету: без кэша каждая смена позы и каждая
    /// новая стена плодили бы по экземпляру, и за раунд их набегали бы сотни.
    ///
    /// <b>Настройка поверхности вынесена в <c>Configure*</c> и живёт здесь же.</b>
    /// Эти же методы вызывает редакторный сборщик палитры, когда пишет
    /// <c>.mat</c>-ассеты в <c>Materials/HoleInWall/</c>: иначе описание глянца
    /// и прозрачности существовало бы в двух местах и разъехалось бы при первой
    /// же правке.
    /// </summary>
    public static class HoleInWallMaterials
    {
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Глянец материалов, у которых он не задан явно.</summary>
        private const float DefaultSmoothness = 0.5f;

        private static readonly Dictionary<Color, Material> OpaqueCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> TransparentCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> EmissiveCache = new Dictionary<Color, Material>();

        private static Shader litShader;

        /// <summary>Обычный непрозрачный материал.</summary>
        public static Material Opaque(Color color)
        {
            if (OpaqueCache.TryGetValue(color, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = ResolveShader();
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            ConfigureOpaque(material, color, DefaultSmoothness, 0f);
            OpaqueCache[color] = material;
            return material;
        }

        /// <summary>
        /// Светящийся материал: контур выреза. Свечение здесь несёт смысл —
        /// силуэт обязан читаться с максимальной дистанции подъезда, — поэтому
        /// оно живёт в отдельном кэше, а не подмешивается в <see cref="Opaque"/>.
        /// </summary>
        public static Material Emissive(Color color)
        {
            if (EmissiveCache.TryGetValue(color, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = ResolveShader();
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            ConfigureEmissive(material, color, HoleInWallPalette.NeonEmission);
            EmissiveCache[color] = material;
            return material;
        }

        /// <summary>
        /// Прозрачный материал: силуэт позы, подсветка выреза, вода.
        ///
        /// URP Lit не становится прозрачным от одной альфы в цвете: нужны и
        /// <c>_Surface</c>, и режим смешивания, и очередь отрисовки, и ключевое
        /// слово шейдера. Выставить что-то одно — получить непрозрачный куб
        /// и потратить прогон на выяснение, почему.
        /// </summary>
        public static Material Transparent(Color color)
        {
            if (TransparentCache.TryGetValue(color, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = ResolveShader();
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            ConfigureTransparent(material, color, DefaultSmoothness);
            TransparentCache[color] = material;
            return material;
        }

        /// <summary>Настроить готовый материал как непрозрачную поверхность палитры.</summary>
        public static void ConfigureOpaque(Material material, Color color, float smoothness, float metallic)
        {
            if (material == null)
            {
                return;
            }

            material.SetFloat(SurfaceId, 0f);
            material.SetFloat(ZWriteId, 1f);
            material.SetFloat(SrcBlendId, (float)BlendMode.One);
            material.SetFloat(DstBlendId, (float)BlendMode.Zero);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Geometry;

            material.SetColor(BaseColorId, color);
            material.color = color;
            material.SetFloat(SmoothnessId, smoothness);
            material.SetFloat(MetallicId, metallic);

            SetEmission(material, Color.black, 0f);
        }

        /// <summary>
        /// Настроить готовый материал как поверхность с текстурой пака:
        /// альбедо-атлас и, если он есть, карта свечения.
        ///
        /// <b>Единственное место в игре, где цвет назначает не палитра.</b>
        /// Правило подфазы 4.2 — «цвет в кадре назначает палитра, а не атлас
        /// пака» — писалось про реквизит: перекрашенная трибуна не спорит
        /// с ареной, а неперекрашенная кричит ярмаркой посреди ночной студии.
        /// С людьми оно даёт ровно обратное. Человек, залитый одним плоским
        /// тоном, теряет лицо, волосы и одежду разом и читается манекеном —
        /// на приёмке 08.09 это назвали «какие-то роботы». У зрителя цвет
        /// и есть его лицо, поэтому здесь работает атлас.
        /// </summary>
        public static void ConfigureTextured(Material material, Texture albedo, Texture emission,
            Color emissionTint, float emissionIntensity, float smoothness)
        {
            if (material == null)
            {
                return;
            }

            ConfigureOpaque(material, Color.white, smoothness, 0f);
            material.SetTexture(BaseMapId, albedo);
            material.mainTexture = albedo;

            if (emission == null || emissionIntensity <= 0f)
            {
                return;
            }

            material.SetTexture(EmissionMapId, emission);
            SetEmission(material, emissionTint, emissionIntensity);
        }

        /// <summary>Настроить готовый материал как светящуюся поверхность палитры.</summary>
        public static void ConfigureEmissive(Material material, Color color, float intensity)
        {
            if (material == null)
            {
                return;
            }

            ConfigureOpaque(material, color, DefaultSmoothness, 0f);
            SetEmission(material, color, intensity);
        }

        /// <summary>Настроить готовый материал как прозрачную поверхность палитры.</summary>
        public static void ConfigureTransparent(Material material, Color color, float smoothness)
        {
            if (material == null)
            {
                return;
            }

            material.SetFloat(SurfaceId, 1f);
            material.SetFloat(BlendId, 0f);
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            material.SetColor(BaseColorId, color);
            material.color = color;
            material.SetFloat(SmoothnessId, smoothness);
            material.SetFloat(MetallicId, 0f);

            SetEmission(material, Color.black, 0f);
        }

        private static void SetEmission(Material material, Color color, float intensity)
        {
            if (intensity > 0f)
            {
                material.EnableKeyword("_EMISSION");

                // Линейное значение, а не sRGB. У URP Lit свойство _EmissionColor
                // помечено [HDR], и такие свойства Unity не переводит из гаммы
                // сама — записанное туда число шейдер берёт как есть. Отдать
                // sRGB — значит получить свечение заметно более блёклое, чем
                // цвет, который стоит рядом в палитре.
                material.SetColor(EmissionColorId, color.linear * intensity);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return;
            }

            material.DisableKeyword("_EMISSION");
            material.SetColor(EmissionColorId, Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static Shader ResolveShader()
        {
            if (litShader != null)
            {
                return litShader;
            }

            litShader = Shader.Find(LitShaderName);
            if (litShader == null)
            {
                Debug.LogError($"Шейдер '{LitShaderName}' не найден — блокаут «Дырки в стене» будет фиолетовым в сборке");
            }

            return litShader;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    }
}
