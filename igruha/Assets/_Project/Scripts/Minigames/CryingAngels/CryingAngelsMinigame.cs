using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

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

        [Header("Дебаг (тест в одиночку)")]
        [Tooltip("Локальный игрок играет за Водящего, иначе за Бегущего")]
        [SerializeField] private bool localPlayerIsKeeper;
        [Tooltip("Переключить роль локального игрока прямо в игре")]
        [SerializeField] private Key roleSwitchKey = Key.F1;

        /// <summary>
        /// Состояние одного Бегущего за раунд. Лучший радиус копится весь раунд
        /// и не сбрасывается ни окаменением, ни авто-респавном застрявшего:
        /// иначе выгодно отсидеться у края, а рывок к центру наказывается.
        /// </summary>
        private sealed class RunnerRecord
        {
            public int PlayerId;
            public PlayerController Avatar;
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

        private PlayerController keeperAvatar;
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

        private void SetBeamEnabled(bool enabled)
        {
            if (BeamEnabled == enabled)
            {
                return;
            }

            BeamEnabled = enabled;
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
                    MoveTo(avatar, spawnPoints?.GetPoint(SpawnRole.Special, 0));
                    continue;
                }

                runners.Add(new RunnerRecord { PlayerId = player.Id, Avatar = avatar });
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
