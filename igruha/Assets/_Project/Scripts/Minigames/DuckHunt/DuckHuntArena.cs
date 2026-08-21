using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Метрика башни и перевод мировой точки в прогресс по трассе.
    ///
    /// Система координат арены (локальная относительно <see cref="origin"/>):
    /// X — вдоль длины этажа, 0…48 ШП; Z — поперёк, 0 у открытой грани и
    /// 14 у глухой стены; Y — высота, этаж i лежит на i × шаг этажей.
    ///
    /// Змейка: лестничная комната этажа N — это вход этажа N+1, поэтому
    /// направление трассы чередуется. Чётные этажи бегут 0 → 48, нечётные
    /// 48 → 0. Прогресс p всегда считается от входа к выходу, то есть у
    /// обоих направлений он растёт одинаково — правила игры о змейке не знают.
    ///
    /// Прогресс берётся из положения, а не из триггеров: триггер можно
    /// проскочить на скорости или в прыжке, а положение есть всегда — в том
    /// числе у того, кто падает сквозь провал в полу.
    /// </summary>
    public sealed class DuckHuntArena : MonoBehaviour
    {
        /// <summary>Допуск на границе этажей, юниты: миллиметр, чтобы стоящий на перекрытии считался этажом выше, а не ниже.</summary>
        private const float FloorBoundaryTolerance = 0.001f;


        [SerializeField] private DuckHuntConfig config;
        [Tooltip("Начало координат арены: угол первого этажа, где X = 0 и Z = 0 у открытой грани. Пусто — берётся сам объект арены")]
        [SerializeField] private Transform origin;
        [Tooltip("Подъёмы лестничных комнат по этажам. Заполняет билдер арены")]
        [SerializeField] private DuckHuntStairs[] stairs = System.Array.Empty<DuckHuntStairs>();

        public DuckHuntConfig Config => config;

        /// <summary>Подъём лестничной комнаты этажа. Пусто — этажа нет или билдер не отработал.</summary>
        public DuckHuntStairs GetStairs(int floorIndex) =>
            floorIndex >= 0 && floorIndex < stairs.Length ? stairs[floorIndex] : null;

        private Transform Origin => origin != null ? origin : transform;

        /// <summary>Этажей в башне.</summary>
        public int FloorCount => config != null ? config.FloorCount : 0;

        /// <summary>Чётные этажи бегут вдоль X вперёд, нечётные — назад. Индекс с нуля.</summary>
        public static bool IsForwardFloor(int floorIndex) => (floorIndex & 1) == 0;

        /// <summary>Номер этажа, на котором находится точка. Индекс с нуля, зажат в границы башни.</summary>
        public int GetFloorIndex(Vector3 worldPosition)
        {
            if (config == null || config.FloorStepUnits <= 0f)
            {
                return 0;
            }

            float localY = Origin.InverseTransformPoint(worldPosition).y;

            // Допуск на границе обязателен: стоящий на перекрытии игрок лежит
            // ровно на ней, а 11.52 / 5.76 в float даёт 1.9999999 — этаж
            // выходил на единицу ниже. Вместе с этажом переворачивалось и
            // направление трассы (змейка), то есть прогресс p становился
            // зеркальным, и по нему считались места.
            int index = Mathf.FloorToInt((localY + FloorBoundaryTolerance) / config.FloorStepUnits);
            return Mathf.Clamp(index, 0, Mathf.Max(0, config.FloorCount - 1));
        }

        /// <summary>
        /// Прогресс вдоль трассы этого этажа, ШП: 0 у входа, длина этажа у выхода.
        /// Направление берётся по номеру этажа, поэтому у змейки прогресс
        /// растёт в ту же сторону на любом этаже.
        /// </summary>
        public float GetProgressWidths(Vector3 worldPosition, int floorIndex)
        {
            if (config == null || config.CharacterWidth <= 0f)
            {
                return 0f;
            }

            float along = Origin.InverseTransformPoint(worldPosition).x / config.CharacterWidth;
            return IsForwardFloor(floorIndex) ? along : config.FloorLengthWidths - along;
        }

        /// <summary>Мировая точка по координатам арены: прогресс вдоль трассы, отступ от открытой грани и этаж.</summary>
        public Vector3 GetWorldPoint(int floorIndex, float progressWidths, float depthWidths, float heightWidths = 0f)
        {
            if (config == null)
            {
                return Origin.position;
            }

            float along = IsForwardFloor(floorIndex)
                ? progressWidths
                : config.FloorLengthWidths - progressWidths;

            return Origin.TransformPoint(new Vector3(
                config.ToUnits(along),
                floorIndex * config.FloorStepUnits + config.ToUnits(heightWidths),
                config.ToUnits(depthWidths)));
        }

        /// <summary>Высота пола этажа в мировых координатах.</summary>
        public float GetFloorBaseY(int floorIndex) =>
            config != null ? Origin.position.y + floorIndex * config.FloorStepUnits : Origin.position.y;

        /// <summary>
        /// Сравнить прогресс двух игроков. Больше нуля — первый дальше по трассе.
        /// Единая точка сравнения: по ней расставляются места и живым, и погибшим.
        /// </summary>
        public static int CompareProgress(int floorA, float progressA, int floorB, float progressB)
        {
            if (floorA != floorB)
            {
                return floorA.CompareTo(floorB);
            }

            return progressA.CompareTo(progressB);
        }
    }
}
