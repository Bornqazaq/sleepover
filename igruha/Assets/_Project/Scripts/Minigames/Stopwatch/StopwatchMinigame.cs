using System.Collections.Generic;
using UnityEngine;
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

        private System.Random random;
        private int startingPlayers;
        private int errorLimit;
        private int subround;
        private StopwatchSubroundType currentType;
        private float currentTarget;
        private bool matchOver;

        /// <summary>Тип текущего подраунда. Наружу — отладочным болванкам соло-прогона.</summary>
        public StopwatchSubroundType CurrentType => currentType;

        /// <summary>Цель текущего подраунда, с. Наружу — отладочным болванкам соло-прогона.</summary>
        public float CurrentTarget => currentTarget;

        /// <summary>Номер текущего подраунда.</summary>
        public int Subround => subround;

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
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (stageState != null)
            {
                stageState.StageElapsed += HandleStageElapsed;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (stageState != null)
            {
                stageState.StageElapsed -= HandleStageElapsed;
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
                    if (!humanControlled && avatar != null)
                    {
                        contestant.Bot = avatar.gameObject.AddComponent<StopwatchDebugBot>();
                        contestant.Bot.Bind(contestant.Button, avatar, Players[i].Id * 7919);
                    }
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
            }

            stageState?.StopSequence();
            scoreboard?.Clear();
        }

        // ========== ПОДРАУНД ==========

        private void BeginSubround()
        {
            subround++;
            currentType = config.GetSubroundType(subround);
            currentTarget = PickTarget(subround);

            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].Measured = 0f;
                contestants[i].Completed = false;
                contestants[i].FaultedThisSubround = false;
            }

            scoreboard?.ShowTask(subround, currentType, currentTarget);
            PublishBoard(false);
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
            for (int i = 0; i < contestants.Count; i++)
            {
                if (!contestants[i].Alive || contestants[i].Button == null)
                {
                    continue;
                }

                contestants[i].Button.OpenWindow();
                contestants[i].Bot?.Arm(currentTarget);
            }

            stageState.EnterStage(StageMeasure, config.MeasureWindowSeconds);
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

            // Кнопки гаснут у всех одновременно — момент чужого «стопа»
            // так и остаётся невидимым.
            for (int i = 0; i < contestants.Count; i++)
            {
                contestants[i].Button?.CloseWindow();
            }

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
                eliminatedThisSubround.Add(c.Session.Id);
                c.Cage?.OpenDoors(config.HatchOpenSeconds);
            }

            if (eliminatedThisSubround.Count > 0)
            {
                ranking.AddEliminationGroup(eliminatedThisSubround);
            }

            stageState.EnterStage(StageHatch, eliminatedThisSubround.Count > 0 ? config.HatchOpenSeconds : 0f);
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
