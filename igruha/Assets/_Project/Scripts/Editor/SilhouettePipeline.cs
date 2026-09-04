using System.Collections.Generic;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Битмап силуэта и всё, что с ним делается по дороге к ломаной контура:
    /// объединение, дилатация, заливка просветов, обводка внешнего края,
    /// упрощение и проверка запаса.
    ///
    /// Живёт отдельно от пекаря, потому что это чистая обработка картинки:
    /// сюда не приходит ни персонаж, ни поза, ни ассет — только пиксели.
    /// </summary>
    /// <remarks>
    /// Все буферы заводятся один раз на прогон: составов 37, поз 4, и по кадру
    /// на каждый заход это полторы сотни лишних мегабайт.
    /// </remarks>
    internal sealed class SilhouettePipeline
    {
        /// <summary>Заведомо большая квадратичная дистанция. Не бесконечность: на ней огибающая парабол даёт NaN.</summary>
        private const float Far = 1e10f;

        /// <summary>Направления обхода: +x, +y, −x, −y. Порядок задаёт левый поворот как +1.</summary>
        private static readonly int[] StepX = { 1, 0, -1, 0 };
        private static readonly int[] StepY = { 0, 1, 0, -1 };

        /// <summary>Порядок перебора направлений в развилке. Поле, чтобы не мусорить на каждом узле обхода.</summary>
        private static readonly int[] Order = new int[4];

        private readonly int width;
        private readonly int height;
        private readonly float originX;
        private readonly float pixelSize;
        private readonly float dilationRadius;
        private readonly float simplifyTolerance;

        private readonly bool[] raw;
        private readonly bool[] grown;
        private readonly bool[] polygon;
        private readonly bool[] outside;
        private readonly float[] distance;
        private readonly float[] column;
        private readonly float[] line;
        private readonly float[] envelope;
        private readonly int[] vertices;
        private readonly float[] boundary;

        private readonly Stack<int> flood = new Stack<int>(1024);
        private readonly List<Vector2Int> loop = new List<Vector2Int>(4096);
        private readonly List<Vector2Int> corners = new List<Vector2Int>(1024);
        private readonly List<Vector2> chain = new List<Vector2>(1024);
        private readonly List<float> crossings = new List<float>(16);
        private readonly List<Vector2> simplified = new List<Vector2>(128);
        private bool[] keep = System.Array.Empty<bool>();

        public int Width => width;

        public int Height => height;

        public int Length => width * height;

        public SilhouettePipeline(float halfWidth, float frameHeight, float pixel, float margin, float tolerance)
        {
            pixelSize = pixel;
            originX = -halfWidth;
            width = Mathf.CeilToInt(2f * halfWidth / pixel);
            height = Mathf.CeilToInt(frameHeight / pixel);
            simplifyTolerance = tolerance;

            // Раздуваем на запас ПЛЮС допуск упрощения: упрощение имеет право
            // срезать угол внутрь ровно на допуск, и без этой добавки обещанный
            // запас в узких местах съело бы оно.
            dilationRadius = (margin + tolerance) / pixel;

            int size = width * height;
            raw = new bool[size];
            grown = new bool[size];
            polygon = new bool[size];
            outside = new bool[size];
            distance = new float[size];
            column = new float[size];

            int longest = Mathf.Max(width, height);
            line = new float[longest];
            envelope = new float[longest + 1];
            vertices = new int[longest];
            boundary = new float[longest];
        }

        /// <summary>Пиксель, в который попала точка. Ложь — точка вне кадра.</summary>
        public bool TryPixel(float x, float y, out int index)
        {
            int pixelX = Mathf.FloorToInt((x - originX) / pixelSize);
            int pixelY = Mathf.FloorToInt(y / pixelSize);
            index = pixelY * width + pixelX;
            return pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height;
        }

        /// <summary>Положить сырой силуэт: с него начинается состав.</summary>
        public void LoadRaw(bool[] source) => System.Array.Copy(source, raw, raw.Length);

        /// <summary>Добавить в состав второй силуэт. Так печётся вырез пары.</summary>
        public void AddRaw(bool[] source)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] |= source[i];
            }
        }

        /// <summary>
        /// Довести сырой силуэт до ломаной контура.
        ///
        /// Порядок обязателен: раздуть → опустить на пол → замкнуть низ →
        /// залить просветы → обвести → упростить. Заливка до замыкания низа
        /// оставила бы просвет между ног открытым снизу и не сочла бы его
        /// дыркой — а в стене это столбик между ног.
        /// </summary>
        /// <param name="clearance">Измеренный запас: на сколько метров контур отстоит от кожи</param>
        /// <returns>Ломаная от левой ступни вверх и вниз к правой, метры. Пусто — силуэта нет.</returns>
        public Vector2[] Extract(out float clearance)
        {
            clearance = 0f;

            Dilate();
            if (!DropToFloor())
            {
                return null;
            }

            CloseBottom();
            FillHoles();

            if (!Trace())
            {
                return null;
            }

            if (!Cut())
            {
                return null;
            }

            Vector2[] result = Simplify();
            clearance = Clearance(result);
            return result;
        }

        // ========== БИТМАП ==========

        /// <summary>
        /// Раздуть силуэт диском радиуса <see cref="dilationRadius"/>: это и есть
        /// запас на пролезание.
        ///
        /// Считается через точное преобразование расстояний (Фельценшвальб):
        /// два прохода по столбцам и строкам, линейное время. Дилатация диском
        /// в лоб стоила бы полутора тысяч проверок на пиксель.
        /// </summary>
        private void Dilate()
        {
            SquaredDistance(raw);
            float limit = dilationRadius * dilationRadius;

            for (int i = 0; i < grown.Length; i++)
            {
                grown[i] = distance[i] <= limit;
            }
        }

        /// <summary>
        /// Квадрат расстояния от каждого пикселя до ближайшего занятого.
        /// Результат ложится в <see cref="distance"/>.
        /// </summary>
        private void SquaredDistance(bool[] source)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    line[y] = source[y * width + x] ? 0f : Far;
                }

                Transform1D(height);

                for (int y = 0; y < height; y++)
                {
                    column[y * width + x] = line[y];
                }
            }

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    line[x] = column[row + x];
                }

                Transform1D(width);

                for (int x = 0; x < width; x++)
                {
                    distance[row + x] = line[x];
                }
            }
        }

        /// <summary>
        /// Одномерное преобразование расстояний: нижняя огибающая парабол,
        /// поднятых на значения <see cref="line"/>. Классический алгоритм
        /// Фельценшвальба–Хаттенлохера, линейный по длине.
        /// </summary>
        private void Transform1D(int count)
        {
            int top = 0;
            vertices[0] = 0;
            envelope[0] = float.NegativeInfinity;
            envelope[1] = float.PositiveInfinity;

            for (int q = 1; q < count; q++)
            {
                float split = Intersection(q, vertices[top]);
                while (split <= envelope[top])
                {
                    top--;
                    split = Intersection(q, vertices[top]);
                }

                top++;
                vertices[top] = q;
                envelope[top] = split;
                envelope[top + 1] = float.PositiveInfinity;
            }

            for (int q = 0; q < count; q++)
            {
                boundary[q] = line[q];
            }

            top = 0;
            for (int q = 0; q < count; q++)
            {
                while (envelope[top + 1] < q)
                {
                    top++;
                }

                int source = vertices[top];
                float offset = q - source;
                line[q] = offset * offset + boundary[source];
            }
        }

        /// <summary>Где пересекаются параболы, поднятые на значения двух отсчётов.</summary>
        private float Intersection(int q, int p) =>
            ((line[q] + q * q) - (line[p] + p * p)) / (2 * q - 2 * p);

        /// <summary>
        /// Опустить силуэт на пол: строки ниже самой нижней занятой заполняются
        /// её содержимым.
        ///
        /// Низ выреза — это пол платформы, и дырка обязана до него доходить.
        /// Практически это пустая операция: поза уже посажена на пол обмером,
        /// и занятая строка нулевая. Но если поза когда-нибудь окажется
        /// приподнятой, вырез не превратится в дырку с перемычкой под ногами.
        /// </summary>
        /// <returns>Ложь — занятых пикселей нет вовсе.</returns>
        private bool DropToFloor()
        {
            int lowest = -1;
            for (int y = 0; y < height && lowest < 0; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (grown[row + x])
                    {
                        lowest = y;
                        break;
                    }
                }
            }

            if (lowest < 0)
            {
                return false;
            }

            for (int y = 0; y < lowest; y++)
            {
                System.Array.Copy(grown, lowest * width, grown, y * width, width);
            }

            return true;
        }

        /// <summary>
        /// Замкнуть нижнюю строку от левого края силуэта до правого.
        ///
        /// Без этого просвет между расставленными ногами открыт снизу, заливка
        /// не считает его дыркой, и в стене остаётся столбик ровно между ног.
        /// Пол платформы сплошной — значит и вырез внизу сплошной.
        /// </summary>
        private void CloseBottom()
        {
            int left = -1;
            int right = -1;

            for (int x = 0; x < width; x++)
            {
                if (!grown[x])
                {
                    continue;
                }

                if (left < 0)
                {
                    left = x;
                }

                right = x;
            }

            for (int x = left; x <= right; x++)
            {
                grown[x] = true;
            }
        }

        /// <summary>
        /// Залить внутренние просветы: между рукой и телом, между ног выше
        /// колен. В стене каждый такой просвет — столбик, между которым надо
        /// протискиваться, а вырез обязан быть проходимым целиком.
        ///
        /// Считается от обратного: разливаем фон от рамки кадра, и всё пустое,
        /// куда он не дотёк, объявляем занятым.
        /// </summary>
        private void FillHoles()
        {
            System.Array.Clear(outside, 0, outside.Length);
            flood.Clear();

            for (int x = 0; x < width; x++)
            {
                Seed(x, 0);
                Seed(x, height - 1);
            }

            for (int y = 0; y < height; y++)
            {
                Seed(0, y);
                Seed(width - 1, y);
            }

            while (flood.Count > 0)
            {
                int index = flood.Pop();
                int x = index % width;
                int y = index / width;

                Seed(x - 1, y);
                Seed(x + 1, y);
                Seed(x, y - 1);
                Seed(x, y + 1);
            }

            for (int i = 0; i < grown.Length; i++)
            {
                if (!outside[i])
                {
                    grown[i] = true;
                }
            }
        }

        private void Seed(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            int index = y * width + x;
            if (grown[index] || outside[index])
            {
                return;
            }

            outside[index] = true;
            flood.Push(index);
        }

        // ========== КОНТУР ==========

        private bool Filled(int x, int y) =>
            x >= 0 && x < width && y >= 0 && y < height && grown[y * width + x];

        /// <summary>
        /// Обвести внешний край: обход по рёбрам решётки так, чтобы занятая
        /// область всё время оставалась слева. Даёт замкнутый обход против
        /// часовой стрелки.
        ///
        /// Стартуем от самого нижнего левого занятого пикселя: у него нижнее
        /// ребро заведомо граничное, и обход заведомо начинается на внешнем
        /// краю, а не вокруг дырки.
        /// </summary>
        private bool Trace()
        {
            loop.Clear();

            int start = -1;
            for (int i = 0; i < grown.Length && start < 0; i++)
            {
                if (grown[i])
                {
                    start = i;
                }
            }

            if (start < 0)
            {
                return false;
            }

            var origin = new Vector2Int(start % width, start / width);
            Vector2Int node = origin;
            int direction = 0;

            // Граничных рёбер не больше, чем пикселей: обход, который столько
            // прошёл и не вернулся, зациклился, и продолжать нечего.
            int guard = width * height;

            do
            {
                loop.Add(node);
                direction = NextDirection(node, direction);
                if (direction < 0)
                {
                    return false;
                }

                node = new Vector2Int(node.x + StepX[direction], node.y + StepY[direction]);
            }
            while (node != origin && --guard > 0);

            if (guard <= 0)
            {
                Debug.LogError("SilhouettePipeline: обход контура не замкнулся");
                return false;
            }

            MergeCollinear();
            return loop.Count >= 3;
        }

        /// <summary>
        /// Куда идти из узла решётки. Ребро в сторону <c>d</c> граничное, если
        /// пиксель слева от него занят, а справа пуст.
        ///
        /// В седловине (занятые пиксели по диагонали) кандидатов два. Берём
        /// левый поворот — тогда область считается связной по сторонам, а не
        /// по углам, и обход не перескакивает через касание в одну точку.
        /// </summary>
        private int NextDirection(Vector2Int node, int incoming)
        {
            bool northEast = Filled(node.x, node.y);
            bool northWest = Filled(node.x - 1, node.y);
            bool southEast = Filled(node.x, node.y - 1);
            bool southWest = Filled(node.x - 1, node.y - 1);

            int candidates = 0;
            if (northEast && !southEast) candidates |= 1;
            if (northWest && !northEast) candidates |= 2;
            if (southWest && !northWest) candidates |= 4;
            if (southEast && !southWest) candidates |= 8;

            if (candidates == 0)
            {
                return -1;
            }

            // Порядок предпочтения: левый поворот, прямо, правый, назад.
            Order[0] = (incoming + 1) & 3;
            Order[1] = incoming;
            Order[2] = (incoming + 3) & 3;
            Order[3] = (incoming + 2) & 3;

            for (int i = 0; i < Order.Length; i++)
            {
                if ((candidates & (1 << Order[i])) != 0)
                {
                    return Order[i];
                }
            }

            return -1;
        }

        /// <summary>
        /// Склеить лежащие на одной прямой узлы. Обход идёт единичными рёбрами,
        /// и без склейки контур из тысяч точек, из которых значимы десятки.
        /// </summary>
        private void MergeCollinear()
        {
            corners.Clear();
            int count = loop.Count;

            for (int i = 0; i < count; i++)
            {
                Vector2Int previous = loop[(i + count - 1) % count];
                Vector2Int current = loop[i];
                Vector2Int next = loop[(i + 1) % count];

                if ((current.x - previous.x) * (next.y - current.y) ==
                    (current.y - previous.y) * (next.x - current.x))
                {
                    continue;
                }

                corners.Add(current);
            }

            loop.Clear();
            loop.AddRange(corners);
        }

        /// <summary>
        /// Разрезать замкнутый обход по полу: выкинуть нижнее ребро и оставить
        /// открытую ломаную от левой ступни вверх и вниз к правой.
        ///
        /// Обход идёт против часовой стрелки, поэтому по полу он движется
        /// слева направо — и нужный кусок это весь остаток, взятый задом
        /// наперёд.
        /// </summary>
        private bool Cut()
        {
            int bottom = -1;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2Int from = loop[i];
                Vector2Int to = loop[(i + 1) % loop.Count];
                if (from.y == 0 && to.y == 0 && to.x > from.x)
                {
                    bottom = i;
                    break;
                }
            }

            if (bottom < 0)
            {
                Debug.LogError("SilhouettePipeline: контур не касается пола — вырез не к чему привязать");
                return false;
            }

            chain.Clear();
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2Int node = loop[(bottom - i + loop.Count * 2) % loop.Count];
                chain.Add(new Vector2(originX + node.x * pixelSize, node.y * pixelSize));
            }

            return chain.Count >= 3;
        }

        /// <summary>
        /// Упростить ломаную Дугласом–Пойкером: снять пиксельную лесенку,
        /// оставив силуэт. Концы неподвижны — они лежат на полу.
        /// </summary>
        private Vector2[] Simplify()
        {
            if (keep.Length < chain.Count)
            {
                keep = new bool[chain.Count];
            }

            System.Array.Clear(keep, 0, chain.Count);
            keep[0] = true;
            keep[chain.Count - 1] = true;

            var pending = new Stack<Vector2Int>(64);
            pending.Push(new Vector2Int(0, chain.Count - 1));

            while (pending.Count > 0)
            {
                Vector2Int span = pending.Pop();
                int farthest = -1;
                float worst = simplifyTolerance;

                for (int i = span.x + 1; i < span.y; i++)
                {
                    float deviation = DistanceToSegment(chain[i], chain[span.x], chain[span.y]);
                    if (deviation > worst)
                    {
                        worst = deviation;
                        farthest = i;
                    }
                }

                if (farthest < 0)
                {
                    continue;
                }

                keep[farthest] = true;
                pending.Push(new Vector2Int(span.x, farthest));
                pending.Push(new Vector2Int(farthest, span.y));
            }

            simplified.Clear();
            for (int i = 0; i < chain.Count; i++)
            {
                if (keep[i])
                {
                    simplified.Add(chain[i]);
                }
            }

            return simplified.ToArray();
        }

        private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 span = to - from;
            float lengthSquared = span.sqrMagnitude;
            if (lengthSquared < 1e-12f)
            {
                return (point - from).magnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - from, span) / lengthSquared);
            return (point - (from + span * t)).magnitude;
        }

        // ========== ПРОВЕРКА ==========

        /// <summary>
        /// Насколько готовый контур отстоит от кожи, м: самое узкое место
        /// по всему силуэту.
        ///
        /// Это и есть проверка обещанного запаса, а не оценка на глаз: контур
        /// заливается обратно в битмап, считается расстояние от каждого его
        /// пикселя до ближайшего пустого, и берётся минимум по занятым
        /// пикселям сырого силуэта. Ноль значит, что кожа торчит наружу —
        /// вырез меньше персонажа.
        /// </summary>
        private float Clearance(Vector2[] result)
        {
            FillPolygon(result);
            SquaredDistanceToEmpty();

            float worst = float.MaxValue;
            for (int i = 0; i < raw.Length; i++)
            {
                if (!raw[i])
                {
                    continue;
                }

                if (!polygon[i])
                {
                    return 0f;
                }

                worst = Mathf.Min(worst, distance[i]);
            }

            return worst == float.MaxValue ? 0f : Mathf.Sqrt(worst) * pixelSize;
        }

        /// <summary>
        /// Залить готовый контур обратно в битмап: ломаная плюс отрезок пола,
        /// которым она замыкается, чётно-нечётным правилом по строкам.
        /// </summary>
        private void FillPolygon(Vector2[] result)
        {
            System.Array.Clear(polygon, 0, polygon.Length);

            for (int y = 0; y < height; y++)
            {
                float scan = (y + 0.5f) * pixelSize;
                crossings.Clear();

                for (int i = 0; i < result.Length; i++)
                {
                    Vector2 from = result[i];
                    Vector2 to = result[(i + 1) % result.Length];

                    if (from.y <= scan == to.y <= scan)
                    {
                        continue;
                    }

                    crossings.Add(from.x + (scan - from.y) / (to.y - from.y) * (to.x - from.x));
                }

                crossings.Sort();

                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    int fromX = Mathf.Max(0, Mathf.CeilToInt((crossings[i] - originX) / pixelSize - 0.5f));
                    int toX = Mathf.Min(width - 1, Mathf.FloorToInt((crossings[i + 1] - originX) / pixelSize - 0.5f));

                    for (int x = fromX; x <= toX; x++)
                    {
                        polygon[y * width + x] = true;
                    }
                }
            }
        }

        /// <summary>Квадрат расстояния от каждого пикселя до ближайшего пикселя ВНЕ контура.</summary>
        private void SquaredDistanceToEmpty()
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                grown[i] = !polygon[i];
            }

            SquaredDistance(grown);
        }
    }
}
