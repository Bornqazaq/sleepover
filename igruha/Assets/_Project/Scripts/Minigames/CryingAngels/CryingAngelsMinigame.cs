using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.Vision;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Правила «Плачущих ангелов» поверх шаблона: раздача ролей, стартовый
    /// отсчёт с выключенным фонарём, конец раунда и расстановка мест.
    /// Бег, присед, толчки, респавн, таймер, обучалка и HUD берутся готовыми
    /// из Igruha.Core.
    ///
    /// Всё, что меняет исход раунда — раздача ролей, зачёт касания, конец
    /// раунда — идёт через методы под <see cref="MinigameControllerBase.HasAuthority"/>,
    /// а состояние Бегущих живёт в <see cref="RunnerRecord"/>, а не в полях,
    /// разбросанных по компонентам. В сетевой фазе эти точки оборачиваются
    /// в серверные вызовы, а записи — в реплицируемое состояние.
    /// </summary>
    public sealed class CryingAngelsMinigame : MinigameControllerBase
    {
        /// <summary>Ключ истории спец-ролей: своя очередь Водящего, независимая от других игр.</summary>
        private const string KeeperRoleKey = "CryingAngels.Keeper";

        /// <summary>Слой укрытий: на нём живут и камни арены, и статуи окаменевших.</summary>
        private const string CoverLayerName = "Cover";

        /// <summary>Слой пола, стен и постамента — так их кладёт билдер арены.</summary>
        private const string GroundLayerName = "Ground";

        [Header("Арена")]
        [SerializeField] private SpawnPointSet spawnPoints;
        [SerializeField] private CryingAngelsConfig config;

        [Header("Водящий")]
        [Tooltip("Камеры сцены: Бегущим 3rd person, Водящему 1st person")]
        [SerializeField] private MinigameCameraController cameraController;
        [Tooltip("Риг фонаря: Light + KeeperBeam + VisionCone. Вешается на аватар Водящего")]
        [SerializeField] private GameObject keeperRigPrefab;
        [Tooltip("Риг обзора от первого лица со сцены — ему задаётся потолок скорости поворота")]
        [SerializeField] private FirstPersonCameraRig firstPersonRig;

        [Header("Бегущий")]
        [Tooltip("Рамка окаменения на экране своего игрока")]
        [SerializeField] private PetrificationVignette vignette;
        [Tooltip("Камера наблюдателя: дошедший смотрит за оставшимися до конца раунда")]
        [SerializeField] private SpectatorCamera spectator;

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Локальный игрок играет за Водящего, иначе за Бегущего")]
        [SerializeField] private bool localPlayerIsKeeper;
        [Tooltip("Переключить роль локального игрока прямо в игре. Работает только во время раунда, не на обучалке. Не F-клавиша: на macOS их перехватывает система, до игры они не доходят")]
        [SerializeField] private Key roleSwitchKey = Key.P;
        [Tooltip("Гнать болванок к постаменту. Выключить — встанут столбами на своих точках")]
        [SerializeField] private bool driveDummyRunners = true;
        [Tooltip("Водить лучом за болванку-Водящего. Выключить — фонарь замрёт в одном направлении")]
        [SerializeField] private bool driveDummyKeeper = true;

        /// <summary>
        /// Состояние одного Бегущего за раунд. Лучший радиус копится весь раунд
        /// и не сбрасывается ни окаменением, ни авто-респавном застрявшего:
        /// иначе выгодно отсидеться у края, а рывок к центру наказывается.
        /// </summary>
        private sealed class RunnerRecord
        {
            public int PlayerId;
            public PlayerController Avatar;
            public Collider Body;
            public RunnerState State;
            public float BestRadius = float.MaxValue;
            public float BestRadiusTime;
            public bool Touched;
            public float TouchTime;
            /// <summary>Номер по порядку зачёта, с 1. Именно он решает места дошедших.</summary>
            public int TouchOrder;
            /// <summary>Водитель болванки. Пусто у живого игрока и в сетевой катке.</summary>
            public DebugPlayerBot Bot;
        }

        private static readonly Comparison<RunnerRecord> RunnerRanking = CompareRunners;

        private readonly List<RunnerRecord> runners = new List<RunnerRecord>(8);
        private readonly List<int> placementOrder = new List<int>(8);
        /// <summary>Буфер под состав Бегущих для сети: переиспользуется, чтобы не аллоцировать на раздаче ролей.</summary>
        private readonly List<int> runnerIds = new List<int>(8);
        // Списки под VisionCone.Evaluate: переиспользуются, чтобы не аллоцировать в FixedUpdate.
        private readonly List<Collider> runnerBodies = new List<Collider>(8);
        private readonly List<PetrifiedStatue> statues = new List<PetrifiedStatue>(8);
        private readonly Dictionary<Collider, RunnerRecord> runnerByBody = new Dictionary<Collider, RunnerRecord>(8);
        private VisionCone keeperVision;

        private CryingAngelsNetwork network;
        private PlayerController keeperAvatar;
        private AngelKeeper keeper;
        private KeeperTouchZone touchZone;
        private int keeperPlayerId = SpecialRoleHistory.NoPlayer;
        private Vector3 arenaCenter;
        private float countdownRemaining;
        private float roundElapsed;
        private int touchCounter;
        /// <summary>Про сбившийся состав луча ругаемся один раз за раунд, а не каждый физический такт.</summary>
        private bool beamMismatchReported;

        /// <summary>Фонарь Водящего горит: отсчёт кончился, раунд идёт. Гейт для KeeperBeam (14.4).</summary>
        public bool BeamEnabled { get; private set; }

        /// <summary>Фонарь включился или погас — чтобы луч не опрашивал флаг каждый кадр.</summary>
        public event Action<bool> BeamEnabledChanged;

        /// <summary>Аватар Водящего этого раунда. Null до раздачи ролей.</summary>
        public PlayerController Keeper => keeperAvatar;

        /// <summary>Центр арены — точка постамента. По ней меряется радиус Бегущих.</summary>
        public Vector3 ArenaCenter => arenaCenter;

        public CryingAngelsConfig Config => config;

        /// <summary>
        /// Потолок скорости поворота Водящего под фактическое число Бегущих.
        /// Границы кривой берутся из определения мини-игры, поэтому число
        /// игроков нигде не зашито.
        /// </summary>
        public float KeeperTurnSpeed
        {
            get
            {
                if (config == null || Definition == null)
                {
                    return 0f;
                }

                return config.GetTurnSpeed(runners.Count, Definition.MinPlayers - 1, Definition.MaxPlayers - 1);
            }
        }

        protected override void Awake()
        {
            base.Awake();

            // Сетевой половины может не быть вовсе: сцену открывают напрямую
            // для соло-теста, и тогда правила работают как обычный MonoBehaviour.
            network = GetComponent<CryingAngelsNetwork>();
        }

        /// <summary>Идёт сетевая катка и сетевая половина живая.</summary>
        private bool Networked => network != null && network.IsActive;

        protected override void OnPlayersReady()
        {
            CacheArenaCenter();
            AssignRoles();
        }

        protected override void OnRoundStarted()
        {
            roundElapsed = 0f;
            countdownRemaining = config != null ? config.StartCountdown : 0f;
            beamMismatchReported = false;
            ResetRunnersForRound();
            SetBeamEnabled(false);
            SetDummyBotsRunning(true);
        }

        /// <summary>
        /// Вернуть Бегущих в исходное состояние раунда.
        ///
        /// Без этого второй раунд подряд ломается: дошедший до постамента
        /// вычеркнут из целей луча и помечен дошедшим, а раздача ролей —
        /// единственное место, где записи собирались заново, — на новый раунд
        /// не зовётся. В итоге луч физически видит игрока, но не считает его
        /// целью: ни заморозки, ни счётчика. Замерено 16.08 на host + client.
        ///
        /// Начало раунда обязано быть самодостаточным: этого ждёт и rematch
        /// из EPIC 4, и любой возврат в ту же сцену.
        /// </summary>
        private void ResetRunnersForRound()
        {
            touchCounter = 0;

            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                runner.Touched = false;
                runner.TouchTime = 0f;
                runner.TouchOrder = 0;
                runner.BestRadius = float.MaxValue;
                runner.BestRadiusTime = 0f;
                runner.State?.ResetState();

                if (runner.Avatar != null && !runner.Avatar.gameObject.activeSelf)
                {
                    runner.Avatar.gameObject.SetActive(true);
                }
            }

            // Списки конуса собираются из тех же записей — их надо пересобрать
            // после возврата снятых, иначе цели так и останутся вычеркнутыми.
            RebindVision();
            BindVignette();

            if (spectator != null && spectator.IsActive)
            {
                spectator.Deactivate();
            }

            PublishRunnerStates();
        }

        protected override void OnRoundEnded()
        {
            SetDummyBotsRunning(false);
            RestoreRunnersAfterRound();
            SetBeamEnabled(false);
            ReleaseAllRunners();
            PublishRunnerStates();
            ClearStatues();
            vignette?.Track(null);
            Hud?.HideCountdown();
        }

        private void Update()
        {
            if (RoundActive && countdownRemaining > 0f)
            {
                Hud?.ShowCountdown(countdownRemaining);
            }

            HandleDebugRoleSwitch();
        }

        private void FixedUpdate()
        {
            if (!RoundActive)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            // Отсчёт крутится у всех: это надпись на экране. Ждать её из сети
            // значило бы показывать клиенту застывшую тройку — а вот сам момент,
            // когда загорается фонарь, объявляет сервер (см. TickCountdown).
            TickCountdown(deltaTime);

            // Луч доводится до желаемого прямо перед расчётом засветки: конус и
            // решение по нему обязаны быть одного такта, иначе на разворотах
            // теряется до двух градусов — на границе конуса это уже разница
            // между «поймал» и «не поймал».
            network?.ServerTickBeamYaw(deltaTime, KeeperTurnSpeed);

            // Прогресс и исход считает только авторитет.
            if (!HasAuthority)
            {
                return;
            }

            roundElapsed += deltaTime;
            TrackRunnerProgress();
            EvaluateBeam();
            EvaluateTouches();
            PublishRunnerStates();
        }

        /// <summary>
        /// Кто дотянулся до Водящего. Считается после луча: заморозка этого же
        /// тика обязана успеть отменить касание, иначе пойманный вплотную
        /// засчитывал бы его в тот самый момент, когда его уже держат.
        ///
        /// Бегущие перебираются строго по списку — на этом порядке стоит
        /// правило спеки о двух касаниях в один тик: он одинаков на всех
        /// машинах, а порядок событий физики — нет.
        /// </summary>
        private void EvaluateTouches()
        {
            if (touchZone == null)
            {
                return;
            }

            for (int i = 0; i < runners.Count && RoundActive; i++)
            {
                RunnerRecord runner = runners[i];
                if (runner.Touched || runner.Avatar == null)
                {
                    continue;
                }

                // Касание засчитывается только свободному: замороженный вплотную
                // к постаменту иначе доходил бы, ничего не сделав.
                if (runner.State != null && !runner.State.IsFree)
                {
                    continue;
                }

                if (touchZone.Contains(runner.Avatar.transform.position))
                {
                    RegisterRunnerTouch(runner.PlayerId);
                }
            }
        }

        /// <summary>
        /// Отсчёт идёт уже внутри раунда: таймер тикает, ввод у всех включён,
        /// Бегущие расходятся — не горит только фонарь. Так у них есть фора,
        /// а Водящий не смотрит в пустой зал.
        /// </summary>
        private void TickCountdown(float deltaTime)
        {
            if (countdownRemaining <= 0f)
            {
                return;
            }

            countdownRemaining -= deltaTime;
            if (countdownRemaining > 0f)
            {
                return;
            }

            countdownRemaining = 0f;
            Hud?.HideCountdown();

            // Фонарь — исход раунда: клиент дожидается сети, а не зажигает свой.
            if (HasAuthority)
            {
                SetBeamEnabled(true);
            }
        }

        /// <summary>
        /// Кого сейчас держит луч. Физический тик, потому что решение зависит
        /// от положения тел, а те двигаются в FixedUpdate.
        /// </summary>
        private void EvaluateBeam()
        {
            if (!BeamEnabled || !EnsureBeamTargets())
            {
                return;
            }

            keeperVision.Evaluate(runnerBodies);

            // Счётчик копится и откатывается у всех, а не только у засвеченных:
            // откат — половина смысла механики, он идёт, пока игрок уже бежит.
            float hottest = 0f;
            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                if (runner.State == null)
                {
                    continue;
                }

                bool wasPetrified = runner.State.Current == RunnerState.Phase.Petrified;
                runner.State.Tick(runner.Body != null && keeperVision.IsVisible(runner.Body), Time.fixedDeltaTime);

                if (!wasPetrified && runner.State.Current == RunnerState.Phase.Petrified)
                {
                    TrySpawnStatue(runner);
                }

                hottest = Mathf.Max(hottest, runner.State.PetrifyProgress);
            }

            ApplyBeamFeedback(hottest);
        }

        /// <summary>
        /// Луч уходит от белого к красному по счётчику самой «горячей» цели.
        /// Без этого Водящий не понимает, что счётчик существует, и бросает
        /// жертву за полсекунды до окаменения.
        /// </summary>
        private void ApplyBeamFeedback(float progress)
        {
            if (config == null || !config.BeamColorFeedback || keeper == null)
            {
                return;
            }

            keeper.SetBeamColor(Color.Lerp(config.BeamColorIdle, config.BeamColorPetrifying, progress));
        }

        /// <summary>Решение авторитета: фонарь загорелся или погас.</summary>
        private void SetBeamEnabled(bool enabled)
        {
            ApplyBeamEnabled(enabled);
            network?.PublishBeam(enabled);
        }

        /// <summary>
        /// Сколько Бегущих луч обязан держать целями прямо сейчас: все, кто
        /// ещё в раунде и у кого есть тело.
        /// </summary>
        private int CountBeamTargets()
        {
            int expected = 0;
            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                if (!runner.Touched && runner.Body != null)
                {
                    expected++;
                }
            }

            return expected;
        }

        /// <summary>
        /// Сверить цели луча с составом и починить, если разошлись. Ложь —
        /// светить нечем: конуса нет, считать засветку не по чему.
        ///
        /// Проверка живёт в такте луча, а не в одной точке на зажигании фонаря.
        /// Список тел и конус собираются только в <see cref="RebindVision"/>,
        /// то есть на раздаче ролей и на старте раунда, а между ними никто не
        /// проверял, что они всё ещё описывают тот же состав. Раунд с
        /// разошедшимся списком проходит вхолостую и молча: игрок стоит в луче,
        /// целью не считается, ни заморозки, ни счётчика, ни строчки в консоли.
        /// Ровно так выглядела жалоба с плейтеста 16.08 — «иду к свету и ничего».
        ///
        /// Стоит это перебора по Бегущим (их не больше семи) и сравнения двух
        /// чисел — дешевле, чем раунд, который игрок считает сломанной игрой.
        /// Ругаемся один раз за раунд: чинит проверка молча, а причину надо
        /// видеть в консоли.
        /// </summary>
        private bool EnsureBeamTargets()
        {
            int expected = CountBeamTargets();
            if (runnerBodies.Count == expected && keeperVision != null)
            {
                return true;
            }

            if (!beamMismatchReported)
            {
                beamMismatchReported = true;
                Debug.LogWarning($"🕯️ Плачущие ангелы: луч сбился с состава — целей {runnerBodies.Count} " +
                                 $"при {expected} Бегущих, конус {(keeperVision != null ? "есть" : "ПОТЕРЯН")}. " +
                                 "Пересобираю: без этого раунд прошёл бы без заморозок", this);
            }

            RebindVision();

            if (keeperVision != null)
            {
                return true;
            }

            // Конус пересборкой не вернуть: он живёт на риге Водящего, а его
            // в раунде нет. Такой раунд обязан был закончиться в DropKeeper.
            return false;
        }

        /// <summary>Фонарь переключил сервер — применяем у себя.</summary>
        public void ApplyNetworkBeam(bool enabled) => ApplyBeamEnabled(enabled);

        private void ApplyBeamEnabled(bool enabled)
        {
            if (BeamEnabled == enabled)
            {
                return;
            }

            BeamEnabled = enabled;

            // Гаснущий фонарь отпускает всех тем же кадром: держать заморозку
            // выключенным лучом нечем.
            if (!enabled)
            {
                ReleaseAllRunners();
            }

            keeper?.SetBeamVisible(enabled);
            BeamEnabledChanged?.Invoke(enabled);
        }

        private void TrackRunnerProgress()
        {
            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                if (runner.Touched || runner.Avatar == null)
                {
                    continue;
                }

                Vector3 offset = runner.Avatar.transform.position - arenaCenter;
                offset.y = 0f;
                float radius = offset.magnitude;
                if (radius >= runner.BestRadius)
                {
                    continue;
                }

                runner.BestRadius = radius;
                runner.BestRadiusTime = roundElapsed;
            }
        }

        // ========== РОЛИ ==========

        private void CacheArenaCenter()
        {
            if (spawnPoints == null)
            {
                Debug.LogError($"{name}: не назначен SpawnPointSet — арену не собрать", this);
                return;
            }

            SpawnPoint pedestal = spawnPoints.GetPoint(SpawnRole.Special, 0);
            arenaCenter = pedestal != null ? pedestal.transform.position : transform.position;
        }

        /// <summary>
        /// Водящий — один. В сетевой катке его выбирает сервер из тех, кто ещё
        /// не был Водящим в этой катке; в соло-тесте роль переключается кнопкой,
        /// иначе Водящего невозможно пощупать в одиночку.
        /// </summary>
        private void AssignRoles()
        {
            if (Players.Count == 0)
            {
                return;
            }

            // Игрок снова в деле: наблюдение снимается вместе с ролью и
            // возвращает ему управление.
            if (spectator != null && spectator.IsActive)
            {
                spectator.Deactivate();
            }

            runners.Clear();
            touchCounter = 0;
            touchZone = null;
            keeperAvatar = null;
            keeperPlayerId = SpecialRoleHistory.NoPlayer;

            int keeperIndex = PickKeeperIndex();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                PlayerController avatar = player.Avatar;
                if (avatar == null)
                {
                    continue;
                }

                // Пересдача ролей возвращает на арену и тех, кто уже дошёл:
                // иначе дошедший получает роль, оставаясь снятым аватаром.
                if (!avatar.gameObject.activeSelf)
                {
                    avatar.gameObject.SetActive(true);
                }

                if (i == keeperIndex)
                {
                    keeperPlayerId = player.Id;
                    keeperAvatar = avatar;
                    keeper = SetupKeeper(avatar);
                    MoveTo(avatar, spawnPoints?.GetPoint(SpawnRole.Special, 0));

                    // Болванка, которой выпала роль Водящего, обязана бросить
                    // ввод: иначе она уедет с постамента, продолжая идти к нему.
                    if (avatar.TryGetComponent(out DebugPlayerBot keeperBot))
                    {
                        keeperBot.Stop();
                    }

                    continue;
                }

                // Бывший Водящий обязан вернуться в норму: без Detach он остался бы
                // обездвиженным и неуязвимым, уже будучи Бегущим.
                ClearKeeper(avatar);
                runners.Add(CreateRunner(player.Id, avatar));
            }

            // Разнос по кольцу считается по числу Бегущих, а не игроков: иначе
            // при одном Водящем в кольце остаётся дыра размером с его слот.
            for (int i = 0; i < runners.Count; i++)
            {
                MoveTo(runners[i].Avatar, spawnPoints?.GetSpreadPoint(SpawnRole.Default, i, runners.Count));
            }

            if (keeperPlayerId != SpecialRoleHistory.NoPlayer && HasAuthority)
            {
                SessionScoreboard.Current?.MarkSpecialRole(keeperPlayerId, KeeperRoleKey);
            }

            // Расклад уходит в сеть до настройки луча: направление, с которого
            // луч стартует, задаётся здесь же.
            PublishRoles();

            // Одна строка в консоль на каждую раздачу: по ней с плейтеста видно,
            // собрался ли состав и досталась ли роль. Без неё «у меня не
            // работает» приходится воспроизводить вслепую.
            //
            // Про отсутствие Водящего предупреждает только авторитет. У клиента
            // первая раздача идёт всегда до того, как расклад приедет из сети,
            // то есть «Водящего нет» там — норма, а не поломка: предупреждение
            // в его консоли сбивало бы с толку ровно в том разборе, ради
            // которого эти строки и заведены.
            if (keeperPlayerId != SpecialRoleHistory.NoPlayer)
            {
                Debug.Log($"🕯️ Плачущие ангелы: Водящий — игрок {keeperPlayerId}, Бегущих {runners.Count}, " +
                          $"потолок поворота {KeeperTurnSpeed:F0}°/с");
            }
            else if (HasAuthority)
            {
                Debug.LogWarning($"🕯️ Плачущие ангелы: Водящего НЕТ (участников {Players.Count}). " +
                                 "Фонарь не загорится: роль раздаётся с двух участников", this);
            }

            RebindVision();
            BindVignette();
            SetDummyBotsRunning(RoundActive);
            keeper?.ApplyTurnSpeed(firstPersonRig, KeeperTurnSpeed);
            keeper?.SetBeamVisible(BeamEnabled);
            network?.ConfigureKeeper(keeper, IsLocal(keeperPlayerId), firstPersonRig);
            ApplyRoleCamera();
        }

        /// <summary>
        /// Сервер объявляет расклад раунда: кому выпала роль и кто в списке
        /// Бегущих. У остальных машин этот же расклад приезжает репликацией и
        /// вызывает пересдачу через <see cref="ApplyNetworkKeeper"/>.
        /// </summary>
        private void PublishRoles()
        {
            if (!Networked || !HasAuthority)
            {
                return;
            }

            PublishRunnerRoster();

            // Луч стартует оттуда, куда развёрнуто тело: иначе он на первом
            // кадре смотрит в нулевой азимут и ползёт к игроку с потолком
            // скорости — до двух секунд светит мимо.
            float startYaw = keeperAvatar != null ? keeperAvatar.transform.eulerAngles.y : 0f;
            network.PublishKeeper(keeperPlayerId, startYaw);
        }

        /// <summary>
        /// Отдать в сеть только состав Бегущих. Отдельно от <see cref="PublishRoles"/>
        /// потому, что тот заодно перезадаёт стартовое направление луча: при
        /// уходе одного из Бегущих это дёрнуло бы фонарь Водящего на ровном месте.
        /// </summary>
        private void PublishRunnerRoster()
        {
            if (!Networked || !HasAuthority)
            {
                return;
            }

            runnerIds.Clear();
            for (int i = 0; i < runners.Count; i++)
            {
                runnerIds.Add(runners[i].PlayerId);
            }

            network.ServerSyncRoster(runnerIds);
        }

        // ========== ПРИЁМ СЕТЕВОГО СОСТОЯНИЯ ==========

        /// <summary>
        /// Роль приехала с сервера. Пересдаём расклад целиком: раздача ролей
        /// идемпотентна, ровно ей же пользуется отладочная пересдача по клавише.
        /// </summary>
        public void ApplyNetworkKeeper()
        {
            if (Players.Count == 0)
            {
                return;
            }

            AssignRoles();
        }

        /// <summary>Состояние Бегущего решено сервером — применяем у себя.</summary>
        public void ApplyNetworkRunnerState(
            int playerId,
            RunnerState.Phase phase,
            int freezePose,
            float freezePoseTime,
            float petrifyProgress,
            bool finished)
        {
            RunnerRecord runner = FindRunner(playerId);
            if (runner == null)
            {
                return;
            }

            RunnerState.Phase before = runner.State != null ? runner.State.Current : RunnerState.Phase.Free;
            runner.State?.ApplyNetworkState(phase, freezePose, freezePoseTime, petrifyProgress);

            // Статую ставит каждая машина у себя по тому же переходу состояния:
            // это укрытие, а не эффект, и на клиентах оно обязано быть.
            if (before != RunnerState.Phase.Petrified && phase == RunnerState.Phase.Petrified)
            {
                TrySpawnStatue(runner);
            }

            if (finished && !runner.Touched)
            {
                runner.Touched = true;

                // Снимать дошедшего с арены имеет смысл только внутри раунда.
                // Если флаг приехал уже к результатам, аватар обязан остаться:
                // конец раунда только что вернул туда всех.
                if (RoundActive)
                {
                    ApplyRunnerRetired(runner);
                }
            }
            else if (!finished && runner.Touched)
            {
                // Сервер пересдал расклад: дошедший снова в игре. Без обратного
                // хода его аватар остался бы снятым весь следующий раунд.
                runner.Touched = false;
                ApplyRunnerReturned(runner);
            }
        }

        /// <summary>
        /// Состав Бегущих приехал с сервера: убрать тех, кого в нём больше нет.
        /// Ушедший иначе остался бы в списке с уничтоженным аватаром и попал бы
        /// в сортировку мест.
        /// </summary>
        public void ApplyNetworkRunnerRoster(IReadOnlyList<int> playerIds)
        {
            for (int i = runners.Count - 1; i >= 0; i--)
            {
                bool present = false;
                for (int j = 0; j < playerIds.Count; j++)
                {
                    if (playerIds[j] == runners[i].PlayerId)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    DropRunner(i);
                }
            }
        }

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Игрок вышел из матча. Зовёт сетевой слой на ближайшем тике после
        /// дисконнекта — только у сервера.
        ///
        /// Правило спеки (раздел 10.2): уход Водящего заканчивает раунд сразу,
        /// потому что светить больше некому и оставшиеся 90 секунд Бегущие
        /// просто шли бы к пустому постаменту. Уход Бегущего раунд не трогает.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            bool wasKeeper = playerId == keeperPlayerId;

            RemoveRunner(playerId);
            RemoveStatuesOf(playerId);

            // Из состава раунда — иначе ушедший получит место в результатах.
            RemovePlayer(playerId);

            if (wasKeeper)
            {
                DropKeeper();
                PublishRunnerRoster();
                EndMinigame();
                return;
            }

            PublishRunnerRoster();

            // Последний Бегущий вышел — светить не в кого, дожидаться нечего.
            if (RoundActive && runners.Count == 0)
            {
                EndMinigame();
            }
        }

        /// <summary>Водящего в раунде больше нет: гасим фонарь и снимаем роль с учёта.</summary>
        private void DropKeeper()
        {
            SetBeamEnabled(false);

            keeperPlayerId = SpecialRoleHistory.NoPlayer;
            keeperAvatar = null;
            keeper = null;
            touchZone = null;

            // Конус жил на аватаре ушедшего: пересобираем привязку, иначе
            // остаёмся подписанными на события уничтоженного объекта.
            RebindVision();

            // Роль обязана уехать в сеть отдельно: иначе у клиентов Водящим
            // до конца раунда числится тот, кого в матче уже нет.
            network?.PublishKeeper(SpecialRoleHistory.NoPlayer, 0f);
            network?.ConfigureKeeper(null, false, firstPersonRig);
        }

        private void RemoveRunner(int playerId)
        {
            for (int i = 0; i < runners.Count; i++)
            {
                if (runners[i].PlayerId == playerId)
                {
                    DropRunner(i);
                    return;
                }
            }
        }

        private void DropRunner(int index)
        {
            RunnerRecord runner = runners[index];
            if (runner.Body != null)
            {
                runnerBodies.Remove(runner.Body);
                runnerByBody.Remove(runner.Body);
            }

            runners.RemoveAt(index);
        }

        /// <summary>Статуи ушедшего снимаются вместе с ним: укрытие от того, кого в матче нет, — подарок остальным.</summary>
        private void RemoveStatuesOf(int playerId)
        {
            for (int i = statues.Count - 1; i >= 0; i--)
            {
                PetrifiedStatue statue = statues[i];
                if (statue == null || statue.OwnerId == playerId)
                {
                    statue?.Remove();
                    statues.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Отдать в сеть состояния Бегущих. В сеть уходит только изменившееся,
        /// поэтому вызов в каждом такте ничего не стоит, пока на арене тихо.
        /// </summary>
        private void PublishRunnerStates()
        {
            if (!Networked || !HasAuthority)
            {
                return;
            }

            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                if (runner.State == null)
                {
                    continue;
                }

                network.ServerSyncRunner(
                    runner.PlayerId,
                    runner.State.Current,
                    runner.State.FreezePose,
                    runner.State.FreezePoseTime,
                    runner.State.PetrifyProgress,
                    runner.Touched);
            }
        }

        private RunnerRecord CreateRunner(int playerId, PlayerController avatar)
        {
            RunnerState state = avatar.GetComponent<RunnerState>();
            if (state == null)
            {
                state = avatar.gameObject.AddComponent<RunnerState>();
            }

            // Роль могла уходить в Водящие и вернуться — компоненты в этом
            // случае погашены, а не сняты (см. ClearRunnerLeftovers).
            state.enabled = true;
            state.Configure(config);
            state.ResetState();

            if (!avatar.TryGetComponent(out FreezePoseDriver poseDriver))
            {
                poseDriver = avatar.gameObject.AddComponent<FreezePoseDriver>();
            }

            poseDriver.enabled = true;

            return new RunnerRecord
            {
                PlayerId = playerId,
                Avatar = avatar,
                Body = avatar.GetComponent<Collider>(),
                State = state,
                Bot = EnsureDummyBot(avatar)
            };
        }

        /// <summary>
        /// Болванке соло-теста нужен свой водитель: стоящие столбами болванки
        /// не дают проверить ни порядок финиша, ни досрочный конец раунда, ни
        /// переключение наблюдателя, а за Водящего при них смотреть не на что.
        ///
        /// Живому игроку и любой сетевой копии бот не ставится: там ввод идёт
        /// от человека, и второй источник ввода — это гонка.
        /// </summary>
        private DebugPlayerBot EnsureDummyBot(PlayerController avatar)
        {
            if (!driveDummyRunners || SessionScoreboard.IsNetworked)
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

            // Препятствие для болванки — ровно та геометрия, из которой собрана
            // арена: пол со стенами и постаментом да укрытия. Своих она не
            // объезжает намеренно — толкучка у постамента здесь и нужна.
            bot.Configure(LayerMask.GetMask(GroundLayerName, CoverLayerName));
            return bot;
        }

        /// <summary>
        /// Болванки бегут только внутри раунда: на обучалке они успели бы
        /// столпиться у постамента ещё до старта, а касание там не засчитывается.
        /// </summary>
        private void SetDummyBotsRunning(bool running)
        {
            for (int i = 0; i < runners.Count; i++)
            {
                DebugPlayerBot bot = runners[i].Bot;
                if (bot == null)
                {
                    continue;
                }

                if (running)
                {
                    bot.SetTarget(arenaCenter);
                }
                else
                {
                    bot.Stop();
                }
            }
        }

        /// <summary>
        /// Пересобрать списки для конуса и переподписаться на его события.
        /// Зовётся на каждой раздаче ролей: при пересдаче меняется и состав
        /// Бегущих, и сам конус (он живёт на риге нового Водящего).
        /// </summary>
        private void RebindVision()
        {
            if (keeperVision != null)
            {
                keeperVision.TargetEntered -= OnRunnerLit;
                keeperVision.TargetExited -= OnRunnerUnlit;
                keeperVision.ResetVisibility();
            }

            runnerBodies.Clear();
            runnerByBody.Clear();
            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];

                // Дошедшего в цели не возвращаем: он уже снят с арены. Иначе
                // пересборка посреди раунда воскрешала бы его как цель, и
                // сверка состава расходилась бы на каждом такте.
                if (runner.Touched || runner.Body == null)
                {
                    continue;
                }

                runnerBodies.Add(runner.Body);
                runnerByBody[runner.Body] = runner;
            }

            keeperVision = keeper != null ? keeper.Vision : null;
            if (keeperVision != null)
            {
                keeperVision.TargetEntered += OnRunnerLit;
                keeperVision.TargetExited += OnRunnerUnlit;
            }
        }

        /// <summary>
        /// Рамка окаменения показывает только своего игрока. Водящему она не
        /// нужна: он не каменеет, и лишняя рамка сбивала бы ему обзор.
        /// </summary>
        private void BindVignette()
        {
            if (vignette == null)
            {
                return;
            }

            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            RunnerRecord localRunner = local != null ? FindRunner(local.Id) : null;
            vignette.Track(localRunner?.State);
        }

        private void OnRunnerLit(Collider body) => SetRunnerFrozen(body, true);

        private void OnRunnerUnlit(Collider body) => SetRunnerFrozen(body, false);

        /// <summary>
        /// Единственная точка заморозки. Выход из-под луча мгновенный — это
        /// правило спеки: задержка на разморозку читается как залипание.
        /// </summary>
        private void SetRunnerFrozen(Collider body, bool frozen)
        {
            if (!HasAuthority || body == null)
            {
                return;
            }

            if (runnerByBody.TryGetValue(body, out RunnerRecord runner) && runner.State != null)
            {
                runner.State.SetFrozen(frozen);
            }
        }

        private AngelKeeper SetupKeeper(PlayerController avatar)
        {
            AngelKeeper component = avatar.GetComponent<AngelKeeper>();
            if (component == null)
            {
                component = avatar.gameObject.AddComponent<AngelKeeper>();
            }

            component.Attach(keeperRigPrefab);
            ClearRunnerLeftovers(avatar);
            SetupKeeperBot(avatar, component);

            // Зона касания висит на самом Водящем, а не на постаменте: трогают
            // его, а постамент арена пересобирает билдером, и любой объект,
            // положенный туда руками, следующая пересборка сотрёт.
            if (!avatar.TryGetComponent(out touchZone))
            {
                touchZone = avatar.gameObject.AddComponent<KeeperTouchZone>();
            }

            touchZone.Configure(config != null ? config.TouchRadius : 0f);
            return component;
        }

        /// <summary>
        /// Снять с Водящего следы роли Бегущего. В сетевой катке роль приезжает
        /// вторым заходом: на первой раздаче клиент ещё не знает Водящего и
        /// заводит Бегущего каждому, включая будущего Водящего. Оставленный
        /// <see cref="FreezePoseDriver"/> держал бы на нём стоп-кадр анимации.
        ///
        /// Компоненты гасим, а не удаляем: <see cref="Destroy"/> откладывается
        /// до конца кадра, а <see cref="FreezePoseDriver"/> требует
        /// <see cref="RunnerState"/> — Unity откажется снимать состояние, пока
        /// на объекте висит зависящий от него драйвер, и напишет об этом
        /// в консоль. Пересдача роли обратно в Бегущие включает их снова.
        /// </summary>
        private static void ClearRunnerLeftovers(PlayerController avatar)
        {
            if (avatar.TryGetComponent(out FreezePoseDriver poseDriver))
            {
                poseDriver.enabled = false;
            }

            if (avatar.TryGetComponent(out RunnerState leftover))
            {
                leftover.ResetState();
                leftover.enabled = false;
            }
        }

        private static void ClearKeeper(PlayerController avatar)
        {
            AngelKeeper component = avatar.GetComponent<AngelKeeper>();
            if (component != null)
            {
                component.Detach();
            }

            // Бывший Водящий не должен остаться ходячей зоной касания.
            KeeperTouchZone zone = avatar.GetComponent<KeeperTouchZone>();
            if (zone != null)
            {
                Destroy(zone);
            }

            DebugKeeperBot bot = avatar.GetComponent<DebugKeeperBot>();
            if (bot != null)
            {
                Destroy(bot);
            }
        }

        /// <summary>
        /// Болванке в роли Водящего нужен свой водитель луча: без него фонарь
        /// стоит неподвижно, и за Бегущего играть не во что — половина игры
        /// это чтение чужого взгляда.
        ///
        /// Живому игроку бот не ставится и снимается при пересдаче роли: иначе
        /// он крутил бы тело под собственной камерой игрока.
        /// </summary>
        private void SetupKeeperBot(PlayerController avatar, AngelKeeper role)
        {
            bool isDummy = avatar.TryGetComponent(out PlayerInputReader reader) && !reader.LocallyControlled;
            DebugKeeperBot bot = avatar.GetComponent<DebugKeeperBot>();

            if (!driveDummyKeeper || !isDummy || SessionScoreboard.IsNetworked)
            {
                if (bot != null)
                {
                    Destroy(bot);
                }

                return;
            }

            if (bot == null)
            {
                bot = avatar.gameObject.AddComponent<DebugKeeperBot>();
            }

            bot.Configure(this, role != null ? role.Vision : null);
        }

        /// <summary>
        /// Статуя на месте окаменения — по флагу конфига. Ставится там, где
        /// игрока настигло, а не там, куда его вернуло: смысл укрытия в том,
        /// что оно осталось на опасном месте.
        /// </summary>
        private void TrySpawnStatue(RunnerRecord runner)
        {
            if (config == null || !config.StatuesRemainAsCover || runner.Body == null)
            {
                return;
            }

            var capsule = runner.Body as CapsuleCollider;
            if (capsule == null)
            {
                return;
            }

            PetrifiedStatue statue = PetrifiedStatue.Create(capsule, LayerMask.NameToLayer(CoverLayerName), transform, runner.PlayerId);
            if (statue != null)
            {
                statues.Add(statue);
            }
        }

        private void ClearStatues()
        {
            for (int i = 0; i < statues.Count; i++)
            {
                if (statues[i] != null)
                {
                    statues[i].Remove();
                }
            }

            statues.Clear();
        }

        /// <summary>Погасший фонарь обязан всех отпустить, иначе замороженный останется стоять навсегда.</summary>
        private void ReleaseAllRunners()
        {
            keeperVision?.ResetVisibility();
            for (int i = 0; i < runners.Count; i++)
            {
                runners[i].State?.ResetState();
            }
        }

        /// <summary>
        /// Камера зависит от роли, а не от мини-игры: Водящий смотрит от первого
        /// лица, Бегущие — из-за спины. Режим из MinigameDefinition для этой игры
        /// поэтому не годится, он один на всех.
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

            bool localIsKeeper = local.Id == keeperPlayerId;
            cameraController.Apply(
                localIsKeeper ? CameraMode.FirstPerson : CameraMode.ThirdPerson,
                local.Avatar.transform);
        }

        private int PickKeeperIndex()
        {
            // Меньше двух участников — Водящего нет, играть некому: раунд
            // всё равно доживёт до результатов, но роль не раздаём.
            if (Players.Count < 2)
            {
                return -1;
            }

            if (!SessionScoreboard.IsNetworked)
            {
                return localPlayerIsKeeper ? 0 : Mathf.Min(1, Players.Count - 1);
            }

            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            if (scoreboard == null || !scoreboard.HasAuthority)
            {
                // Клиент роль не выбирает — она приезжает из сети. Пока не
                // приехала, Водящего нет вовсе: назначить наугад значило бы
                // обездвижить не того игрока и повесить ему на голову фонарь.
                return IndexOfPlayer(network != null ? network.KeeperPlayerId : SpecialRoleHistory.NoPlayer);
            }

            int pickedId = scoreboard.PickSpecialRole(KeeperRoleKey);
            int picked = IndexOfPlayer(pickedId);
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

        private void MoveTo(PlayerController avatar, SpawnPoint point)
        {
            if (avatar == null || point == null)
            {
                return;
            }

            avatar.RequestTeleport(point.transform.position, point.transform.rotation);

            // Точка спавна Бегущего — она же его точка возврата после окаменения.
            if (avatar.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(point.transform);
            }
        }

        private void HandleDebugRoleSwitch()
        {
            // В сетевой катке роли одинаковы у всех — переключать их нельзя.
            if (SessionScoreboard.IsNetworked || !RoundActive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[roleSwitchKey].wasPressedThisFrame)
            {
                return;
            }

            localPlayerIsKeeper = !localPlayerIsKeeper;
            AssignRoles();
            Debug.Log($"Плачущие ангелы: локальный игрок теперь {(localPlayerIsKeeper ? "ВОДЯЩИЙ" : "БЕГУЩИЙ")}");
        }

        // ========== КАСАНИЕ И КОНЕЦ РАУНДА ==========

        /// <summary>
        /// Бегущий дотронулся до Водящего. Единственная точка зачёта: её зовёт
        /// зона касания на постаменте (14.8), и в сетевой фазе она уходит
        /// за серверную проверку без правки логики ниже.
        /// </summary>
        public void RegisterRunnerTouch(int playerId)
        {
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            RunnerRecord runner = FindRunner(playerId);
            if (runner == null || runner.Touched)
            {
                return;
            }

            runner.Touched = true;
            runner.TouchTime = roundElapsed;
            runner.TouchOrder = ++touchCounter;
            runner.BestRadius = 0f;

            RetireRunner(runner);

            if (AllRunnersTouched())
            {
                EndMinigame();
            }
        }

        /// <summary>
        /// Дошедший уходит с арены. Оставить его стоять нельзя: он продолжал бы
        /// ловить луч, закрывать собой Водящего и служить укрытием остальным,
        /// уже выйдя из игры.
        /// </summary>
        private void RetireRunner(RunnerRecord runner)
        {
            // Сначала отпустить, потом убрать из списка конуса: убранная цель
            // считается вышедшей из луча, но искать её состояние будет уже негде.
            runner.State?.ResetState();
            runner.Bot?.Stop();

            if (runner.Body != null)
            {
                runnerBodies.Remove(runner.Body);
                runnerByBody.Remove(runner.Body);
            }

            ApplyRunnerRetired(runner);
        }

        /// <summary>
        /// Видимая половина ухода — она обязана отработать на каждой машине,
        /// поэтому вынесена из серверного учёта: у авторитета её зовёт
        /// <see cref="RetireRunner"/>, у остальных — приехавший флаг «дошёл».
        /// </summary>
        private void ApplyRunnerRetired(RunnerRecord runner)
        {
            // Аватар снимается до передачи камеры наблюдателю, а не после:
            // наблюдатель выбирает первую живую цель в момент включения, и на
            // ещё живом своём теле он выберет самого игрока — тот на кадр
            // увидит, как исчезает он сам.
            if (runner.Avatar != null)
            {
                runner.Avatar.gameObject.SetActive(false);
            }

            if (IsLocal(runner.PlayerId))
            {
                // Рамку окаменения снимаем вместе с аватаром: своего Бегущего
                // на арене больше нет, а рамка осталась бы висеть до конца раунда.
                vignette?.Track(null);
                spectator?.Activate(Players);
            }
        }

        /// <summary>Обратный ход к <see cref="ApplyRunnerRetired"/>: игрок снова в раунде.</summary>
        private void ApplyRunnerReturned(RunnerRecord runner)
        {
            if (runner.Avatar != null && !runner.Avatar.gameObject.activeSelf)
            {
                runner.Avatar.gameObject.SetActive(true);
            }

            if (!IsLocal(runner.PlayerId))
            {
                return;
            }

            if (spectator != null && spectator.IsActive)
            {
                spectator.Deactivate();
            }

            vignette?.Track(runner.State);
        }

        private static bool IsLocal(int playerId)
        {
            SessionPlayer local = SessionScoreboard.Current?.LocalPlayer;
            return local != null && local.Id == playerId;
        }

        /// <summary>
        /// К экрану результатов снятые возвращаются на арену: их убрали только
        /// на время раунда, а на итогах в зале обязаны стоять все.
        ///
        /// Возвращаются все записи, а не только помеченные дошедшими: у клиента
        /// флаг «дошёл» и фаза раунда приезжают разными сообщениями, и порядок
        /// их прихода ничем не задан. Когда фаза обгоняла флаг, клиент
        /// заканчивал раунд, ещё ничего не восстановив, а следом прятал аватар —
        /// и на экране результатов свой Бегущий пропадал с арены, хотя у
        /// остальных он там стоял. Замерено 16.08 на host + client.
        ///
        /// Наблюдение при этом не выключается: выход из него вернул бы игроку
        /// управление, а на результатах все стоят на месте по правилам шаблона.
        ///
        /// Флаг «дошёл» здесь не трогаем: по нему сразу после этого считаются
        /// места (<see cref="CollectResults"/>), а чистит его начало раунда.
        /// </summary>
        private void RestoreRunnersAfterRound()
        {
            for (int i = 0; i < runners.Count; i++)
            {
                RunnerRecord runner = runners[i];
                if (runner.Avatar != null && !runner.Avatar.gameObject.activeSelf)
                {
                    runner.Avatar.gameObject.SetActive(true);
                }
            }
        }

        private bool AllRunnersTouched()
        {
            for (int i = 0; i < runners.Count; i++)
            {
                if (!runners[i].Touched)
                {
                    return false;
                }
            }

            return runners.Count > 0;
        }

        private RunnerRecord FindRunner(int playerId)
        {
            for (int i = 0; i < runners.Count; i++)
            {
                if (runners[i].PlayerId == playerId)
                {
                    return runners[i];
                }
            }

            return null;
        }

        // ========== МЕСТА ==========

        /// <summary>
        /// Спека, раздел 6. Бегущие: сначала дошедшие по времени касания, затем
        /// остальные по лучшему радиусу за раунд. Водящий встаёт ровно после
        /// дошедших: его место = число дошедших + 1.
        /// </summary>
        protected override void CollectResults(MinigameResults results)
        {
            runners.Sort(RunnerRanking);

            placementOrder.Clear();
            int touched = 0;
            for (int i = 0; i < runners.Count; i++)
            {
                placementOrder.Add(runners[i].PlayerId);
                if (runners[i].Touched)
                {
                    touched++;
                }
            }

            if (keeperPlayerId != SpecialRoleHistory.NoPlayer)
            {
                placementOrder.Insert(Mathf.Clamp(touched, 0, placementOrder.Count), keeperPlayerId);
            }

            // Место обязано быть у всех: тот, кто остался без аватара (дисконнект
            // до старта), иначе выпал бы из результатов и сломал начисление очков.
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

        private static int CompareRunners(RunnerRecord a, RunnerRecord b)
        {
            if (a.Touched != b.Touched)
            {
                return a.Touched ? -1 : 1;
            }

            // Дошедшие — строго по порядку зачёта. Сравнивать по времени нельзя:
            // два касания в одном тике дают одинаковый TouchTime, а List.Sort
            // нестабилен — места разъехались бы между машинами. Счётчик же
            // возрастает при каждом зачёте и одинаков у всех.
            if (a.Touched)
            {
                return a.TouchOrder.CompareTo(b.TouchOrder);
            }

            // Ближе к центру — выше. При равном радиусе выше тот, кто достиг его раньше.
            const float RadiusEpsilon = 0.05f;
            if (Mathf.Abs(a.BestRadius - b.BestRadius) > RadiusEpsilon)
            {
                return a.BestRadius.CompareTo(b.BestRadius);
            }

            if (!Mathf.Approximately(a.BestRadiusTime, b.BestRadiusTime))
            {
                return a.BestRadiusTime.CompareTo(b.BestRadiusTime);
            }

            // Полное равенство: порядок по идентификатору — он одинаков везде,
            // и сортировка перестаёт зависеть от нестабильности List.Sort.
            return a.PlayerId.CompareTo(b.PlayerId);
        }
    }
}
