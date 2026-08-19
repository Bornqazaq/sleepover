using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Arena;
using Igruha.Core.CameraSystems;
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
        }

        private static readonly Comparison<DuckRecord> DuckRanking = CompareDucks;

        private readonly List<DuckRecord> ducks = new List<DuckRecord>(8);
        private readonly List<SessionPlayer> aliveDucks = new List<SessionPlayer>(8);
        private readonly List<int> placementOrder = new List<int>(8);

        private HunterController hunter;
        private PlayerController hunterAvatar;
        private int hunterPlayerId = SpecialRoleHistory.NoPlayer;
        private float countdownRemaining;
        private float roundElapsed;
        private int finishCounter;

        /// <summary>Стартовый отсчёт кончился: Утки бегут, Охотник стреляет.</summary>
        public bool RoundLive { get; private set; }

        // ========== ЖИЗНЕННЫЙ ЦИКЛ ==========

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
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (finishZone != null)
            {
                finishZone.DuckArrived -= HandleDuckArrived;
            }
        }

        protected override void OnRoundStarted()
        {
            roundElapsed = 0f;
            finishCounter = 0;
            countdownRemaining = config != null ? config.StartCountdown : 0f;

            ResetTraps();
            SetRoundLive(false);
        }

        protected override void OnRoundEnded()
        {
            SetRoundLive(false);
            StopAllBots();
            RestoreDucks();
            Hud?.HideCountdown();
            Hud?.HideStatus();

            // На экране результатов прицел висел бы поверх таблицы мест.
            if (crosshair != null)
            {
                crosshair.SetActive(false);
            }
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
            // Прогресс и зачёт решают исход раунда, поэтому считает только авторитет.
            if (!RoundActive || !HasAuthority)
            {
                return;
            }

            roundElapsed += Time.fixedDeltaTime;
            TickCountdown();

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
        /// </summary>
        private void TickCountdown()
        {
            if (countdownRemaining <= 0f)
            {
                return;
            }

            countdownRemaining -= Time.fixedDeltaTime;
            if (countdownRemaining > 0f)
            {
                return;
            }

            countdownRemaining = 0f;
            Hud?.HideCountdown();
            SetRoundLive(true);
        }

        private void SetRoundLive(bool live)
        {
            RoundLive = live;

            for (int i = 0; i < ducks.Count; i++)
            {
                PlayerController avatar = ducks[i].Avatar;
                if (avatar != null && !ducks[i].Progress.Retired)
                {
                    avatar.MovementLocked = !live;
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

            SetDummyBotsRunning(live);
            RefreshHunterStatus();
        }

        private void SampleProgress()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                ducks[i].Progress.Sample(arena);
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
            SetRoundLive(RoundActive && countdownRemaining <= 0f);
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
                LastFloor = progress.Floor
            };

            // Подписка одна на аватар за раунд: Restore не отписывает, поэтому
            // снимаем прошлую подписку перед новой — при пересдаче ролей иначе
            // накапливаются дубли и один выстрел засчитывается несколько раз.
            elimination.Hidden -= HandleAnyDuckHidden;
            elimination.Hidden += HandleAnyDuckHidden;

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
            if (hunter == null)
            {
                return;
            }

            hunter.DuckShot -= HandleDuckShot;
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

                if (duck.IsDummy && !driveDummyDucks)
                {
                    PlaceAsTarget(duck, i);
                }
                else
                {
                    MoveTo(duck.Avatar, spawnPoints?.GetSpreadPoint(SpawnRole.Default, i, ducks.Count));
                }

                duck.Progress.ResetProgress(arena);
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
                // Клиент роль не выбирает — придёт из сети. Ростер уже одинаков,
                // поэтому до этого момента берём первого детерминированно.
                return 0;
            }

            int pickedId = scoreboard.PickSpecialRole(HunterRoleKey);
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == pickedId)
                {
                    return i;
                }
            }

            return 0;
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

            // Прицел — только своему Охотнику. Утке он не нужен и мешал бы.
            if (crosshair != null)
            {
                crosshair.SetActive(localIsHunter);
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
            if (duck == null || duck.Progress.Retired)
            {
                return;
            }

            // Прогресс фиксируется ДО импульса: иначе отлёт успевает утащить
            // тело с того места, где Утку застрелили, и в зачёт попадает точка
            // падения вместо точки гибели.
            duck.Progress.Sample(arena);
            duck.Progress.MarkDead(roundElapsed);

            duck.Bot?.Stop();
            RemoveFromAlive(duck.PlayerId);
            victim.Eliminate(hitPoint, impulse);

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

        /// <summary>Тело подстреленной Утки исчезло — своему игроку пора в наблюдатели.</summary>
        private void HandleAnyDuckHidden()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                DuckRecord duck = ducks[i];
                if (duck.Elimination != null && duck.Elimination.IsHidden && IsLocal(duck.PlayerId))
                {
                    spectator?.Activate(aliveDucks);
                    return;
                }
            }
        }

        private void CheckRoundOver()
        {
            for (int i = 0; i < ducks.Count; i++)
            {
                if (!ducks[i].Progress.Retired)
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
                ducks[i].Elimination?.Restore();

                if (ducks[i].Avatar != null)
                {
                    ducks[i].Avatar.MovementLocked = false;
                }
            }
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
                ducks[i].Bot?.Stop();
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
                if (duck.Bot == null || duck.Progress.Retired || duck.Avatar == null)
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

        // ========== ПОИСК ==========

        private DuckRecord FindDuckByElimination(PlayerElimination elimination)
        {
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
            for (int i = 0; i < ducks.Count; i++)
            {
                if (ducks[i].Progress == progress)
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
                if (ducks[i].Progress.Finished)
                {
                    finished++;
                }
            }

            if (hunterPlayerId != SpecialRoleHistory.NoPlayer)
            {
                placementOrder.Insert(Mathf.Clamp(finished, 0, placementOrder.Count), hunterPlayerId);
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
            if (categoryA == 0)
            {
                return a.Progress.FinishOrder.CompareTo(b.Progress.FinishOrder);
            }

            // Дальше по трассе — выше место, поэтому сравнение развёрнуто.
            int byProgress = -DuckHuntArena.CompareProgress(
                a.Progress.Floor, a.Progress.Progress,
                b.Progress.Floor, b.Progress.Progress);
            if (byProgress != 0)
            {
                return byProgress;
            }

            if (categoryA == 2 && !Mathf.Approximately(a.Progress.DeathTime, b.Progress.DeathTime))
            {
                return b.Progress.DeathTime.CompareTo(a.Progress.DeathTime);
            }

            // Полное равенство: порядок по идентификатору одинаков на всех
            // машинах, и сортировка перестаёт зависеть от нестабильности List.Sort.
            return a.PlayerId.CompareTo(b.PlayerId);
        }

        private static int Category(DuckRecord duck)
        {
            if (duck.Progress.Finished)
            {
                return 0;
            }

            return duck.Progress.Dead ? 2 : 1;
        }
    }
}
