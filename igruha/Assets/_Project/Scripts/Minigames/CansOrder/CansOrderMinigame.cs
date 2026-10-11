using Igruha.Core.UI;
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
    /// Правила «Порядка банок»: один раунд, худший результат хода опускает клетку.
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
    public sealed class CansOrderMinigame : MinigameControllerBase, IMinigameNetworkTarget
    {
        /// <summary>Стадии круга. Значения уезжают в сеть байтом, порядок менять нельзя.</summary>
        private const byte StageBriefing = 1;
        private const byte StagePlacement = 2;
        /// <summary>
        /// Показ результатов. <b>Публичная</b>, потому что на неё вешается
        /// вспышка табло (<see cref="CansOrderEffects"/>): эффект обязан знать
        /// ту же стадию, что и правила, а не свою копию числа.
        /// </summary>
        public const byte StageReveal = 3;
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
        [SerializeField] private PitBear secondBear;
        public int BearCount => secondBear != null ? 2 : bear != null ? 1 : 0;
        public PitBear GetBear(int index) => index == 0 ? bear : index == 1 ? secondBear : null;
        [SerializeField] private CircusAttackPresentation attackPresentation;
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
            public CircusKnockout Elimination;
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
            public bool ShelfLocked, MovementWasLocked;
            public double HatchStartedAt;

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
        private bool waitingForPitFinale;
        private float finaleClearAt = -1f;

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
        private CansShelfVisibility shelfVisibility;
        private CircusFinaleGate finaleGate;

        private bool LocalKnockoutPresenting()
        {
            foreach (var c in contestants)
                if (c.LocallyControlled && c.Session?.Avatar != null && c.Elimination != null && c.Elimination.IsPresenting)
                    return true;
            return false;
        }

        void IMinigameNetworkTarget.ApplyPhase(MinigamePhase next)
        {
            finaleGate ??= new CircusFinaleGate(base.ApplyPhase, base.ApplyResults);
            finaleGate.ReceivePhase(next, !HasAuthority && LocalKnockoutPresenting(), Time.realtimeSinceStartup);
        }

        void IMinigameNetworkTarget.ApplyResults(MinigameResults results, bool seriesFinal)
        {
            finaleGate ??= new CircusFinaleGate(base.ApplyPhase, base.ApplyResults);
            finaleGate.ReceiveResults(results, seriesFinal, !HasAuthority && LocalKnockoutPresenting(), Time.realtimeSinceStartup);
        }

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
        public float PuzzleSecondsLeft => round.Deadline <= 0 ? config.RoundSeconds : Mathf.Clamp((float)(round.Deadline - NetworkClock.Now), 0f, config.RoundSeconds);
        public float PuzzleDuration => config.RoundSeconds;

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
            shelfVisibility = new CansShelfVisibility(cameraController, shelfCameraRig);
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
            finaleGate?.Cancel();
            // Scene cancellation/disconnection does not necessarily deliver Results.
            OnRoundEnded();
            shelfVisibility?.Dispose();
            base.OnDisable();
            if (stageState != null)
            {
                stageState.StageStarted -= HandleStageStarted;
                stageState.StageElapsed -= HandleStageElapsed;
            }
        }

        protected override void OnPlayersReady()
        {
            finaleGate?.Cancel();
            shelfVisibility?.Dispose();
            if (config == null || arenaConfig == null)
            {
                Debug.LogError($"{name}: не назначены CansOrderConfig / CircusArenaConfig — играть нечем", this);
                return;
            }

            foreach (var previous in contestants) CircusAnimationCullingScope.Release(previous.Session?.Avatar);
            contestants.Clear();
            ranking.Clear();
            matchOver = false;
            waitingForPitFinale = false;
            finaleClearAt = -1f;
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

            for (int bearIndex = 0; bearIndex < BearCount; bearIndex++)
            {
                PitBear bear = GetBear(bearIndex);
                if (bearConfig != null)
                {
                    bearConfig.Apply(bear, arenaConfig.PitRadius);
                }
                else
                {
                    Debug.LogError($"{name}: не назначен CircusBearConfig — медведь останется на своих заготовочных числах", this);
                }

                bear.CaughtByBear -= HandleBearCaught;
                bear.CaughtByBear += HandleBearCaught;
                bear.ConfigureCompanion(GetBear(1 - bearIndex), bearIndex);
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
                if (contestant.LocallyControlled) shelfVisibility?.Bind(avatar.transform);

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
                    contestant.Elimination = avatar.GetComponent<CircusKnockout>();
                    if (contestant.Elimination == null)
                    {
                        contestant.Elimination = avatar.gameObject.AddComponent<CircusKnockout>();
                    }

                    contestant.Elimination.BodyHidden += HandleBodyHidden;
                    if (contestant.LocallyControlled) attackPresentation?.Bind(avatar, contestant.Session.Id);
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

            Timer?.StopTimer();
            BeginRound();
        }

        protected override void OnRoundEnded()
        {
            shelfVisibility?.SetActive(false);
            SetShelfMovementLock(false);
            attackPresentation?.ResetPresentation();
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
                CircusAnimationCullingScope.Release(c.Session?.Avatar);
            }

            for (int bearIndex = 0; bearIndex < BearCount; bearIndex++)
            {
                PitBear bear = GetBear(bearIndex);
                bear.CaughtByBear -= HandleBearCaught;
            }

            spectator?.Deactivate();

            // Строго ПОСЛЕ наблюдателя: Deactivate() возвращает камеру туда,
            // где она была на момент его включения, — а это вполне мог быть
            // риг полки. Вернём кадр своему персонажу последним словом.
            RestoreCameraToLocalAvatar();

            stageState?.StopSequence();
            scoreboard?.Clear();
            // Core disables input before this callback. Restoring the fallen
            // avatar and spectator must not reopen controls over Results.
            var localAvatar = SessionScoreboard.Current?.LocalPlayer?.Avatar;
            if (Phase == MinigamePhase.Results && localAvatar != null &&
                localAvatar.TryGetComponent(out PlayerInputReader reader) && reader.LocallyControlled)
                reader.enabled = false;
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

            round.Round = 1;
            round.Deadline = NetworkClock.Now + config.BriefingSeconds + config.RoundSeconds;
            round.Circle = BriefingCircle;
            round.AliveAtStart = alive;
            round.CanCount = config.GetCanCount(alive);
            round.Quota = 0;
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
                c.Entry.BottomChances = config.BottomChances;
                c.Entry.EliminatedCircle = 0;
                c.Entry.Penalty = CansOrderPenalty.None;
                c.HatchStartedAt = 0;
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

        /// <summary>Apply this turn's one-step penalty or open an exhausted bottom cage.</summary>
        private void DescendCages()
        {
            double startedAt = NetworkClock.Now;
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Entry.Penalty == CansOrderPenalty.Dropped && !c.InPit)
                    BeginPitFall(c, startedAt);
                else if (c.Entry.Alive && c.Entry.Penalty == CansOrderPenalty.Descended)
                    c.Cage?.MoveToFraction(c.Entry.HeightFraction, config.CageDescendSeconds, startedAt);
            }
        }

        private void BeginCircle()
        {
            if (PuzzleSecondsLeft <= 0f) { FinishPuzzle(); return; }
            round.Circle++;
            resultsRevealed = false;

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                c.Entry.SolvedThisCircle = false;
                c.Entry.Penalty = CansOrderPenalty.None;
                if (c.Entry.Solved || !c.Entry.Alive) continue;
                c.Entry.Confirmed = false;
                c.Entry.Matches = 0;
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

            stageState.BeginSubround(round.Circle, StagePlacement, Mathf.Min(config.PlacementWindowSeconds, PuzzleSecondsLeft));
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
        public string BuildLocalDangerText()
        {
            Contestant local = FindLocal();
            if (local == null || !local.Entry.Alive || local.Entry.Solved) return string.Empty;
            int level = Mathf.RoundToInt(local.Entry.HeightFraction * arenaConfig.MaxLevelSteps);
            return level > 0
                ? "ДО НИЖНЕГО УРОВНЯ: " + level + "\nХудший результат хода — на уровень вниз"
                : "НИЖНИЙ УРОВЕНЬ • ШАНСЫ: " + local.Entry.BottomChances + " / " + config.BottomChances
                    + "\nШанс теряется только при худшем результате";
        }

        /// <summary>Typed local presentation, with the same reveal gate as the text HUD.</summary>
        public bool TryGetLocalHudState(out CansOrderEntry entry, out int levels, out int chanceLimit,
            List<int> shownOrder, bool previous, out bool hasPrevious, out bool confirmed, out int matches)
        {
            var local=FindLocal();entry=default;levels=0;chanceLimit=config!=null?config.BottomChances:0;
            hasPrevious=false;confirmed=false;matches=0;shownOrder.Clear();
            if(local==null)return false;
            entry=local.Entry;levels=arenaConfig.MaxLevelSteps;
            // Local confirmation is public to its author; score is not.
            if(!resultsRevealed){entry.Matches=0;entry.SolvedThisCircle=false;}
            if(previous)
            {
                hasPrevious=lastCircleRound==round.Round && lastCircleNumber>=0;
                confirmed=hasPrevious && lastCircleConfirmed;matches=confirmed?lastCircleMatches:0;
                if(confirmed)shownOrder.AddRange(lastCircleArrangement);
            }
            else if(resultsRevealed)
            {
                confirmed=entry.Confirmed;matches=entry.Matches;
                if(confirmed && !entry.Solved)shownOrder.AddRange(local.Submitted);
            }
            return true;
        }

        private static string DescentMessage(CansOrderEntry entry)
        {
            switch (entry.Penalty)
            {
                case CansOrderPenalty.Descended:
                    return entry.HeightFraction <= 0 ? "ХУДШИЙ РЕЗУЛЬТАТ — НИЖНИЙ УРОВЕНЬ\nОсталось два шанса"
                        : "ХУДШИЙ РЕЗУЛЬТАТ — НА УРОВЕНЬ НИЖЕ";
                case CansOrderPenalty.LastChance: return "ХУДШИЙ РЕЗУЛЬТАТ — ПОСЛЕДНИЙ ШАНС";
                case CansOrderPenalty.Dropped: return "ШАНСЫ ИСЧЕРПАНЫ — ЛЮК ОТКРЫВАЕТСЯ";
                default: return "КЛЕТКА ОСТАЁТСЯ НА МЕСТЕ";
            }
        }

        public string LocalWaitHint()
        {
            Contestant local = FindLocal();
            if (local == null || !local.Entry.Alive)
            {
                return string.Empty;
            }

            if (local.Entry.Solved)
            {
                return "ТЫ СОБРАЛ РАССТАНОВКУ — клетка в безопасности";
            }

            return local.Entry.Confirmed
                ? "РАССТАНОВКА ПРИНЯТА — ждём остальных"
                : string.Empty;
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
                return "<size=40>ТЫ УЖЕ СОБРАЛ — клетка в безопасности</size>";
            }

            if (!local.Entry.Confirmed)
            {
                return "<size=40>НЕ ПОДТВЕРДИЛ</size>\n<size=26>" + DescentMessage(local.Entry) + "</size>";
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
                .Append(" из ").Append(local.Submitted.Count).Append("</b></size>\n<size=26>")
                .Append(DescentMessage(local.Entry)).Append("</size>");
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
            if (stage != StagePlacement) SetShelfMovementLock(false);
            if (stage == StagePlacement)
            {
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
                    SetShelfMovementLock(true);
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
        private void SetShelfMovementLock(bool locked)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                var player = c.Session.Avatar;
                if (player == null) continue;
                bool shouldLock = locked && c.Entry.Alive && !c.Entry.Solved;
                if (shouldLock && !c.ShelfLocked)
                {
                    c.MovementWasLocked = player.MovementLocked;
                    c.ShelfLocked = true;
                    player.MovementLocked = true;
                }
                else if (!shouldLock && c.ShelfLocked)
                {
                    c.ShelfLocked = false;
                    player.MovementLocked = c.MovementWasLocked;
                }
            }
        }

        private void ApplyShelfCamera(bool toShelf)
        {
            if ((attackPresentation != null && attackPresentation.OwnsCamera) || cameraController == null ||
                spectator == null || spectator.IsActive)
            {
                shelfVisibility?.SetActive(false);
                return;
            }

            Contestant local = FindLocal();
            if (local == null || local.Session?.Avatar == null)
            {
                shelfVisibility?.SetActive(false);
                return;
            }

            if (!toShelf || local.Shelf == null || !local.Entry.Alive || local.Entry.Solved)
            {
                shelfVisibility?.SetActive(false);
                cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
                return;
            }

            Transform board = local.Shelf.Board;
            if (board == null)
            {
                shelfVisibility?.SetActive(false);
                cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
                return;
            }

            if (shelfCameraRig == null)
            {
                shelfVisibility?.SetActive(false);
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
            shelfVisibility?.SetActive(true);
        }

        /// <summary>
        /// Вернуть камеру своему персонажу перед выходом из мини-игры.
        ///
        /// Без этого камера уезжает в хаб в режиме <see cref="CameraMode.Fixed"/>,
        /// нацеленная на <c>shelfCameraRig</c> — объект ЭТОЙ сцены, который
        /// умирает вместе с ней. Цель становится null, и кадр висит там, где
        /// риг стоял в последний момент, пока хаб не выставит свой: игрок
        /// видит рывок на входе в лобби (IGR-375).
        ///
        /// Замороженного не касается: меняется не риг и не его настройки,
        /// а только то, на что смотрит <see cref="MinigameCameraController"/>.
        /// </summary>
        private void RestoreCameraToLocalAvatar()
        {
            shelfVisibility?.SetActive(false);
            if (cameraController == null)
            {
                return;
            }

            Contestant local = FindLocal();
            if (local?.Session?.Avatar == null)
            {
                return;
            }

            cameraController.Apply(CameraMode.ThirdPerson, local.Session.Avatar.transform);
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
                    if (NotSolvedCount == 0 || PuzzleSecondsLeft <= 0f)
                    {
                        FinishPuzzle();
                        return;
                    }
                    stageState.EnterStage(StagePause, Mathf.Min(config.PauseSeconds, PuzzleSecondsLeft));
                    break;

                case StagePause:
                    BeginCircle();
                    break;

                case StageHatch:
                    waitingForPitFinale = true;
                    TryFinishPitFinale();
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
            candidates.Clear();
            for (int i = 0; i < contestants.Count; i++) candidates.Add(contestants[i].Entry);
            int worst = CanOrderDescentRules.WorstScore(candidates);
            for (int i = 0; i < contestants.Count; i++)
                CanOrderDescentRules.Apply(ref contestants[i].Entry, worst, arenaConfig.MaxLevelSteps, round.Circle);
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
            round.Quota = 0;

            Debug.Log($"🚪 Состав изменился: живых {alive}, квота вылета {round.Quota}, " +
                      $"собрать до конца раунда {Mathf.Max(0, alive - round.Quota)}", this);
        }

        /// <summary>
        /// Кто-то собрал расстановку — фанфара пошла над его клеткой.
        ///
        /// Единственная воронка на все машины: сервер приходит сюда из разбора
        /// круга, клиент — через <see cref="ApplyNetworkSolvedFanfare"/>.
        /// Своего состояния событие не заводит, поэтому звук и эффект
        /// подписываются на него без единого пакета ради себя.
        /// </summary>
        public event System.Action<Vector3> SolvedFanfarePlayed;

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
            SolvedFanfarePlayed?.Invoke(at);
        }


        /// <summary>Close submissions while preserving every surviving cage.</summary>
        private void FinishPuzzle()
        {
            // The time limit preserves surviving cages and their remaining chances.
            stageState.EnterStage(StageHatch, 0f);
        }

        private void BeginPitFall(Contestant c, double startedAt)
        {
            c.HatchStartedAt = startedAt;
            c.InPit = true;
            CircusAnimationCullingScope.Bind(c.Session.Avatar);
            if (c.LocallyControlled) attackPresentation?.BeginPit();
            for (int b = 0; b < BearCount; b++) GetBear(b).RegisterFallen(c.Session.Avatar);
            c.Bot?.Disarm();
            c.Shelf?.Release();
            c.Button?.CloseWindow();
            c.Cage?.OpenDoors(config.HatchOpenSeconds);
        }

        /// <summary>Добавить ушедших в группу вылета этого раунда, не задваивая уже попавших.</summary>
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

            // 1. Сервер ещё принимает ответы этого круга. После ResolveCircle
            //    поздний пакет уже нельзя включить в опубликованный счёт.
            if (Stage != StagePlacement || placementWindowCircle != round.Circle || arrival > placementWindowEnd)
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

            // Порядок финиша определяет сервер, клиент не может прислать более раннее время.
            c.Entry.ConfirmTime = arrival;

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

            // Все, кому ещё было что подтверждать, подтвердили — дальше окно
            // тикает впустую, и люди просто сидят и смотрят на таймер каждый
            // круг (IGR-373). Закрываем стадию досрочно.
            //
            // Решает только сервер: EndStageNow сам уходит по !HasAuthority,
            // но мы и так под ним — вся эта функция серверная.
            if (stageState != null && stageState.Stage == StagePlacement && AllAliveConfirmed())
            {
                stageState.EndStageNow();
            }

            return true;
        }

        /// <summary>
        /// Подтвердили ли уже все, от кого этого вообще ждут.
        ///
        /// Считаются только живые и ещё не собравшие: выбывший подтверждать
        /// не может, отвалившегося по сети в составе уже нет, а собравший
        /// выведен из круга — его полка больше не оживает, и ждать его
        /// значило бы не дождаться никогда.
        ///
        /// Пустой знаменатель даёт <c>false</c>: если ждать вообще некого,
        /// круг закрывает свой обычный ход, а не эта проверка.
        /// </summary>
        private bool AllAliveConfirmed()
        {
            bool anyoneExpected = false;

            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant other = contestants[i];
                if (!other.Entry.Alive || other.Entry.Solved)
                {
                    continue;
                }

                anyoneExpected = true;

                if (!other.Entry.Confirmed)
                {
                    return false;
                }
            }

            return anyoneExpected;
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
        /// Шаг медведя. Финал завершается после погони за неуспевшими.
        ///
        /// В фазе 3 этот <c>Update</c> уйдёт целиком за <c>IsServer</c> — медведя
        /// двигает только сервер, остальные получают позицию через
        /// <c>NetworkTransform</c>.
        /// </summary>
        private void Update()
        {
            finaleGate?.Tick(LocalKnockoutPresenting(), Time.realtimeSinceStartup);
            // The puzzle owns its deadline and waits for the pit finale. Feed the
            // shared HUD the same remaining time without enabling automatic round end.
            if (Phase == MinigamePhase.Round && Timer != null && config != null)
            {
                Timer.SyncFromNetwork(PuzzleSecondsLeft, config.RoundSeconds);
                Timer.StopTimer();
            }
            // Наблюдатель держит список живых ПО ССЫЛКЕ и перечитывает его
            // каждый кадр — так написано в его собственном контракте. Отдать
            // снимок нельзя: он устаревает на первой же смерти, дальше цель
            // перестаёт быть watchable, обход не находит в снимке никого
            // живого и уходит в «живых не осталось» — камера замирает
            // на последней позиции посреди раунда (IGR-374).
            //
            // Обновляем ДО проверки авторитета: выбывает человек на своей
            // машине, а не на сервере, и наблюдатель нужен каждому.
            if (spectator != null && spectator.IsActive)
            {
                CollectAlivePlayers();
            }

            if (bear == null || !HasAuthority || !Phase.IsGameplay())
            {
                return;
            }

            bool lowest = SomeoneOnLowestCage();
            for (int bearIndex = 0; bearIndex < BearCount; bearIndex++)
            {
                PitBear pursuer = GetBear(bearIndex);
                pursuer.Tick(Time.deltaTime, FindNearestInPit(pursuer), lowest);
                int targetId = -1;
                var target = pursuer.AttackVictim != null ? pursuer.AttackVictim : pursuer.Target;
                for (int i = 0; i < contestants.Count; i++)
                    if (contestants[i].Session.Avatar == target && target != null) targetId = contestants[i].Session.Id;
                pursuer.PresentationTargetId = targetId;
                network?.PublishBearState((byte)pursuer.State, targetId, bearIndex);
            }
            if (waitingForPitFinale) TryFinishPitFinale();
        }

        /// <summary>Ближайшая к медведю жертва среди упавших в яму.</summary>
        // In a two-player game the old hatch timer ended the whole minigame
        // before landing, head start and attack could happen. Keep the final
        // chase playable, then let the loser reach spectator before results.
        private void TryFinishPitFinale()
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Session.Avatar != null && (c.InPit || (c.Elimination != null && c.Elimination.IsPresenting)))
                {
                    finaleClearAt = -1f;
                    return;
                }
            }
            if (finaleClearAt < 0) finaleClearAt = Time.time + 1f;
            if (Time.time < finaleClearAt) return;
            waitingForPitFinale = false;
            matchOver = true;
            EndMinigame();
        }

        private PlayerController FindNearestInPit(PitBear bear)
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

                if (!bear.CanChase(c.Session.Avatar, Time.deltaTime))
                {
                    continue;
                }

                // Keep a chosen runner: two nearby players must not reset the head start every frame.
                if (bear.Target == c.Session.Avatar)
                {
                    return c.Session.Avatar;
                }

                float sqr = (c.Session.Avatar.transform.position - bearPosition).sqrMagnitude;
                if (bear.Companion != null && bear.Companion.Target == c.Session.Avatar) sqr += 400f;
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
        private void HandleBearCaught(PitBear bear, PlayerController victim, Vector3 impulse)
        {
            for (int i = 0; i < contestants.Count; i++)
            {
                Contestant c = contestants[i];
                if (c.Session.Avatar != victim || !c.InPit)
                {
                    continue;
                }

                c.InPit = false;
                for (int b = 0; b < BearCount; b++) GetBear(b).ForgetRunner(victim);
                if (c.LocallyControlled) attackPresentation?.UseBear(bear);
                Vector3 hitPoint = victim.Position;
                Vector3 bearPosition = bear.transform.position;
                float bearYaw = bear.transform.eulerAngles.y;
                CircusKnockout.CaptureContact(victim, impulse, out KnockdownType fallType, out float contactYaw);
                c.Elimination?.Eliminate(hitPoint, impulse, fallType, contactYaw);
                bear.ShowImpact(hitPoint + Vector3.up, false);
                c.Elimination?.TraceImpact(bear);

                // Тип падения и разворот выбираются сервером один раз:
                // предсказанный Facing владельца к приходу RPC уже может отличаться.
                network?.AnnounceCaught(c.Entry.PlayerId, hitPoint, impulse, (byte)fallType, contactYaw, bearPosition, bearYaw, bear == secondBear ? 1 : 0);
                return;
            }
        }

        /// <summary>Тело исчезло — выбывший переходит в наблюдатели.</summary>
        private void HandleBodyHidden(CircusKnockout elimination)
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
                FinishTime = c.Entry.Solved ? c.Entry.ConfirmTime : 0,
                HeightFraction = c.Entry.HeightFraction,
                BottomChances = (byte)c.Entry.BottomChances,
                Penalty = (byte)c.Entry.Penalty,
                EliminatedCircle = c.Entry.EliminatedCircle,
                HatchStartedAt = c.HatchStartedAt,
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
            for (int b = 0; b < BearCount; b++) GetBear(b).ForgetRunner(c.Session.Avatar);

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
            if (c.LocallyControlled) shelfVisibility?.Dispose();
            CircusAnimationCullingScope.Release(c.Session?.Avatar);
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
            int quota, int aliveAtStart, int solvedCount, double deadline = 0)
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
            round.Deadline = deadline;

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
            c.Entry.BottomChances = state.BottomChances;
            c.Entry.Penalty = (CansOrderPenalty)state.Penalty;
            c.Entry.EliminatedCircle = state.EliminatedCircle;
            c.HatchStartedAt = state.HatchStartedAt;
            if (state.Solved) c.Entry.ConfirmTime = state.FinishTime;

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

        /// <summary>Reproduce the authoritative destination from the shared stage start.</summary>
        private void ApplyNetworkHeight(Contestant c, float fraction)
        {
            if (c.Cage == null || Mathf.Approximately(c.Entry.HeightFraction, fraction))
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
                c.InPit = true;
                CircusAnimationCullingScope.Bind(c.Session.Avatar);
                if (c.LocallyControlled) attackPresentation?.BeginPit();
                // Opening belongs to this cage, not to a global final-stage clock.
                c.Cage.OpenDoors(config.HatchOpenSeconds, (float)System.Math.Max(0, NetworkClock.Now - c.HatchStartedAt));
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

            SetShelfMovementLock(Stage == StagePlacement);
            ApplyShelfCamera(Stage == StagePlacement);
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
            localHud?.RefreshResult();
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
        /// тем же импульсом, типом падения и разворотом, выбранными сервером.
        /// </summary>
        public void ApplyNetworkCaught(int playerId, Vector3 hitPoint, Vector3 impulse, byte fallType, float contactYaw,
            Vector3 bearPosition, float bearYaw, int bearIndex = 0)
        {
            PitBear bear = GetBear(bearIndex);
            if (HasAuthority)
            {
                return;
            }

            // The replicated Attack state starts the swipe before this impact RPC.
            Contestant c = Find(playerId);
            if (c == null)
            {
                return;
            }

            c.InPit = false;
            if (c.LocallyControlled) attackPresentation?.UseBear(bear);
            bear?.SetPresentationTarget(c.Session.Avatar);
            CircusAnimationCullingScope.Bind(c.Session.Avatar);
            c.Elimination?.Eliminate(hitPoint, impulse, (KnockdownType)fallType, contactYaw);
            bear?.ShowImpact(hitPoint + Vector3.up, true, bearPosition, bearYaw);
            c.Elimination?.TraceImpact(bear);
        }

        /// <summary>Состояние медведя пришло из сети — показать, не считая ИИ.</summary>
        public void ApplyNetworkBearState(byte state, float elapsed = 0f, int targetId = -1, int bearIndex = 0)
        {
            PitBear bear = GetBear(bearIndex);
            if (HasAuthority || bear == null || bearConfig == null)
            {
                return;
            }

            bear.PresentationTargetId = targetId;
            bear.SetPresentationTarget(Find(targetId)?.Session.Avatar);
            var next = (PitBear.BearState)state;
            bear.ApplyNetworkState(next, next == PitBear.BearState.Chase
                ? bearConfig.ChaseSpeed
                : (next == PitBear.BearState.Patrol ? bearConfig.PatrolSpeed : 0f), elapsed);
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
        public override string ResultMetricTitle => "ВРЕМЯ";
        public override RoundResultDetail GetResultDetail(int playerId)
        {
            var c = Find(playerId);
            if (c == null) return new RoundResultDetail("—", string.Empty);
            if (!c.Entry.Solved) return new RoundResultDetail(Mathf.Max(0, c.Entry.BestMatches) + " / " + round.CanCount,
                c.Entry.Alive ? "Сохранил клетку" : "Шансы исчерпаны");
            double elapsed = System.Math.Max(0, c.Entry.ConfirmTime - (round.Deadline - config.RoundSeconds));
            return new RoundResultDetail($"{elapsed:F1} с", "Собрал");
        }

        protected override void CollectResults(MinigameResults results)
        {
            results.Clear();
            for (int i = 0; i < contestants.Count; i++)
            {
                int place = 1;
                for (int j = 0; j < contestants.Count; j++)
                    if (CanOrderRanking.CompareFinish(contestants[j].Entry, contestants[i].Entry) < 0) place++;
                results.Add(contestants[i].Entry.PlayerId, place);
            }
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
