using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Печёт спрайты интерфейса: карточку, капсулу, клавишу, круг, сектор
    /// колеса и виньетку.
    ///
    /// Спрайты рисуются кодом, а не берутся из пака, по трём причинам.
    /// Во-первых, весь остальной арт проекта собирается пересборкой, и
    /// интерфейс не должен быть единственным местом, которое живёт руками.
    /// Во-вторых, скруглению нужен ровно один параметр — радиус, — и держать
    /// ради него PNG из чужого набора незачем. В-третьих, девять сцен копируют
    /// один шаблон канваса: любая правка «в инспекторе» чинит одну сцену
    /// из девяти, а пересборка — все девять сразу.
    ///
    /// Скругление рисуется со сглаживанием по расстоянию до скруглённого
    /// прямоугольника: без него край на 4K выглядит лесенкой, а девятислайс
    /// растягивает эту лесенку до толщины пальца.
    /// </summary>
    public static class UiSpriteBaker
    {
        /// <summary>Куда складываются испечённые спрайты.</summary>
        internal const string Folder = "Assets/_Project/Art/UI";

        internal const string Card = Folder + "/UI_Card.png";
        internal const string Chip = Folder + "/UI_Chip.png";
        internal const string KeyCap = Folder + "/UI_KeyCap.png";
        internal const string KeyCapEdge = Folder + "/UI_KeyCapEdge.png";
        internal const string Circle = Folder + "/UI_Circle.png";
        internal const string Sector = Folder + "/UI_Sector.png";
        internal const string Vignette = Folder + "/UI_Vignette.png";
        internal const string Stroke = Folder + "/UI_Stroke.png";

        /// <summary>Сторона квадратной текстуры девятислайсовых спрайтов, пикселей.</summary>
        private const int PlateSize = 128;

        /// <summary>Сторона круглых спрайтов и виньетки, пикселей.</summary>
        private const int RoundSize = 256;

        /// <summary>Толщина рамки у обводочных спрайтов, пикселей.</summary>
        private const float StrokeWidth = 5f;

        /// <summary>Ширина сектора колеса, градусов. Остаток до 45° — зазор между секторами.</summary>
        private const float SectorSweep = 40f;

        /// <summary>Внутренний радиус кольца колеса, долей от половины текстуры.</summary>
        private const float SectorInner = 0.46f;

        /// <summary>Внешний радиус кольца колеса, долей от половины текстуры.</summary>
        private const float SectorOuter = 0.98f;

        [MenuItem("Igruha/Арт/Интерфейс: испечь спрайты")]
        public static void BakeAll()
        {
            Directory.CreateDirectory(Folder);

            float cardRadius = 40f;
            float keyRadius = 26f;

            Write(Card, RoundedRect(PlateSize, PlateSize, cardRadius, 0f), Mathf.CeilToInt(cardRadius) + 2);
            Write(Chip, RoundedRect(PlateSize, PlateSize, PlateSize * 0.5f, 0f), PlateSize / 2);
            Write(KeyCap, RoundedRect(PlateSize, PlateSize, keyRadius, 0f), Mathf.CeilToInt(keyRadius) + 2);
            Write(KeyCapEdge, RoundedRect(PlateSize, PlateSize, keyRadius, StrokeWidth), Mathf.CeilToInt(keyRadius) + 2);
            Write(Stroke, RoundedRect(PlateSize, PlateSize, cardRadius, StrokeWidth), Mathf.CeilToInt(cardRadius) + 2);
            Write(Circle, RoundedRect(RoundSize, RoundSize, RoundSize * 0.5f, 0f), 0);
            Write(Sector, RingSector(RoundSize), 0);
            Write(Vignette, RadialFade(RoundSize), 0);

            AssetDatabase.Refresh();
            Debug.Log("🎛 Спрайты интерфейса испечены: " + Folder);
        }

        /// <summary>
        /// Скруглённый прямоугольник. <paramref name="stroke"/> больше нуля —
        /// рисуется только рамка такой толщины, середина остаётся прозрачной.
        /// </summary>
        private static Color[] RoundedRect(int width, int height, float radius, float stroke)
        {
            var pixels = new Color[width * height];
            var half = new Vector2(width * 0.5f, height * 0.5f);
            float r = Mathf.Min(radius, Mathf.Min(half.x, half.y));

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                    float distance = RoundedRectDistance(p, half, r);
                    float alpha = Coverage(distance);

                    if (stroke > 0f)
                    {
                        // Рамка — разница двух силуэтов: внешнего и утопленного
                        // внутрь на толщину. Считать её вычитанием проще, чем
                        // вторым проходом: сглаживание получается одинаковым
                        // с обеих сторон рамки само собой.
                        alpha -= Coverage(distance + stroke);
                    }

                    pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            return pixels;
        }

        /// <summary>Расстояние со знаком до скруглённого прямоугольника: внутри — отрицательное.</summary>
        private static float RoundedRectDistance(Vector2 point, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - (half - Vector2.one * radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        /// <summary>Доля пикселя внутри силуэта: сглаживание шириной в пиксель.</summary>
        private static float Coverage(float distance)
        {
            return Mathf.Clamp01(0.5f - distance);
        }

        /// <summary>
        /// Сектор кольца, направленный вверх. Колесо ставит восемь копий
        /// поворотом, поэтому в текстуре он ровно один.
        /// </summary>
        private static Color[] RingSector(int size)
        {
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            float inner = half * SectorInner;
            float outer = half * SectorOuter;
            float halfSweep = SectorSweep * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float radius = p.magnitude;

                    // Угол от направления «вверх», по модулю: сектор
                    // симметричен, и знак поворота ему безразличен.
                    float angle = Mathf.Abs(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg);

                    float ring = Mathf.Min(Coverage(inner - radius), Coverage(radius - outer));
                    float wedge = Coverage((angle - halfSweep) * Mathf.Deg2Rad * Mathf.Max(radius, 1f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(ring, wedge)));
                }
            }

            return pixels;
        }

        /// <summary>Мягкое затемнение к краям: круглая дырка в центре, плотный край.</summary>
        private static Color[] RadialFade(int size)
        {
            var pixels = new Color[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float t = Mathf.Clamp01(p.magnitude / half);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, t));
                }
            }

            return pixels;
        }

        private static void Write(string path, Color[] pixels, int border)
        {
            int side = Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.spriteBorder = border > 0
                ? new Vector4(border, border, border, border)
                : Vector4.zero;
            importer.SaveAndReimport();
        }
    }
}
