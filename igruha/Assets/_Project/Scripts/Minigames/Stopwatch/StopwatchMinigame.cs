using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Minigames.Circus;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Правила «Секундомера»: последовательность подраундов до последнего
    /// выжившего.
    ///
    /// Подсчёт относительный, а не абсолютный: ошибку получают K худших,
    /// а не те, кто вышел за порог. Поэтому игра всегда движется к финалу,
    /// как бы точны ни были игроки в конкретном лобби.
    ///
    /// Весь рандом — типы, цели, период тика — считает авторитет и объявляет
    /// остальным. Клиент ничего не выбирает сам.
    /// </summary>
    public sealed class StopwatchMinigame : MinigameControllerBase
    {
        /// <summary>Стадии подраунда. Значения уезжают в сеть байтом, порядок менять нельзя.</summary>
        private const byte StageBriefing = 1;
        private const byte StageMeasure = 2;
        private const byte StageResults = 3;
        private const byte StageDescend = 4;
        private const byte StageHatch = 5;
        private const byte StagePause = 6;

        [SerializeField] private StopwatchConfig config;
        [SerializeField] private CircusArenaConfig arenaConfig;
        [SerializeField] private MinigameStageState stageState;
        [SerializeField] private StopwatchScoreboard scoreboard;
        [Tooltip("Клетки арены — все восемь. Лишние гасятся по числу игроков")]
        [SerializeField] private CageStation[] cages = System.Array.Empty<CageStation>();
        [Tooltip("Медведь в яме")]
        [SerializeField] private PitBear bear;
        [Tooltip("Камера наблюдателя — включается выбывшему")]
        [SerializeField] private SpectatorCamera spectator;
        [Tooltip("Отвлекалки: тик, рёв, толпа, прожекторы")]
        [SerializeField] private DistractionDirector distractions;

        /// <summary>Участник матча: клетка, кнопка, ошибки, замер подраунда.</summary>
        private sealed class Contestant
        {
            public SessionPlayer Session;
            public CageStation Cage;
            public CageButton Button;
            public int Errors;
            public bool Alive = true;
            public float Measured;
            public bool Completed;
            public bool FaultedThisSubround;
            public float TotalDeviation;
            public StopwatchDebugBot Bot;
            public PlayerElimination Elimination;
            /// <summary>Упал в яму и ещё не убит медведем.</summary>
            public bool InPit;
            public bool LocallyControlled;
        }

        private readonly List<Contestant> contestants = new List<Contestant>(8);
        private readonly List<StopwatchEntry> entries = new List<StopwatchEntry>(8);
        private readonly List<int> faulted = new List<int>(8);
        private readonly List<int> eliminatedThisSubround = new List<int>(8);
        private readonly EliminationRanking ranking = new EliminationRanking();

        // Буферы для табло: пересоздавать списки каждый подраунд незачем.
        private readonly List<string> boardNames = new List<string>(8);
        private readonly List<float> boardTimes = new List<float>(8);
        private readonly List<bool> boardCompleted = new List<bool>(8);
        private readonly List<int> boardErrors = new List<int>(8);
        private readonly List<bool> boardAlive = new List<bool>(8);
        private readonly List<bool> boardFaulted = new List<bool>(8);

        private StopwatchNetwork network;

        /// <summary>
        /// Результаты подраунда уже показаны. Держится от стадии показа
        /// до начала следующего подраунда: пока флага нет, замеры не уходят
        /// в сеть вообще, а не прячутся в интерфейсе.
        /// </summary>
        private bool resultsRevealed;

        private System.Random random;
        private int startingPlayers;
        private int errorLimit;
        private int subround;
        private StopwatchSubroundType currentType;
        private float currentTarget;
        private float currentTickPeriod;
        private bool matchOver;

        /// <summary>Тип текущего подраунда. Наружу — отладочным болванкам соло-прогона.</summary>
        public StopwatchSubroundType CurrentType => currentType;

        /// <summary>Цель текущего подраунда, с. Наружу — отладочным болванкам соло-прогона.</summary>
        public float CurrentTarget => currentTarget;

        /// <summary>Номер текущего подраунда.</summary>
        public int Subround => subround;

        /// <summary>Период тика текущего подраунда, с. Наружу — для замеров приёмки.</summary>
        public float CurrentTickPeriod => currentTickPeriod;

        /// <summary>
        /// Кнопка того игрока, которым управляет эта машина. Нужна строке
        /// состояния: она единственный элемент интерфейса, у каждого свой.
        /// </summary>
        public CageButton LocalButton { get; private set; }

        /// <summary>Сколько игроков ещё в игре.</summary>
        public int AliveCount
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < contestants.Count; i++)
                {
                    if (contestants[i].Alive)
                    {
                        alive++;
                    }
                }

                return alive;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (stageState == null)
            {
                stageState = GetComponent<MinigameStageState>();
            }

            // Сетевая половина лежит на том же объекте. Ссылку берём кодом,
            // а не полем инспектора: арену пересобирает пункт меню, и ручную
            // ссылку пришлось бы перецеплять после каждой пересборки.
            network = GetComponent<StopwatchNetwork>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (stageState != null)
            {
                stageState.StageElapsed += HandleStageElapsed;
                stageState.StageStarted += HandleStageStarted;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (stageState != null)
            {
                stageState.StageElapsed -= HandleStageElapsed;
                stageState.StageStarted -= HandleStageStarted;
            }
        }

        protected override void OnPlayersReady()
        {
            if (config == null || arenaConfig == null)
            {
                Debug.LogError($"{name}: не назначены StopwatchConfig / CircusArenaConfig — играть нечем", this);
                return;
            }

            contestants.Clear();
            startingPlayers = Players.Count;
            errorLimit = config.GetErrorLimit(startingPlayers);
            // Рандом только серверный: сид берётся у авторитета и в фазе 3
            // уедет в сеть вместе с выбранными типами и целями.
            random = new System.Random(System.Environment.TickCount);
            subround = 0;
            matchOver = false;

            AssignCages();

            distractions?.Configure(config.RoarIntervalRange, config.SpotlightIntervalRange, arenaConfig.PitRadius);

            if (bear != null)
            {
                bear.Configure(config.BearSpeed, config.BearPatrolSpeed, config.BearStrikeRadius,
                    config.BearFirstAttackDelay, config.BearKnockbackSpeed, arenaConfig.PitRadius);
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
                // Клетку настраиваем до посадки игрока: при лимите 2 верхняя
                // ступень ниже стартовой, и игрок, посаженный раньше, падал бы
                // внутрь клетки на два метра.
                cage.Configure(arenaConfig, errorLimit);

                var contestant = new Contestant
                {
                    Session = Players[i],
                    Cage = cage,
                    Button = cage.PropSlot != null ? cage.PropSlot.GetComponentInChildren<CageButton>(true) : null
                };

                PlayerController avatar = Players[i].Avatar;
                cage.SetOccupant(avatar);
                if (avatar != null && cage.PropSlot != null)
                {
                    avatar.RequestTeleport(cage.PropSlot.position - cage.transform.forward * (arenaConfig.CageInnerSize * 0.25f),
                        Quaternion.LookRotation(cage.transform.forward));
                }

                bool humanControlled = avatar != null && avatar.TryGetComponent(out PlayerInputReader reader) && reader.LocallyControlled;

                if (contestant.Button != null)
                {
                    contestant.Button.SetOwner(avatar);
                    contestant.Button.NeighbourLightsEnabled = config.NeighbourButtonLights;
                    contestant.Button.ViewedByOwner = humanControlled;
                    contestant.Button.Stopped += HandleButtonStopped;
                    contestant.Button.CloseWindow();

                    // Болванка жмёт кнопку за того, кем никто не управляет.
                    // Без этого соло-прогон проверяет только вылет молчунов,
                    // а рейтинг худших остаётся непроверенным.
                    //
                    // В сети болванок быть не должно: OnPlayersReady идёт на
                    // всех машинах, и «этой машиной не управляется» верно для
                    // каждого чужого игрока — каждый клиент навесил бы бота
                    // на всех остальных и жал бы за них кнопки.
                    if (!humanControlled && avatar != null && !WorldAuthority.IsNetworkSession)
                    {
                        contestant.Bot = avatar.gameObject.AddComponent<StopwatchDebugBot>();
                        contestant.Bot.Bind(contestant.Button, avatar, Players[i].Id * 7919);
                    }
                }

                contestant.LocallyControlled = humanControlled;
                if (humanControlled)
                {
                    LocalButton = contestant.Button;
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

            BeginSubround();
        }

        protected override void OnRoundEnded()
        {
            // Всё, что мини-игра навесила на игрока, она обязана снять сама:
            // персонаж переезжает между сценами живым, и незакрытая роль
            // уезжает в хаб вместе с ним.
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Button != null)
                {
                    c.Button.Stopped -= HandleButtonStopped;
                    c.Button.CloseWindow();
                }

                c.Bot?.Disarm();
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

            LocalButton = null;
            distractions?.Stop();
            spectator?.Deactivate();
            stageState?.StopSequence();
            scoreboard?.Clear();
        }

        // ========== ПОДРАУНД ==========

        private void BeginSubround()
        {
            subround++;
            resultsRevealed = false;
            currentType = config.GetSubroundType(subround);
            currentTarget = PickTarget(subround);

            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].Measured = 0f;
                contestants[i].Completed = false;
                contestants[i].FaultedThisSubround = false;
            }

            // Период тика крутит авторитет, а не каждая машина: иначе игроки
            // мерили бы время под разную подсказку, и подраунд перестал бы
            // быть честным. В фазе 3 это же число уедет в NetworkVariable.
            currentTickPeriod = config.GetTickPeriod((float)random.NextDouble());
            distractions?.BeginSubround(currentTickPeriod, config.GetDistractionIntensity(subround));

            scoreboard?.ShowTask(subround, currentType, currentTarget);
            PublishBoard(false);

            // Задание уходит в сеть до стадии: клиент должен знать тип и цель
            // раньше, чем у него откроется окно отмера.
            network?.PublishTask(subround, currentType, currentTarget, currentTickPeriod);

            stageState.BeginSubround(subround, StageBriefing, config.BriefingSeconds);
        }

        /// <summary>Цель подраунда. Рандом серверный: клиент её только получает.</summary>
        private float PickTarget(int index)
        {
            config.GetTargetWindow(index, out float low, out float high);
            float raw = low + (float)random.NextDouble() * (high - low);
            return Mathf.Clamp(config.RoundTarget(raw), low, high);
        }

        private void HandleStageElapsed(byte stage)
        {
            if (matchOver)
            {
                return;
            }

            switch (stage)
            {
                case StageBriefing:
                    EnterMeasure();
                    break;
                case StageMeasure:
                    EnterResults();
                    break;
                case StageResults:
                    EnterDescend();
                    break;
                case StageDescend:
                    EnterHatch();
                    break;
                case StageHatch:
                    stageState.EnterStage(StagePause, config.PauseSeconds);
                    break;
                case StagePause:
                    if (AliveCount < 2)
                    {
                        matchOver = true;
                        EndMinigame();
                        return;
                    }

                    BeginSubround();
                    break;
            }
        }

        private void EnterMeasure()
        {
            OpenMeasureWindows(true);
            stageState.EnterStage(StageMeasure, config.MeasureWindowSeconds);
        }

        /// <summary>
        /// Открыть окно отмера всем живым. Зовётся и у авторитета из
        /// <see cref="EnterMeasure"/>, и на клиенте по пришедшей стадии:
        /// без открытого окна хозяин кнопки не смог бы её нажать, а чужие
        /// лампы не загорелись бы вовсе.
        /// </summary>
        private void OpenMeasureWindows(bool armBots)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (!contestants[i].Alive || contestants[i].Button == null)
                {
                    continue;
                }

                contestants[i].Button.OpenWindow();
                if (armBots)
                {
                    contestants[i].Bot?.Arm(currentTarget);
                }
            }
        }

        /// <summary>Погасить все кнопки разом — момент чужого «стопа» так и остаётся невидимым.</summary>
        private void CloseMeasureWindows()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].Button?.CloseWindow();
            }
        }

        /// <summary>
        /// Стадия отмера кончается досрочно, как только все живые нажали «стоп».
        /// Без этого подраунд с целью 4 с стоит полных 15 секунд ожидания,
        /// и мини-игра на восемь подраундов растягивается вдвое против
        /// заявленных двух с половиной минут.
        /// </summary>
        private void HandleButtonStopped(CageButton button, float seconds)
        {
            if (!HasAuthority || stageState.Stage != StageMeasure)
            {
                return;
            }

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Alive)
                {
                    continue;
                }

                if (c.Button == null || !c.Button.Completed)
                {
                    return;
                }
            }

            stageState.EndStageNow();
        }

        private void EnterResults()
        {
            entries.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Alive)
                {
                    continue;
                }

                c.Completed = c.Button != null && c.Button.Completed;
                c.Measured = c.Button != null ? c.Button.Measured : 0f;
                if (c.Completed)
                {
                    c.TotalDeviation += Mathf.Abs(c.Measured - currentTarget);
                }

                entries.Add(new StopwatchEntry
                {
                    PlayerId = c.Session.Id,
                    Seconds = c.Measured,
                    Completed = c.Completed
                });
            }

            CloseMeasureWindows();

            int alive = AliveCount;
            StopwatchRanking.SortWorstFirst(entries, currentType, currentTarget);
            StopwatchRanking.PickFaulted(entries, currentType, currentTarget,
                config.GetWorstCount(startingPlayers, alive), config.TwoPlayerTieThreshold, faulted);

            for (int i = 0; i < faulted.Count; i++)
            {
                Contestant c = Find(faulted[i]);
                if (c == null)
                {
                    continue;
                }

                c.Errors++;
                c.FaultedThisSubround = true;
            }

            // Результаты появляются только здесь и сразу у всех: иначе поздно
            // нажимающие подстроились бы под уже известные цифры.
            PublishBoard(true);
            stageState.EnterStage(StageResults, config.ResultsSeconds);
        }

        private void EnterDescend()
        {
            bool anyDescends = false;
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Alive || !c.FaultedThisSubround || c.Cage == null)
                {
                    continue;
                }

                c.Cage.DescendTo(Mathf.Max(0, errorLimit - c.Errors), config.CageDescendSeconds);
                anyDescends = true;
            }

            stageState.EnterStage(StageDescend, anyDescends ? config.CageDescendSeconds : 0f);
        }

        /// <summary>
        /// Открытие створок — только тем, кто исчерпал лимит. Если таких нет,
        /// стадия проходит мгновенно: лишние две секунды на каждом подраунде
        /// съели бы четверть заявленной длительности матча.
        /// </summary>
        private void EnterHatch()
        {
            eliminatedThisSubround.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Alive || c.Errors < errorLimit)
                {
                    continue;
                }

                c.Alive = false;
                c.InPit = true;
                eliminatedThisSubround.Add(c.Session.Id);
                c.Cage?.OpenDoors(config.HatchOpenSeconds);
            }

            if (eliminatedThisSubround.Count > 0)
            {
                ranking.AddEliminationGroup(eliminatedThisSubround);
            }

            stageState.EnterStage(StageHatch, eliminatedThisSubround.Count > 0 ? config.HatchOpenSeconds : 0f);
        }

        /// <summary>
        /// Шаг медведя. Забег в яме идёт параллельно: следующий подраунд
        /// стартует сразу, а погоня доигрывается внизу фоном. При восьми
        /// игроках и двух вылетающих блокирующая сцена добавляла бы по 5–9 с
        /// к каждому подраунду против заявленных 2,5 минут на всю игру.
        ///
        /// В фазе 3 этот Update уйдёт целиком за IsServer — медведя двигает
        /// только сервер, остальные получают позицию через NetworkTransform.
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

        /// <summary>Есть ли кто-то на последней ступени — медведю есть кого пугать.</summary>
        private bool SomeoneOnLowestCage()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Alive && contestants[i].Cage != null && contestants[i].Cage.Level == 0)
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

        private readonly List<SessionPlayer> aliveBuffer = new List<SessionPlayer>(8);

        private IReadOnlyList<SessionPlayer> CollectAlivePlayers()
        {
            aliveBuffer.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Alive)
                {
                    aliveBuffer.Add(contestants[i].Session);
                }
            }

            return aliveBuffer;
        }

        // ========== СЕТЬ ==========

        /// <summary>
        /// Стадия началась — и у авторитета, и на клиенте, куда её принёс
        /// <see cref="StopwatchNetwork"/>.
        /// </summary>
        private void HandleStageStarted(byte started)
        {
            // Флаг общий для всех машин: он решает, публикуются ли замеры
            // и показывает ли табло время.
            if (started == StageResults)
            {
                resultsRevealed = true;
            }

            // У авторитета окна кнопок ставят сами Enter*-методы. Второй раз —
            // значит переоткрыть окно и обнулить уже сделанный замер.
            if (HasAuthority)
            {
                return;
            }

            switch (started)
            {
                case StageMeasure:
                    OpenMeasureWindows(false);
                    break;
                case StageResults:
                    CloseMeasureWindows();
                    break;
            }
        }

        /// <summary>Сколько участников в матче. Читает <see cref="StopwatchNetwork"/>.</summary>
        public int ContestantCount => contestants.Count;

        /// <summary>
        /// Снимок состояния клетки для репликации.
        ///
        /// Замер и признак «успел» уходят наружу только после
        /// <see cref="resultsRevealed"/>. До него в сетевом состоянии их нет
        /// физически — это единственный способ удержать §5.1 спеки против
        /// клиента, который читает состояние напрямую.
        /// </summary>
        public bool TryGetCageState(int index, out CageNetState state)
        {
            state = default;
            if (index < 0 || index >= contestants.Count)
            {
                return false;
            }

            Contestant c = contestants[index];
            state = new CageNetState
            {
                PlayerId = c.Session.Id,
                Errors = (byte)Mathf.Clamp(c.Errors, 0, byte.MaxValue),
                Level = (byte)(c.Cage != null ? Mathf.Clamp(c.Cage.Level, 0, byte.MaxValue) : 0),
                Lit = c.Button != null && c.Button.WindowOpen && c.Button.State != CageButton.ButtonState.Idle,
                Alive = c.Alive,
                Faulted = c.FaultedThisSubround,
                Measured = resultsRevealed ? c.Measured : 0f,
                Completed = resultsRevealed && c.Completed
            };

            return true;
        }

        /// <summary>Задание подраунда пришло из сети. Весь рандом отыгран на сервере.</summary>
        public void ApplyNetworkTask(int netSubround, StopwatchSubroundType type, float target, float tickPeriod)
        {
            if (HasAuthority)
            {
                return;
            }

            subround = netSubround;
            resultsRevealed = false;
            currentType = type;
            currentTarget = target;
            currentTickPeriod = tickPeriod;

            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].Measured = 0f;
                contestants[i].Completed = false;
                contestants[i].FaultedThisSubround = false;
            }

            distractions?.BeginSubround(currentTickPeriod, config.GetDistractionIntensity(subround));
            scoreboard?.ShowTask(subround, currentType, currentTarget);
        }

        /// <summary>Состояние одной клетки пришло из сети.</summary>
        public void ApplyNetworkCage(int playerId, int errors, int level, bool lit,
            bool alive, bool faulted, float measured, bool completed)
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

            c.Errors = errors;
            c.Alive = alive;
            c.FaultedThisSubround = faulted;
            c.Measured = measured;
            c.Completed = completed;

            // Чужая лампа горит по серверному состоянию: своя кнопка на этой
            // машине зажигается сама, ей круг через сеть не нужен.
            if (c.Button != null && !c.LocallyControlled)
            {
                c.Button.NetworkLit = lit;
            }

            // Пока клетка едет, высоту считает её собственная анимация —
            // подменять её присланным уровнем значило бы дёргать пассажира.
            if (c.Cage != null && !c.Cage.Descending && c.Cage.Level != level)
            {
                c.Cage.SnapToLevel(level);
            }
        }

        /// <summary>Состояния клеток приехали целиком — перерисовать табло один раз.</summary>
        public void ApplyNetworkCagesCommitted()
        {
            if (HasAuthority)
            {
                return;
            }

            PublishBoard(resultsRevealed);
        }

        private Contestant Find(int playerId)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Session.Id == playerId)
                {
                    return contestants[i];
                }
            }

            return null;
        }

        private void PublishBoard(bool showTimes)
        {
            if (scoreboard == null || !scoreboard.HasBoard)
            {
                return;
            }

            boardNames.Clear();
            boardTimes.Clear();
            boardCompleted.Clear();
            boardErrors.Clear();
            boardAlive.Clear();
            boardFaulted.Clear();

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                boardNames.Add(c.Session.DisplayName);
                boardTimes.Add(c.Measured);
                boardCompleted.Add(c.Completed);
                boardErrors.Add(c.Errors);
                boardAlive.Add(c.Alive);
                boardFaulted.Add(c.FaultedThisSubround);
            }

            scoreboard.ShowPlayers(boardNames, boardTimes, boardCompleted, boardErrors, boardAlive, boardFaulted, showTimes);
        }

        /// <summary>
        /// Места по порядку вылета. Несколько выживших даёт только жёсткий
        /// таймаут: меньше ошибок — лучше, при равенстве — меньше сумма
        /// отклонений за весь матч, при полном равенстве — одно место на всех.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                if (contestants[i].Alive)
                {
                    ranking.AddSurvivor(contestants[i].Session.Id);
                }
            }

            ranking.Build(results, CompareSurvivors);
        }

        private int CompareSurvivors(int a, int b)
        {
            Contestant left = Find(a);
            Contestant right = Find(b);
            if (left == null || right == null)
            {
                return 0;
            }

            int byErrors = left.Errors.CompareTo(right.Errors);
            if (byErrors != 0)
            {
                return byErrors;
            }

            return left.TotalDeviation.CompareTo(right.TotalDeviation);
        }
    }
}
