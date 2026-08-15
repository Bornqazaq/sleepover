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

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Локальный игрок играет за Водящего, иначе за Бегущего")]
        [SerializeField] private bool localPlayerIsKeeper;
        [Tooltip("Переключить роль локального игрока прямо в игре. Работает только во время раунда, не на обучалке. Не F-клавиша: на macOS их перехватывает система, до игры они не доходят")]
        [SerializeField] private Key roleSwitchKey = Key.P;

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
        }

        private static readonly Comparison<RunnerRecord> RunnerRanking = CompareRunners;

        private readonly List<RunnerRecord> runners = new List<RunnerRecord>(8);
        private readonly List<int> placementOrder = new List<int>(8);
        // Списки под VisionCone.Evaluate: переиспользуются, чтобы не аллоцировать в FixedUpdate.
        private readonly List<Collider> runnerBodies = new List<Collider>(8);
        private readonly Dictionary<Collider, RunnerRecord> runnerByBody = new Dictionary<Collider, RunnerRecord>(8);
        private VisionCone keeperVision;

        private PlayerController keeperAvatar;
        private AngelKeeper keeper;
        private int keeperPlayerId = SpecialRoleHistory.NoPlayer;
        private Vector3 arenaCenter;
        private float countdownRemaining;
        private float roundElapsed;
        private int touchCounter;

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

        protected override void OnPlayersReady()
        {
            CacheArenaCenter();
            AssignRoles();
        }

        protected override void OnRoundStarted()
        {
            roundElapsed = 0f;
            countdownRemaining = config != null ? config.StartCountdown : 0f;
            SetBeamEnabled(false);
        }

        protected override void OnRoundEnded()
        {
            SetBeamEnabled(false);
            ReleaseAllRunners();
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
            // Отсчёт и прогресс — исход раунда, поэтому считает только авторитет.
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            roundElapsed += Time.fixedDeltaTime;
            TickCountdown();
            TrackRunnerProgress();
            EvaluateBeam();
        }

        /// <summary>
        /// Отсчёт идёт уже внутри раунда: таймер тикает, ввод у всех включён,
        /// Бегущие расходятся — не горит только фонарь. Так у них есть фора,
        /// а Водящий не смотрит в пустой зал.
        /// </summary>
        private void TickCountdown()
        {
            if (countdownRemaining <= 0f)
            {
                return;
            }

            countdownRemaining -= Time.fixedDeltaTime;
            if (countdownRemaining <= 0f)
            {
                countdownRemaining = 0f;
                Hud?.HideCountdown();
                SetBeamEnabled(true);
            }
        }

        /// <summary>
        /// Кого сейчас держит луч. Физический тик, потому что решение зависит
        /// от положения тел, а те двигаются в FixedUpdate.
        /// </summary>
        private void EvaluateBeam()
        {
            if (!BeamEnabled || keeperVision == null)
            {
                return;
            }

            keeperVision.Evaluate(runnerBodies);
        }

        private void SetBeamEnabled(bool enabled)
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

            runners.Clear();
            touchCounter = 0;
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

                if (i == keeperIndex)
                {
                    keeperPlayerId = player.Id;
                    keeperAvatar = avatar;
                    keeper = SetupKeeper(avatar);
                    MoveTo(avatar, spawnPoints?.GetPoint(SpawnRole.Special, 0));
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

            RebindVision();
            keeper?.ApplyTurnSpeed(firstPersonRig, KeeperTurnSpeed);
            keeper?.SetBeamVisible(BeamEnabled);
            ApplyRoleCamera();
        }

        private RunnerRecord CreateRunner(int playerId, PlayerController avatar)
        {
            RunnerState state = avatar.GetComponent<RunnerState>();
            if (state == null)
            {
                state = avatar.gameObject.AddComponent<RunnerState>();
            }

            state.ResetState();

            return new RunnerRecord
            {
                PlayerId = playerId,
                Avatar = avatar,
                Body = avatar.GetComponent<Collider>(),
                State = state
            };
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
                if (runner.Body == null)
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
            return component;
        }

        private static void ClearKeeper(PlayerController avatar)
        {
            AngelKeeper component = avatar.GetComponent<AngelKeeper>();
            if (component != null)
            {
                component.Detach();
            }
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
                // Клиент роль не выбирает — придёт из сети. До этого момента
                // ростер уже одинаков, поэтому берём первого детерминированно.
                return 0;
            }

            int pickedId = scoreboard.PickSpecialRole(KeeperRoleKey);
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == pickedId)
                {
                    return i;
                }
            }

            return 0;
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

            if (AllRunnersTouched())
            {
                EndMinigame();
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
