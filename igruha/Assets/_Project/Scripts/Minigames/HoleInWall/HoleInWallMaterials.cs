using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Серые материалы каркаса: один источник на всю игру — арену строит
    /// редактор, стену и силуэты собирает рантайм, и цвета у них общие.
    ///
    /// ⚠️ <c>CreatePrimitive</c> вешает встроенный Default-Material, шейдер
    /// которого не из URP и в сборку не попадает: в билде объект стал бы
    /// фиолетовым, хотя в редакторе выглядит нормально (STATE 3.9). Поэтому
    /// материал берётся только отсюда.
    ///
    /// Материалы кэшируются по цвету: без кэша каждая смена позы и каждая
    /// новая стена плодили бы по экземпляру, и за раунд их набегали бы сотни.
    /// </summary>
    public static class HoleInWallMaterials
    {
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly Dictionary<Color, Material> OpaqueCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> TransparentCache = new Dictionary<Color, Material>();

        private static Shader litShader;

        /// <summary>Обычный непрозрачный блокаутный материал.</summary>
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

            var material = new Material(shader) { color = color };
            material.SetColor(BaseColorId, color);
            OpaqueCache[color] = material;
            return material;
        }

        /// <summary>
        /// Прозрачный материал: силуэт позы и подсветка выреза.
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

            TransparentCache[color] = material;
            return material;
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
    }
}
