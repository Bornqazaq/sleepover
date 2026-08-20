using System;
using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Числа баланса «Порядка банок» (спека, раздел 8). Один ассет на игру,
    /// крутится без пересборки.
    ///
    /// Здесь лежит только то, у чего нет дома в другом конфиге — иначе получилось
    /// бы два источника правды и балансировка «не там». Остальное правится
    /// по месту:
    /// — размеры арены, высоты клетки и границы её хода — <see cref="Igruha.Minigames.Circus.CircusArenaConfig"/>
    ///   (арена общая с «Секундомером», числа тоже общие);
    /// — скорости, радиус удара и задержка медведя — <c>CircusBearConfig</c>
    ///   (общий на обе игры, поэтому не здесь: вторая копия разъехалась бы с первой);
    /// — жёсткий таймаут 600 с, мин/макс игроков, тексты обучалки, режим камеры —
    ///   <see cref="Igruha.Core.Minigame.MinigameDefinition"/> (CansOrder.asset);
    /// — сколько болванок спавнить в соло-тесте — PlayerSpawner.debugPlayerCount
    ///   на объекте _Spawns, там же, где это работает у всех остальных игр;
    /// — скорость бега и высота прыжка — CharacterConfig.
    /// </summary>
    [CreateAssetMenu(fileName = "CansOrderConfig", menuName = "Igruha/Minigames/Cans Order Config")]
    public sealed class CansOrderConfig : ScriptableObject
    {
        /// <summary>
        /// Строка состава: сколько банок в задании и сколько игроков выбывает
        /// за раунд при таком числе живых.
        ///
        /// Таблицы 8.2 и 8.3 спеки сведены в одну структуру намеренно: обе
        /// читаются по одному и тому же ключу — числу живых в начале раунда,
        /// — и держать их порознь значило бы дважды описывать один и тот же
        /// состав и однажды забыть поправить вторую.
        /// </summary>
        [Serializable]
        public struct RosterRule
        {
            [Tooltip("Живых игроков в начале раунда")]
            public int players;
            [Tooltip("Сколько банок в задании при таком составе (8.2)")]
            public int cans;
            [Tooltip("Сколько игроков выбывает за раунд при таком составе (6.1 и 8.3)")]
            public int eliminatedPerRound;
        }

        /// <summary>
        /// Одна банка палитры: цвет и символ.
        ///
        /// Символ не украшение и не дубль цвета: он единственное, по чему
        /// расстановку на табло различает дальтоник. Требование проверяется
        /// отдельным пунктом DoD арт-фазы, поэтому пара живёт вместе и порознь
        /// не задаётся.
        /// </summary>
        [Serializable]
        public struct CanKind
        {
            [Tooltip("Имя банки — для отладочных логов и подсказок взаимодействия")]
            public string displayName;
            [Tooltip("Цвет банки. Он же красит символ в строке табло")]
            public Color color;
            [Tooltip("Символ банки. На табло рисуется текстом, на самой банке — гранью (арт-фаза)")]
            public string symbol;
        }

        [Header("Длительности стадий, с (8.1)")]
        [Tooltip("Брифинг раунда: табло объявляет номер раунда и число банок, клетки выживших едут наверх")]
        [SerializeField] private float briefingSeconds = 4f;
        [Tooltip("Подъём клеток наверх внутри брифинга. Обязан быть не длиннее самого брифинга")]
        [SerializeField] private float briefingRiseSeconds = 2f;
        [Tooltip("Окно выставления. КРИТИЧЕСКИЙ параметр: главный рычаг длительности матча (8.7)")]
        [SerializeField] private float placementWindowSeconds = 10f;
        [Tooltip("Показ результатов на табло. КРИТИЧЕСКИЙ параметр: второй рычаг длительности (8.7)")]
        [SerializeField] private float resultsSeconds = 5f;
        [Tooltip("Спуск клетки на новую высоту. Идёт внутри показа результатов, в первые его секунды")]
        [SerializeField] private float cageDescendSeconds = 2f;
        [Tooltip("Открытие створок дна — только на последнем круге раунда")]
        [SerializeField] private float hatchOpenSeconds = 2f;
        [Tooltip("Пауза между кругами: табло гаснет")]
        [SerializeField] private float pauseSeconds = 1f;

        [Header("Состав: банки и выбывание (8.2, 8.3)")]
        [Tooltip("Таблицы 8.2 и 8.3: сколько банок и сколько выбывает при таком числе живых")]
        [SerializeField] private RosterRule[] rosterRules =
        {
            new RosterRule { players = 2, cans = 5, eliminatedPerRound = 1 },
            new RosterRule { players = 3, cans = 5, eliminatedPerRound = 1 },
            new RosterRule { players = 4, cans = 5, eliminatedPerRound = 1 },
            new RosterRule { players = 5, cans = 5, eliminatedPerRound = 1 },
            new RosterRule { players = 6, cans = 5, eliminatedPerRound = 2 },
            new RosterRule { players = 7, cans = 5, eliminatedPerRound = 2 },
            new RosterRule { players = 8, cans = 5, eliminatedPerRound = 2 }
        };

        [Header("Палитра банок (8.2)")]
        [Tooltip("Пять пар «цвет + символ». Строк должно быть не меньше, чем банок в самом большом задании. Символ обязан быть в шрифте табло: отсутствующий TMP рисует квадратиком, и две банки становятся неразличимыми")]
        [SerializeField] private CanKind[] palette =
        {
            new CanKind { displayName = "Круг",        color = new Color(0.85f, 0.20f, 0.20f), symbol = "●" },
            new CanKind { displayName = "Треугольник", color = new Color(0.20f, 0.45f, 0.90f), symbol = "▲" },
            new CanKind { displayName = "Квадрат",     color = new Color(0.95f, 0.78f, 0.15f), symbol = "■" },
            new CanKind { displayName = "Звезда",      color = new Color(0.25f, 0.72f, 0.32f), symbol = "*" },
            new CanKind { displayName = "Крест",       color = new Color(0.75f, 0.35f, 0.85f), symbol = "×" }
        };

        [Header("Страховки от бесконечного матча (6.5, 8.3)")]
        [Tooltip("Потолок кругов в одном раунде. Страховка, а не правило: в нормальной игре не срабатывает. Жёсткий таймаут всей мини-игры (600 с) лежит не здесь, а в MinigameDefinition")]
        [SerializeField] private int roundCircleCap = 12;

        [Header("Флаги плейтеста (8.8)")]
        [Tooltip("Сколько полных расстановок показывать на табло (8.4). Ноль — вернуть поведение «только счёт»")]
        [SerializeField] private int boardTopRows = 3;
        [Tooltip("Полка хранит прошлую расстановку между кругами. Выключить, если окажется, что это слишком облегчает")]
        [SerializeField] private bool shelfKeepsArrangement = true;
        [Tooltip("ОТЛАДОЧНЫЙ: показать своё число совпадений сразу после подтверждения. Ломает честность игры — в матче не включать")]
        [SerializeField] private bool showOwnMatchesImmediately;
        [Tooltip("ОТЛАДОЧНЫЙ: печатать скрытую расстановку в консоль сервера")]
        [SerializeField] private bool debugRevealSolution;

        public float BriefingSeconds => Mathf.Max(0f, briefingSeconds);

        /// <summary>Подъём не может быть длиннее брифинга, внутри которого идёт: иначе клетки не доедут до первого окна выставления.</summary>
        public float BriefingRiseSeconds => Mathf.Clamp(briefingRiseSeconds, 0f, BriefingSeconds);

        public float PlacementWindowSeconds => Mathf.Max(0.01f, placementWindowSeconds);
        public float ResultsSeconds => Mathf.Max(0f, resultsSeconds);

        /// <summary>Спуск идёт внутри показа результатов и не может его пережить — иначе клетка едет уже в следующем круге.</summary>
        public float CageDescendSeconds => Mathf.Clamp(cageDescendSeconds, 0f, ResultsSeconds);

        public float HatchOpenSeconds => Mathf.Max(0f, hatchOpenSeconds);
        public float PauseSeconds => Mathf.Max(0f, pauseSeconds);

        /// <summary>Длина обычного круга: выставление + показ + пауза. Створки сюда не входят — они только на последнем круге раунда.</summary>
        public float CircleSeconds => PlacementWindowSeconds + ResultsSeconds + PauseSeconds;

        public int RoundCircleCap => Mathf.Max(1, roundCircleCap);
        public int BoardTopRows => Mathf.Max(0, boardTopRows);
        public bool ShelfKeepsArrangement => shelfKeepsArrangement;
        public bool ShowOwnMatchesImmediately => showOwnMatchesImmediately;
        public bool DebugRevealSolution => debugRevealSolution;

        /// <summary>Сколько всего банок описано палитрой — потолок задания.</summary>
        public int PaletteSize => palette != null ? palette.Length : 0;

        /// <summary>Банка палитры по идентификатору. Идентификатор банки — это её индекс в палитре.</summary>
        public CanKind GetCanKind(int canId)
        {
            if (palette == null || palette.Length == 0)
            {
                Debug.LogError($"{name}: палитра банок пуста — рисовать нечем", this);
                return new CanKind { displayName = "?", color = Color.white, symbol = "?" };
            }

            return palette[Mathf.Clamp(canId, 0, palette.Length - 1)];
        }

        /// <summary>
        /// Сколько банок в задании при таком числе живых (8.2). Зажато палитрой:
        /// задание из шести банок при пяти описанных цветах — это две одинаковые
        /// банки на полке, то есть расстановка, которую нельзя ни собрать, ни прочитать.
        /// </summary>
        public int GetCanCount(int alivePlayers)
        {
            int table = FindRule(alivePlayers).cans;
            return Mathf.Clamp(table, 1, Mathf.Max(1, PaletteSize));
        }

        /// <summary>
        /// Сколько игроков выбывает за раунд при таком числе живых (6.1).
        ///
        /// Зажато числом живых минус один: квота, равная составу, закончила бы
        /// раунд всеобщим вылетом, и победителя у матча не осталось бы.
        /// В штатной таблице это никогда не срабатывает — страховка от правки
        /// таблицы в инспекторе.
        /// </summary>
        public int GetEliminationQuota(int alivePlayers)
        {
            int table = FindRule(alivePlayers).eliminatedPerRound;
            return Mathf.Max(1, Mathf.Min(table, alivePlayers - 1));
        }

        /// <summary>Ближайшая строка таблицы. Вне диапазона — ближайший известный состав.</summary>
        private RosterRule FindRule(int alivePlayers)
        {
            if (rosterRules == null || rosterRules.Length == 0)
            {
                Debug.LogError($"{name}: таблица состава пуста — число банок и квоту вылета брать неоткуда", this);
                return new RosterRule { players = alivePlayers, cans = 5, eliminatedPerRound = 1 };
            }

            RosterRule best = rosterRules[0];
            int bestDistance = Mathf.Abs(best.players - alivePlayers);
            for (int i = 1; i < rosterRules.Length; i++)
            {
                int distance = Mathf.Abs(rosterRules[i].players - alivePlayers);
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
