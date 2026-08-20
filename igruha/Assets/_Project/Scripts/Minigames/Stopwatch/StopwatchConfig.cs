using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Числа баланса «Секундомера» (спека, раздел 8). Один ассет на игру,
    /// крутится без пересборки.
    ///
    /// Здесь лежит только то, у чего нет дома в другом конфиге — иначе получилось
    /// бы два источника правды и балансировка «не там». Остальное правится
    /// по месту:
    /// — размеры арены, шаг и скорость спуска клетки — <see cref="Igruha.Minigames.Circus.CircusArenaConfig"/>
    ///   (арена общая с «Порядком банок», спуск задан её геометрией);
    /// — жёсткий таймаут 300 с, число игроков, тексты обучалки, режим камеры —
    ///   <see cref="Igruha.Core.Minigame.MinigameDefinition"/> (Stopwatch.asset);
    /// — сколько болванок спавнить в соло-тесте — PlayerSpawner.debugPlayerCount
    ///   на объекте _Spawns, там же, где это работает у всех остальных игр;
    /// — скорость бега и высота прыжка — CharacterConfig;
    /// — порог застревания в клетке — StuckDetector.
    /// </summary>
    [CreateAssetMenu(fileName = "StopwatchConfig", menuName = "Igruha/Minigames/Stopwatch Config")]
    public sealed class StopwatchConfig : ScriptableObject
    {
        /// <summary>
        /// Строка таблицы 6.3: лимит ошибок и сколько худших получают ошибку
        /// за подраунд при таком стартовом составе.
        /// </summary>
        [System.Serializable]
        public struct RosterRule
        {
            [Tooltip("Стартовое число игроков")]
            public int players;
            [Tooltip("Сколько ошибок до вылета")]
            public int errorLimit;
            [Tooltip("K — сколько худших получают ошибку за подраунд")]
            public int worstPerSubround;
        }

        [Header("Подраунд — длительности стадий, с")]
        [Tooltip("Показ задания: табло объявляет тип и цель, кнопки не активны")]
        [SerializeField] private float briefingSeconds = 4f;
        [Tooltip("Окно отмера. Кончается досрочно, как только все живые нажали «стоп»")]
        [SerializeField] private float measureWindowSeconds = 15f;
        [Tooltip("Показ результатов: все результаты выводятся одновременно")]
        [SerializeField] private float resultsSeconds = 5f;
        [Tooltip("Опускание клеток получивших ошибку")]
        [SerializeField] private float cageDescendSeconds = 2f;
        [Tooltip("Открытие створок дна — только если кто-то исчерпал лимит")]
        [SerializeField] private float hatchOpenSeconds = 2f;
        [Tooltip("Пауза перед следующим подраундом")]
        [SerializeField] private float pauseSeconds = 1f;

        [Header("Цели")]
        [Tooltip("Нижняя граница окна целей на первом подраунде и её потолок, с")]
        [SerializeField] private Vector2 targetLowRange = new Vector2(3f, 8f);
        [Tooltip("Верхняя граница окна целей на первом подраунде и её потолок, с")]
        [SerializeField] private Vector2 targetHighRange = new Vector2(5f, 10f);
        [Tooltip("На сколько секунд окно целей уезжает вверх за подраунд")]
        [SerializeField] private float targetGrowthPerSubround = 0.8f;
        [Tooltip("Шаг округления цели, с")]
        [SerializeField] private float targetRounding = 0.5f;

        [Header("Ошибки")]
        [Tooltip("Таблица 6.3: лимит ошибок и K по стартовому составу")]
        [SerializeField] private RosterRule[] rosterRules =
        {
            new RosterRule { players = 2, errorLimit = 3, worstPerSubround = 1 },
            new RosterRule { players = 3, errorLimit = 3, worstPerSubround = 1 },
            new RosterRule { players = 4, errorLimit = 3, worstPerSubround = 1 },
            new RosterRule { players = 5, errorLimit = 3, worstPerSubround = 1 },
            new RosterRule { players = 6, errorLimit = 2, worstPerSubround = 2 },
            new RosterRule { players = 7, errorLimit = 2, worstPerSubround = 2 },
            new RosterRule { players = 8, errorLimit = 2, worstPerSubround = 2 }
        };
        [Tooltip("Лобби из двух: если отклонения различаются меньше чем на столько секунд, ошибку получают оба")]
        [SerializeField] private float twoPlayerTieThreshold = 0.05f;

        [Header("Медведь")]
        [Tooltip("Скорость медведя, м/с. Меньше, чем у игрока (6.5): догоняет срезанием по хорде, а не скоростью")]
        [SerializeField] private float bearSpeed = 5.5f;
        [Tooltip("Сколько секунд медведь разворачивается и разгоняется, прежде чем впервые ударить")]
        [SerializeField] private float bearFirstAttackDelay = 3f;
        [Tooltip("Радиус удара лапой, м")]
        [SerializeField] private float bearStrikeRadius = 1.5f;
        [Tooltip("Скорость отлёта от удара, м/с")]
        [SerializeField] private float bearKnockbackSpeed = 8f;
        [Tooltip("Сколько длится отлёт и падение, с")]
        [SerializeField] private float bearKnockdownDuration = 1.5f;
        [Tooltip("Через сколько секунд после падения тело исчезает. Коллизия снимается сразу")]
        [SerializeField] private float bodyDespawnDelay = 1.5f;
        [Tooltip("Скорость патрулирования пустой ямы, м/с")]
        [SerializeField] private float bearPatrolSpeed = 2.5f;

        [Header("Отвлекалки")]
        [Tooltip("С какого подраунда включаются отвлекалки")]
        [SerializeField] private int distractionFirstSubround = 3;
        [Tooltip("За сколько подраундов интенсивность доезжает до максимума")]
        [SerializeField] private float distractionRampSubrounds = 4f;
        [Tooltip("Период тикающего звука, с. Выбирается случайно на подраунд — и он врёт")]
        [SerializeField] private Vector2 tickPeriodRange = new Vector2(0.75f, 1.35f);
        [Tooltip("Запретная зона вокруг ровной секунды, с. Диапазон 0.75…1.35 её накрывает, а честный тик убивает всю механику: под него игрок просто считает секунды. Ноль — зону выключить")]
        [SerializeField] private float tickHonestDeadZone = 0.06f;
        [Tooltip("Интервал между рёвами медведя, с")]
        [SerializeField] private Vector2 roarIntervalRange = new Vector2(6f, 12f);
        [Tooltip("Интервал между пробегами луча прожектора, с")]
        [SerializeField] private Vector2 spotlightIntervalRange = new Vector2(5f, 9f);

        [Header("Флаги плейтеста (раздел 8.7)")]
        [Tooltip("Вернуть блокирующий забег в яме из LDD, если параллельный окажется незаметным")]
        [SerializeField] private bool blockingPitScene;
        [Tooltip("Кнопка соседа загорается на его «старт» и горит до конца фазы отмера. Выключить, если подсказка окажется вредной")]
        [SerializeField] private bool neighbourButtonLights = true;
        [Tooltip("Показать скрытый таймер отмера — только для отладки замеров")]
        [SerializeField] private bool debugShowTimer;

        public float BriefingSeconds => briefingSeconds;
        public float MeasureWindowSeconds => measureWindowSeconds;
        public float ResultsSeconds => resultsSeconds;
        public float CageDescendSeconds => cageDescendSeconds;
        public float HatchOpenSeconds => hatchOpenSeconds;
        public float PauseSeconds => pauseSeconds;
        public float TargetRounding => Mathf.Max(0.01f, targetRounding);
        public float TwoPlayerTieThreshold => twoPlayerTieThreshold;

        public float BearSpeed => bearSpeed;
        public float BearFirstAttackDelay => bearFirstAttackDelay;
        public float BearStrikeRadius => bearStrikeRadius;
        public float BearKnockbackSpeed => bearKnockbackSpeed;
        public float BearKnockdownDuration => bearKnockdownDuration;
        public float BodyDespawnDelay => bodyDespawnDelay;
        public float BearPatrolSpeed => bearPatrolSpeed;

        public int DistractionFirstSubround => distractionFirstSubround;
        public Vector2 TickPeriodRange => tickPeriodRange;

        /// <summary>
        /// Период тика для подраунда. <paramref name="roll"/> — 0..1 от
        /// серверного рандома.
        ///
        /// Значения вокруг ровной секунды выбрасываются: тик, совпадающий
        /// с секундой, перестаёт врать, и подраунд превращается в устный счёт
        /// под метроном — ровно то, против чего эта механика и придумана.
        /// </summary>
        public float GetTickPeriod(float roll)
        {
            float period = Mathf.Lerp(tickPeriodRange.x, tickPeriodRange.y, Mathf.Clamp01(roll));
            if (tickHonestDeadZone <= 0f)
            {
                return period;
            }

            float distance = period - 1f;
            if (Mathf.Abs(distance) >= tickHonestDeadZone)
            {
                return period;
            }

            // Отодвигаем к ближайшей границе зоны, а не пересчитываем заново:
            // повторный бросок сместил бы распределение сильнее.
            float pushed = distance >= 0f ? 1f + tickHonestDeadZone : 1f - tickHonestDeadZone;
            return Mathf.Clamp(pushed, tickPeriodRange.x, tickPeriodRange.y);
        }
        public Vector2 RoarIntervalRange => roarIntervalRange;
        public Vector2 SpotlightIntervalRange => spotlightIntervalRange;

        public bool BlockingPitScene => blockingPitScene;
        public bool NeighbourButtonLights => neighbourButtonLights;
        public bool DebugShowTimer => debugShowTimer;

        /// <summary>
        /// Тип подраунда: А → Б → А → В → А → Б → А → В. Базовый тип чередуется
        /// с необычными, поэтому всегда есть и знакомый ритм, и новизна.
        /// </summary>
        public StopwatchSubroundType GetSubroundType(int subround)
        {
            if (subround % 2 == 1)
            {
                return StopwatchSubroundType.Precision;
            }

            return (subround / 2) % 2 == 1 ? StopwatchSubroundType.Ceiling : StopwatchSubroundType.Floor;
        }

        /// <summary>
        /// Окно, из которого сервер берёт цель подраунда. Едет вверх от
        /// подраунда к подраунду: с длинным интервалом ошибиться заметно легче,
        /// и игра сама сходится к финалу.
        /// </summary>
        public void GetTargetWindow(int subround, out float low, out float high)
        {
            float growth = targetGrowthPerSubround * Mathf.Max(0, subround - 1);
            low = Mathf.Clamp(targetLowRange.x + growth, targetLowRange.x, targetLowRange.y);
            high = Mathf.Clamp(targetHighRange.x + growth, targetHighRange.x, targetHighRange.y);
            if (high < low)
            {
                high = low;
            }
        }

        /// <summary>Округление цели до шага. Рандом только серверный — это его результат.</summary>
        public float RoundTarget(float seconds)
        {
            float step = TargetRounding;
            return Mathf.Round(seconds / step) * step;
        }

        /// <summary>
        /// Интенсивность отвлекалок на подраунде, 0..1. До
        /// <see cref="DistractionFirstSubround"/> — ноль: первые подраунды
        /// нужны, чтобы игрок понял механику, а не тонул в шуме.
        /// </summary>
        public float GetDistractionIntensity(int subround)
        {
            float ramp = Mathf.Max(1f, distractionRampSubrounds);
            return Mathf.Clamp01((subround - (distractionFirstSubround - 1)) / ramp);
        }

        /// <summary>Лимит ошибок для стартового состава. Вне таблицы — ближайшая известная строка.</summary>
        public int GetErrorLimit(int startingPlayers)
        {
            return FindRule(startingPlayers).errorLimit;
        }

        /// <summary>
        /// Сколько худших получают ошибку в этом подраунде.
        ///
        /// Табличное K зажимается числом живых минус один: иначе лобби из шести,
        /// доигравшее до двух живых, каждый подраунд выдавало бы ошибку обоим,
        /// и матч заканчивался бы всеобщим вылетом вместо победителя.
        /// </summary>
        public int GetWorstCount(int startingPlayers, int aliveCount)
        {
            int table = FindRule(startingPlayers).worstPerSubround;
            return Mathf.Max(0, Mathf.Min(table, aliveCount - 1));
        }

        private RosterRule FindRule(int startingPlayers)
        {
            if (rosterRules == null || rosterRules.Length == 0)
            {
                Debug.LogError($"{name}: таблица состава пуста — лимит ошибок и K брать неоткуда", this);
                return new RosterRule { players = startingPlayers, errorLimit = 2, worstPerSubround = 1 };
            }

            RosterRule best = rosterRules[0];
            int bestDistance = Mathf.Abs(best.players - startingPlayers);
            for (int i = 1; i < rosterRules.Length; i++)
            {
                int distance = Mathf.Abs(rosterRules[i].players - startingPlayers);
                if (distance < bestDistance)
                {
                    best = rosterRules[i];
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
