using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Полотно стены: прямоугольная плита с вырезами по контуру позы,
    /// собираемая мешем.
    ///
    /// До 04.09 стена была набором коробок: её резали на 24 горизонтальные
    /// полосы, и дырка складывалась из прямоугольников — лесенка вместо
    /// контура. Теперь дырка режется по настоящей ломаной.
    /// </summary>
    /// <remarks>
    /// <b>Вырез — выемка снизу, а не дырка внутри полотна.</b> Низ любого
    /// выреза лежит на полу платформы, поэтому контур позы вставляется прямо
    /// в нижнюю кромку прямоугольника, и полотно остаётся односвязным: ни
    /// мостов, ни второго обхода. Отсюда же и то, что в игре не бывает
    /// «столбика между ног» — просвет внутри силуэта залит ещё на этапе
    /// сборки ассета.
    ///
    /// <b>Полотно — только картинка.</b> Столкновения остались коробками
    /// (<c>SweepingWall</c>): точность физики тут не нужна — коллайдер утоплен
    /// на 0.45 м и снимается вердиктом с прошедших (STATE 3.52), — а
    /// невыпуклый <c>MeshCollider</c> на кинематическом теле стоил бы куда
    /// дороже любой выгоды.
    ///
    /// Буферы живут в поле: полотно пересобирается на старте стены и в момент
    /// подвоха, то есть в кадре, и мусорить там нечем.
    /// </remarks>
    internal sealed class WallSurface
    {
        /// <summary>Вырез, вставляемый в кромку: ломаная контура и её место по ширине стены.</summary>
        public readonly struct Cutout
        {
            public Vector2[] Outline { get; }
            public float Offset { get; }

            public Cutout(Vector2[] outline, float offset)
            {
                Outline = outline;
                Offset = offset;
            }

            public bool Valid => Outline != null && Outline.Length >= 2;
        }

        /// <summary>
        /// Насколько вырез обязан не доходить до края полотна, м. Впритык
        /// он оставил бы плиту нулевой ширины, а такая на триангуляции даёт
        /// вырожденные уши.
        /// </summary>
        private const float EdgeMargin = 0.02f;

        /// <summary>Ближе этого расстояния соседние точки контура считаются одной, м.</summary>
        private const float WeldDistance = 0.001f;

        /// <summary>Отклонение от прямой, ниже которого средняя точка лишняя, м².</summary>
        private const float CollinearEpsilon = 1e-6f;

        private readonly List<Vector2> outline = new List<Vector2>(256);
        private readonly List<int> faceTriangles = new List<int>(768);
        private readonly List<int> working = new List<int>(256);
        private readonly List<Vector3> vertices = new List<Vector3>(2048);
        private readonly List<Vector2> uv = new List<Vector2>(2048);
        private readonly List<int> triangles = new List<int>(4096);

        /// <summary>
        /// Пересобрать полотно. Вырезы приходят уже отсортированными слева
        /// направо — этим занимается стена, потому что зеркальный переворот
        /// меняет их местами.
        /// </summary>
        /// <returns>Ложь — полотно вырождено и меш не собран.</returns>
        public bool Build(Mesh mesh, float width, float height, float thickness,
            Cutout first, Cutout second)
        {
            float halfWidth = width * 0.5f;
            float limit = halfWidth - EdgeMargin;
            float ceiling = height - EdgeMargin;

            outline.Clear();
            outline.Add(new Vector2(-halfWidth, 0f));

            AppendCutout(first, limit, ceiling);
            AppendCutout(second, limit, ceiling);

            outline.Add(new Vector2(halfWidth, 0f));
            outline.Add(new Vector2(halfWidth, height));
            outline.Add(new Vector2(-halfWidth, height));

            Simplify();

            if (!PolygonTriangulator.Triangulate(outline, faceTriangles, working))
            {
                Debug.LogError("WallSurface: полотно стены не триангулируется — вырезы налезли друг на друга");
                return false;
            }

            Extrude(mesh, thickness);
            return true;
        }

        /// <summary>
        /// Вставить контур выреза в нижнюю кромку. Точки за краем полотна
        /// поджимаются: вырез обязан оставаться внутри стены, иначе полотно
        /// перестаёт быть простым многоугольником и не режется на уши.
        /// </summary>
        private void AppendCutout(Cutout cutout, float limit, float ceiling)
        {
            if (!cutout.Valid)
            {
                return;
            }

            Vector2[] points = cutout.Outline;
            bool trimmed = false;

            for (int i = 0; i < points.Length; i++)
            {
                float x = cutout.Offset + points[i].x;
                float y = points[i].y;
                trimmed |= x < -limit || x > limit || y > ceiling;

                outline.Add(new Vector2(
                    Mathf.Clamp(x, -limit, limit),
                    Mathf.Clamp(y, 0f, ceiling)));
            }

            if (trimmed)
            {
                Debug.LogError($"WallSurface: вырез на {cutout.Offset:F2} не помещается в стену " +
                               $"(предел по ширине {limit:F2}, по высоте {ceiling:F2}) — " +
                               "проверь предельное смещение в WallPatternGenerator");
            }
        }

        /// <summary>
        /// Убрать слипшиеся и лежащие на одной прямой точки. Отрезание ушей
        /// на таких вершинах буксует: у них нулевая площадь, ухом они
        /// не считаются, а обход стопорится.
        /// </summary>
        private void Simplify()
        {
            int write = 0;
            for (int read = 0; read < outline.Count; read++)
            {
                Vector2 point = outline[read];
                if (write > 0 && (point - outline[write - 1]).sqrMagnitude < WeldDistance * WeldDistance)
                {
                    continue;
                }

                outline[write++] = point;
            }

            outline.RemoveRange(write, outline.Count - write);

            // Замыкание тоже может слипнуться: у выреза, дошедшего до края
            // полотна, последняя точка совпадает с первой.
            while (outline.Count > 3 &&
                   (outline[outline.Count - 1] - outline[0]).sqrMagnitude < WeldDistance * WeldDistance)
            {
                outline.RemoveAt(outline.Count - 1);
            }

            for (int i = outline.Count - 2; i >= 1 && outline.Count > 3; i--)
            {
                if (Collinear(outline[i - 1], outline[i], outline[i + 1]))
                {
                    outline.RemoveAt(i);
                }
            }
        }

        private static bool Collinear(Vector2 a, Vector2 b, Vector2 c) =>
            Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) < CollinearEpsilon;

        /// <summary>
        /// Вытянуть плоский контур в плиту толщиной <paramref name="thickness"/>:
        /// передняя грань, задняя и лента боковин по всему обходу, включая
        /// стенки вырезов.
        ///
        /// Вершины у граней свои, общих нет: <c>RecalculateNormals</c> усредняет
        /// нормали по общим вершинам, и на общей вершине угол плиты растёкся бы
        /// в скруглённый.
        /// </summary>
        private void Extrude(Mesh mesh, float thickness)
        {
            vertices.Clear();
            uv.Clear();
            triangles.Clear();

            float front = -thickness * 0.5f;
            float back = thickness * 0.5f;
            int count = outline.Count;

            // Передняя грань смотрит в −Z — туда, откуда на стену едут игроки.
            //
            // ⚠️ Обход при этом ПО часовой стрелке в XY, то есть обратный
            // тому, каким его отдаёт отрезание ушей. Проверено по встроенному
            // Quad: у него нормаль (0,0,−1) и обход по часовой. Ошибиться тут
            // легко и незаметно — полотно просто не рисуется, потому что
            // игрок видит его изнанку, а URP Lit односторонний.
            int frontStart = vertices.Count;
            AppendFace(front);
            for (int i = 0; i < faceTriangles.Count; i += 3)
            {
                triangles.Add(frontStart + faceTriangles[i]);
                triangles.Add(frontStart + faceTriangles[i + 2]);
                triangles.Add(frontStart + faceTriangles[i + 1]);
            }

            int backStart = vertices.Count;
            AppendFace(back);
            for (int i = 0; i < faceTriangles.Count; i++)
            {
                triangles.Add(backStart + faceTriangles[i]);
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 from = outline[i];
                Vector2 to = outline[(i + 1) % count];
                AppendSide(from, to, front, back);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void AppendFace(float z)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 point = outline[i];
                vertices.Add(new Vector3(point.x, point.y, z));
                uv.Add(point);
            }
        }

        /// <summary>
        /// Боковина одного отрезка обхода. Ставится так, чтобы замкнуть плиту
        /// с передней гранью: та проходит контур по часовой стрелке, значит
        /// боковина обязана идти по тому же ребру в обратную сторону.
        /// </summary>
        private void AppendSide(Vector2 from, Vector2 to, float front, float back)
        {
            int start = vertices.Count;
            float length = (to - from).magnitude;

            vertices.Add(new Vector3(from.x, from.y, front));
            vertices.Add(new Vector3(to.x, to.y, front));
            vertices.Add(new Vector3(to.x, to.y, back));
            vertices.Add(new Vector3(from.x, from.y, back));

            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(length, 0f));
            uv.Add(new Vector2(length, back - front));
            uv.Add(new Vector2(0f, back - front));

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
