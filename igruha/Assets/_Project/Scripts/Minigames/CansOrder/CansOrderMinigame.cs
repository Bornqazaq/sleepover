using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Minigames.Circus;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Правила «Порядка банок»: раунды из кругов до последнего выжившего.
    ///
    /// Ритм круга держит <see cref="MinigameStageState"/> из Core — тот самый,
    /// что написан под «Секундомер» ровно для этого. Стадии здесь байты,
    /// смысл задаёт игра.
    ///
    /// Порядок:
    /// <code>
    /// Брифинг → (Выставление → Показ → Пауза)* → Выставление → Показ → Створки → Брифинг следующего
    /// </code>
    /// Пауза на последнем круге раунда заменяется створками: раунд кончился,
    /// и лишняя секунда между падением и новым раундом разносила бы причину
    /// и следствие.
    ///
    /// <b>Network-ready.</b> Состояние раунда и участников лежит в структурах
    /// <see cref="CansOrderRoundState"/> и <see cref="CansOrderEntry"/>, а не
    /// в разрозненных полях: в фазе 3 они уедут в <c>NetworkVariable</c>
    /// и <c>NetworkList</c>. Всё, что меняет состояние круга, проходит через
    /// стадии, и каждая точка перехода уйдёт за <c>IsServer</c> без переписывания.
    /// </summary>
    public sealed class CansOrderMinigame : MinigameControllerBase
    {
        /// <summary>Стадии круга. Значения уезжают в сеть байтом, порядок менять нельзя.</summary>
        private const byte StageBriefing = 1;
        private const byte StagePlacement = 2;
        private const byte StageReveal = 3;
        private const byte StageHatch = 4;
        private const byte StagePause = 5;

        /// <summary>Номер «круга» брифинга: он идёт до первого настоящего круга раунда.</summary>
        private const int BriefingCircle = 0;

        /// <summary>Допуск на долю высоты, ниже которого клетка считается стоящей на нижней ступени.</summary>
        private const float LowestCageEpsilon = 0.001f;

        [SerializeField] private CansOrderConfig config;
        [SerializeField] private CircusArenaConfig arenaConfig;
        [SerializeField] private CircusBearConfig bearConfig;
        [SerializeField] private MinigameStageState stageState;
        [Tooltip("Табло над ямой — единственный источник информации в игре")]
        [SerializeField] private CanOrderBoard scoreboard;
        [Tooltip("Клетки арены — все восемь. Лишние гасятся по числу игроков")]
        [SerializeField] private CageStation[] cages = System.Array.Empty<CageStation>();
        [Tooltip("Медведь в яме")]
        [SerializeField] private PitBear bear;
        [Tooltip("Камера наблюдателя — включается выбывшему")]
        [SerializeField] private SpectatorCamera spectator;
        [Tooltip("Конфетти и вспышка над клеткой собравшего. Ставит CanOrderPropBuilder")]
        [SerializeField] private GameObject solvedFanfarePrefab;
        [Tooltip("На сколько метров над дном клетки бьёт фанфара")]
        [SerializeField] private float fanfareHeight = 2.4f;
        [Tooltip("Через сколько секунд убрать отыгравшую фанфару")]
        [SerializeField] private float fanfareLifetime = 3.5f;

        /// <summary>Участник матча: сессия, клетка, полка, кнопка и его состояние за круг.</summary>
        private sealed class Contestant
        {
            public SessionPlayer Session;
            public CageStation Cage;
            public CanShelf Shelf;
            public CanConfirmButton Button;
            public PlayerElimination Elimination;
            public bool LocallyControlled;
            public CansOrderEntry Entry;

            /// <summary>
            /// Снимок расстановки на момент подтверждения.
            ///
            /// Снимать его обязательно именно тогда, а не в конце круга:
            /// кнопка после нажатия гаснет, а полка остаётся живой до конца
            /// окна, и игрок может переставить банки уже после подтверждения.
            /// Счёт по поздней расстановке был бы счётом не того, что он отправил.
            /// В фазе 3 этот же снимок приедет аргументом <c>ServerRpc</c>.
            /// </summary>
            public readonly List<int> Submitted = new List<int>(8);

            /// <summary>Упал в яму и ещё не убит медведем.</summary>
            public bool InPit;
        }

        private readonly List<Contestant> contestants = new List<Contestant>(8);
        private readonly EliminationRanking ranking = new EliminationRanking();

        /// <summary>Группа вылета текущего раунда. Переиспользуется между раундами.</summary>
        private readonly List<int> eliminatedThisRound = new List<int>(8);

        /// <summary>Буфер под ранжирование кандидатов на вылет.</summary>
        private readonly List<CansOrderEntry> candidates = new List<CansOrderEntry>(8);

        /// <summary>Живые для камеры наблюдателя.</summary>
        private readonly List<SessionPlayer> aliveBuffer = new List<SessionPlayer>(8);

        private CansOrderRoundState round;
        private bool matchOver;

        /// <summary>
        /// Скрытая расстановка раунда — одна на всех и её никогда не показывают.
        ///
        /// Лежит только здесь и в фазе 3 не реплицируется ни при каких
        /// условиях: это и есть ответ, и клиент, читающий сетевое
        /// состояние напрямую, не должен найти его там физически (спека 10.1).
        /// </summary>
        private readonly List<int> solution = new List<int>(8);

        /// <summary>Буфер под перетасовку. Переиспользуется, чтобы не аллоцировать каждый раунд.</summary>
        private readonly List<int> shuffleBuffer = new List<int>(8);

        /// <summary>Позиции, уже встретившиеся в присланной расстановке — проверка на перестановку.</summary>
        private readonly HashSet<int> validationSeen = new HashSet<int>();

        /// <summary>
        /// Рандом только серверный. В соло это та же машина, в фазе 3
        /// генерация уйдёт за <c>IsServer</c> без переписывания правил.
        /// </summary>
        private System.Random random;

        /// <summary>
        /// Результаты круга уже объявлены. Держится от начала стадии
        /// показа до начала следующего круга.
        ///
        /// Пока флага нет, совпадения не выходят из контроллера вообще,
        /// а не прячутся в интерфейсе. В фазе 3 это единственный способ
        /// удержать честность против клиента, который читает сетевое
        /// состояние напрямую (спека 10.1). Тот же приём, что
        /// у <c>StopwatchMinigame.TryGetCageState</c>.
        /// </summary>
        private bool resultsRevealed;

        /// <summary>Состояние раунда. Наружу — табло и отладочным болванкам соло-прогона.</summary>
        public CansOrderRoundState Round => round;

        /// <summary>Сколько участников в матче.</summary>
        public int ContestantCount => contestants.Count;

        /// <summary>Текущая стадия круга. Наружу — для замеров приёмки.</summary>
        public byte Stage => stageState != null ? stageState.Stage : MinigameStageState.NoStage;

        /// <summary>Сколько игроков ещё в матче.</summary>
        public int AliveCount
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < contestants.Count; i++)
                {
                    if (contestants[i].Entry.Alive)
                    {
                        alive++;
                    }
                }

                return alive;
            }
        }

        /// <summary>Сколько живых ещё не собрало расстановку в этом раунде.</summary>
        public int NotSolvedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < contestants.Count; i++)
                {
                    if (contestants[i].Entry.Alive && !contestants[i].Entry.Solved)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (stageState == null)
            {
                stageState = GetComponent<MinigameStageState>();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (stageState != null)
            {
                stageState.StageStarted += HandleStageStarted;
                stageState.StageElapsed += HandleStageElapsed;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
                stageState.StageElapsed -= HandleStageElapsed;
            }
        }

        protected override void OnPlayersReady()
        {
            if (config == null || arenaConfig == null)
            {
                Debug.LogError($"{name}: не назначены CansOrderConfig / CircusArenaConfig — играть нечем", this);
                return;
            }

            contestants.Clear();
            ranking.Clear();
            matchOver = false;
            round = default;
            solution.Clear();

            // Сид берётся у авторитета и в фазе 3 останется там же: весь
            // рандом игры — скрытая расстановка и стартовые полки — считается
            // только сервером (igruha/CLAUDE.md, раздел 3.1).
            random = new System.Random(System.Environment.TickCount);

            AssignCages();

            eliminatedThisRound.Clear();

            if (bear != null)
            {
                if (bearConfig != null)
                {
                    bearConfig.Apply(bear, arenaConfig.PitRadius);
                }
                else
                {
                    Debug.LogError($"{name}: не назначен CircusBearConfig — медведь останется на своих заготовочных числах", this);
                }

                bear.Caught -= HandleBearCaught;
                bear.Caught += HandleBearCaught;
            }
        }

        /// <summary>
        /// Раздать клетки и рассадить игроков. Клеток на арене всегда восемь,
        /// но включается ровно по числу игроков и с тем же разносом по кольцу,
        /// каким спавнер раздаёт точки: пустая клетка обязана означать ровно
        /// одно — оттуда уже кто-то выпал.
        /// </summary>
        private void AssignCages()
        {
            for (int i = 0; i < cages.Length; i++)
            {
                if (cages[i] != null)
                {
                    cages[i].gameObject.SetActive(false);
                }
            }

            int count = Players.Count;
            for (int i = 0; i < count; i++)
            {
                int slot = SpreadSlot(i, count, cages.Length);
                CageStation cage = cages[slot];
                if (cage == null)
                {
                    Debug.LogError($"{name}: клетка {slot} не назначена", this);
                    continue;
                }

                cage.gameObject.SetActive(true);
                // Границы хода — весь диапазон конфига: высота здесь считается
                // долей, а не ступенями, и обязана уметь встать в любую точку.
                cage.Configure(arenaConfig, arenaConfig.MaxLevelSteps);

                var contestant = new Contestant
                {
                    Session = Players[i],
                    Cage = cage,
                    Shelf = cage.PropSlot != null ? cage.PropSlot.GetComponentInChildren<CanShelf>(true) : null,
                    Button = cage.PropSlot != null ? cage.PropSlot.GetComponentInChildren<CanConfirmButton>(true) : null,
                    Entry = new CansOrderEntry { PlayerId = Players[i].Id, Alive = true, HeightFraction = 1f }
                };

                PlayerController avatar = Players[i].Avatar;
                cage.SetOccupant(avatar);
                if (avatar != null && cage.PropSlot != null)
                {
                    avatar.RequestTeleport(cage.PropSlot.position - cage.transform.forward * (arenaConfig.CageInnerSize * 0.25f),
                        Quaternion.LookRotation(cage.transform.forward));
                }

                contestant.LocallyControlled = avatar != null
                                               && avatar.TryGetComponent(out PlayerInputReader reader)
                                               && reader.LocallyControlled;

                if (contestant.Shelf != null)
                {
                    contestant.Shelf.SetOwner(avatar);
                    contestant.Shelf.Active = false;
                }

                if (contestant.Button != null)
                {
                    contestant.Button.SetOwner(avatar);
                    contestant.Button.SetShelf(contestant.Shelf);
                    contestant.Button.ResetForRound();
                    contestant.Button.Confirmed += HandleConfirmed;
                }

                if (avatar != null)
                {
                    // Компонент вешаем здесь, а не в префаб персонажа: префаб
                    // общий на все мини-игры, и лишний компонент уехал бы
                    // в те, где смерти насмерть нет вовсе.
                    contestant.Elimination = avatar.GetComponent<PlayerElimination>();
                    if (contestant.Elimination == null)
                    {
                        contestant.Elimination = avatar.gameObject.AddComponent<PlayerElimination>();
                    }

                    contestant.Elimination.BodyHidden += HandleBodyHidden;
                }

                contestants.Add(contestant);
            }
        }

        /// <summary>
        /// Тот же разнос, что у <see cref="SpawnPointSet.GetSpreadPoint"/>:
        /// клетка обязана достаться игроку там же, где ему досталась точка спавна.
        /// </summary>
        private static int SpreadSlot(int index, int count, int total)
        {
            if (count >= total || count <= 0)
            {
                return index % Mathf.Max(1, total);
            }

            return index * total / count;
        }

        protected override void OnRoundStarted()
        {
            if (!HasAuthority || contestants.Count == 0)
            {
                return;
            }

            BeginRound();
        }

        protected override void OnRoundEnded()
        {
            // Всё, что мини-игра навесила на игрока, она обязана снять сама:
            // персонаж переезжает между сценами живым, и незакрытая роль
            // уезжает в хаб вместе с ним (спека 10.5).
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];

                if (c.Button != null)
                {
                    c.Button.Confirmed -= HandleConfirmed;
                    c.Button.CloseWindow();
                }

                // Банка кинематическая и прицеплена к руке: если матч кончился,
                // пока игрок её держал, она уедет в хаб вместе с ним.
                c.Shelf?.Release();
                c.Cage?.ReleaseOccupant();

                if (c.Elimination != null)
                {
                    c.Elimination.BodyHidden -= HandleBodyHidden;
                    // Невидимое тело с выключенным коллайдером уехало бы в хаб
                    // вместе с персонажем — он переезжает между сценами живым.
                    c.Elimination.Restore();
                }
            }

            if (bear != null)
            {
                bear.Caught -= HandleBearCaught;
            }

            spectator?.Deactivate();
            stageState?.StopSequence();
            scoreboard?.Clear();
        }

        // ========== РАУНД И КРУГИ ==========

        /// <summary>
        /// Начать раунд: пересчитать состав, поднять клетки выживших наверх
        /// и объявить задание. Накопления между раундами нет — высота снова
        /// читается как «положение в этом раунде» (спека 5.5).
        /// </summary>
        /// <summary>
        /// Начать раунд: пересчитать состав, сгенерировать скрытую
        /// расстановку, раздать полки и поднять клетки выживших наверх.
        /// Накопления между раундами нет — высота снова читается
        /// как «положение в этом раунде» (спека 5.5).
        /// </summary>
        private void BeginRound()
        {
            int alive = AliveCount;

            round.Round++;
            round.Circle = BriefingCircle;
            round.AliveAtStart = alive;
            round.CanCount = config.GetCanCount(alive);
            round.Quota = config.GetEliminationQuota(alive);
            round.SolvedCount = 0;

            GenerateSolution(round.CanCount);

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Entry.Alive)
                {
                    continue;
                }

                c.Entry.Solved = false;
                c.Entry.SolvedThisCircle = false;
                c.Entry.Confirmed = false;
                c.Entry.Matches = 0;
                c.Entry.Attempts = 0;
                c.Entry.BestMatches = -1;
                c.Entry.BestCircle = 0;
                c.Entry.HeightFraction = 1f;
                c.Submitted.Clear();

                c.Button?.ResetForRound();

                if (c.Shelf == null)
                {
                    continue;
                }

                c.Shelf.Build(config, round.CanCount);
                // Стартовая расстановка — своя у каждого. Одинаковая дала бы
                // за первый круг один отклик на всех вместо восьми (спека 8.2).
                Shuffle(round.CanCount);
                c.Shelf.SetArrangement(shuffleBuffer);
            }

            if (config.DebugRevealSolution)
            {
                Debug.Log($"🔑 [ОТЛАДКА] Скрытая расстановка раунда {round.Round}: [{string.Join(",", solution)}]", this);
            }

            scoreboard?.ShowTask(round.Round, round.CanCount);
            RaiseCagesForBriefing();
            stageState.BeginSubround(BriefingCircle, StageBriefing, config.BriefingSeconds);
        }

        /// <summary>Клетки выживших едут наверх за время подъёма внутри брифинга.</summary>
        private void RaiseCagesForBriefing()
        {
            double startedAt = NetworkClock.Now;
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Entry.Alive && c.Cage != null)
                {
                    c.Cage.MoveToFraction(1f, config.BriefingRiseSeconds, startedAt);
                }
            }
        }

        /// <summary>
        /// Опустить клетки не собравших на высоту по доле справившихся.
        ///
        /// Здесь высота означает не накопленные ошибки, как у «Секундомера»,
        /// а насколько ты отстал от тех, кто уже справился (спека 5.5):
        /// <code>
        /// доля = собравших в раунде / (живых в начале − выбывающих за раунд)
        /// </code>
        /// Знаменатель нормирует картинку под любой состав: на восьмерых
        /// шаг спуска — шестая часть хода, на двоих — весь ход разом.
        /// К концу раунда доля равна единице, и не собравший стоит
        /// на нижней ступени ровно к моменту, когда откроется дно.
        ///
        /// Клетка собравшего не трогается вовсе — она замирает там, где он
        /// собрал, и это его положение в кадре относительно остальных.
        /// </summary>
        private void DescendCages()
        {
            // Момент начала — общий для всех машин: от него каждая считает
            // высоту по одной формуле, поэтому расхождение не копится.
            double startedAt = NetworkClock.Now;
            float height = 1f - LaggingFraction();

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Entry.Alive || c.Entry.Solved || c.Cage == null)
                {
                    continue;
                }

                c.Entry.HeightFraction = height;
                c.Cage.MoveToFraction(height, config.CageDescendSeconds, startedAt);
            }
        }

        /// <summary>
        /// Доля справившихся, 0…1. Знаменатель — сколько всего должно
        /// собрать, чтобы раунд кончился (таблица 6.1, колонка
        /// «раунд кончается, когда собрали»).
        /// </summary>
        private float LaggingFraction()
        {
            int needed = round.AliveAtStart - round.Quota;
            if (needed <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01(round.SolvedCount / (float)needed);
        }


        private void BeginCircle()
        {
            round.Circle++;
            resultsRevealed = false;

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                c.Entry.Confirmed = false;
                c.Entry.Matches = 0;
                c.Entry.SolvedThisCircle = false;
                c.Submitted.Clear();

                // Полка хранит прошлую расстановку между кругами: в новом круге
                // игрок двигает только то, что решил изменить. Это единственная
                // разрешённая «запись» в игре — она физическая и на виду (спека 4).
                // Флаг плейтеста выключает это, если окажется слишком лёгким.
                if (config.ShelfKeepsArrangement || c.Shelf == null || !c.Entry.Alive || c.Entry.Solved)
                {
                    continue;
                }

                Shuffle(round.CanCount);
                c.Shelf.SetArrangement(shuffleBuffer);
            }

            stageState.BeginSubround(round.Circle, StagePlacement, config.PlacementWindowSeconds);
        }

        /// <summary>
        /// Открыть или закрыть окно выставления. Собравшему окно не открывается:
        /// его полка гаснет, клетка замирает, и он смотрит — это и есть награда.
        /// </summary>
        private void SetPlacementWindow(bool open)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                bool active = open && c.Entry.Alive && !c.Entry.Solved;

                if (c.Shelf != null)
                {
                    c.Shelf.Active = active;
                }

                if (c.Button == null)
                {
                    continue;
                }

                if (active)
                {
                    c.Button.OpenWindow();
                }
                else
                {
                    c.Button.CloseWindow();
                }
            }
        }

        /// <summary>
        /// Стадия началась. Зовётся и у авторитета, и на клиенте, куда стадию
        /// принесёт сетевая половина, — поэтому окна полок открываются здесь,
        /// а не в точке перехода: без этого у клиента полка не ожила бы.
        /// </summary>
        /// <summary>
        /// Стадия началась. Зовётся и у авторитета, и на клиенте, куда стадию
        /// принесёт сетевая половина, — поэтому окна полок открываются здесь,
        /// а не в точке перехода: без этого у клиента полка не ожила бы.
        /// </summary>
        private void HandleStageStarted(byte stage)
        {
            // Флаг общий для всех машин: он решает, выходят ли совпадения
            // из контроллера вообще.
            if (stage == StageReveal)
            {
                resultsRevealed = true;
                // Строго после флага: до него контроллер не отдаёт
                // совпадения даже табло, и оно нарисовало бы нули.
                scoreboard?.ShowResults(this);
                // Клетки едут внутри стадии показа, а не отдельной стадией:
                // читаешь табло — и одновременно чувствуешь, как проваливаешься.
                // Отдельная стадия спуска добавила бы к кругу две секунды
                // и разнесла бы причину и следствие (спека 13, пункт 14).
                DescendCages();
            }

            switch (stage)
            {
                case StagePlacement:
                    SetPlacementWindow(true);
                    break;
                case StageReveal:
                case StageHatch:
                case StagePause:
                case StageBriefing:
                    SetPlacementWindow(false);
                    break;
            }
        }

        /// <summary>
        /// Стадия отыграла своё. Единственная точка, которая двигает круг
        /// вперёд, — в фазе 3 она целиком уйдёт за <c>IsServer</c>.
        /// </summary>
        /// <summary>
        /// Стадия отыграла своё. Единственная точка, которая двигает круг
        /// вперёд, — в фазе 3 она целиком уйдёт за <c>IsServer</c>.
        /// </summary>
        private void HandleStageElapsed(byte stage)
        {
            if (matchOver)
            {
                return;
            }

            switch (stage)
            {
                case StageBriefing:
                    BeginCircle();
                    break;

                case StagePlacement:
                    ResolveCircle();
                    stageState.EnterStage(StageReveal, config.ResultsSeconds);
                    break;

                case StageReveal:
                    if (ShouldEndRound())
                    {
                        EnterHatch(false);
                        return;
                    }

                    if (round.Circle >= config.RoundCircleCap)
                    {
                        // Потолок кругов — страховка, а не правило: в нормальной
                        // игре до неё не доходит даже вдвоём (спека 6.5).
                        Debug.LogWarning($"{name}: раунд {round.Round} упёрся в потолок {config.RoundCircleCap} кругов — " +
                                         "выбывают худшие по лучшему достигнутому счёту", this);
                        EnterHatch(true);
                        return;
                    }

                    stageState.EnterStage(StagePause, config.PauseSeconds);
                    break;

                case StagePause:
                    BeginCircle();
                    break;

                case StageHatch:
                    if (AliveCount < 2)
                    {
                        matchOver = true;
                        EndMinigame();
                        return;
                    }

                    BeginRound();
                    break;
            }
        }

        /// <summary>
        /// Разобрать круг: посчитать попытки и совпадения.
        ///
        /// Попытка тратится и у того, кто ничего не подтвердил, — отсидеться
        /// нельзя. Но ноль совпадений ему при этом <b>не приписывается</b>:
        /// ноль это полноценная информация, он вычёркивает все позиции сразу,
        /// и фальшивый ноль отравил бы общий котёл (спека 5.4).
        /// </summary>
        /// <summary>
        /// Разобрать круг: потратить попытки и посчитать совпадения
        /// по снятым в момент подтверждения расстановкам.
        ///
        /// Попытка тратится и у того, кто ничего не подтвердил, — отсидеться
        /// нельзя. Но ноль совпадений ему при этом <b>не приписывается</b>:
        /// ноль — это полноценная информация, он вычёркивает все позиции
        /// сразу, и фальшивый ноль отравил бы общий котёл, ради которого
        /// и выбран этот вариант игры (спека 5.4 и 13, пункт 3).
        /// Состояние такого игрока — отдельное: <c>Confirmed == false</c>.
        /// </summary>
        private void ResolveCircle()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Entry.Alive || c.Entry.Solved)
                {
                    continue;
                }

                // Круг, прожитый тем, кто ещё не собрал, — это и есть попытка.
                c.Entry.Attempts++;

                if (!c.Entry.Confirmed)
                {
                    // Ничего не считаем и ничего не трогаем: ни совпадений,
                    // ни лучшего счёта. На табло у него будет «НЕ ПОДТВЕРДИЛ».
                    continue;
                }

                int matches = CountMatches(c.Submitted);
                c.Entry.Matches = matches;

                if (matches > c.Entry.BestMatches)
                {
                    c.Entry.BestMatches = matches;
                    c.Entry.BestCircle = round.Circle;
                }

                if (matches != round.CanCount)
                {
                    continue;
                }

                c.Entry.Solved = true;
                c.Entry.SolvedThisCircle = true;
                round.SolvedCount++;

                // Полка гаснет, клетка замирает на той высоте, где он собрал,
                // и игрок сидит и смотрит. Это и есть его награда (спека 5.2).
                c.Button?.MarkSolved();
                if (c.Shelf != null)
                {
                    c.Shelf.Active = false;
                }

                SpawnFanfare(c);
            }
        }

        /// <summary>
        /// Конфетти над клеткой собравшего.
        ///
        /// Это не украшение, а читаемость момента: в ту же секунду все
        /// остальные клетки уезжают вниз разом, а эта остаётся висеть.
        /// Без акцента рывок читается как тихое движение геометрии,
        /// а игрок в этот момент смотрит на свою полку.
        /// </summary>
        private void SpawnFanfare(Contestant contestant)
        {
            if (solvedFanfarePrefab == null || contestant.Cage == null)
            {
                return;
            }

            Vector3 at = contestant.Cage.transform.position + Vector3.up * fanfareHeight;
            GameObject instance = Instantiate(solvedFanfarePrefab, at, Quaternion.identity);
            Destroy(instance, fanfareLifetime);
        }


        /// <summary>
        /// Раунд заканчивается, как только не собравших осталось не больше
        /// квоты вылета: их места уже определены, и доигрывать нечего
        /// (спека 5.6).
        /// </summary>
        private bool ShouldEndRound() => NotSolvedCount <= round.Quota;

        /// <summary>
        /// Раунд кончился: разобрать, кто выбывает, и распахнуть им дно.
        ///
        /// Пустая клетка остаётся висеть на своей высоте до конца матча —
        /// это единственный смысл пустой клетки на арене и кладбище,
        /// которое никто не рисовал (спека 5.7 и 7).
        ///
        /// Забег в яме идёт <b>параллельно</b> следующему раунду: стадия
        /// створок длится 2 с и ничего не ждёт, а погоня доигрывается внизу
        /// фоном. Блокирующая сцена добавляла бы по 5–9 с к каждому раунду.
        /// </summary>
        private void EnterHatch(bool byCircleCap)
        {
            SelectEliminated(byCircleCap, eliminatedThisRound);

            for (int i = 0; i < eliminatedThisRound.Count; i++)
            {
                Contestant c = Find(eliminatedThisRound[i]);
                if (c == null)
                {
                    continue;
                }

                c.Entry.Alive = false;
                c.Entry.Solved = false;
                c.InPit = true;

                // Банка из руки возвращается до падения: она кинематическая
                // и прицеплена к персонажу — иначе улетит в яму вместе с ним,
                // а потом и в хаб (спека 10.5).
                c.Shelf?.Release();
                c.Button?.CloseWindow();
                c.Cage?.OpenDoors(config.HatchOpenSeconds);
            }

            if (eliminatedThisRound.Count > 0)
            {
                ranking.AddEliminationGroup(eliminatedThisRound);
                Debug.Log($"🦊 Раунд {round.Round} закрыт на круге {round.Circle}: выбывают " +
                          $"{eliminatedThisRound.Count} из {round.AliveAtStart} при квоте {round.Quota}", this);
            }

            stageState.EnterStage(StageHatch, eliminatedThisRound.Count > 0 ? config.HatchOpenSeconds : 0f);
        }

        /// <summary>
        /// Кто выбывает (спека 5.6). Три случая, и путать их нельзя:
        ///
        /// 1. <b>Не собрали от 1 до квоты</b> — выбывают все не собравшие,
        ///    даже если их меньше квоты.
        /// 2. <b>Не собрали ноль</b> — все собрали в одном круге. Этого случая
        ///    LDD не разбирает, а он реален: круги синхронные, и порог может
        ///    перешагнуть сразу несколько человек. Без этого правила раунд
        ///    закончился бы, никого не выбив, и матч не сошёлся бы.
        ///    Выбывают худшие по потраченным попыткам, при равенстве —
        ///    подтвердивший позже.
        /// 3. <b>Потолок кругов</b> — худшие по лучшему достигнутому счёту,
        ///    при равенстве — достигший позже.
        ///
        /// Во втором и третьем случае при <b>полном равенстве</b> на линии
        /// отсечения выбывают все, кто на ней, даже если их больше квоты:
        /// матч от этого только короче (LDD 14).
        /// </summary>
        private void SelectEliminated(bool byCircleCap, List<int> into)
        {
            into.Clear();

            if (!byCircleCap)
            {
                for (int i = 0; i < contestants.Count; i++)
                {
                    Contestant c = contestants[i];
                    if (c.Entry.Alive && !c.Entry.Solved)
                    {
                        into.Add(c.Entry.PlayerId);
                    }
                }

                if (into.Count > 0)
                {
                    return;
                }
            }

            // Либо собрали все, либо исчерпан потолок кругов: ранжируем всех живых.
            candidates.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Entry.Alive)
                {
                    candidates.Add(contestants[i].Entry);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            if (byCircleCap)
            {
                CanOrderRanking.SortWorstFirstByBestMatches(candidates);
            }
            else
            {
                CanOrderRanking.SortWorstFirstBySpentAttempts(candidates);
            }

            int take = Mathf.Clamp(round.Quota, 1, candidates.Count);
            for (int i = 0; i < take; i++)
            {
                into.Add(candidates[i].PlayerId);
            }

            // Полное равенство на линии отсечения: забираем всех равных.
            // Решать чью-то судьбу по идентификатору игрока нельзя.
            CansOrderEntry last = candidates[take - 1];
            for (int i = take; i < candidates.Count; i++)
            {
                bool tied = byCircleCap
                    ? CanOrderRanking.FullyTiedByBestMatches(last, candidates[i])
                    : CanOrderRanking.FullyTiedByAttempts(last, candidates[i]);

                if (!tied)
                {
                    break;
                }

                into.Add(candidates[i].PlayerId);
            }
        }


        /// <summary>
        /// Игрок подтвердил расстановку. Единственная точка входа намерения:
        /// в фазе 3 оно приедет сюда же, но из <c>ServerRpc</c>, и правила
        /// не изменятся.
        /// </summary>
        /// <summary>
        /// Игрок подтвердил расстановку. Единственная точка входа намерения:
        /// в фазе 3 оно приедет сюда же, но из <c>ServerRpc</c>, и правила
        /// не изменятся.
        /// </summary>
        private void HandleConfirmed(CanConfirmButton button, PlayerController player)
        {
            if (!HasAuthority || stageState == null || stageState.Stage != StagePlacement)
            {
                return;
            }

            Contestant c = FindByAvatar(player);
            if (c == null || !c.Entry.Alive || c.Entry.Solved || c.Entry.Confirmed)
            {
                return;
            }

            if (c.Shelf == null || !c.Shelf.TryGetArrangement(c.Submitted))
            {
                Debug.LogWarning($"{name}: игрок {c.Entry.PlayerId} подтвердил неполную расстановку — отказ", this);
                return;
            }

            if (!IsPermutation(c.Submitted, round.CanCount))
            {
                // Практически недостижимо: механика обмена не даёт собрать
                // невалидную расстановку. Проверка стоит как предохранитель
                // от подделанного пакета в фазе 3 (спека 10.3, правило 3).
                Debug.LogWarning($"{name}: от игрока {c.Entry.PlayerId} пришла не перестановка " +
                                 $"[{string.Join(",", c.Submitted)}] при {round.CanCount} банках — отказ", this);
                c.Submitted.Clear();
                return;
            }

            c.Entry.Confirmed = true;
            c.Entry.ConfirmTime = NetworkClock.Now;
            button.MarkAccepted();

            if (config.ShowOwnMatchesImmediately)
            {
                // ОТЛАДОЧНЫЙ режим и ничто иное: он ломает честность игры,
                // показывая результат раньше общего показа.
                Debug.Log($"🔑 [ОТЛАДКА] игрок {c.Entry.PlayerId} подтвердил [{string.Join(",", c.Submitted)}] — " +
                          $"совпадений {CountMatches(c.Submitted)} из {round.CanCount}", this);
            }
        }

        /// <summary>
        /// Сколько банок стоит на своих местах. Единственный отклик игры —
        /// число, без указания, какие именно.
        /// </summary>
        private int CountMatches(List<int> arrangement)
        {
            int matches = 0;
            int count = Mathf.Min(arrangement.Count, solution.Count);
            for (int i = 0; i < count; i++)
            {
                if (arrangement[i] == solution[i])
                {
                    matches++;
                }
            }

            return matches;
        }

        /// <summary>Скрытая расстановка раунда: случайная перестановка, одна на всех.</summary>
        private void GenerateSolution(int canCount)
        {
            Shuffle(canCount);
            solution.Clear();
            for (int i = 0; i < shuffleBuffer.Count; i++)
            {
                solution.Add(shuffleBuffer[i]);
            }
        }

        /// <summary>Перетасовка Фишера—Йетса в <see cref="shuffleBuffer"/>. Рандом только серверный.</summary>
        private void Shuffle(int canCount)
        {
            shuffleBuffer.Clear();
            for (int i = 0; i < canCount; i++)
            {
                shuffleBuffer.Add(i);
            }

            for (int i = shuffleBuffer.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int swap = shuffleBuffer[i];
                shuffleBuffer[i] = shuffleBuffer[j];
                shuffleBuffer[j] = swap;
            }
        }

        /// <summary>Присланное — перестановка ровно N различных банок палитры раунда.</summary>
        private bool IsPermutation(List<int> arrangement, int canCount)
        {
            if (arrangement.Count != canCount)
            {
                return false;
            }

            validationSeen.Clear();
            for (int i = 0; i < arrangement.Count; i++)
            {
                int id = arrangement[i];
                if (id < 0 || id >= canCount || !validationSeen.Add(id))
                {
                    return false;
                }
            }

            return true;
        }


        private Contestant FindByAvatar(PlayerController avatar)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Session.Avatar == avatar)
                {
                    return contestants[i];
                }
            }

            return null;
        }

        private Contestant Find(int playerId)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Entry.PlayerId == playerId)
                {
                    return contestants[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Шаг медведя. Забег в яме идёт параллельно: следующий раунд
        /// стартует сразу, а погоня доигрывается внизу фоном.
        ///
        /// В фазе 3 этот <c>Update</c> уйдёт целиком за <c>IsServer</c> — медведя
        /// двигает только сервер, остальные получают позицию через
        /// <c>NetworkTransform</c>.
        /// </summary>
        private void Update()
        {
            if (bear == null || !HasAuthority || Phase != MinigamePhase.Round)
            {
                return;
            }

            bear.Tick(Time.deltaTime, FindNearestInPit(), SomeoneOnLowestCage());
        }

        /// <summary>Ближайшая к медведю жертва среди упавших в яму.</summary>
        private PlayerController FindNearestInPit()
        {
            PlayerController nearest = null;
            float nearestSqr = float.MaxValue;
            Vector3 bearPosition = bear.transform.position;

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.InPit || c.Session.Avatar == null)
                {
                    continue;
                }

                if (c.Elimination != null && c.Elimination.IsEliminated)
                {
                    continue;
                }

                float sqr = (c.Session.Avatar.transform.position - bearPosition).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = c.Session.Avatar;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Есть ли кто-то на нижней ступени — медведю есть кого пугать.
        ///
        /// Считается по доле высоты, а <b>не</b> по <c>CageStation.Level</c>:
        /// клетка здесь ездит долями, а при дробном ходе <c>Level</c>
        /// не обновляется и врёт. У «Секундомера» этот признак взят именно
        /// из <c>Level</c>, и повторить там было бы ошибкой.
        /// </summary>
        private bool SomeoneOnLowestCage()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Entry.Alive && c.Entry.HeightFraction <= LowestCageEpsilon)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Медведь достал выпавшего. Место игрока уже посчитано в момент
        /// падения — гибель ничего не решает, она только доигрывает сцену.
        /// </summary>
        private void HandleBearCaught(PlayerController victim, Vector3 impulse)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Session.Avatar != victim || !c.InPit)
                {
                    continue;
                }

                c.InPit = false;
                c.Elimination?.Eliminate(victim.transform.position, impulse);
                return;
            }
        }

        /// <summary>Тело исчезло — выбывший переходит в наблюдатели.</summary>
        private void HandleBodyHidden(PlayerElimination elimination)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Elimination != elimination)
                {
                    continue;
                }

                // Камера наблюдателя одна на сцену и показывает то, что видит
                // человек за этой машиной. Болванке она не нужна.
                if (c.LocallyControlled && spectator != null)
                {
                    spectator.Activate(CollectAlivePlayers());
                }

                return;
            }
        }

        private IReadOnlyList<SessionPlayer> CollectAlivePlayers()
        {
            aliveBuffer.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Entry.Alive)
                {
                    aliveBuffer.Add(contestants[i].Session);
                }
            }

            return aliveBuffer;
        }


        /// <summary>
        /// Состояние участника для табло и отчётов.
        ///
        /// <b>Совпадения отдаются только в стадии показа.</b> До неё они
        /// не прячутся в интерфейсе, а не покидают контроллер вовсе:
        /// своё число совпадений игрок узнаёт вместе со всеми и ни секундой
        /// раньше (спека 4).
        /// </summary>
        public bool TryGetEntry(int index, out CansOrderEntry entry, out string displayName)
        {
            if (index < 0 || index >= contestants.Count)
            {
                entry = default;
                displayName = string.Empty;
                return false;
            }

            Contestant c = contestants[index];
            entry = c.Entry;
            displayName = c.Session.DisplayName;

            if (!resultsRevealed)
            {
                entry.Matches = 0;
                entry.Confirmed = false;
                entry.SolvedThisCircle = false;
            }

            return true;
        }

        /// <summary>Расстановка, которую участник подтвердил в этом круге. До стадии показа не отдаётся.</summary>
        public bool TryGetSubmitted(int index, List<int> into)
        {
            if (into == null || index < 0 || index >= contestants.Count || !resultsRevealed)
            {
                return false;
            }

            Contestant c = contestants[index];
            if (!c.Entry.Confirmed)
            {
                return false;
            }

            into.Clear();
            for (int i = 0; i < c.Submitted.Count; i++)
            {
                into.Add(c.Submitted[i]);
            }

            return true;
        }


        /// <summary>
        /// Места по порядку вылета. Считает <see cref="EliminationRanking"/>
        /// из Core: место = сколько игроков стоит выше, плюс один. Оттуда само
        /// собой следует и деление места внутри группы вылета, и сдвиг
        /// следующей группы на размер предыдущей.
        /// </summary>
        /// <summary>
        /// Места по порядку вылета. Считает <see cref="EliminationRanking"/>
        /// из Core: место = сколько игроков стоит выше, плюс один. Оттуда
        /// само собой следует и деление места внутри группы вылета,
        /// и сдвиг следующей группы на размер предыдущей, а не на единицу.
        ///
        /// Ранжирование по числу попыток из LDD 7.4 финальных мест не даёт
        /// и здесь не используется: оно решает только, кого выбить при
        /// равенстве (5.6). Разбор — спека 13, пункт 10.
        ///
        /// Несколько выживших даёт только жёсткий таймаут мини-игры
        /// (600 с в <c>MinigameDefinition</c>) — тогда их разводит компаратор
        /// по правилу 6.5, а полное равенство оставляет им общее место.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Entry.Alive)
                {
                    ranking.AddSurvivor(contestants[i].Entry.PlayerId);
                }
            }

            ranking.Build(results, CompareSurvivors);
        }

        /// <summary>
        /// Компаратор выживших для жёсткого таймаута (спека 6.5).
        /// При обычном финале выживший один, и <see cref="EliminationRanking"/>
        /// сюда не заглядывает вовсе.
        /// </summary>
        private int CompareSurvivors(int a, int b)
        {
            Contestant left = Find(a);
            Contestant right = Find(b);
            if (left == null || right == null)
            {
                return 0;
            }

            return CanOrderRanking.CompareSurvivors(left.Entry, right.Entry);
        }
    }
}
