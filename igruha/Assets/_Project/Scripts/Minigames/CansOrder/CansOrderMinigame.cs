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

        /// <summary>
        /// На сколько подтверждение может опоздать после конца окна, с.
        ///
        /// Это дорога пакета, а не поблажка: игрок нажал до дедлайна, и терять
        /// его попытку из-за пинга нельзя (спека 10.3, правило 1). Само время
        /// нажатия при этом берётся из метки, а не из момента прибытия, поэтому
        /// допуск не даёт никакого преимущества тому, у кого канал хуже.
        /// </summary>
        private const double ConfirmGraceSeconds = 0.3d;

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
        [Tooltip("Экранные подсказки: остаток стадии и что сейчас сделает E")]
        [SerializeField] private CansOrderLocalHud localHud;
        [Tooltip("Переключатель ригов. Нужен, чтобы в окне выставления встать на полку")]
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Трансформ fixed-рига (_Camera/ShelfCameraRig). Мини-игра ставит его сама: рига без Body и Aim Cinemachine не двигает")]
        [SerializeField] private Transform shelfCameraRig;
        [Tooltip("С какого расстояния камера смотрит на полку в окне выставления, м. Игрок стоит в 1.9 м от доски, и камера обязана быть заметно ближе — иначе она встаёт ему в затылок")]
        [SerializeField] private float shelfCameraDistance = 1.2f;
        [Tooltip("На сколько камера поднята над доской полки, м")]
        [SerializeField] private float shelfCameraHeight = 0.5f;
        [Tooltip("Конфетти и вспышка над клеткой собравшего. Ставит CanOrderPropBuilder")]
        [SerializeField] private GameObject solvedFanfarePrefab;
        [Tooltip("На сколько метров над дном клетки бьёт фанфара")]
        [SerializeField] private float fanfareHeight = 2.4f;
        [Tooltip("Через сколько секунд убрать отыгравшую фанфару")]
        [SerializeField] private float fanfareLifetime = 3.5f;

        [Header("Соло-прогон")]
        [Tooltip("С какой вероятностью болванка выставляет верную расстановку за круг. На живых игроков не влияет")]
        [Range(0f, 1f)]
        [SerializeField] private float botSolveChance = 0.28f;

        /// <summary>Кому мы сами заблокировали ноги на время окна: снимаем ровно свою блокировку.</summary>
        private readonly List<PlayerController> movementLockedByShelf = new List<PlayerController>(8);

        /// <summary>Рендереры своего персонажа, погашенные на время окна. Возвращаем ровно те, что гасили.</summary>
        private readonly List<Renderer> hiddenLocalRenderers = new List<Renderer>(16);

        /// <summary>Буфер под выборку рендереров: без него каждый круг плодил бы массив.</summary>
        private readonly List<Renderer> rendererBuffer = new List<Renderer>(16);

        /// <summary>Буфер своей карточки результата. Поле, а не локальная переменная: строка пересобирается каждый круг.</summary>
        private readonly System.Text.StringBuilder revealText = new System.Text.StringBuilder(160);

        /// <summary>Буфер подсказки про прошлый круг. Отдельный от карточки: строки живут в разных стадиях.</summary>
        private readonly System.Text.StringBuilder lastCircleText = new System.Text.StringBuilder(160);

        /// <summary>
        /// Что локальный игрок отправил в прошлом круге и сколько совпало.
        ///
        /// <b>Ровно один круг назад, и это дизайнерское решение, а не экономия.</b>
        /// Полный журнал попыток превратил бы игру в «Мастермайнд с блокнотом»:
        /// имея все свои расстановки со счётом, скрытую находишь логикой за пару
        /// кругов, и память как механика умирает вместе со списыванием с табло
        /// (решение 18.4 LDD — «заметок нет»). Один прошлый круг снимает только
        /// тупую фрустрацию «забыл, что ставил пятнадцать секунд назад»:
        /// круги с первого по позапрошлый всё равно держишь в голове сам.
        /// Решение геймдизайнера 22.08.
        /// </summary>
        private readonly List<int> lastCircleArrangement = new List<int>(8);
        private int lastCircleMatches;
        private int lastCircleRound = -1;
        private int lastCircleNumber = -1;
        private bool lastCircleConfirmed;

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
            public CansOrderDebugBot Bot;

            /// <summary>
            /// Участник встретился в последнем приехавшем списке. Только на
            /// клиенте: сервер убирает ушедшего из состава, и по отсутствию
            /// строки клиент понимает, что его пора убрать и у себя.
            /// </summary>
            public bool NetSeen;
        }

        private readonly List<Contestant> contestants = new List<Contestant>(8);
        private readonly EliminationRanking ranking = new EliminationRanking();

        /// <summary>Группа вылета текущего раунда. Переиспользуется между раундами.</summary>
        private readonly List<int> eliminatedThisRound = new List<int>(8);

        /// <summary>Буфер под ранжирование кандидатов на вылет.</summary>
        private readonly List<CansOrderEntry> candidates = new List<CansOrderEntry>(8);

        /// <summary>Живые для камеры наблюдателя.</summary>
        private readonly List<SessionPlayer> aliveBuffer = new List<SessionPlayer>(8);

        /// <summary>
        /// Снимок расстановки в момент нажатия. Читаем сюда, а не сразу
        /// в <c>Contestant.Submitted</c>: у клиента расстановка сначала уходит
        /// в сеть и может быть отвергнута, а <c>Submitted</c> обязан означать
        /// «зачтено», иначе карточка результата покажет непринятое.
        /// </summary>
        private readonly List<int> intentBuffer = new List<int>(8);

        /// <summary>
        /// Неизменный порядок для чужих полок. Он ничего не значит и никем
        /// не синхронизируется: разглядеть полку соседа из своей клетки
        /// нельзя, а выйти к ней невозможно (спека 10.2).
        /// </summary>
        private readonly List<int> decorativeBuffer = new List<int>(8);

        /// <summary>Своя расстановка, приехавшая от сервера, пока полка под неё не построена.</summary>
        private readonly List<int> pendingShelf = new List<int>(8);

        /// <summary>
        /// Кто ушёл из матча в этом раунде. Они выбывают <b>сверх квоты</b>
        /// и делят место с теми, кого выбило правилом (спека 10.4).
        /// </summary>
        private readonly List<int> leftThisRound = new List<int>(4);

        private CansOrderRoundState round;
        private bool matchOver;

        /// <summary>Состав изменился уходом игрока — числа раунда пересчитать на границе круга.</summary>
        private bool rosterDirty;

        /// <summary>Границы окна выставления на общих часах. По ним сервер проверяет метку подтверждения.</summary>
        private double placementWindowStart;
        private double placementWindowEnd;

        /// <summary>Круг, которому принадлежит окно выше. Подтверждение из чужого круга — отказ.</summary>
        private int placementWindowCircle = -1;

        /// <summary>Раунд, под который у клиента уже построены полки. Только на клиенте.</summary>
        private int shelvesBuiltForRound = -1;

        /// <summary>Сетевая половина. Пусто — сцену открыли напрямую, и всё работает как в соло.</summary>
        private CansOrderNetwork network;

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

            network = GetComponent<CansOrderNetwork>();
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
            leftThisRound.Clear();
            pendingShelf.Clear();
            rosterDirty = false;
            placementWindowCircle = -1;
            shelvesBuiltForRound = -1;

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

                // Клетка встаёт на верхнюю ступень сразу и на каждой машине.
                // Доля высоты у всех начинается с единицы, и без этой строки
                // клиент остался бы с той высотой, на которой клетку оставила
                // сцена: подъём в брифинге первого раунда — ход из единицы
                // в единицу, то есть ничего.
                cage.MoveToFraction(1f, 0f, NetworkClock.Now);

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

                // Подсказке про E нужна полка того игрока, которым управляют
                // с этой машины. Здесь единственное место, где она уже известна:
                // клетки раздаются после загрузки сцены, и HUD сам себя
                // связать не может.
                if (contestant.LocallyControlled && localHud != null)
                {
                    localHud.BindLocalShelf(contestant.Shelf);
                    localHud.BindRules(this);
                }

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

                    // Кнопка становится последней ячейкой ряда полки: курсор
                    // доезжает до неё, и подтверждать можно не сходя с места.
                    contestant.Shelf?.SetButton(contestant.Button);
                }

                // Болванка играет за того, кем никто не управляет. В сети их быть
                // не должно: OnPlayersReady идёт на всех машинах, и каждый клиент
                // навесил бы бота на всех остальных и подтверждал бы за них.
                if (!contestant.LocallyControlled && avatar != null && !WorldAuthority.IsNetworkSession
                    && contestant.Shelf != null && contestant.Button != null)
                {
                    contestant.Bot = avatar.gameObject.AddComponent<CansOrderDebugBot>();
                    contestant.Bot.Bind(this, contestant.Shelf, contestant.Button, avatar,
                        Players[i].Id * 7919 + 13, botSolveChance);
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
            SetShelfMovementLock(false);
            HideLocalAvatar(false);
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
                c.Bot?.Disarm();
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

            // Ушедшие прошлого раунда уже получили место вместе с его группой
            // вылета — но между створками и этой строкой есть две секунды,
            // и ушедший в них не попал ни в какую группу. Такой закрывает
            // свою: он пережил тех, кого выбило правилом, и место у него выше.
            if (leftThisRound.Count > 0)
            {
                ranking.AddEliminationGroup(leftThisRound);
                leftThisRound.Clear();
            }

            rosterDirty = false;

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
                AssignStartingShelf(c);
            }

            if (config.DebugRevealSolution)
            {
                Debug.Log($"🔑 [ОТЛАДКА] Скрытая расстановка раунда {round.Round}: [{string.Join(",", solution)}]", this);
            }

            scoreboard?.ShowTask(round.Round, round.CanCount);
            RaiseCagesForBriefing();
            stageState.BeginSubround(BriefingCircle, StageBriefing, config.BriefingSeconds);
        }

        /// <summary>
        /// Стартовая расстановка полки. Своя у каждого: одинаковая дала бы
        /// за первый круг один отклик на всех вместо восьми (спека 8.2).
        ///
        /// В сети своя расстановка уходит владельцу <b>адресным</b> пакетом,
        /// а на чужие полки на этой машине кладётся неизменный декоративный
        /// порядок: что стоит у соседа, разобрать невозможно по дизайну,
        /// значит и возить это незачем (спека 10.2). Вне сети раскладываем
        /// всем на месте — болванкам соло-прогона нужна настоящая полка.
        ///
        /// Рандом при этом остаётся серверным целиком: клиент получает готовый
        /// результат, а не сид, по которому его можно было бы повторить.
        /// </summary>
        private void AssignStartingShelf(Contestant c)
        {
            if (c.Shelf == null)
            {
                return;
            }

            Shuffle(round.CanCount);

            if (!WorldAuthority.IsNetworkSession || c.LocallyControlled)
            {
                c.Shelf.SetArrangement(shuffleBuffer);
                return;
            }

            network?.SendShelf(c.Entry.PlayerId, shuffleBuffer);
            c.Shelf.SetArrangement(DecorativeOrder(c.Shelf.SlotCount));
        }

        /// <summary>Неизменный порядок чужой полки: 1, 2, 3… Он ничего не значит и никогда не меняется.</summary>
        private IReadOnlyList<int> DecorativeOrder(int count)
        {
            decorativeBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                decorativeBuffer.Add(i);
            }

            return decorativeBuffer;
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

                AssignStartingShelf(c);
            }

            stageState.BeginSubround(round.Circle, StagePlacement, config.PlacementWindowSeconds);
        }

        /// <summary>
        /// Заблокировать ноги на время окна выставления.
        ///
        /// На плейтесте 22.08: «на фоне всё равно модель ходит, и кнопки на неё
        /// работают». В окне выставления камера стоит на полке, ходить некуда
        /// и незачем, а WASD уводил персонажа из кадра — со стороны это
        /// выглядело просто сломанным.
        ///
        /// <b>Цена решения:</b> <c>PlayerEmoteAbility</c> не открывает колесо
        /// насмешек при <c>MovementLocked</c>, то есть Tab не работает те
        /// секунды, что открыто окно. В брифинге, показе результатов и на
        /// падении он работает как раньше. Это осознанный размен, а не
        /// недосмотр: именно из-за него на IGR-370 курсор посадили на мышь,
        /// а не на A/D.
        ///
        /// Снимаем ровно свою блокировку и строго до <see cref="DescendCages"/>:
        /// клетка на спуске ставит свою, и затирать её нельзя.
        ///
        /// <b>По сети блокировка не едет и ехать не должна.</b> Реплицируется
        /// стадия, а блокировка — её локальное следствие: каждая машина ставит
        /// её своему игроку, потому что ввод читается только там, где им
        /// управляют. Чужая копия едет <c>NetworkTransform</c>'ом, и блокировать
        /// её здесь нечего.
        /// </summary>
        private void SetShelfMovementLock(bool locked)
        {
            if (!locked)
            {
                for (int i = 0; i < movementLockedByShelf.Count; i++)
                {
                    if (movementLockedByShelf[i] != null)
                    {
                        movementLockedByShelf[i].MovementLocked = false;
                    }
                }

                movementLockedByShelf.Clear();
                return;
            }

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                PlayerController avatar = c.Session != null ? c.Session.Avatar : null;
                if (avatar == null || !c.Entry.Alive || c.Entry.Solved)
                {
                    continue;
                }

                if (WorldAuthority.IsNetworkSession && !c.LocallyControlled)
                {
                    continue;
                }

                avatar.MovementLocked = true;
                movementLockedByShelf.Add(avatar);
            }
        }

        /// <summary>
        /// Запомнить, что локальный игрок отправил в этом круге и что получил.
        /// Зовётся в стадии показа — там же, где совпадения впервые выходят
        /// из контроллера, и ни секундой раньше.
        /// </summary>
        private void CaptureLocalCircle()
        {
            Contestant local = FindLocal();
            if (local == null)
            {
                lastCircleRound = -1;
                return;
            }

            lastCircleRound = round.Round;
            lastCircleNumber = round.Circle;
            lastCircleConfirmed = local.Entry.Confirmed;
            lastCircleMatches = local.Entry.Matches;

            lastCircleArrangement.Clear();
            for (int i = 0; i < local.Submitted.Count; i++)
            {
                lastCircleArrangement.Add(local.Submitted[i]);
            }
        }

        /// <summary>
        /// Подсказка «прошлый круг» для окна выставления. Пустая строка — либо
        /// круг первый в раунде, либо раунд сменился и прошлое обнулилось:
        /// скрытая расстановка в новом раунде другая, и старые числа врали бы.
        /// </summary>
        public string BuildLastCircleText()
        {
            if (config == null || lastCircleRound != round.Round || lastCircleNumber < 0)
            {
                return string.Empty;
            }

            if (!lastCircleConfirmed)
            {
                return "<size=22>ПРОШЛЫЙ КРУГ</size>\n<size=26>не подтвердил</size>";
            }

            lastCircleText.Clear();
            lastCircleText.Append("<size=22>ПРОШЛЫЙ КРУГ</size>\n<size=38>");
            for (int i = 0; i < lastCircleArrangement.Count; i++)
            {
                CansOrderConfig.CanKind kind = config.GetCanKind(lastCircleArrangement[i]);
                lastCircleText.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(kind.color)).Append('>')
                    .Append(kind.symbol).Append("</color> ");
            }

            lastCircleText.Append("</size>\n<size=28>совпало ").Append(lastCircleMatches).Append("</size>");
            return lastCircleText.ToString();
        }

        /// <summary>
        /// Чем занят локальный игрок, когда его полка молчит посреди окна
        /// выставления. Пустая строка — молчать не о чем.
        ///
        /// Молчащая полка без объяснения читается как поломка: игрок жмёт,
        /// а ничего не происходит.
        /// </summary>
        public string LocalWaitHint()
        {
            Contestant local = FindLocal();
            if (local == null || !local.Entry.Alive)
            {
                return string.Empty;
            }

            if (local.Entry.Solved)
            {
                return "ТЫ СОБРАЛ РАССТАНОВКУ — остаёшься наверху";
            }

            return local.Entry.Confirmed
                ? "РАССТАНОВКА ПРИНЯТА — ждём остальных"
                : string.Empty;
        }

        /// <summary>
        /// Спрятать своего персонажа на время окна выставления.
        ///
        /// Камера стоит в 1.4 м перед доской, игрок — примерно там же, и его
        /// собственная голова временами закрывает половину ряда (плейтест
        /// 22.08). Двигать камеру дальше нельзя: ряд с кнопкой перестаёт
        /// помещаться в кадр.
        ///
        /// Гасим только рендереры и только у своего персонажа: коллайдер,
        /// физика и всё остальное на месте, чужие видят его как обычно.
        /// Возвращаем ровно те, что гасили сами, — <c>PlayerElimination</c>
        /// хранит своё состояние рендереров и восстанавливает точно, и затирать
        /// его нельзя.
        /// </summary>
        private void HideLocalAvatar(bool hide)
        {
            if (!hide)
            {
                for (int i = 0; i < hiddenLocalRenderers.Count; i++)
                {
                    if (hiddenLocalRenderers[i] != null)
                    {
                        hiddenLocalRenderers[i].enabled = true;
                    }
                }

                hiddenLocalRenderers.Clear();
                return;
            }

            if (hiddenLocalRenderers.Count > 0)
            {
                return;
            }

            Contestant local = FindLocal();
            if (local == null || !local.Entry.Alive || local.Entry.Solved)
            {
                return;
            }

            PlayerController avatar = local.Session != null ? local.Session.Avatar : null;
            if (avatar == null)
            {
                return;
            }

            avatar.GetComponentsInChildren(true, rendererBuffer);
            for (int i = 0; i < rendererBuffer.Count; i++)
            {
                Renderer r = rendererBuffer[i];
                if (r == null || !r.enabled)
                {
                    continue;
                }

                r.enabled = false;
                hiddenLocalRenderers.Add(r);
            }
        }

        /// <summary>
        /// Своя карточка результата для экрана: что игрок отправил и сколько
        /// совпало. Пустая строка — показывать нечего.
        ///
        /// Считает контроллер, а не интерфейс: до стадии показа совпадения
        /// не покидают его вовсе, и <see cref="resultsRevealed"/> здесь тот же
        /// замок, что у табло.
        /// </summary>
        public string BuildLocalRevealText()
        {
            if (!resultsRevealed || config == null)
            {
                return string.Empty;
            }

            Contestant local = FindLocal();
            if (local == null || !local.Entry.Alive)
            {
                return string.Empty;
            }

            if (local.Entry.Solved && !local.Entry.SolvedThisCircle)
            {
                return "<size=40>ТЫ УЖЕ СОБРАЛ — сидишь наверху</size>";
            }

            if (!local.Entry.Confirmed)
            {
                return "<size=40>НЕ ПОДТВЕРДИЛ</size>\n<size=28>круг всё равно потрачен</size>";
            }

            revealText.Clear();
            revealText.Append("<size=28>ТВОЯ РАССТАНОВКА</size>\n<size=54>");
            for (int i = 0; i < local.Submitted.Count; i++)
            {
                CansOrderConfig.CanKind kind = config.GetCanKind(local.Submitted[i]);
                revealText.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(kind.color)).Append('>')
                    .Append(kind.symbol).Append("</color>  ");
            }

            revealText.Append("</size>\n");

            if (local.Entry.SolvedThisCircle)
            {
                revealText.Append("<size=46><b>СОБРАЛ ВСЮ РАССТАНОВКУ</b></size>");
                return revealText.ToString();
            }

            revealText.Append("<size=46><b>СОВПАЛО ").Append(local.Entry.Matches)
                .Append(" из ").Append(local.Submitted.Count).Append("</b></size>");
            return revealText.ToString();
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
            // Свою блокировку ног снимаем первым делом — строго до DescendCages:
            // на спуске клетка ставит собственную, и порядок здесь не косметика.
            SetShelfMovementLock(false);
            HideLocalAvatar(false);
            if (stage == StagePlacement)
            {
                SetShelfMovementLock(true);
                HideLocalAvatar(true);
                RememberPlacementWindow();
            }

            // Флаг общий для всех машин: он решает, выходят ли совпадения
            // из контроллера вообще.
            if (stage == StageReveal)
            {
                resultsRevealed = true;
                CaptureLocalCircle();
                // Строго после флага: до него контроллер не отдаёт
                // совпадения даже табло, и оно нарисовало бы нули.
                scoreboard?.ShowResults(this);
                // Клетки едут внутри стадии показа, а не отдельной стадией:
                // читаешь табло — и одновременно чувствуешь, как проваливаешься.
                // Отдельная стадия спуска добавила бы к кругу две секунды
                // и разнесла бы причину и следствие (спека 13, пункт 14).
                //
                // Доли высот назначает сервер: у клиента клетки едут от той же
                // доли, приехавшей списком, и от того же момента начала стадии.
                if (HasAuthority)
                {
                    DescendCages();
                }
            }

            switch (stage)
            {
                case StagePlacement:
                    SetPlacementWindow(true);
                    ApplyShelfCamera(true);
                    break;
                case StageReveal:
                case StageHatch:
                case StagePause:
                case StageBriefing:
                    SetPlacementWindow(false);
                    ApplyShelfCamera(false);
                    break;
            }
        }

        /// <summary>
        /// Камера на время окна выставления встаёт перед полкой и смотрит
        /// на неё, в показе результатов возвращается на обычную орбиту.
        ///
        /// Это единственное разрешённое исключение из замороженной камеры
        /// (igruha/CLAUDE.md, раздел 0): положение под конкретную мини-игру
        /// через <see cref="MinigameCameraController"/>, сам риг и его
        /// настройки не трогаются.
        ///
        /// Зачем: в окне выставления игрок работает с полкой в полуметре
        /// перед собой, а орбитальная камера в этот момент показывает его
        /// собственную спину крупным планом — полка и кнопка оказываются
        /// за ней. Плюс мышь в этой стадии освобождается и ведёт курсор
        /// по ряду банок, а не крутит камеру.
        ///
        /// Выбывшему и наблюдателю камеру не трогаем: у них своя.
        /// </summary>
        private void ApplyShelfCamera(bool toShelf)
        {
            if (cameraController == null || spectator == null || spectator.IsActive)
            {
                return;
            }

            Contestant local = FindLocal();
            if (local == null || local.Session?.Avatar == null)
            {
                return;
            }

            if (!toShelf || local.Shelf == null || !local.Entry.Alive || local.Entry.Solved)
            {
                cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
                return;
            }

            Transform board = local.Shelf.Board;
            if (board == null)
            {
                cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
                return;
            }

            if (shelfCameraRig == null)
            {
                cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
                return;
            }

            // Двигаем сам риг, а не отдельный якорь: у fixed-рига нет ни Body,
            // ни Aim, и Cinemachine его не возит — TrackingTarget такому ригу
            // ничего не задаёт. Камера едет ровно туда, куда поставим.
            //
            // Доска смотрит внутрь клетки, игрок стоит перед ней — значит
            // камера уходит против её forward и приподнимается над рядом,
            // чтобы банки читались сверху, а не с торца.
            shelfCameraRig.position = board.position - board.forward * shelfCameraDistance + Vector3.up * shelfCameraHeight;
            shelfCameraRig.rotation = Quaternion.LookRotation(board.position - shelfCameraRig.position, Vector3.up);

            cameraController.Apply(CameraMode.Fixed, shelfCameraRig);
        }

        private Contestant FindLocal()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].LocallyControlled)
                {
                    return contestants[i];
                }
            }

            return null;
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
            // Пересчёт состава после чужого ухода применяется здесь, на границе
            // круга, и только здесь. Посреди стадии от него поехали бы разом
            // три числа — квота вылета, знаменатель доли высоты клеток и порог
            // конца раунда, — и клетки уехали бы под ногами у тех, кто ещё
            // выставляет (спека 10.4).
            ApplyPendingRoster();

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
                network?.AnnounceSolved(c.Entry.PlayerId);
            }
        }

        /// <summary>
        /// Пересчитать раунд под новый состав после чужого ухода.
        ///
        /// Все три числа — квота вылета (таблица 6.1), знаменатель доли высоты
        /// клеток и порог конца раунда — следуют из одного: сколько живых.
        /// Поэтому пересчёт и умещается в две строки, а не размазан по правилам.
        ///
        /// Ушедший при этом выбывает <b>сверх квоты</b>, а не вместо кого-то:
        /// его идентификатор уже лежит в <see cref="leftThisRound"/> и попадёт
        /// в ту же группу вылета (спека 10.4).
        /// </summary>
        private void ApplyPendingRoster()
        {
            if (!rosterDirty || config == null)
            {
                return;
            }

            rosterDirty = false;

            int alive = AliveCount;
            round.AliveAtStart = alive;
            round.Quota = config.GetEliminationQuota(alive);

            Debug.Log($"🚪 Состав изменился: живых {alive}, квота вылета {round.Quota}, " +
                      $"собрать до конца раунда {Mathf.Max(0, alive - round.Quota)}", this);
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

            // Ушедшие из матча в этом раунде делят место с теми, кого выбило
            // правилом: они выбыли на текущий момент и попадают в текущую
            // группу вылета (спека 10.4). Клетки у них уже пустые — Find
            // вернёт null, и створки им не откроются.
            MergeLeavers(eliminatedThisRound);

            int doorsOpened = 0;
            for (int i = 0; i < eliminatedThisRound.Count; i++)
            {
                Contestant c = Find(eliminatedThisRound[i]);
                if (c == null)
                {
                    continue;
                }

                doorsOpened++;

                c.Entry.Alive = false;
                c.Entry.Solved = false;
                c.InPit = true;

                // Банка из руки возвращается до падения: она кинематическая
                // и прицеплена к персонажу — иначе улетит в яму вместе с ним,
                // а потом и в хаб (спека 10.5).
                c.Bot?.Disarm();
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

            // Длительность стадии — по реально распахнутым створкам, а не по
            // размеру группы: группа из одних ушедших не открывает ни одной
            // клетки, и ждать её нечего.
            stageState.EnterStage(StageHatch, doorsOpened > 0 ? config.HatchOpenSeconds : 0f);
        }

        /// <summary>Добавить ушедших в группу вылета этого раунда, не задваивая уже попавших.</summary>
        private void MergeLeavers(List<int> into)
        {
            for (int i = 0; i < leftThisRound.Count; i++)
            {
                if (!into.Contains(leftThisRound[i]))
                {
                    into.Add(leftThisRound[i]);
                }
            }

            leftThisRound.Clear();
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
            Contestant c = FindByAvatar(player);
            if (c == null || c.Shelf == null)
            {
                return;
            }

            // Снимок берётся в момент нажатия и на той машине, где нажали:
            // полка после этого замирает, и считается ровно отправленное.
            if (!c.Shelf.TryGetArrangement(intentBuffer))
            {
                Debug.LogWarning($"{name}: игрок {c.Entry.PlayerId} подтвердил неполную расстановку — отказ", this);
                return;
            }

            if (HasAuthority)
            {
                ServerApplyArrangement(c.Entry.PlayerId, intentBuffer, NetworkClock.Now, NetworkClock.Now);
                return;
            }

            // Клиент шлёт намерение, исход считает сервер: он один знает
            // скрытую расстановку. Лампа загорится от его ответа, а не от
            // факта нажатия — так же, как у кнопки «Секундомера».
            CopyInto(intentBuffer, c.Submitted);

            // Полка замирает сразу, не дожидаясь ответа: игрок отправил, и
            // менять на ней больше нечего. Именно этого не хватало на
            // плейтесте 22.08 — «меняю после подтверждения, а не считается».
            c.Shelf.Active = false;
            network?.SubmitArrangement(c.Submitted, NetworkClock.Now);
        }

        /// <summary>
        /// Сервер принял намерение. Здесь и только здесь — все пять правил
        /// приёма из спеки 10.3; клиенту не доверяется ничего.
        ///
        /// Возвращает решение: оно уходит адресным пакетом тому, кто нажимал,
        /// и никому больше. Отвергнутое подтверждение равно «не подтвердил» —
        /// попытка потрачена, на табло <c>НЕ ПОДТВЕРДИЛ</c>.
        /// </summary>
        /// <param name="stamp">Момент нажатия на общих часах, присланный клиентом.</param>
        /// <param name="arrival">Момент прибытия пакета на сервер.</param>
        public bool ServerApplyArrangement(int playerId, IReadOnlyList<int> arrangement, double stamp, double arrival)
        {
            if (!HasAuthority || arrangement == null || config == null)
            {
                return false;
            }

            Contestant c = Find(playerId);
            if (c == null)
            {
                return false;
            }

            // 1. Стадия. Окно этого круга ещё идёт либо кончилось не более
            //    ConfirmGraceSeconds назад: пакет, отправленный до дедлайна,
            //    не должен пропадать из-за пинга.
            if (placementWindowCircle != round.Circle || arrival > placementWindowEnd + ConfirmGraceSeconds)
            {
                Debug.LogWarning($"{name}: подтверждение игрока {playerId} пришло вне окна круга {round.Circle} — отказ", this);
                return false;
            }

            // 2. Метка. Момент нажатия попадает внутрь окна. Точность здесь
            //    не нужна: важно только «до дедлайна или после», а не
            //    «на сколько миллисекунд раньше соседа».
            if (stamp < placementWindowStart || stamp > placementWindowEnd)
            {
                Debug.LogWarning($"{name}: метка подтверждения игрока {playerId} ({stamp:F3}) вне окна " +
                                 $"{placementWindowStart:F3}…{placementWindowEnd:F3} — отказ", this);
                return false;
            }

            // 5. Состав раунда: жив и ещё не собрал.
            // 4. Повтор: в этом круге ещё не подтверждал.
            if (!c.Entry.Alive || c.Entry.Solved || c.Entry.Confirmed)
            {
                return false;
            }

            // 3. Состав. Практически недостижимый отказ: механика обмена
            //    не даёт собрать невалидную расстановку. Проверка стоит
            //    предохранителем от подделанного пакета, а не рабочей веткой.
            if (!IsPermutation(arrangement, round.CanCount))
            {
                Debug.LogWarning($"{name}: от игрока {playerId} пришла не перестановка " +
                                 $"[{string.Join(",", arrangement)}] при {round.CanCount} банках — отказ", this);
                return false;
            }

            CopyInto(arrangement, c.Submitted);
            c.Entry.Confirmed = true;

            // Время подтверждения для тайбрейка берётся из метки, зажатой
            // в границы окна, а не из момента прибытия: иначе тайбрейк решал бы
            // качество канала, а не то, кто раньше нажал.
            c.Entry.ConfirmTime = stamp < placementWindowStart
                ? placementWindowStart
                : (stamp > placementWindowEnd ? placementWindowEnd : stamp);

            // Лампа и замершая полка — только у того, кто нажал, и только
            // на его машине. Иначе хост видел бы, кто уже подтвердил, а клиенты
            // нет: «принято» до стадии показа не должно быть известно никому,
            // кроме самого игрока. Вне сети раскладка та же, но локальны все —
            // болванкам соло-прогона лампа зажигается как раньше.
            if (c.LocallyControlled || !WorldAuthority.IsNetworkSession)
            {
                c.Button?.MarkAccepted();
                if (c.Shelf != null)
                {
                    c.Shelf.Active = false;
                }
            }

            if (config.ShowOwnMatchesImmediately)
            {
                // ОТЛАДОЧНЫЙ режим и ничто иное: он ломает честность игры,
                // показывая результат раньше общего показа.
                Debug.Log($"🔑 [ОТЛАДКА] игрок {playerId} подтвердил [{string.Join(",", c.Submitted)}] — " +
                          $"совпадений {CountMatches(c.Submitted)} из {round.CanCount}", this);
            }

            return true;
        }

        /// <summary>Запомнить границы окна выставления: по ним сервер проверяет метку подтверждения.</summary>
        private void RememberPlacementWindow()
        {
            if (!HasAuthority || stageState == null)
            {
                return;
            }

            placementWindowCircle = round.Circle;
            placementWindowEnd = stageState.StageEndTime;
            placementWindowStart = placementWindowEnd - stageState.StageDuration;
        }

        private static void CopyInto(IReadOnlyList<int> from, List<int> into)
        {
            into.Clear();
            for (int i = 0; i < from.Count; i++)
            {
                into.Add(from[i]);
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
        private bool IsPermutation(IReadOnlyList<int> arrangement, int canCount)
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

            // Позицию везёт серверный NetworkTransform, а вот рёв и стойка
            // на лапах — решение сервера: иначе на одной машине медведь
            // дразнит клетку, а на другой молча ходит кругами.
            network?.PublishBearState((byte)bear.State);
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

                // Направление отлёта уезжает готовым: тогда клип падения
                // выбирается одинаково у всех, и смерть выглядит одной и той же
                // на каждой машине.
                network?.AnnounceCaught(c.Entry.PlayerId, victim.transform.position, impulse);
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

            // Пустой снимок при поднятом «подтвердил» бывает ровно в одном
            // случае: у клиента это собравший, чью расстановку сервер
            // не публикует намеренно. Отказываем, а не отдаём пустую строку,
            // иначе она заняла бы место в тройке табло.
            if (!c.Entry.Confirmed || c.Submitted.Count == 0)
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

        /// <summary>Окно выставления из конфига. Читают болванки соло-прогона.</summary>
        public float PlacementWindowSeconds => config != null ? config.PlacementWindowSeconds : 1f;

        /// <summary>
        /// Расстановка для болванки соло-прогона: либо верная,
        /// либо случайная.
        ///
        /// <b>Это единственная точка во всём коде, где скрытая расстановка
        /// покидает контроллер</b>, и она закрыта дважды: работает только
        /// у авторитета и только вне сетевой катки. В сети болванок нет
        /// вообще, и этот метод там ничего не отдаёт.
        /// </summary>
        public bool TryGetBotArrangement(bool wantSolution, List<int> into)
        {
            if (into == null || !HasAuthority || WorldAuthority.IsNetworkSession || solution.Count == 0)
            {
                return false;
            }

            into.Clear();
            if (wantSolution)
            {
                for (int i = 0; i < solution.Count; i++)
                {
                    into.Add(solution[i]);
                }

                return true;
            }

            Shuffle(round.CanCount);
            for (int i = 0; i < shuffleBuffer.Count; i++)
            {
                into.Add(shuffleBuffer[i]);
            }

            return true;
        }

        // ========== СЕТЬ: СЕРВЕР ОТДАЁТ ==========

        /// <summary>
        /// Снимок состояния участника для репликации.
        ///
        /// <b>Совпадения, факт подтверждения и «собрал в этом круге» уходят
        /// наружу только со стадии показа.</b> До неё их в сетевом состоянии
        /// нет физически, а не спрятаны в интерфейсе: клиент, читающий
        /// состояние напрямую, не должен найти там свой счёт раньше остальных.
        /// Это тот же замок, что у <see cref="TryGetEntry"/>, и тот же приём,
        /// что у <c>StopwatchMinigame.TryGetCageState</c>.
        /// </summary>
        public bool TryGetEntryNetState(int index, out CansOrderEntryNetState state)
        {
            state = default;
            if (index < 0 || index >= contestants.Count)
            {
                return false;
            }

            Contestant c = contestants[index];
            state = new CansOrderEntryNetState
            {
                PlayerId = c.Entry.PlayerId,
                Alive = c.Entry.Alive,
                Solved = c.Entry.Solved,
                DoorsOpen = c.Cage != null && c.Cage.DoorsOpen,
                Attempts = (byte)Mathf.Clamp(c.Entry.Attempts, 0, byte.MaxValue),
                HeightFraction = c.Entry.HeightFraction,
                Revealed = resultsRevealed,
                Confirmed = resultsRevealed && c.Entry.Confirmed,
                SolvedThisCircle = resultsRevealed && c.Entry.SolvedThisCircle,
                Matches = resultsRevealed ? (byte)Mathf.Clamp(c.Entry.Matches, 0, byte.MaxValue) : (byte)0
            };

            // Расстановку кладём в пакет ровно на тех же условиях, на каких
            // её показывает табло: стадия показа, игрок подтвердил и не собрал.
            //
            // Условие «не собрал» здесь не косметика, а замок: расстановка
            // собравшего и есть ответ раунда, и она не должна оказаться
            // в сетевом состоянии вообще — ни в стадии показа, ни после
            // (спека 5.3 и 12). Остальные расстановки приехать обязаны, иначе
            // у клиента на табло не будет тройки, ради которой вся катка
            // и списывает с лидера круга.
            if (resultsRevealed && c.Entry.Confirmed && !c.Entry.Solved
                && CansOrderEntryNetState.TryPack(c.Submitted, out ulong packed, out byte packedCount))
            {
                state.Arrangement = packed;
                state.ArrangementCount = packedCount;
            }

            return true;
        }

        /// <summary>
        /// Участник вышел из матча. Зовёт сетевая половина, только у сервера.
        ///
        /// Клетка гаснет и остаётся висеть пустой на своей высоте: пустая
        /// клетка на арене означает ровно одно — отсюда уже выбыли, и уход
        /// читается так же, как вылет (спека 10.4).
        ///
        /// <b>Числа раунда здесь не трогаются намеренно.</b> Квота, знаменатель
        /// доли высоты и порог конца раунда пересчитываются на границе круга,
        /// в <see cref="ApplyPendingRoster"/>: посреди стадии они поехали бы
        /// под ногами у тех, кто ещё выставляет.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            Contestant c = Find(playerId);
            if (c == null)
            {
                return;
            }

            // Из состава раунда — иначе беглец получит место, хотя его нет
            // в матче. Ростер сессии чистит Core, здесь свой локальный список.
            RemovePlayer(playerId);

            if (c.Entry.Alive)
            {
                c.Entry.Alive = false;
                leftThisRound.Add(playerId);
                rosterDirty = true;

                // Он больше не среди справившихся: доля высоты клеток считается
                // от числа собравших среди живых, и оставленный счётчик увёл бы
                // оставшихся вниз быстрее, чем они заслужили.
                if (c.Entry.Solved && round.SolvedCount > 0)
                {
                    round.SolvedCount--;
                }
            }

            c.Entry.Solved = false;

            // Медведь бросает ушедшего сам: цель он выбирает среди тех, кто
            // в яме, а ушедший из ямы вычеркнут. Опустела яма — вернётся
            // в патруль, там есть кто-то ещё — переключится на ближайшего.
            DetachContestant(c);
            contestants.Remove(c);

            if (matchOver)
            {
                return;
            }

            // Осталось меньше двух живых по любой причине — матч кончился,
            // места по накопленному порядку вылета (спека 10.4).
            if (AliveCount < 2)
            {
                matchOver = true;
                EndMinigame();
            }
        }

        /// <summary>
        /// Снять с участника всё, что мини-игра на него навесила.
        ///
        /// Один список на два случая — ушёл из матча и убран с клиента, — потому
        /// что забыть здесь строку значит увезти её в хаб вместе с персонажем:
        /// он переезжает между сценами живым (спека 10.5).
        /// </summary>
        private void DetachContestant(Contestant c)
        {
            if (c.Button != null)
            {
                c.Button.Confirmed -= HandleConfirmed;
                c.Button.CloseWindow();
            }

            c.Bot?.Disarm();
            c.Shelf?.Release();
            c.Cage?.ReleaseOccupant();

            if (c.Elimination != null)
            {
                c.Elimination.BodyHidden -= HandleBodyHidden;
            }

            c.InPit = false;
        }

        // ========== СЕТЬ: КЛИЕНТ ПРИМЕНЯЕТ ==========

        /// <summary>
        /// Задание раунда и номер круга приехали из сети. Весь рандом уже
        /// отыгран на сервере, скрытой расстановки здесь нет и не будет.
        /// </summary>
        public void ApplyNetworkRound(int roundNumber, int circle, int canCount,
            int quota, int aliveAtStart, int solvedCount)
        {
            if (HasAuthority || config == null)
            {
                return;
            }

            // Полки перестраиваем по отдельной отметке, а не по смене номера
            // раунда: состояние могло приехать раньше, чем контроллер собрал
            // состав, и тогда строить было нечего — а номер раунда уже
            // записался бы, и второй попытки не случилось бы никогда.
            bool needShelves = contestants.Count > 0 && shelvesBuiltForRound != roundNumber;
            bool newCircle = circle != round.Circle;

            round.Round = roundNumber;
            round.Circle = circle;
            round.CanCount = canCount;
            round.Quota = quota;
            round.AliveAtStart = aliveAtStart;
            round.SolvedCount = solvedCount;

            if (needShelves)
            {
                shelvesBuiltForRound = roundNumber;
                RebuildShelvesForRound();
                scoreboard?.ShowTask(roundNumber, canCount);
            }

            if (newCircle)
            {
                // Новый круг гасит прошлые числа: до стадии показа совпадения
                // не выходят из контроллера, и старые показали бы результат
                // прошлого круга как результат текущего.
                resultsRevealed = false;
                for (int i = 0; i < contestants.Count; i++)
                {
                    Contestant c = contestants[i];
                    c.Entry.Confirmed = false;
                    c.Entry.Matches = 0;
                    c.Entry.SolvedThisCircle = false;
                    c.Submitted.Clear();
                }
            }

            TryApplyPendingShelf();
        }

        /// <summary>
        /// Новый раунд у клиента: перестроить полки под новое число банок.
        /// Своя получит расстановку адресным пакетом сервера, чужие — неизменный
        /// декоративный порядок (спека 10.2).
        /// </summary>
        private void RebuildShelvesForRound()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                c.Button?.ResetForRound();

                if (c.Shelf == null)
                {
                    continue;
                }

                c.Shelf.Build(config, round.CanCount);
                c.Shelf.SetArrangement(DecorativeOrder(c.Shelf.SlotCount));
            }
        }

        /// <summary>Своя стартовая расстановка приехала от сервера.</summary>
        public void ApplyNetworkShelf(IReadOnlyList<int> arrangement)
        {
            if (HasAuthority || arrangement == null)
            {
                return;
            }

            CopyInto(arrangement, pendingShelf);
            TryApplyPendingShelf();
        }

        /// <summary>
        /// Разложить приехавшую расстановку, как только полка под неё построена.
        ///
        /// Адресный пакет с расстановкой и состояние раунда — два разных канала,
        /// и порядок их прибытия не гарантирован: расстановка вполне может
        /// опередить число банок, под которое полка ещё не перестроена. Ждём
        /// совпадения длины вместо того, чтобы гадать о порядке.
        /// </summary>
        private void TryApplyPendingShelf()
        {
            if (pendingShelf.Count == 0)
            {
                return;
            }

            Contestant local = FindLocal();
            if (local?.Shelf == null || local.Shelf.SlotCount != pendingShelf.Count)
            {
                return;
            }

            local.Shelf.SetArrangement(pendingShelf);
            pendingShelf.Clear();
        }

        /// <summary>Начало разбора приехавшего списка: помечаем всех как ненайденных.</summary>
        public void ApplyNetworkEntriesBegin()
        {
            if (HasAuthority)
            {
                return;
            }

            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].NetSeen = false;
            }
        }

        /// <summary>Состояние одного участника приехало из сети.</summary>
        public void ApplyNetworkEntry(CansOrderEntryNetState state)
        {
            if (HasAuthority)
            {
                return;
            }

            Contestant c = Find(state.PlayerId);
            if (c == null)
            {
                return;
            }

            c.NetSeen = true;
            c.Entry.Alive = state.Alive;
            c.Entry.Solved = state.Solved;
            c.Entry.Attempts = state.Attempts;

            // Совпадения, подтверждение и «собрал в этом круге» приезжают только
            // со стадии показа — до неё их в пакете нет. Затирать ими своё
            // локальное «принято» нельзя: ответ на подтверждение приходит
            // адресно и раньше, и игрок увидел бы, что его расстановка
            // не принята, хотя она принята.
            if (state.Revealed)
            {
                c.Entry.Confirmed = state.Confirmed;
                c.Entry.SolvedThisCircle = state.SolvedThisCircle;
                c.Entry.Matches = state.Matches;

                // Чужая расстановка приезжает готовой — своя уже лежит здесь
                // с момента нажатия, и затирать её приехавшей нельзя: у своей
                // она есть даже тогда, когда сервер её не публикует, то есть
                // когда игрок собрал.
                if (!c.LocallyControlled)
                {
                    CansOrderEntryNetState.Unpack(state.Arrangement, state.ArrangementCount, c.Submitted);
                }
            }

            ApplyNetworkHeight(c, state.HeightFraction);
            ApplyNetworkDoors(c, state.DoorsOpen);
        }

        /// <summary>
        /// Высота клетки на этой машине.
        ///
        /// Клетка едет сама — по той же формуле и от того же момента, что
        /// у сервера, поэтому расхождение не копится, а машина, получившая
        /// долю позже, сразу встаёт на верную высоту и едет дальше, вместо
        /// того чтобы догонять рывком.
        ///
        /// Пока клетка в ходу, новая доля не перебивает старую: круг длиннее
        /// хода вчетверо, и перебивать нечего, а вот дрожание от догоняющих
        /// пакетов было бы видно. Не применённая доля возьмётся следующим
        /// кадром — сравнение идёт с тем, что реально применено.
        /// </summary>
        private void ApplyNetworkHeight(Contestant c, float fraction)
        {
            if (c.Cage == null || c.Cage.Descending || Mathf.Approximately(c.Entry.HeightFraction, fraction))
            {
                return;
            }

            c.Entry.HeightFraction = fraction;

            byte stage = stageState != null ? stageState.Stage : MinigameStageState.NoStage;

            // Момент начала стадии: конец минус длительность. Считается
            // одинаково у всех, поэтому от него и пляшем.
            double startedAt = stageState != null
                ? stageState.StageEndTime - stageState.StageDuration
                : NetworkClock.Now;

            switch (stage)
            {
                case StageBriefing:
                    c.Cage.MoveToFraction(fraction, config.BriefingRiseSeconds, startedAt);
                    break;
                case StageReveal:
                    c.Cage.MoveToFraction(fraction, config.CageDescendSeconds, startedAt);
                    break;
                default:
                    // Вне стадий хода доля означает поправку — например,
                    // подключившемуся посреди раунда. Встаём сразу.
                    c.Cage.MoveToFraction(fraction, 0f, NetworkClock.Now);
                    break;
            }
        }

        /// <summary>
        /// Створки: открывает их объявление сервера, а падение дальше — обычная
        /// гравитация на машине владельца. Сервер никого не толкает.
        /// </summary>
        private void ApplyNetworkDoors(Contestant c, bool doorsOpen)
        {
            if (c.Cage == null || c.Cage.DoorsOpen == doorsOpen)
            {
                return;
            }

            if (doorsOpen)
            {
                c.Cage.OpenDoors(config.HatchOpenSeconds);
            }
            else
            {
                c.Cage.CloseDoors();
            }
        }

        /// <summary>Список приехал целиком — убрать ушедших и перерисовать табло один раз.</summary>
        public void ApplyNetworkEntriesCommitted()
        {
            if (HasAuthority)
            {
                return;
            }

            // Ушедших сервер убирает из состава — убираем и здесь, иначе табло
            // до конца матча держало бы строку того, кого в матче нет.
            for (int i = contestants.Count - 1; i >= 0; i--)
            {
                if (contestants[i].NetSeen)
                {
                    continue;
                }

                DetachContestant(contestants[i]);
                contestants.RemoveAt(i);
            }

            if (!resultsRevealed)
            {
                return;
            }

            // Табло и своя карточка рисуются после того, как приехали все
            // строки: иначе первая же дельта списка перерисовала бы их
            // по половине данных. Свою прошлую расстановку снимаем здесь же —
            // на стадии показа число совпадений уже приехало.
            CaptureLocalCircle();
            scoreboard?.ShowResults(this);
        }

        /// <summary>
        /// Ответ сервера на своё подтверждение. Приходит адресно и только тому,
        /// кто нажимал: факт «этот уже подтвердил» до стадии показа не знает
        /// никто, кроме него самого.
        /// </summary>
        public void ApplyNetworkConfirmAnswer(bool accepted)
        {
            if (HasAuthority)
            {
                return;
            }

            Contestant local = FindLocal();
            if (local == null)
            {
                return;
            }

            if (!accepted)
            {
                // Отвергнутое подтверждение равно «не подтвердил»: попытка
                // потрачена, на табло будет «НЕ ПОДТВЕРДИЛ» (спека 10.3).
                local.Submitted.Clear();
                return;
            }

            local.Entry.Confirmed = true;
            local.Button?.MarkAccepted();
            if (local.Shelf != null)
            {
                local.Shelf.Active = false;
            }
        }

        /// <summary>Сервер объявил, что игрок собрал расстановку — отыграть фанфару над его клеткой.</summary>
        public void ApplyNetworkSolvedFanfare(int playerId)
        {
            if (HasAuthority)
            {
                return;
            }

            Contestant c = Find(playerId);
            if (c != null)
            {
                SpawnFanfare(c);
            }
        }

        /// <summary>
        /// Сервер объявил, что медведь достал игрока. Отыгрываем ту же гибель
        /// тем же импульсом: направление приезжает готовым, поэтому клип падения
        /// выбирается одинаково у всех.
        /// </summary>
        public void ApplyNetworkCaught(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            if (HasAuthority)
            {
                return;
            }

            Contestant c = Find(playerId);
            if (c == null)
            {
                return;
            }

            c.InPit = false;
            c.Elimination?.Eliminate(hitPoint, impulse);
        }

        /// <summary>Состояние медведя пришло из сети — показать, не считая ИИ.</summary>
        public void ApplyNetworkBearState(byte state)
        {
            if (HasAuthority || bear == null || bearConfig == null)
            {
                return;
            }

            var next = (PitBear.BearState)state;
            bear.ApplyNetworkState(next, next == PitBear.BearState.Chase
                ? bearConfig.ChaseSpeed
                : (next == PitBear.BearState.Patrol ? bearConfig.PatrolSpeed : 0f));
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
            // Матч мог кончиться, не дойдя до створок: например, ушли все,
            // кроме одного. Ушедшие этого раунда получают место последней
            // группой вылета — иначе их не будет в итогах вовсе.
            if (leftThisRound.Count > 0)
            {
                ranking.AddEliminationGroup(leftThisRound);
                leftThisRound.Clear();
            }

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
