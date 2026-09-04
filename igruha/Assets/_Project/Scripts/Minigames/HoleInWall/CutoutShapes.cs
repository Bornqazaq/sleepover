using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Формы вырезов одной дорожки: контур на каждую из четырёх поз, готовый
    /// к употреблению стеной.
    ///
    /// Заводится один раз на раунд, когда известен состав дорожки, и дальше
    /// только читается. Вся тяжёлая работа с силуэтом — скиннинг, растеризация,
    /// объединение пары — сделана на этапе сборки ассета: меши персонажей
    /// в рантайме нечитаемы (<c>isReadable = false</c>), да и миллион вершин
    /// в кадре никто не ждёт.
    /// </summary>
    /// <remarks>
    /// <b>Контур всегда есть.</b> Не нашёлся состав — берётся ростерный,
    /// не нашёлся и он — строится прямоугольник по <c>HoleInWallConfig</c>,
    /// как было до настоящих силуэтов. Поэтому у стены нет ветки «формы нет»:
    /// она всегда режет полотно по ломаной.
    /// </remarks>
    public sealed class CutoutShapes
    {
        /// <summary>Разделитель имён в ключе пары.</summary>
        private const char CompositionSeparator = '+';

        private readonly HoleInWallConfig config;

        /// <summary>Контуры по номеру позы минус один. Пусто в ячейке — поза считается прямоугольником.</summary>
        private readonly CutoutSilhouette[] shapes = new CutoutSilhouette[HoleInWallConfig.PoseCount];

        /// <summary>Прямоугольные запасные ломаные. Строятся по требованию и живут до конца раунда.</summary>
        private readonly Vector2[][] fallbackOutlines = new Vector2[HoleInWallConfig.PoseCount][];

        /// <summary>Ключ состава, по которому нашлись контуры. Для отчёта в логе.</summary>
        public string Composition { get; }

        /// <summary>Контуры нашлись под сам состав, а не под ростер и не прямоугольником.</summary>
        public bool Exact { get; }

        public CutoutShapes(HoleInWallConfig gameConfig, string composition)
        {
            config = gameConfig;
            Composition = composition;

            HoleInWallSilhouettes asset = gameConfig != null ? gameConfig.Silhouettes : null;
            if (asset == null)
            {
                return;
            }

            bool exact = !string.IsNullOrEmpty(composition);

            for (int i = 0; i < shapes.Length; i++)
            {
                var pose = (HoleInWallPose)(i + 1);
                CutoutSilhouette shape = asset.Find(composition, pose);

                if (shape == null || !shape.Valid)
                {
                    // Состава нет в ассете — падаем на ростерный контур: он
                    // велик каждому, но пролезть в него может любой.
                    shape = asset.Roster(pose);
                    exact = false;
                }

                shapes[i] = shape != null && shape.Valid ? shape : null;
            }

            Exact = exact;
        }

        /// <summary>
        /// Ключ персонажа — имя его контроллера аниматора.
        ///
        /// Берём именно контроллер, а не имя объекта: у клона имя зависит от
        /// того, кто его создал (сеть оставляет «Aza(Clone)», локальный спавн
        /// переименовывает в «Player_1»), а контроллер у каждого персонажа
        /// свой и переживает и то, и другое. Заводить на префабе новый
        /// компонент нельзя: префабы персонажей заморожены.
        /// </summary>
        public static string KeyOf(GameObject avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            var animator = avatar.GetComponentInChildren<Animator>(true);
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            return controller != null ? controller.name : null;
        }

        /// <summary>
        /// Ключ состава из ключей участников. Порядок не значим — пара
        /// сама решает, кто в какой вырез, — поэтому имена сортируются.
        /// </summary>
        public static string Compose(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return string.CompareOrdinal(first, second) <= 0
                ? first + CompositionSeparator + second
                : second + CompositionSeparator + first;
        }

        /// <summary>Контур позы: от левой ступни вверх и вниз к правой, метры от места игрока.</summary>
        public Vector2[] Outline(HoleInWallPose pose)
        {
            int index = (int)pose - 1;
            if (index < 0 || index >= shapes.Length)
            {
                return null;
            }

            CutoutSilhouette shape = shapes[index];
            return shape != null ? shape.Outline : FallbackOutline(pose, index);
        }

        /// <summary>Описанный прямоугольник выреза, м. По нему считается разнос вырезов и место на дорожке.</summary>
        public Vector2 Size(HoleInWallPose pose)
        {
            int index = (int)pose - 1;
            if (index < 0 || index >= shapes.Length)
            {
                return Vector2.zero;
            }

            CutoutSilhouette shape = shapes[index];
            return shape != null ? shape.Size : config.SilhouetteSize(pose);
        }

        /// <summary>
        /// Допуск попадания в этот вырез, м от его центра.
        ///
        /// Это <b>не</b> геометрия, а правило: игра намеренно пропускает тех,
        /// кто по геометрии не влезает. Вырез — это силуэт плюс 0.11 м, то
        /// есть по-настоящему отклониться можно на 11 см, а допуск впятеро
        /// шире. Так задумано (спека 5.3): проверка дискретная, иначе край
        /// выреза наказывал бы толстого там, где проходит тонкий.
        ///
        /// ⚠️ <b>Но шире самого выреза допуск быть не может.</b> Пока вырез был
        /// один на весь ростер, он был не уже 1.37 м, и настроенные ±0.576 м
        /// всегда лежали внутри. Вырезы под конкретного персонажа уже: у Шланги
        /// «Свечка» всего 0.69 м. Без этого ограничения игрок стоял бы целиком
        /// на сплошной плите — не задевая край, а мимо всей дырки — и проходил.
        /// Затрагивает 10 сочетаний из 32, все на узких позах у мелких
        /// персонажей; на «Титанике» и «Чайнике» допуск остаётся настроенным.
        /// </summary>
        public float Tolerance(HoleInWallPose pose) =>
            Mathf.Min(config.HitTolerance, Size(pose).x * 0.5f);

        /// <summary>
        /// Самый широкий пролёт выреза на отрезке высоты, м от места игрока.
        ///
        /// Берётся именно самый широкий: по этим пролётам ставятся коробки
        /// коллизии, и ошибка в большую сторону — лишний сантиметр дырки,
        /// а в меньшую — застрявший в стене игрок.
        /// </summary>
        /// <returns>Ложь — на этой высоте выреза нет.</returns>
        public bool TrySpan(HoleInWallPose pose, float fromY, float toY, out float left, out float right)
        {
            left = 0f;
            right = 0f;

            Vector2[] outline = Outline(pose);
            if (outline == null || outline.Length < 2 || toY <= fromY)
            {
                return false;
            }

            left = float.MaxValue;
            right = float.MinValue;

            for (int i = 1; i < outline.Length; i++)
            {
                Accumulate(outline[i - 1], outline[i], fromY, toY, ref left, ref right);
            }

            return right > left;
        }

        /// <summary>
        /// Отрезок контура, обрезанный полосой высот: чем он достаёт влево
        /// и вправо. Обрезка честная — считаем x в точках пересечения с
        /// границами полосы, а не берём вершины целиком.
        /// </summary>
        private static void Accumulate(Vector2 from, Vector2 to, float fromY, float toY,
            ref float left, ref float right)
        {
            float lowY = Mathf.Min(from.y, to.y);
            float highY = Mathf.Max(from.y, to.y);

            if (highY < fromY || lowY > toY)
            {
                return;
            }

            float startX;
            float endX;

            if (Mathf.Approximately(from.y, to.y))
            {
                startX = from.x;
                endX = to.x;
            }
            else
            {
                float startY = Mathf.Clamp(from.y, fromY, toY);
                float endY = Mathf.Clamp(to.y, fromY, toY);
                float slope = (to.x - from.x) / (to.y - from.y);
                startX = from.x + (startY - from.y) * slope;
                endX = from.x + (endY - from.y) * slope;
            }

            left = Mathf.Min(left, Mathf.Min(startX, endX));
            right = Mathf.Max(right, Mathf.Max(startX, endX));
        }

        /// <summary>
        /// Прямоугольная ломаная по габариту из конфига: контура нет вовсе,
        /// а вырез в стене быть обязан. Ровно то, чем игра жила до настоящих
        /// силуэтов.
        /// </summary>
        private Vector2[] FallbackOutline(HoleInWallPose pose, int index)
        {
            if (fallbackOutlines[index] != null)
            {
                return fallbackOutlines[index];
            }

            Vector2 size = config != null ? config.SilhouetteSize(pose) : Vector2.zero;
            if (size.x <= 0f || size.y <= 0f)
            {
                return null;
            }

            float half = size.x * 0.5f;
            fallbackOutlines[index] = new[]
            {
                new Vector2(-half, 0f),
                new Vector2(-half, size.y),
                new Vector2(half, size.y),
                new Vector2(half, 0f)
            };

            return fallbackOutlines[index];
        }
    }
}
