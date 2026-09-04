using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Триангуляция простого многоугольника отрезанием ушей.
    ///
    /// Нужна ровно одному месту — полотну стены с вырезами по контуру позы, —
    /// но вынесена отдельно: это чистая функция от списка точек, её видно
    /// на стенде без всякой стены (<c>Editor/HoleInWallCutoutProof</c>).
    /// </summary>
    /// <remarks>
    /// <b>Дырок нет и не будет.</b> Вырез в стене доходит до пола, то есть
    /// это не дырка внутри полотна, а выемка снизу, и полотно остаётся
    /// односвязным. Поэтому ни мостов, ни второго контура здесь не нужно:
    /// на вход приходит один замкнутый обход против часовой стрелки.
    ///
    /// Многоугольник стены — это четыре угла плюс две ломаные вырезов,
    /// около 170 точек. Отрезание ушей на такой длине стоит доли миллисекунды
    /// и случается дважды за проезд стены: на старте и в момент подвоха.
    /// </remarks>
    internal static class PolygonTriangulator
    {
        /// <summary>Площадь уха ниже этой считается нулевой: точки слиплись или лежат на одной прямой.</summary>
        private const float AreaEpsilon = 1e-7f;

        /// <summary>
        /// Разрезать многоугольник на треугольники. Обход обязан идти против
        /// часовой стрелки — так его отдаёт построитель полотна.
        /// </summary>
        /// <returns>Ложь — многоугольник вырожден, треугольников не вышло.</returns>
        public static bool Triangulate(List<Vector2> polygon, List<int> triangles, List<int> workingSet)
        {
            triangles.Clear();
            workingSet.Clear();

            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                workingSet.Add(i);
            }

            // Счётчик холостых проходов: пока уши находятся, он сбрасывается.
            // Кончился — значит многоугольник самопересекается, и резать его
            // ушами нельзя.
            int idle = workingSet.Count;
            int cursor = 0;

            while (workingSet.Count > 3 && idle > 0)
            {
                int count = workingSet.Count;
                int previous = workingSet[(cursor + count - 1) % count];
                int current = workingSet[cursor];
                int next = workingSet[(cursor + 1) % count];

                if (IsEar(polygon, workingSet, previous, current, next))
                {
                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);

                    workingSet.RemoveAt(cursor);
                    if (cursor >= workingSet.Count)
                    {
                        cursor = 0;
                    }

                    idle = workingSet.Count;
                    continue;
                }

                cursor = (cursor + 1) % count;
                idle--;
            }

            if (workingSet.Count == 3)
            {
                triangles.Add(workingSet[0]);
                triangles.Add(workingSet[1]);
                triangles.Add(workingSet[2]);
            }

            return triangles.Count >= 3;
        }

        /// <summary>
        /// Ухо — выпуклая вершина, в треугольник которой не попала ни одна
        /// другая вершина многоугольника.
        /// </summary>
        private static bool IsEar(List<Vector2> polygon, List<int> workingSet, int previous, int current, int next)
        {
            Vector2 a = polygon[previous];
            Vector2 b = polygon[current];
            Vector2 c = polygon[next];

            float area = Cross(a, b, c);
            if (area <= AreaEpsilon)
            {
                return false;
            }

            for (int i = 0; i < workingSet.Count; i++)
            {
                int index = workingSet[i];
                if (index == previous || index == current || index == next)
                {
                    continue;
                }

                if (Inside(a, b, c, polygon[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Удвоенная площадь со знаком. Положительная — поворот против часовой стрелки.</summary>
        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        /// <summary>
        /// Точка строго внутри треугольника. Лежащая на стороне не считается:
        /// иначе слипшиеся вершины контура запрещали бы резать уши там, где
        /// резать можно.
        /// </summary>
        private static bool Inside(Vector2 a, Vector2 b, Vector2 c, Vector2 point) =>
            Cross(a, b, point) > AreaEpsilon &&
            Cross(b, c, point) > AreaEpsilon &&
            Cross(c, a, point) > AreaEpsilon;
    }
}
