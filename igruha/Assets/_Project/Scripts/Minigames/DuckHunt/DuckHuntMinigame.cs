using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Arena;
using Igruha.Core.CameraSystems;
using Igruha.Core.Combat;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.Traps;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Правила Duck Hunt поверх шаблона: раздача ролей, стартовый отсчёт,
    /// зачёт попаданий и финиша, конец раунда и расстановка мест.
    /// Бег, прыжок, присед, толчки, таймер, обучалка и HUD берутся готовыми
    /// из Igruha.Core.
    ///
    /// Всё, что меняет исход раунда — раздача ролей, смерть Утки, зачёт
    /// финиша, конец раунда — идёт через методы под
    /// <see cref="MinigameControllerBase.HasAuthority"/>, а состояние гонки
    /// живёт в <see cref="DuckProgress"/> на самих Утках, а не в полях,
    /// разбросанных по компонентам. В сетевой фазе эти точки оборачиваются
    /// в серверные вызовы, а прогресс — в реплицируемое состояние.
    /// </summary>
    public sealed class DuckHuntMinigame : MinigameControllerBase
    {
        /// <summary>Ключ истории спец-ролей: своя очередь Охотника, независимая от других игр.</summary>
        private const string HunterRoleKey = "DuckHunt.Hunter";

        /// <summary>Момент свистка ещё не объявлен сервером.</summary>
        public const double NotAnnounced = -1d;

        /// <summary>
        /// Насколько точка выстрела, присланная Охотником, может отстоять от
        /// его же тела на сервере, м. Допуск щедрый намеренно: глаз почти на
        /// полтора метра выше центра капсулы, и ещё столько же берём на
        /// отставание лифта на машине Охотника от серверного.
        /// </summary>
        private const float MuzzleReachTolerance = 3.5f;

        /// <summary>Короче этого присланное направление считается мусором, а не прицелом.</summary>
        private const float MinAimSqrMagnitude = 1e-4f;

        /// <summary>
        /// Сколько ловушек арены реплицируется. Состояние едет битовой маской
        /// в одном числе: тридцать два бита — тридцать две ловушки. У Duck Hunt
        /// их десять, но молча обрезать лишние нельзя — ловушка, о которой сеть
        /// не знает, срабатывает только у сервера.
        /// </summary>
        private const int MaxSyncedTraps = 32;

        private const string GroundLayerName = "Ground";
        private const string CoverLayerName = "Cover";

        /// <summary>Середина коридора по глубине, ШП — по ней ведут болванок.</summary>
        private const float PathDepthWidths = 7f;

        /// <summary>За сколько ШП до лестничной комнаты болванка начинает целиться в первую ступень, а не в коридор.</summary>
        private const float StairApproachWidths = 4f;

        /// <summary>С какого недобора по трассе точка маршрута считается пройденной, ШП.</summary>
        private const float RouteReachWidths = 0.5f;

        /// <summary>Радиус тела на случай, если капсулу у болванки не нашли, м.</summary>
        private const float DefaultBodyRadius = 0.36f;

        /// <summary>Насколько разводятся полосы соседних болванок по глубине коридора, ШП.</summary>
        private const float DuckLaneSpacingWidths = 2f;

        /// <summary>Глубина, на которую болванка сходит с лестницы, ШП. Ближе к открытой грани, чем проём в перекрытии.</summary>
        private const float StairExitDepthWidths = 2f;

        /// <summary>
        /// Порог перешагивания для болванок, доля от высоты прыжка. Держать его
        /// низким важнее, чем кажется: щуп «это стена или ступень» пускается на
        /// этой высоте плюс толщина тела, и завышенный порог упирается в
        /// перекрытие над лестницей — болванка решает, что замурована, и встаёт.
        /// </summary>
        private const float BotStepHeightFactor = 0.35f;

        [Header("Арена")]
        [SerializeField] private DuckHuntConfig config;
        [SerializeField] private DuckHuntArena arena;
        [SerializeField] private SpawnPointSet spawnPoints;
        [Tooltip("Платформа лифта, на которой заперт Охотник")]
        [SerializeField] private RidePlatform elevator;
        [Tooltip("Площадка финиша на крыше")]
        [SerializeField] private DuckHuntFinishZone finishZone;

        [Header("Камеры")]
        [Tooltip("Утки смотрят от третьего лица, Охотник — от первого")]
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Риг первого лица со сцены — ему задаются пределы наклона и горизонтальный обзор")]
        [SerializeField] private FirstPersonCameraRig firstPersonRig;
        [Tooltip("Камера наблюдателя: погибший и дошедший смотрят за оставшимися")]
        [SerializeField] private SpectatorCamera spectator;

        [Header("Ловушки")]
        [Tooltip("Все ловушки арены — сбрасываются на старте раунда")]
        [SerializeField] private TrapBase[] traps = Array.Empty<TrapBase>();
        [Tooltip("Гейзеры: сила подброса считается из высоты в конфиге и массы персонажа")]
        [SerializeField] private SpringTrap[] geysers = Array.Empty<SpringTrap>();

        /// <summary>Интерактор своего аватара — источник подсказки «что нажать».</summary>
        private PlayerInteractor localInteractor;

        /// <summary>Что показано в подсказке сейчас. Нужен, чтобы не пересобирать строку каждый кадр.</summary>
        private IInteractable promptTarget;

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Локальный игрок играет за Охотника, иначе за Утку")]
        [SerializeField] private bool localPlayerIsHunter;
        [Tooltip("Переключить роль локального игрока прямо в игре. Работает только во время раунда. Не F-клавиша: на macOS их перехватывает система")]
        [SerializeField] private Key roleSwitchKey = Key.P;
        [Tooltip("Гнать болванок-Уток по трассе. Выключить — встанут неподвижными мишенями, см. поля ниже")]
        [SerializeField] private bool driveDummyDucks = true;
        [Tooltip("Этаж, на котором встают неподвижные мишени (с нуля). Работает, когда болванок не гонят")]
        [SerializeField] private int targetFloor;
        [Tooltip("Где по трассе встают мишени, ШП. 24 — середина этажа, прямой угол обзора Охотника")]
        [SerializeField] private float targetProgress = 24f;

        [Header("Прицел")]
        [Tooltip("Прицел Охотника: включается только у того, кому выпала эта роль")]
        [SerializeField] private GameObject crosshair;

        /// <summary>
        /// Слепок итога Утки: всё, чем решаются места, и ничего больше.
        ///
        /// Нужен из-за ухода игрока. <see cref="DuckProgress"/> живёт на
        /// аватаре, а аватар вышедшего NGO уничтожает — к моменту подсчёта
        /// мест читать с него было бы обращением к разрушенному объекту.
        /// Поэтому места считаются не с компонента, а отсюда: пока Утка в
        /// игре, слепок зеркалит её прогресс, после выхода остаётся
        /// единственным источником.
        /// </summary>
        private readonly struct DuckOutcome
        {
            public readonly int Floor;
            public readonly float Progress;
            public readonly int FinishOrder;
            public readonly bool Dead;
            public readonly float DeathTime;

            public DuckOutcome(int floor, float progress, int finishOrder, bool dead, float deathTime)
            {
                Floor = floor;
                Progress = progress;
                FinishOrder = finishOrder;
                Dead = dead;
                DeathTime = deathTime;
            }

            /// <summary>Утка добежала. Порядок прибытия начинается с единицы, поэтому ноль и значит «не добежала».</summary>
            public bool Finished => FinishOrder > 0;

            /// <summary>Утка выбыла из гонки — неважно, добежала или погибла.</summary>
            public bool Retired => Finished || Dead;

            public static DuckOutcome From(DuckProgress progress) => new DuckOutcome(
                progress.Floor, progress.Progress, progress.FinishOrder, progress.Dead, progress.DeathTime);

            /// <summary>Тот же итог, но погибшей: этим уход Утки приравнивается к смерти на её точке.</summary>
            public DuckOutcome AsDead(float deathTime) =>
                new DuckOutcome(Floor, Progress, FinishOrder, true, deathTime);
        }

        /// <summary>
        /// Состояние одной Утки за раунд. Сам прогресс живёт в
        /// <see cref="DuckProgress"/> на аватаре — здесь только ссылки,
        /// чтобы не искать компоненты в кадровом цикле.
        /// </summary>
        private sealed class DuckRecord
        {
            public int PlayerId;
            public PlayerController Avatar;
            public DuckProgress Progress;
            public PlayerElimination Elimination;
            /// <summary>Водитель болванки. Пусто у живого игрока, в сетевой катке и когда болванок не гонят.</summary>
            public DebugPlayerBot Bot;

            /// <summary>
            /// Этой копией не управляет человек. Отдельно от <see cref="Bot"/>:
            /// водителя у болванки может не быть вовсе (её попросили стоять
            /// мишенью), а болванкой она от этого быть не перестаёт.
            /// </summary>
            public bool IsDummy;

            /// <summary>
            /// Болванка уже свернула на лестницу. Флаг нужен потому, что второй
            /// марш идёт обратно вдоль трассы: прогресс на нём убывает, и по
            /// одному лишь прогрессу болванка посреди подъёма решила бы, что
            /// снова в коридоре, и пошла бы вниз.
            /// </summary>
            public bool Climbing;

            /// <summary>Этаж, на котором болванка была в прошлый раз: по его смене сбрасывается подъём.</summary>
            public int LastFloor;

            /// <summary>
            /// Ломаная по коридору текущего этажа — по ней болванка обходит
            /// укрытия. Состояние маршрута живёт здесь, а не пересчитывается
            /// из геометрии каждый кадр: пересчёт и был причиной того, что
            /// каждая правка вскрывала новый частный случай.
            /// </summary>
            public readonly List<Vector3> Route = new List<Vector3>(24);

            /// <summary>Номер точки маршрута, к которой идём сейчас.</summary>
            public int RouteIndex;

            /// <summary>
            /// Игрок вышел из матча. Ссылки на аватар и его компоненты с этого
            /// момента пусты — NGO уничтожил тело, — а итог живёт в
            /// <see cref="Frozen"/>.
            /// </summary>
            public bool Left;

            /// <summary>
            /// Последний известный итог. Пока Утка в игре, его обновляет
            /// авторитет в <see cref="SampleProgress"/>, а на остальных машинах —
            /// приезжающее состояние. После выхода игрока не меняется.
            /// </summary>
            public DuckOutcome Frozen;

            /// <summary>
            /// Итог Утки — живой или ушедшей. Единственная точка, откуда его
            /// читают места: обращаться к <see cref="Progress"/> напрямую нельзя,
            /// у ушедшего игрока этого компонента уже нет.
            /// </summary>
            public DuckOutcome Outcome => Left || Progress == null
                ? Frozen
                : DuckOutcome.From(Progress);
        }

        private static readonly Comparison<DuckRecord> DuckRanking = CompareDucks;

        private readonly List<DuckRecord> ducks = new List<DuckRecord>(8);
        private readonly List<SessionPlayer> aliveDucks = new List<SessionPlayer>(8);
        private readonly List<int> placementOrder = new List<int>(8);

        private HunterController hunter;
        private PlayerController hunterAvatar;
        private int hunterPlayerId = SpecialRoleHistory.NoPlayer;

        /// <summary>
        /// Охотник вышел из матча посреди раунда. Роль за ним остаётся —
        /// по ней ему выдаётся место, — но место это последнее (спека, 10.2).
        /// </summary>
        private bool hunterLeft;

        private float roundElapsed;
        private int finishCounter;
        private DuckHuntNetwork network;

        /// <summary>
        /// Обработчики срабатывания ловушек, по одному на ловушку. Держим их
        /// полем, потому что каждый замыкает свой номер: без сохранённой ссылки
        /// от такой подписки потом не отписаться.
        /// </summary>
        private Action[] trapFiredHandlers;

        /// <summary>
        /// Момент свистка на общих часах: с него раунд живой. Объявляет его
        /// авторитет один раз, дальше каждая машина считает отсчёт сама — так он
        /// кончается у всех одновременно и не зависит от пинга.
        /// </summary>
        private double liveTime = NotAnnounced;

        /// <summary>Стартовый отсчёт кончился: Утки бегут, Охотник стреляет.</summary>
        public bool RoundLive { get; private set; }

        /// <summary>Идёт сетевая катка и сетевая половина игры живая.</summary>
        private bool Networked => network != null && network.IsActive;

        /// <summary>Момент свистка объявлен. Пока нет — раунд держит всех на месте.</summary>
        private bool CountdownAnnounced => liveTime >= 0d;

        /// <summary>Сколько осталось до свистка, с. Ноль — отсчёт вышел.</summary>
        private float CountdownRemaining =>
            CountdownAnnounced ? Mathf.Max(0f, (float)(liveTime - NetworkClock.Now)) : 0f;

        /// <summary>Про ожидание свистка уже сказали. Иначе строка шла бы каждый кадр.</summary>
        private bool waitingForWhistleLogged;

        // ========== ЖИЗНЕННЫЙ ЦИКЛ ==========

        protected override void Awake()
        {
            base.Awake();
            network = GetComponent<DuckHuntNetwork>();

            if (traps.Length > MaxSyncedTraps)
            {
                Debug.LogError($"{name}: ловушек {traps.Length}, а реплицируется не больше " +
                               $"{MaxSyncedTraps} — лишние будут срабатывать только у сервера", this);
            }
        }

        protected override void OnPlayersReady()
        {
            AssignRoles();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (finishZone != null)
            {
                finishZone.DuckArrived += HandleDuckArrived;
            }

            SubscribeTraps();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (finishZone != null)
            {
                finishZone.DuckArrived -= HandleDuckArrived;
            }

            UnsubscribeTraps();
        }

        /// <summary>
        /// Подписаться на срабатывание каждой ловушки. Номер ловушки — её место
        /// в массиве сцены: он одинаков на всех машинах, поэтому по нему и
        /// адресуем оповещение.
        /// </summary>
        private void SubscribeTraps()
        {
            if (trapFiredHandlers != null)
            {
                return;
            }

            trapFiredHandlers = new Action[traps.Length];
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] == null)
                {
                    continue;
                }

                int index = i;
                trapFiredHandlers[i] = () => AnnounceTrapFired(index);
                traps[i].Fired += trapFiredHandlers[i];
            }
        }

        private void UnsubscribeTraps()
        {
            if (trapFiredHandlers == null)
            {
                return;
            }

            for (int i = 0; i < traps.Length && i < trapFiredHandlers.Length; i++)
            {
                if (traps[i] != null && trapFiredHandlers[i] != null)
                {
                    traps[i].Fired -= trapFiredHandlers[i];
                }
            }

            trapFiredHandlers = null;
        }

        /// <summary>
        /// Ловушка сработала у авторитета — разослать остальным. Клиент своё
        /// срабатывание не объявляет: у него его и не бывает, решает сервер.
        /// </summary>
        private void AnnounceTrapFired(int index)
        {
            if (!HasAuthority || index >= MaxSyncedTraps)
            {
                return;
            }

            network?.AnnounceTrapFired(index);
        }

        protected override void OnRoundStarted()
        {
            roundElapsed = 0f;
            finishCounter = 0;
            hunterLeft = false;

            // Свисток назначает авторитет, остальные ждут его объявления.
            // Не назначь мы момент здесь — клиент отпустил бы Уток в башню
            // по своему отсчёту, и первые секунды гонки шли бы у него
            // раньше, чем сервер начал их считать.
            liveTime = HasAuthority
                ? NetworkClock.Now + (config != null ? config.StartCountdown : 0f)
                : NotAnnounced;
            waitingForWhistleLogged = false;

            Debug.Log($"🦆 Duck Hunt: раунд начат ({(HasAuthority ? "АВТОРИТЕТ" : "клиент")}), " +
                      $"участников {Players.Count}, Охотник {hunterPlayerId}, " +
                      $"свисток {(CountdownAnnounced ? $"через {CountdownRemaining:F1} с" : "ещё не объявлен")}");

            ResetTraps();
            SetRoundLive(false);
        }

        protected override void OnRoundEnded()
        {
            liveTime = NotAnnounced;
            SetRoundLive(false);
            StopAllBots();
            RestoreDucks();
            Hud?.HideCountdown();
            Hud?.HideStatus();
        }

        private void Update()
        {
            TickCountdown();
            TickDuckPrompt();
            HandleDebugRoleSwitch();
        }

        private void FixedUpdate()
        {
            // Прогресс и зачёт решают исход раунда, поэтому считает только авторитет.
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            roundElapsed += Time.fixedDeltaTime;

            if (!RoundLive)
            {
                return;
            }

            SampleProgress();
            DriveDummyDucks();
        }

        /// <summary>
        /// Отсчёт идёт уже внутри раунда: таймер тикает, но движение
        /// заблокировано у всех, а Охотник не может выстрелить. Так Утки
        /// не разбегаются до свистка, а Охотник не расстреливает толпу
        /// на старте, пока она стоит кучей.
        ///
        /// Считает отсчёт каждая машина сама — от одного объявленного момента
        /// на общих часах. Досылать остаток каждый кадр не нужно, а свисток
        /// звучит у всех одновременно.
        /// </summary>
        private void TickCountdown()
        {
            if (!RoundActive || RoundLive)
            {
                return;
            }

            if (!CountdownAnnounced)
            {
                // Клиент ждёт момент свистка от сервера. Пока его нет, раунд
                // держит всех на месте — и это самое место, где сетевой раунд
                // может встать молча, поэтому оно говорит о себе вслух.
                if (!waitingForWhistleLogged)
                {
                    waitingForWhistleLogged = true;
                    Debug.Log("🦆 Duck Hunt: жду от сервера момент свистка");
                }

                return;
            }

            float remaining = CountdownRemaining;
            if (remaining > 0f)
            {
                Hud?.ShowCountdown(remaining);
                return;
            }

            Hud?.HideCountdown();
            SetRoundLive(true);
        }

        private void SetRoundLive(bool live)
        {
            // Свисток — единственное место, где сетевой раунд способен встать
            // молча, поэтому оно говорит о себе вслух. Заморозку не пишем:
            // она случается и на старте раунда, и в его конце.
            if (live && !RoundLive)
            {
                Debug.Log($"🦆 Duck Hunt: СВИСТОК — Утки побежали ({ducks.Count}), " +
                          $"Охотник {hunterPlayerId}, {(HasAuthority ? "АВТОРИТЕТ" : "клиент")}");
            }

            RoundLive = live;

            for (int i = 0; i < ducks.Count; i++)
            {
                PlayerController avatar = ducks[i].Avatar;
                if (avatar != null && !ducks[i].Outcome.Retired)
                {
                    avatar.MovementLocked = !live;
                    SuspendInput(avatar, !live);
                }
            }

            // Тело Охотника заблокировано всегда — ему отпускается не движение,
            // а право стрелять и водить лифт.
            if (hunter != null)
            {
                if (live)
                {
                    hunter.Attach(config, elevator, firstPersonRig);
                }
                else
                {
                    hunter.Detach();
                }
            }

            // Охотнику ввод глушим тоже: без этого он на отсчёте толкает
            // и взаимодействует, хоть и не стреляет.
            SuspendInput(hunterAvatar, !live);

            // Лифт останавливает авторитет, и только он. Ось Охотника, играющего
            // с клиента, приезжает намерением и после свистка больше не придёт —
            // платформа уехала бы до верхней границы на глазах у всех, пока
            // на экране висят места.
            if (!live && HasAuthority)
            {
                elevator?.SetAxis(0f);
            }

            ApplyHunterAuthority();
            SetDummyBotsRunning(live);
            RefreshHunterStatus();
            RefreshCrosshair();
        }

        /// <summary>
        /// Заморозить ввод игрока на время стартового отсчёта.
        ///
        /// <c>MovementLocked</c> держит только шаги, прыжок и присед — толчок,
        /// взаимодействие и переноска живут в отдельных способностях и про блокировку
        /// не знают. На отсчёте управления не должно быть вообще, поэтому
        /// глушим в корне — в самом ридере.
        /// </summary>
        private static void SuspendInput(PlayerController avatar, bool suspended)
        {
            if (avatar != null && avatar.TryGetComponent(out PlayerInputReader reader))
            {
                reader.SetSuspended(suspended);
            }
        }

        /// <summary>
        /// Перечитать положение всех Уток и обновить слепки. Слепок ведётся
        /// здесь же, а не только в момент выхода игрока: дисконнект разбирается
        /// на ближайшем тике, и к этому тику NGO уже успевает уничтожить аватар —
        /// читать прогресс было бы не с чего.
        /// </summary>
        private void SampleProgress()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                DuckRecord duck = ducks[i];
                if (duck.Left || duck.Progress == null)
                {
                    continue;
                }

                duck.Progress.Sample(arena);
                duck.Frozen = DuckOutcome.From(duck.Progress);
            }
        }

        // ========== РОЛИ ==========

        /// <summary>
        /// Охотник — один. В сетевой катке его выбирает сервер из тех, кто ещё
        /// не был Охотником в этой катке; в соло-тесте роль переключается
        /// кнопкой, иначе за неё невозможно подержаться в одиночку.
        /// </summary>
        private void AssignRoles()
        {
            if (Players.Count == 0)
            {
                return;
            }

            if (spectator != null && spectator.IsActive)
            {
                spectator.Deactivate();
            }

            ClearHunter();
            ducks.Clear();
            aliveDucks.Clear();
            finishCounter = 0;
            hunterPlayerId = SpecialRoleHistory.NoPlayer;

            int hunterIndex = PickHunterIndex();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                PlayerController avatar = player.Avatar;
                if (avatar == null)
                {
                    continue;
                }

                if (i == hunterIndex)
                {
                    hunterPlayerId = player.Id;
                    hunterAvatar = avatar;
                    SetupHunter(avatar);
                    continue;
                }

                ducks.Add(CreateDuck(player, avatar));
                aliveDucks.Add(player);
            }

            PlaceDucksAtStart();
            ApplyGeyserForce();

            if (hunterPlayerId != SpecialRoleHistory.NoPlayer && HasAuthority)
            {
                SessionScoreboard.Current?.MarkSpecialRole(hunterPlayerId, HunterRoleKey);
            }

            ApplyRoleCamera();
            SetRoundLive(RoundActive && CountdownAnnounced && CountdownRemaining <= 0f);
        }

        private DuckRecord CreateDuck(SessionPlayer player, PlayerController avatar)
        {
            // Бывший Охотник обязан вернуться в норму: без Detach он остался бы
            // обездвиженным и неуязвимым, уже будучи Уткой.
            if (avatar.TryGetComponent(out HunterController formerHunter))
            {
                formerHunter.Detach();
            }

            DuckProgress progress = avatar.GetComponent<DuckProgress>();
            if (progress == null)
            {
                progress = avatar.gameObject.AddComponent<DuckProgress>();
            }

            PlayerElimination elimination = avatar.GetComponent<PlayerElimination>();
            if (elimination == null)
            {
                elimination = avatar.gameObject.AddComponent<PlayerElimination>();
            }

            elimination.Restore();
            progress.ResetProgress(arena);

            var record = new DuckRecord
            {
                PlayerId = player.Id,
                Avatar = avatar,
                Progress = progress,
                Elimination = elimination,
                Bot = EnsureDummyBot(avatar),
                IsDummy = avatar.TryGetComponent(out PlayerInputReader reader) && !reader.LocallyControlled,
                LastFloor = progress.Floor,
                Frozen = DuckOutcome.From(progress)
            };

            // Подписка одна на аватар за раунд: Restore не отписывает, поэтому
            // снимаем прошлую подписку перед новой — при пересдаче ролей иначе
            // накапливаются дубли и один выстрел засчитывается несколько раз.
            elimination.BodyHidden -= HandleAnyDuckHidden;
            elimination.BodyHidden += HandleAnyDuckHidden;

            return record;
        }

        private void SetupHunter(PlayerController avatar)
        {
            hunter = avatar.GetComponent<HunterController>();
            if (hunter == null)
            {
                hunter = avatar.gameObject.AddComponent<HunterController>();
            }

            // Охотник не Утка: снимаем прогресс и выбывание, если этот аватар
            // ими уже обзавёлся на прошлой раздаче ролей.
            if (avatar.TryGetComponent(out PlayerElimination elimination))
            {
                elimination.Restore();
            }

            if (avatar.TryGetComponent(out DebugPlayerBot bot))
            {
                bot.Stop();
            }

            hunter.DuckShot -= HandleDuckShot;
            hunter.DuckShot += HandleDuckShot;

            // Выстрел разослать остальным может только тот, кто его посчитал,
            // а событие приходит одинаково — и когда стреляет хост сам, и когда
            // он исполняет намерение клиента. Отсюда одна подписка на оба случая.
            hunter.Fired -= HandleHunterFired;
            hunter.Fired += HandleHunterFired;

            hunter.Attach(config, elevator, firstPersonRig);

            MoveTo(avatar, spawnPoints?.GetPoint(SpawnRole.Special, 0));

            if (hunter.Weapon != null)
            {
                hunter.Weapon.AmmoChanged -= HandleAmmoChanged;
                hunter.Weapon.AmmoChanged += HandleAmmoChanged;
                hunter.Weapon.ReloadingChanged -= HandleReloadingChanged;
                hunter.Weapon.ReloadingChanged += HandleReloadingChanged;
            }
        }

        private void ClearHunter()
        {
            // Тело отпускаем всегда, даже если самого компонента уже нет: при
            // уходе Охотника аватар уничтожен, проверка ниже отсечёт разрушенную
            // роль — и ссылка на тело осталась бы висеть до конца раунда.
            hunterAvatar = null;

            if (hunter == null)
            {
                return;
            }

            hunter.DuckShot -= HandleDuckShot;
            hunter.Fired -= HandleHunterFired;
            if (hunter.Weapon != null)
            {
                hunter.Weapon.AmmoChanged -= HandleAmmoChanged;
                hunter.Weapon.ReloadingChanged -= HandleReloadingChanged;
            }

            hunter.Detach();
            hunter = null;
            hunterAvatar = null;
        }

        /// <summary>
        /// Утки расходятся по входу первого этажа. Разнос считается по числу
        /// Уток, а не игроков: при одном Охотнике в ряду иначе остаётся дыра
        /// размером с его слот.
        ///
        /// Когда болванок не гонят, они вместо старта встают неподвижными
        /// мишенями на виду у Охотника. Иначе проверить стрельбу нечем: на
        /// точках входа они стоят в косом углу, где попасть тяжело и без
        /// того — и непонятно, промах это или сломанный выстрел.
        /// </summary>
        private void PlaceDucksAtStart()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                DuckRecord duck = ducks[i];

                // Ушедшего расставлять нечем: тела нет, в списке он остался
                // только ради места в результатах.
                if (duck.Left || duck.Progress == null)
                {
                    continue;
                }

                if (duck.IsDummy && !driveDummyDucks)
                {
                    PlaceAsTarget(duck, i);
                }
                else
                {
                    MoveTo(duck.Avatar, spawnPoints?.GetSpreadPoint(SpawnRole.Default, i, ducks.Count));
                }

                duck.Progress.ResetProgress(arena);
                duck.Frozen = DuckOutcome.From(duck.Progress);
            }
        }

        /// <summary>Поставить болванку неподвижной мишенью: на виду, вдоль трассы, с разносом по глубине.</summary>
        private void PlaceAsTarget(DuckRecord duck, int index)
        {
            if (arena == null || duck.Avatar == null)
            {
                return;
            }

            int floor = Mathf.Clamp(targetFloor, 0, Mathf.Max(0, arena.FloorCount - 1));
            float depth = 3f + index % 4 * 2.5f;
            float progress = targetProgress + index / 4 * 2f;

            Vector3 point = arena.GetWorldPoint(floor, progress, depth) + Vector3.up * 0.2f;
            duck.Avatar.RequestTeleport(point, Quaternion.LookRotation(Vector3.forward));
        }

        private void MoveTo(PlayerController avatar, SpawnPoint point)
        {
            if (avatar == null || point == null)
            {
                return;
            }

            avatar.RequestTeleport(point.transform.position, point.transform.rotation);

            if (avatar.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(point.transform);
            }
        }

        private int PickHunterIndex()
        {
            // Меньше двух участников — Охотника нет, стрелять некому.
            if (Players.Count < 2)
            {
                return -1;
            }

            if (!SessionScoreboard.IsNetworked)
            {
                return localPlayerIsHunter ? 0 : Mathf.Min(1, Players.Count - 1);
            }

            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            if (scoreboard == null || !scoreboard.HasAuthority)
            {
                // Клиент роль не выбирает — она приезжает из сети. Пока не
                // приехала, Охотника нет вовсе: назначить наугад значило бы
                // запереть на лифте не того игрока и отдать ему ружьё.
                return IndexOfPlayer(network != null ? network.HunterPlayerId : SpecialRoleHistory.NoPlayer);
            }

            int picked = IndexOfPlayer(scoreboard.PickSpecialRole(HunterRoleKey));
            return picked >= 0 ? picked : 0;
        }

        private int IndexOfPlayer(int playerId)
        {
            if (playerId == SpecialRoleHistory.NoPlayer)
            {
                return -1;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Камера зависит от роли, а не от мини-игры: Охотник смотрит от первого
        /// лица, Утки — из-за спины. Режим из MinigameDefinition тут не годится,
        /// он один на всех.
        /// </summary>
        private void ApplyRoleCamera()
        {
            if (cameraController == null)
            {
                return;
            }

            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            if (local?.Avatar == null)
            {
                return;
            }

            bool localIsHunter = local.Id == hunterPlayerId;
            cameraController.Apply(
                localIsHunter ? CameraMode.FirstPerson : CameraMode.ThirdPerson,
                local.Avatar.transform);

            RefreshCrosshair();
        }

        /// <summary>
        /// Прицел — только своему Охотнику и только в живом раунде. На стартовом
        /// отсчёте стрелять нельзя, и висящий прицел обещает игроку то, чего
        /// ему пока не дают; на экране результатов он лёг бы поверх таблицы мест.
        /// </summary>
        private void RefreshCrosshair()
        {
            if (crosshair != null)
            {
                crosshair.SetActive(RoundLive && IsLocal(hunterPlayerId));
            }
        }

        // ========== ЛОВУШКИ ==========

        private void ResetTraps()
        {
            for (int i = 0; i < traps.Length; i++)
            {
                traps[i]?.ResetTrap();
            }
        }

        /// <summary>
        /// Сила гейзера считается из высоты подброса, массы персонажа и его
        /// гравитации на взлёте. Задавать импульс числом нельзя: спека
        /// настраивает высоту, а она зависит ещё и от настроек тела — стоит
        /// поменять массу, и «выше укрытия, ниже потолка» перестаёт держаться.
        /// </summary>
        private void ApplyGeyserForce()
        {
            if (config == null || geysers.Length == 0)
            {
                return;
            }

            CharacterConfig character = FindCharacterConfig();
            float mass = character != null ? character.Mass : 2f;
            float riseGravity = character != null ? character.RiseGravityMultiplier : 1f;
            float impulse = config.GetGeyserImpulse(mass, riseGravity);

            for (int i = 0; i < geysers.Length; i++)
            {
                geysers[i]?.SetLaunchForce(impulse);
            }
        }

        private CharacterConfig FindCharacterConfig()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                if (ducks[i].Avatar != null && ducks[i].Avatar.Config != null)
                {
                    return ducks[i].Avatar.Config;
                }
            }

            return hunterAvatar != null ? hunterAvatar.Config : null;
        }

        // ========== ЗАЧЁТ ==========

        /// <summary>
        /// Попадание по Утке. Единственная точка зачёта смерти: её зовёт
        /// Охотник, и в сетевой фазе она уходит за серверную проверку без
        /// правки логики ниже.
        /// </summary>
        private void HandleDuckShot(PlayerElimination victim, Vector3 hitPoint, Vector3 impulse)
        {
            if (!RoundLive || !HasAuthority)
            {
                return;
            }

            DuckRecord duck = FindDuckByElimination(victim);
            if (duck == null || duck.Left || duck.Progress == null || duck.Progress.Retired)
            {
                return;
            }

            // Прогресс фиксируется ДО импульса: иначе отлёт успевает утащить
            // тело с того места, где Утку застрелили, и в зачёт попадает точка
            // падения вместо точки гибели.
            duck.Progress.Sample(arena);
            duck.Progress.MarkDead(roundElapsed);

            // Слепок обновляем здесь же, а не ждём следующего опроса: выйди
            // игрок в этом же кадре, замораживать было бы уже нечего.
            duck.Frozen = DuckOutcome.From(duck.Progress);

            duck.Bot?.Stop();
            RemoveFromAlive(duck.PlayerId);
            victim.Eliminate(hitPoint, impulse);

            // Прогресс и выбывание приедут состоянием сами, а вот сторона
            // отлёта нужна ровно в этот миг: по ней выбирается клип падения.
            network?.AnnounceDuckDeath(duck.PlayerId, hitPoint, impulse);

            CheckRoundOver();
        }

        /// <summary>Утка добежала до крыши. Порядок прибытия решает места дошедших.</summary>
        private void HandleDuckArrived(DuckProgress progress)
        {
            if (!RoundLive || !HasAuthority || progress == null || progress.Retired)
            {
                return;
            }

            DuckRecord duck = FindDuckByProgress(progress);
            if (duck == null)
            {
                return;
            }

            progress.MarkFinished(++finishCounter);
            duck.Frozen = DuckOutcome.From(progress);
            duck.Bot?.Stop();

            if (duck.Avatar != null)
            {
                duck.Avatar.MovementLocked = true;
            }

            RemoveFromAlive(duck.PlayerId);

            if (IsLocal(duck.PlayerId))
            {
                spectator?.Activate(aliveDucks);
            }

            CheckRoundOver();
        }

        /// <summary>
        /// Тело подстреленной Утки исчезло — своему игроку пора в наблюдатели.
        /// Событие приносит сам компонент, поэтому искать «у кого спрятано» по
        /// списку больше не нужно: спрашиваем ровно того, кто позвал.
        /// </summary>
        private void HandleAnyDuckHidden(PlayerElimination elimination)
        {
            DuckRecord duck = FindDuckByElimination(elimination);
            if (duck != null && IsLocal(duck.PlayerId))
            {
                spectator?.Activate(aliveDucks);
            }
        }

        private void CheckRoundOver()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                if (!ducks[i].Outcome.Retired)
                {
                    return;
                }
            }

            // Уток нет вовсе (лобби из одного игрока) — раунд доживает до таймера.
            if (ducks.Count > 0)
            {
                EndMinigame();
            }
        }

        private void RemoveFromAlive(int playerId)
        {
            for (int i = 0; i < aliveDucks.Count; i++)
            {
                if (aliveDucks[i].Id == playerId)
                {
                    aliveDucks.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// К экрану результатов все возвращаются в игру: тела погибших снова
        /// видны, а движение отпущено — на итогах в кадре обязаны стоять все.
        /// </summary>
        private void RestoreDucks()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                DuckRecord duck = ducks[i];

                // Ушедшего возвращать некуда: тела нет, и в кадре итогов его
                // быть не должно — в списке он остался только ради места.
                if (duck.Left)
                {
                    continue;
                }

                if (duck.Elimination != null)
                {
                    duck.Elimination.Restore();
                }

                if (duck.Avatar != null)
                {
                    duck.Avatar.MovementLocked = false;
                }
            }
        }

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Участник вышел из матча. Зовёт сетевая половина на ближайшем тике
        /// после дисконнекта — и только у авторитета, он один знает про уход.
        ///
        /// Правило спеки, раздел 10.2: уход Охотника заканчивает раунд
        /// немедленно — стрелять больше некому, и оставшиеся Утки добежали бы
        /// до крыши по пустой башне. Уход Утки раунд не трогает: она числится
        /// погибшей на той точке, где её застал выход.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            if (playerId != SpecialRoleHistory.NoPlayer && playerId == hunterPlayerId)
            {
                HandleHunterLeft();
                return;
            }

            DuckRecord duck = FindDuckById(playerId);
            if (duck == null)
            {
                // Ни Охотник, ни Утка: игрок вышел до раздачи ролей. Место ему
                // считать не по чему, поэтому просто вычёркиваем из состава —
                // иначе он получит место в результатах, не сыграв ни секунды.
                RemovePlayer(playerId);
                return;
            }

            RetireLeftDuck(duck);

            Debug.Log($"🦆 Duck Hunt: Утка {playerId} вышла — зачтена погибшей " +
                      $"на этаже {duck.Frozen.Floor}, живых осталось {aliveDucks.Count}");

            // Последняя Утка вышла — стрелять больше не в кого. Общая проверка
            // закроет раунд сама: ушедшая числится выбывшей наравне с погибшими.
            CheckRoundOver();
        }

        /// <summary>
        /// Охотник вышел. Раунд закрывается сразу же, а место ему выдаётся
        /// последнее — за это отвечает <see cref="hunterLeft"/> в
        /// <see cref="CollectResults"/>.
        ///
        /// Порядок самих Уток при этом не трогаем: живые и так стоят выше
        /// погибших и ранжируются по достигнутой точке — ровно то, чего спека
        /// требует от такого раунда.
        ///
        /// Роль за ушедшим сохраняется намеренно: обнули мы <c>hunterPlayerId</c>
        /// здесь, выдавать место было бы уже некому.
        /// </summary>
        private void HandleHunterLeft()
        {
            if (hunterLeft)
            {
                return;
            }

            hunterLeft = true;

            Debug.Log($"🦆 Duck Hunt: Охотник {hunterPlayerId} вышел — раунд закрывается досрочно, " +
                      $"живых Уток {aliveDucks.Count}");

            // Оружие и лифт отвязываем до конца раунда: подписки висели на
            // компоненте уничтоженного аватара, а ось лифта больше никто не
            // пришлёт — платформа иначе уедет до упора на экране результатов.
            ClearHunter();
            elevator?.SetAxis(0f);
            ApplyHunterAuthority();
            RefreshCrosshair();

            EndMinigame();
        }

        /// <summary>
        /// Утка ушла из матча: числится погибшей на той точке, где её застал
        /// выход, тело убирается.
        /// </summary>
        private void RetireLeftDuck(DuckRecord duck)
        {
            if (duck.Left)
            {
                return;
            }

            if (duck.Progress != null && !duck.Progress.Retired)
            {
                // Прогресс дочитывается перед заморозкой: место считается по
                // точке выхода, а не по той, где Утку последний раз опросили.
                duck.Progress.Sample(arena);
                duck.Progress.MarkDead(roundElapsed);
                duck.Frozen = DuckOutcome.From(duck.Progress);
            }
            else if (!duck.Frozen.Retired)
            {
                // Аватар уничтожен раньше, чем разобрали уход. Дочитывать
                // нечего — домечаем погибшей по последнему слепку.
                duck.Frozen = duck.Frozen.AsDead(roundElapsed);
            }

            MarkDuckLeft(duck);
        }

        /// <summary>
        /// Забыть тело ушедшей Утки.
        ///
        /// Ссылки обнуляются явно, а не оставляются «уничтоженными»: оператор
        /// <c>?.</c> проверяет настоящий null и разрушенный объект Unity не
        /// отсекает, поэтому вызов по такой ссылке падает
        /// <c>MissingReferenceException</c>. После обнуления все проверки
        /// <c>!= null</c> по коду мини-игры срабатывают как надо.
        /// </summary>
        private void MarkDuckLeft(DuckRecord duck)
        {
            if (duck.Left)
            {
                return;
            }

            duck.Left = true;

            if (duck.Bot != null)
            {
                duck.Bot.Stop();
            }

            RemoveFromAlive(duck.PlayerId);

            duck.Avatar = null;
            duck.Progress = null;
            duck.Elimination = null;
            duck.Bot = null;
            duck.Route.Clear();
            duck.RouteIndex = 0;

            // Наблюдателю говорить ничего не нужно: список живых он держит по
            // ссылке и перечитывает каждый кадр, а с пропавшей цели уходит сам.
        }

        // ========== HUD ОХОТНИКА ==========

        private void HandleAmmoChanged(int ammo) => RefreshHunterStatus();

        private void HandleReloadingChanged(bool reloading) => RefreshHunterStatus();

        /// <summary>
        /// Обойма показывается только своему Охотнику. Строка пересобирается
        /// на смене состояния, а не каждый кадр — это событие, а не опрос.
        /// </summary>
        private void RefreshHunterStatus()
        {
            if (Hud == null || hunter?.Weapon == null || !IsLocal(hunterPlayerId))
            {
                return;
            }

            Hud.ShowStatus(hunter.Weapon.IsReloading
                ? "Перезарядка…"
                : $"Патроны: {hunter.Weapon.Ammo} / {hunter.Weapon.MagazineSize}");
        }

        // ========== ПОДСКАЗКА ВЗАИМОДЕЙСТВИЯ ==========

        /// <summary>
        /// Строка «что можно нажать» для локальной Утки.
        ///
        /// Без неё рычаг ловушки не найти: он маленький и врезан в стену, а
        /// радиус взаимодействия — 1.8 м, и стоя на полшага дальше игрок жмёт
        /// E в пустоту, не понимая, почему ничего не происходит. Строка
        /// состояния у Утки свободна — обойму в ней показывает только свой
        /// Охотник, — и подсказке там место.
        ///
        /// Опрос каждый кадр, а строка пересобирается только на смене цели:
        /// <c>InteractionPrompt</c> склеивает текст, и держать это в кадре
        /// нельзя.
        /// </summary>
        private void TickDuckPrompt()
        {
            if (Hud == null || IsLocal(hunterPlayerId))
            {
                return;
            }

            IInteractable target = RoundLive ? ResolveLocalInteractable() : null;
            if (ReferenceEquals(target, promptTarget))
            {
                return;
            }

            promptTarget = target;

            if (target == null)
            {
                Hud.HideStatus();
                return;
            }

            Hud.ShowStatus($"[E] {target.InteractionPrompt}");
        }

        /// <summary>
        /// Что сейчас в руках у локальной Утки. Интерактор берётся у своего
        /// аватара из табло, а не поиском по сцене, и переспрашивается только
        /// пока не найден: аватар появляется позже правил.
        /// </summary>
        private IInteractable ResolveLocalInteractable()
        {
            if (localInteractor == null)
            {
                SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
                if (local?.Avatar == null)
                {
                    return null;
                }

                local.Avatar.TryGetComponent(out localInteractor);
            }

            return localInteractor != null && localInteractor.enabled
                ? localInteractor.CurrentInteractable
                : null;
        }

        // ========== БОЛВАНКИ СОЛО-ТЕСТА ==========

        /// <summary>
        /// Болванке нужен свой водитель: без бегущих Уток нечем проверить ни
        /// порядок финиша, ни досрочный конец раунда, ни переключение
        /// наблюдателя, а за Охотника при них стрелять не по кому.
        ///
        /// Живому игроку и любой сетевой копии бот не ставится: там ввод идёт
        /// от человека, и второй источник ввода — это гонка.
        /// </summary>
        private DebugPlayerBot EnsureDummyBot(PlayerController avatar)
        {
            if (!driveDummyDucks || SessionScoreboard.IsNetworked)
            {
                return null;
            }

            if (!avatar.TryGetComponent(out PlayerInputReader reader) || reader.LocallyControlled)
            {
                return null;
            }

            if (!avatar.TryGetComponent(out DebugPlayerBot bot))
            {
                bot = avatar.gameObject.AddComponent<DebugPlayerBot>();
            }

            bot.Configure(LayerMask.GetMask(GroundLayerName, CoverLayerName), GetBotStepHeight(avatar));
            return bot;
        }

        /// <summary>
        /// Что болванка считает ступенью, а что стеной. Считается от реальной
        /// высоты прыжка персонажа, а не задаётся числом: высота прыжка живёт
        /// в общем CharacterConfig и может измениться без ведома этой игры.
        /// </summary>
        private static float GetBotStepHeight(PlayerController avatar)
        {
            CharacterConfig character = avatar.Config;
            if (character == null)
            {
                return 0.6f;
            }

            float gravity = Mathf.Abs(Physics.gravity.y) * Mathf.Max(1f, character.RiseGravityMultiplier);
            float jumpHeight = character.JumpSpeed * character.JumpSpeed / (2f * gravity);
            return jumpHeight * BotStepHeightFactor;
        }

        private void SetDummyBotsRunning(bool running)
        {
            if (running)
            {
                DriveDummyDucks();
                return;
            }

            StopAllBots();
        }

        private void StopAllBots()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                // Через != null, а не через ?.: водитель живёт на аватаре, и
                // у ушедшего игрока это уничтоженный объект, которого оператор
                // ?. не отсекает.
                if (ducks[i].Bot != null)
                {
                    ducks[i].Bot.Stop();
                }
            }
        }

        /// <summary>
        /// Болванки идут к выходу со своего этажа, а на последнем — к площадке
        /// финиша. Цель пересчитывается каждый тик, потому что этаж под ними
        /// меняется: и когда они поднимаются сами, и когда их роняет провал.
        /// </summary>
        private void DriveDummyDucks()
        {
            if (arena == null || config == null)
            {
                return;
            }

            for (int i = 0; i < ducks.Count; i++)
            {
                DuckRecord duck = ducks[i];
                if (duck.Left || duck.Bot == null || duck.Avatar == null || duck.Progress == null ||
                    duck.Progress.Retired)
                {
                    continue;
                }

                Vector3 duckTarget = GetDuckTarget(duck, out bool stopOnArrival);
                duck.Bot.SetTarget(duckTarget, stopOnArrival);
            }
        }

        /// <summary>
        /// Куда идти болванке. По коридору — по точкам маршрута в обход
        /// укрытий, свернув на лестницу — по ступеням одна за другой.
        ///
        /// Останавливаться разрешено только на последней цели: и ступени, и
        /// точки маршрута — путевые. Замерев на любой из них, болванка стоит
        /// до конца раунда.
        /// </summary>
        private Vector3 GetDuckTarget(DuckRecord duck, out bool stopOnArrival)
        {
            Vector3 position = duck.Avatar.transform.position;
            stopOnArrival = false;

            // Выше верхнего этажа болванка может быть только на крыше — там
            // цель одна, площадка финиша.
            if (finishZone != null && position.y >= arena.GetFloorBaseY(arena.FloorCount) - 0.1f)
            {
                stopOnArrival = true;
                return finishZone.transform.position;
            }

            float stairRoomStart = config.FloorLengthWidths - config.StairRoomSizeUnits / config.CharacterWidth;

            // Смена этажа означает новый подъём: прошлый пройден до конца.
            if (duck.LastFloor != duck.Progress.Floor)
            {
                duck.LastFloor = duck.Progress.Floor;
                duck.Climbing = false;
                BuildDuckRoute(duck, stairRoomStart);
            }

            if (!duck.Climbing && duck.Progress.Progress >= stairRoomStart - StairApproachWidths)
            {
                duck.Climbing = true;
            }

            if (duck.Climbing)
            {
                DuckHuntStairs stairs = arena.GetStairs(duck.Progress.Floor);
                if (stairs != null && stairs.TryGetNextStep(position, out Vector3 step))
                {
                    return step;
                }

                // Подъём пройден — цель уже на следующем этаже, у его входа.
                // Глубину берём ближе к открытой грани: прямо над лестницей в
                // перекрытии проём, и цель посреди коридора увела бы обратно в него.
                int next = Mathf.Min(duck.Progress.Floor + 1, arena.FloorCount - 1);
                return arena.GetWorldPoint(next, 4f, StairExitDepthWidths);
            }

            return GetRouteTarget(duck, stairRoomStart);
        }

        /// <summary>
        /// Очередная точка ломаной по коридору. Точка считается пройденной по
        /// прогрессу вдоль трассы, а не по расстоянию до неё: обходя укрытие,
        /// болванка проходит мимо точки сбоку и до самой точки может не
        /// дотянуться — а трассу при этом прошла.
        /// </summary>
        private Vector3 GetRouteTarget(DuckRecord duck, float stairRoomStart)
        {
            if (duck.Route.Count == 0)
            {
                BuildDuckRoute(duck, stairRoomStart);
            }

            while (duck.RouteIndex < duck.Route.Count)
            {
                Vector3 point = duck.Route[duck.RouteIndex];
                float pointProgress = arena.GetProgressWidths(point, duck.Progress.Floor);
                if (duck.Progress.Progress < pointProgress - RouteReachWidths)
                {
                    return point;
                }

                duck.RouteIndex++;
            }

            return arena.GetWorldPoint(duck.Progress.Floor, stairRoomStart, PathDepthWidths);
        }

        /// <summary>
        /// Построить маршрут по коридору текущего этажа болванки — от её
        /// прогресса до входа в лестничную комнату.
        /// </summary>
        private void BuildDuckRoute(DuckRecord duck, float stairRoomStart)
        {
            duck.RouteIndex = 0;
            DuckHuntBotRoute.Build(
                duck.Route,
                arena,
                duck.Progress.Floor,
                Mathf.Max(0f, duck.Progress.Progress),
                stairRoomStart,
                LayerMask.GetMask(GroundLayerName, CoverLayerName),
                GetBotBodyRadius(duck.Avatar),
                GetDuckLaneDepth(duck));
        }

        /// <summary>
        /// Своя полоса коридора каждой болванке. Общая полоса на всех сводит их
        /// в одну точку, и они упираются друг в друга насмерть: чужое тело в
        /// помехи маршрута не входит, обходить его болванка не умеет.
        /// </summary>
        private float GetDuckLaneDepth(DuckRecord duck)
        {
            int index = ducks.IndexOf(duck);
            if (index < 0 || ducks.Count <= 1)
            {
                return PathDepthWidths;
            }

            float middle = (ducks.Count - 1) * 0.5f;
            return PathDepthWidths + (index - middle) * DuckLaneSpacingWidths;
        }

        /// <summary>Радиус тела болванки: по капсуле, а не по числу из спеки — капсулы у персонажей общие, но берём фактическую.</summary>
        private static float GetBotBodyRadius(PlayerController avatar)
        {
            return avatar != null && avatar.TryGetComponent(out CapsuleCollider capsule)
                ? capsule.radius
                : DefaultBodyRadius;
        }

        private void HandleDebugRoleSwitch()
        {
            if (SessionScoreboard.IsNetworked || !RoundActive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[roleSwitchKey].wasPressedThisFrame)
            {
                return;
            }

            localPlayerIsHunter = !localPlayerIsHunter;
            AssignRoles();
            Debug.Log($"Duck Hunt: локальный игрок теперь {(localPlayerIsHunter ? "ОХОТНИК" : "УТКА")}");
        }

        // ========== СЕТЕВОЕ СОСТОЯНИЕ ==========

        /// <summary>Кому выпала роль Охотника. Это число и реплицирует сетевая половина.</summary>
        public int HunterPlayerId => hunterPlayerId;

        /// <summary>Момент свистка на общих часах. <see cref="NotAnnounced"/> — ещё не объявлен.</summary>
        public double LiveTime => liveTime;

        /// <summary>Высота лифта на этой машине.</summary>
        public float ElevatorHeight => elevator != null ? elevator.CurrentY : 0f;

        /// <summary>Уток в раунде.</summary>
        public int DuckCount => ducks.Count;

        /// <summary>
        /// Какие ловушки сейчас сработаны — по биту на ловушку в порядке массива.
        /// Одним числом, а не списком: состояние ловушки это ровно один флаг,
        /// а список пришлось бы отдельно держать в согласии с массивом сцены.
        /// </summary>
        public uint TrapMask
        {
            get
            {
                uint mask = 0u;
                int count = Mathf.Min(traps.Length, MaxSyncedTraps);
                for (int i = 0; i < count; i++)
                {
                    if (traps[i] != null && traps[i].IsSprung)
                    {
                        mask |= 1u << i;
                    }
                }

                return mask;
            }
        }

        /// <summary>Обойма Охотника для репликации. Ложь — Охотника или оружия ещё нет.</summary>
        public bool TryGetHunterWeapon(out int ammo, out double reloadEndsAt)
        {
            HitscanWeapon weapon = hunter != null ? hunter.Weapon : null;
            if (weapon == null)
            {
                ammo = 0;
                reloadEndsAt = 0d;
                return false;
            }

            ammo = weapon.Ammo;
            reloadEndsAt = weapon.ReloadEndsAt;
            return true;
        }

        /// <summary>Состояние Утки для репликации. Ложь — такой Утки в раунде нет.</summary>
        public bool TryGetDuckState(int index, out DuckNetState state)
        {
            if (index < 0 || index >= ducks.Count)
            {
                state = default;
                return false;
            }

            DuckRecord duck = ducks[index];

            // Через слепок, а не с компонента: у ушедшего игрока компонента
            // уже нет, а его замороженный итог обязан доехать до клиентов —
            // иначе они до конца раунда считают его бегущим.
            DuckOutcome outcome = duck.Outcome;
            state = new DuckNetState
            {
                PlayerId = duck.PlayerId,
                Floor = (byte)Mathf.Clamp(outcome.Floor, 0, byte.MaxValue),
                Progress = outcome.Progress,
                FinishOrder = (byte)Mathf.Clamp(outcome.FinishOrder, 0, byte.MaxValue),
                Dead = outcome.Dead,
                DeathTime = outcome.DeathTime
            };

            return true;
        }

        /// <summary>
        /// Намерение Охотника подвинуть лифт, приехавшее с его машины.
        /// Проверок две, и обе обязательны: лифтом правит только тот, кому
        /// выпала роль, и только пока раунд живой — иначе платформа ездила бы
        /// и на отсчёте, и на экране результатов.
        /// </summary>
        public void ServerMoveElevator(int playerId, float axis)
        {
            if (!HasAuthority || !RoundLive || playerId != hunterPlayerId)
            {
                return;
            }

            elevator?.SetAxis(axis);
        }

        /// <summary>
        /// Намерение Охотника выстрелить, приехавшее с его машины. Клиент
        /// присылает только точку и направление — обойму, задержку, конус
        /// разброса и сам луч считает эта сторона, и клиентский результат
        /// не спрашивается вовсе.
        /// </summary>
        public void ServerFire(int playerId, Vector3 origin, Vector3 direction)
        {
            if (!HasAuthority || !RoundLive || playerId != hunterPlayerId || hunter == null)
            {
                return;
            }

            if (!IsUsable(origin) || !IsUsable(direction) || direction.sqrMagnitude < MinAimSqrMagnitude)
            {
                Debug.LogWarning($"{name}: выстрел игрока {playerId} отклонён — негодная точка или направление", this);
                return;
            }

            hunter.TryFire(ValidateMuzzle(origin, playerId), direction.normalized);
        }

        /// <summary>Намерение перезарядиться. Обойма — состояние раунда, меняет её только авторитет.</summary>
        public void ServerReload(int playerId)
        {
            if (!HasAuthority || !RoundLive || playerId != hunterPlayerId)
            {
                return;
            }

            hunter?.Weapon?.Reload();
        }

        /// <summary>
        /// Точку выстрела берём клиентскую: целится игрок своей камерой, и
        /// подменять её серверной значит стрелять не туда, куда он смотрел.
        /// А вот выстрелить из другого конца карты ему нельзя — слишком
        /// далёкая точка заменяется на свою.
        /// </summary>
        private Vector3 ValidateMuzzle(Vector3 origin, int playerId)
        {
            Vector3 own = hunter.GetMuzzlePosition();
            float offset = (origin - own).magnitude;
            if (offset <= MuzzleReachTolerance)
            {
                return origin;
            }

            Debug.LogWarning($"{name}: точка выстрела игрока {playerId} в {offset:F1} м от его тела — беру серверную", this);
            return own;
        }

        /// <summary>Числом из сети можно пользоваться: без NaN и бесконечностей.</summary>
        private static bool IsUsable(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary>
        /// Выстрел состоялся у авторитета — разослать его остальным, чтобы звук,
        /// вспышка и трассер были у всех одинаковые.
        /// </summary>
        private void HandleHunterFired(HitscanWeapon.HitResult result)
        {
            if (!HasAuthority)
            {
                return;
            }

            network?.AnnounceShot(result.Origin, result.Point, result.Hit);
        }

        /// <summary>Выстрел, посчитанный сервером: отыграть его картинку у себя.</summary>
        public void ApplyNetworkShot(Vector3 origin, Vector3 point, bool hit) =>
            hunter?.PlayRemoteShot(origin, point, hit);

        /// <summary>
        /// Утку убили — сервер прислал точку попадания и импульс отлёта.
        /// Прогресс и выход из гонки приедут отдельно, состоянием: это событие
        /// отвечает только за то, как именно улетит тело.
        /// </summary>
        public void ApplyNetworkDuckDeath(int playerId, Vector3 hitPoint, Vector3 impulse)
        {
            DuckRecord duck = FindDuckById(playerId);
            if (duck?.Elimination == null || duck.Elimination.IsEliminated)
            {
                return;
            }

            duck.Bot?.Stop();
            duck.Elimination.Eliminate(hitPoint, impulse);
        }

        /// <summary>
        /// Роль приехала с сервера. Пересдаём расклад целиком: раздача ролей
        /// идемпотентна, ровно ей же пользуется отладочная пересдача по клавише.
        /// </summary>
        public void ApplyNetworkHunter()
        {
            if (Players.Count == 0)
            {
                return;
            }

            AssignRoles();
        }

        /// <summary>Момент свистка, объявленный сервером.</summary>
        public void ApplyNetworkLiveTime(double value)
        {
            if (HasAuthority)
            {
                return;
            }

            liveTime = value;
        }

        /// <summary>Высота лифта, решённая сервером.</summary>
        public void ApplyNetworkElevatorHeight(float height) => elevator?.ApplyNetworkHeight(height);

        /// <summary>Вернуть лифт под управление этой машины: сетевого состояния больше нет.</summary>
        public void ReleaseNetworkElevator() => elevator?.ReleaseNetworkHeight();

        /// <summary>Обойма и перезарядка, решённые сервером.</summary>
        public void ApplyNetworkHunterWeapon(int ammo, double reloadEndsAt)
        {
            if (hunter != null && hunter.Weapon != null)
            {
                hunter.Weapon.ApplyNetworkState(ammo, reloadEndsAt);
            }
        }

        /// <summary>
        /// Ловушка сработала по решению сервера — отыграть это у себя.
        /// Состояние ловушки приезжает отдельно, маской: здесь только момент,
        /// под звук и вспышку.
        /// </summary>
        public void ApplyNetworkTrapFired(int index)
        {
            if (index < 0 || index >= traps.Length)
            {
                return;
            }

            traps[index]?.PlayFired();
        }

        /// <summary>Состояние ловушек, решённое сервером.</summary>
        public void ApplyNetworkTrapMask(uint mask)
        {
            int count = Mathf.Min(traps.Length, MaxSyncedTraps);
            for (int i = 0; i < count; i++)
            {
                traps[i]?.ApplySprung((mask & (1u << i)) != 0u);
            }
        }

        /// <summary>
        /// Состояние Утки, решённое сервером. Применяется целиком, вместе
        /// с выходом из гонки: без этого клиент до конца раунда считает
        /// погибших живыми — отпускает им движение на свистке, держит их
        /// в списке живых и водит по ним камеру наблюдателя.
        /// </summary>
        public void ApplyNetworkDuck(int playerId, int floor, float progress, int finishOrder, bool dead, float deathTime)
        {
            DuckRecord duck = FindDuckById(playerId);
            if (duck == null)
            {
                return;
            }

            bool wasRetired = duck.Outcome.Retired;

            // Слепок ведём всегда: он и есть то, по чему эта машина потом
            // покажет места, а тела к тому моменту может уже не быть.
            duck.Frozen = new DuckOutcome(floor, progress, finishOrder, dead, deathTime);

            // Аватара ушедшего игрока NGO уничтожил и на этой машине тоже.
            // Состояние доехало в слепок, применять его больше не к чему:
            // тело убрано, из живых Утку надо вычеркнуть.
            if (duck.Progress == null)
            {
                MarkDuckLeft(duck);
                return;
            }

            duck.Progress.ApplyNetworkState(floor, progress, finishOrder, dead, deathTime);

            if (wasRetired == duck.Progress.Retired)
            {
                return;
            }

            if (duck.Progress.Retired)
            {
                ApplyDuckRetired(duck);
                return;
            }

            // Сервер пересдал расклад: выбывшая Утка снова в гонке.
            duck.Elimination?.Restore();
            if (duck.Avatar != null)
            {
                duck.Avatar.MovementLocked = !RoundLive;
            }

            AddToAlive(duck.PlayerId);
        }

        /// <summary>
        /// Утка выбыла по решению сервера. Дошедшую останавливаем, погибшую
        /// роняем: событие выстрела с точкой попадания и импульсом отлёта
        /// приезжает отдельно, но состояние важнее позы — тело не должно
        /// стоять живым только потому, что направление ещё в дороге.
        /// </summary>
        private void ApplyDuckRetired(DuckRecord duck)
        {
            duck.Bot?.Stop();
            RemoveFromAlive(duck.PlayerId);

            if (duck.Outcome.Finished)
            {
                if (duck.Avatar != null)
                {
                    duck.Avatar.MovementLocked = true;
                }

                if (IsLocal(duck.PlayerId))
                {
                    spectator?.Activate(aliveDucks);
                }

                return;
            }

            // Наблюдателя своему игроку включит BodyHidden — тот же путь, что
            // и у авторитета: тело сначала должно долететь и исчезнуть.
            if (duck.Elimination != null && !duck.Elimination.IsEliminated && duck.Avatar != null)
            {
                duck.Elimination.Eliminate(duck.Avatar.transform.position, FallbackDeathImpulse(duck.Avatar));
            }
        }

        /// <summary>
        /// Чем ронять Утку, когда известно только то, что она погибла.
        /// Направление берём от её собственного лица: пока событие выстрела
        /// не приехало, важно, что тело падает, а не куда именно.
        /// </summary>
        private Vector3 FallbackDeathImpulse(PlayerController avatar)
        {
            float force = config != null ? config.DeathImpulse : 18f;
            float lift = config != null ? config.DeathImpulseLift : 0.4f;
            Vector3 direction = avatar != null ? -avatar.Facing : Vector3.back;
            return (direction + Vector3.up * lift).normalized * force;
        }

        /// <summary>
        /// Кто на этой машине распоряжается лифтом и оружием Охотника.
        /// Зовётся после каждой выдачи роли: Attach создаёт оружие заново,
        /// и режим ему надо проставить снова.
        /// </summary>
        private void ApplyHunterAuthority()
        {
            if (Networked)
            {
                network.ConfigureHunter(hunter, IsLocal(hunterPlayerId));
                return;
            }

            if (hunter == null)
            {
                return;
            }

            hunter.Relay = null;

            if (hunter.Weapon != null)
            {
                hunter.Weapon.DrivenExternally = false;
            }
        }

        private void AddToAlive(int playerId)
        {
            for (int i = 0; i < aliveDucks.Count; i++)
            {
                if (aliveDucks[i].Id == playerId)
                {
                    return;
                }
            }

            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    aliveDucks.Add(Players[i]);
                    return;
                }
            }
        }

        // ========== ПОИСК ==========

        private DuckRecord FindDuckByElimination(PlayerElimination elimination)
        {
            // Пустую ссылку не ищем: у ушедших Уток она обнулена, и поиск по
            // null нашёл бы первую попавшуюся из них.
            if (elimination == null)
            {
                return null;
            }

            for (int i = 0; i < ducks.Count; i++)
            {
                if (ducks[i].Elimination == elimination)
                {
                    return ducks[i];
                }
            }

            return null;
        }

        private DuckRecord FindDuckByProgress(DuckProgress progress)
        {
            // Та же причина, что и в поиске по выбыванию: у ушедших ссылка пуста.
            if (progress == null)
            {
                return null;
            }

            for (int i = 0; i < ducks.Count; i++)
            {
                if (ducks[i].Progress == progress)
                {
                    return ducks[i];
                }
            }

            return null;
        }

        private DuckRecord FindDuckById(int playerId)
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                if (ducks[i].PlayerId == playerId)
                {
                    return ducks[i];
                }
            }

            return null;
        }

        private static bool IsLocal(int playerId)
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            return local != null && local.Id == playerId;
        }

        // ========== МЕСТА ==========

        /// <summary>
        /// Спека, раздел 6. Порядок: дошедшие по времени прибытия, затем живые
        /// недошедшие по достигнутой точке, затем погибшие по точке гибели.
        /// Охотник встаёт ровно после дошедших — его место равно числу
        /// финишировавших плюс один.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            ducks.Sort(DuckRanking);

            placementOrder.Clear();
            int finished = 0;
            for (int i = 0; i < ducks.Count; i++)
            {
                placementOrder.Add(ducks[i].PlayerId);
                if (ducks[i].Outcome.Finished)
                {
                    finished++;
                }
            }

            if (hunterPlayerId != SpecialRoleHistory.NoPlayer)
            {
                // Ушедший Охотник встаёт последним (спека, 10.2): он бросил
                // раунд, и вставать выше тех, кто добежал сам, ему не за что.
                // Порядок самих Уток при этом не меняется — живые и так стоят
                // выше погибших и ранжируются по достигнутой точке, а это
                // ровно то, что спека требует от такого раунда.
                int slot = hunterLeft
                    ? placementOrder.Count
                    : Mathf.Clamp(finished, 0, placementOrder.Count);

                placementOrder.Insert(slot, hunterPlayerId);
            }

            // Место обязано быть у всех: тот, кто остался без аватара
            // (дисконнект до старта), иначе выпал бы из результатов и сломал
            // начисление очков.
            for (int i = 0; i < Players.Count; i++)
            {
                if (!placementOrder.Contains(Players[i].Id))
                {
                    placementOrder.Add(Players[i].Id);
                }
            }

            for (int i = 0; i < placementOrder.Count; i++)
            {
                results.Add(placementOrder[i], i + 1);
            }
        }

        /// <summary>
        /// Дошедшие выше живых, живые выше погибших. Внутри группы — кто дальше
        /// по башне, тот выше; у погибших при полном равенстве выше тот, кто
        /// продержался дольше.
        /// </summary>
        private static int CompareDucks(DuckRecord a, DuckRecord b)
        {
            int categoryA = Category(a);
            int categoryB = Category(b);
            if (categoryA != categoryB)
            {
                return categoryA.CompareTo(categoryB);
            }

            // Дошедшие — строго по порядку зачёта. Сравнивать по времени нельзя:
            // два финиша в одном тике дают одинаковое время, а List.Sort
            // нестабилен — места разъехались бы между машинами.
            DuckOutcome left = a.Outcome;
            DuckOutcome right = b.Outcome;

            if (categoryA == 0)
            {
                return left.FinishOrder.CompareTo(right.FinishOrder);
            }

            // Дальше по трассе — выше место, поэтому сравнение развёрнуто.
            int byProgress = -DuckHuntArena.CompareProgress(
                left.Floor, left.Progress,
                right.Floor, right.Progress);
            if (byProgress != 0)
            {
                return byProgress;
            }

            if (categoryA == 2 && !Mathf.Approximately(left.DeathTime, right.DeathTime))
            {
                return right.DeathTime.CompareTo(left.DeathTime);
            }

            // Полное равенство: порядок по идентификатору одинаков на всех
            // машинах, и сортировка перестаёт зависеть от нестабильности List.Sort.
            return a.PlayerId.CompareTo(b.PlayerId);
        }

        private static int Category(DuckRecord duck)
        {
            DuckOutcome outcome = duck.Outcome;
            if (outcome.Finished)
            {
                return 0;
            }

            return outcome.Dead ? 2 : 1;
        }
    }
}
