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

        [SerializeField] private CansOrderConfig config;
        [SerializeField] private CircusArenaConfig arenaConfig;
        [SerializeField] private CircusBearConfig bearConfig;
        [SerializeField] private MinigameStageState stageState;
        [Tooltip("Клетки арены — все восемь. Лишние гасятся по числу игроков")]
        [SerializeField] private CageStation[] cages = System.Array.Empty<CageStation>();
        [Tooltip("Медведь в яме")]
        [SerializeField] private PitBear bear;
        [Tooltip("Камера наблюдателя — включается выбывшему")]
        [SerializeField] private SpectatorCamera spectator;

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
        }

        private readonly List<Contestant> contestants = new List<Contestant>(8);
        private readonly EliminationRanking ranking = new EliminationRanking();

        private CansOrderRoundState round;
        private bool matchOver;

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

            AssignCages();

            if (bear != null && bearConfig != null)
            {
                bearConfig.Apply(bear, arenaConfig.PitRadius);
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
                c.Elimination?.Restore();
            }

            spectator?.Deactivate();
            stageState?.StopSequence();
        }

        // ========== РАУНД И КРУГИ ==========

        /// <summary>
        /// Начать раунд: пересчитать состав, поднять клетки выживших наверх
        /// и объявить задание. Накопления между раундами нет — высота снова
        /// читается как «положение в этом раунде» (спека 5.5).
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

                c.Button?.ResetForRound();
                c.Shelf?.Build(config, round.CanCount);
            }

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

        private void BeginCircle()
        {
            round.Circle++;

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                c.Entry.Confirmed = false;
                c.Entry.Matches = 0;
                c.Entry.SolvedThisCircle = false;
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
        private void HandleStageStarted(byte stage)
        {
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
                        stageState.EnterStage(StageHatch, config.HatchOpenSeconds);
                        return;
                    }

                    if (round.Circle >= config.RoundCircleCap)
                    {
                        // Потолок кругов — страховка, а не правило: в нормальной
                        // игре до неё не доходит даже вдвоём (спека 6.5).
                        Debug.LogWarning($"{name}: раунд {round.Round} упёрся в потолок {config.RoundCircleCap} кругов", this);
                        stageState.EnterStage(StageHatch, config.HatchOpenSeconds);
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
        private void ResolveCircle()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (!c.Entry.Alive || c.Entry.Solved)
                {
                    continue;
                }

                c.Entry.Attempts++;
            }
        }

        /// <summary>
        /// Раунд заканчивается, как только не собравших осталось не больше
        /// квоты вылета: их места уже определены, и доигрывать нечего
        /// (спека 5.6).
        /// </summary>
        private bool ShouldEndRound() => NotSolvedCount <= round.Quota;

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

            c.Entry.Confirmed = true;
            c.Entry.ConfirmTime = NetworkClock.Now;
            button.MarkAccepted();
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

        /// <summary>
        /// Места по порядку вылета. Считает <see cref="EliminationRanking"/>
        /// из Core: место = сколько игроков стоит выше, плюс один. Оттуда само
        /// собой следует и деление места внутри группы вылета, и сдвиг
        /// следующей группы на размер предыдущей.
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

            ranking.Build(results);
        }
    }
}
