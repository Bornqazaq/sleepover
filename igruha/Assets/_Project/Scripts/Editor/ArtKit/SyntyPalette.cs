using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замер собственного цвета моделей пака — механика подфазы 4.2, одна на все
    /// мини-игры.
    ///
    /// Зачем это вообще нужно. Паки Synty текстурированы одним атласом: у модели
    /// нет «цвета», у неё есть кусок общей картинки, и на этом куске бывает и
    /// зебра, и шахматка. Пипеткой по такому куску снимается случайный пиксель:
    /// на зебре это либо чёрный, либо белый, и оба ответа неверны. Поэтому цвет
    /// считается <b>усреднением по UV</b> — по тем пикселям атласа, которые
    /// модель действительно показывает, с весом по площади треугольников.
    ///
    /// <b>Усреднение идёт в линейном пространстве, а результат отдаётся в sRGB.</b>
    /// Свет складывается линейно, и чёрно-белая зебра издали выглядит светлее
    /// среднего серого, а не как серединка между кодами цветов. Складывать
    /// в sRGB — значит получить тон темнее, чем видит глаз. Наружу число всё же
    /// уходит в sRGB: в этом же виде записаны палитры в брифах (#E8E8EC), и
    /// сравнивать надо с ними.
    ///
    /// Текстуры пака читаются <b>через RenderTexture</b>, а не через
    /// <c>Texture2D.GetPixels</c>: у импортированных текстур не стоит
    /// Read/Write Enabled, а включать его — значит менять настройки импорта
    /// в чужом паке и удваивать память под каждую текстуру проекта.
    /// </summary>
    internal static class SyntyPalette
    {
        /// <summary>Сторона, до которой уменьшается текстура при чтении. Среднему хватает.</summary>
        private const int MaxSide = 512;

        /// <summary>Барицентрическая сетка выборок на треугольник по каждой стороне.</summary>
        private const int SamplesPerEdge = 4;

        /// <summary>Имена свойств с картой альбедо: у шейдеров Synty оно своё, у URP — своё.</summary>
        private static readonly string[] AlbedoProperties = { "_Albedo_Map", "_BaseMap", "_MainTex" };

        /// <summary>Имена свойств с общим тоном материала.</summary>
        private static readonly string[] TintProperties = { "_BaseColor", "_Color", "_Base_Color" };

        private readonly struct Decoded
        {
            public readonly Color[] Pixels;
            public readonly int Width;
            public readonly int Height;

            public Decoded(Color[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }

        private static readonly Dictionary<Texture, Decoded> decodedTextures = new Dictionary<Texture, Decoded>(16);

        /// <summary>Сбросить разобранные текстуры. Вызывать в начале пересборки.</summary>
        internal static void ClearCache()
        {
            decodedTextures.Clear();
        }

        /// <summary>
        /// Средний цвет всего префаба: все рендереры, все подмеши, вес — площадь
        /// треугольников. Ложь — ни одного подмеша с текстурой и UV не нашлось.
        /// </summary>
        internal static bool TryAverage(string prefabPath, out Color srgb)
        {
            srgb = Color.black;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return false;
            }

            var sum = new Vector3();
            float weight = 0f;

            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material source = sub < materials.Length ? materials[sub] : null;
                    if (!TryAccumulate(mesh, sub, source, ref sum, ref weight))
                    {
                        continue;
                    }
                }
            }

            if (weight <= 0f)
            {
                return false;
            }

            srgb = new Color(sum.x / weight, sum.y / weight, sum.z / weight).gamma;
            return true;
        }

        /// <summary>Средний цвет одного подмеша. Ложь — нет текстуры, UV или площади.</summary>
        internal static bool TryAverage(Mesh mesh, int subMesh, Material source, out Color srgb)
        {
            srgb = Color.black;

            var sum = new Vector3();
            float weight = 0f;
            if (!TryAccumulate(mesh, subMesh, source, ref sum, ref weight) || weight <= 0f)
            {
                return false;
            }

            srgb = new Color(sum.x / weight, sum.y / weight, sum.z / weight).gamma;
            return true;
        }

        /// <summary>
        /// Яркость по Rec. 709, посчитанная в линейном пространстве. Ровно она
        /// решает, что читается светлым, а что тёмным, — а не сумма кодов sRGB.
        /// </summary>
        internal static float Luminance(Color srgb)
        {
            Color linear = srgb.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        /// <summary>Цвет как #RRGGBB — в этом виде палитры записаны в брифах.</summary>
        internal static string Hex(Color srgb)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(srgb);
        }

        private static bool TryAccumulate(Mesh mesh, int subMesh, Material source, ref Vector3 sum, ref float weight)
        {
            if (mesh == null || source == null || subMesh < 0 || subMesh >= mesh.subMeshCount)
            {
                return false;
            }

            // ⚠️ Проверять здесь `mesh.isReadable` нельзя — она у всех паков
            // Synty ложна, а данные при этом читаются.
            //
            // Так и было до 04.09: стоял ранний выход по `!mesh.isReadable`,
            // и замер не работал ни у одной мини-игры вовсе — все палитры
            // молча садились на сохранённые числа, набитые руками. Проверено
            // на `SM_Bld_Concrete_Floor_01`: `isReadable` = false, при этом
            // `vertices` даёт 120 вершин, `uv` — 120, `GetTriangles(0)` — 228.
            // Флаг говорит о доступности меша <b>в сборке</b>, а редактор
            // держит копию на CPU всегда, и весь этот код — редакторный.
            //
            // Включать Read/Write в импортёре по-прежнему нельзя: это правка
            // настроек чужого пака и удвоение памяти под каждый меш проекта.
            // Вместо флага проверяются сами массивы: пусто — честное «не смог»,
            // вызывающий возьмёт сохранённое значение.

            Texture texture = FindAlbedo(source);
            if (!Decode(texture, out Decoded map))
            {
                return false;
            }

            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            if (uvs == null || uvs.Length == 0 || vertices.Length == 0)
            {
                return false;
            }

            Color tint = FindTint(source).linear;
            int[] triangles = mesh.GetTriangles(subMesh);
            bool any = false;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];
                if (i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
                {
                    continue;
                }

                Vector3 v0 = vertices[i0], v1 = vertices[i1], v2 = vertices[i2];
                float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
                if (area <= Mathf.Epsilon)
                {
                    continue;
                }

                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
                for (int a = 0; a < SamplesPerEdge; a++)
                {
                    for (int b = 0; a + b < SamplesPerEdge; b++)
                    {
                        // Сетка сдвинута внутрь треугольника: выборка ровно по
                        // вершине попадает на шов атласа и тянет в соседний остров.
                        float w0 = (a + 0.25f) / (SamplesPerEdge + 0.5f);
                        float w1 = (b + 0.25f) / (SamplesPerEdge + 0.5f);
                        float w2 = 1f - w0 - w1;
                        if (w2 < 0f)
                        {
                            continue;
                        }

                        Vector2 uv = uv0 * w0 + uv1 * w1 + uv2 * w2;
                        Color linear = Sample(map, uv).linear;
                        sum += new Vector3(linear.r * tint.r, linear.g * tint.g, linear.b * tint.b) * area;
                        weight += area;
                        any = true;
                    }
                }
            }

            return any;
        }

        private static Texture FindAlbedo(Material material)
        {
            foreach (string property in AlbedoProperties)
            {
                if (!material.HasProperty(property))
                {
                    continue;
                }

                Texture texture = material.GetTexture(property);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static Color FindTint(Material material)
        {
            foreach (string property in TintProperties)
            {
                if (material.HasProperty(property))
                {
                    return material.GetColor(property);
                }
            }

            return Color.white;
        }

        private static Color Sample(Decoded map, Vector2 uv)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * map.Width), 0, map.Width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * map.Height), 0, map.Height - 1);
            return map.Pixels[y * map.Width + x];
        }

        private static bool Decode(Texture texture, out Decoded map)
        {
            map = default;
            if (texture == null)
            {
                return false;
            }

            if (decodedTextures.TryGetValue(texture, out map))
            {
                return true;
            }

            int width = Mathf.Clamp(texture.width, 1, MaxSide);
            int height = Mathf.Clamp(texture.height, 1, MaxSide);

            RenderTexture buffer = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, buffer);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = buffer;

            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            copy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);

            map = new Decoded(copy.GetPixels(), width, height);
            decodedTextures[texture] = map;
            Object.DestroyImmediate(copy);
            return true;
        }
    }
}
