using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.Spawning;
using Igruha.Core.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Правила «Переноски предмета»: кто в какой команде, когда конец, кто
    /// выиграл и какое у каждого место.
    ///
    /// Ролей в игре нет — обе команды делают одно и то же, — поэтому нет и
    /// дыры «отвалился единственный носитель роли». Худший случай, команда из
    /// одного человека, легален и играется.
    ///
    /// Досрочного конца нет намеренно: даже полный бак раунд не останавливает.
    /// Излишек некуда девать, а соперник ещё может догнать.
    ///
    /// <b>Сеть.</b> Состав и счёт решает сервер и объявляет через
    /// <see cref="CarryItemNetwork"/>; клиент их только отображает. Без сетевой
    /// половины (сцена открыта напрямую) правила работают как раньше — авторитет
    /// у единственной машины.
    /// </summary>
    public sealed class CarryItemMinigame : MinigameControllerBase
    {
        /// <summary>Всё, что нужно одной команде на арене. Парой полей это разъехалось бы при первой же правке.</summary>
        [System.Serializable]
        private sealed class TeamRig
        {
            [Tooltip("Кран команды в стартовой зоне: зона наполнения и стоянка тележки")]
            public WaterTap Tap;

            /// <summary>Тележка команды на этот раунд. Спавнит сервер, у клиента приезжает и усыновляется по команде.</summary>
            [System.NonSerialized] public WaterCart Cart;
            [Tooltip("Бак команды — он же её счёт")]
            public WaterTank Tank;
            [Tooltip("Роль точек спавна этой команды")]
            public SpawnRole SpawnRole = SpawnRole.TeamA;
            [Tooltip("Маршрут болванок соло-теста: доска, горлышко, доска. Живому игроку не нужен")]
            public CarryItemBotRoute Route;
        }

        /// <summary>Участник раунда: место в составе и команда.</summary>
        private struct Entry
        {
            public int PlayerId;
            public TeamSide Team;
            public PlayerController Avatar;
            public Transform OriginalRespawn;
            public double RespawnAt;
            public bool Waiting;
            public bool AwaitingRespawnPosition;
            public bool MotorWasEnabled;
            public RigidbodyConstraints BodyConstraints;
            public bool WasLocked;
            public bool HandsWereBlocked;

            /// <summary>
            /// Игрок вышел из матча. Строку не удаляем: место ему полагается
            /// наравне с остальными, по накопленному командой на момент выхода
            /// (спека 10.1). Из состава он выбывает только как пара рук.
            /// </summary>
            public bool Left;
        }

        [Header("Переноска предмета")]
        [SerializeField] private CarryItemConfig config;
        [SerializeField] private SpawnPointSet spawnPoints;
        [SerializeField] private CartRamDetector ramDetector;
        [Tooltip("Префаб тележки. Спавнит сервер на стоянке крана, по одной на команду, на весь раунд")]
        [SerializeField] private WaterCart cartPrefab;
        [Tooltip("Плашка диктора: объявляет закрытые ходки. Пусто — молча")]
        [SerializeField] private AnnouncerBanner announcer;
        [SerializeField] private TeamProgressBar progressBar;
        [SerializeField] private CarryItemRespawnPresentation respawnPresentation;

        [Header("Команды")]
        [SerializeField] private TeamRig teamA = new TeamRig { SpawnRole = SpawnRole.TeamA };
        [SerializeField] private TeamRig teamB = new TeamRig { SpawnRole = SpawnRole.TeamB };

        [Header("Арена")]
        [Tooltip("Ниже этой отметки бутыль считается улетевшей в пропасть и теряется целиком")]
        [SerializeField] private float voidLevel = -5f;
        [Tooltip("Слои, которые болванки соло-теста считают препятствием")]
        [SerializeField] private LayerMask botObstacles;

        public CartCoordinationHud CoordinationHud { get; private set; }

        private readonly List<Entry> entries = new List<Entry>(8);
        private readonly List<TeamRanking.Entry> rankingBuffer = new List<TeamRanking.Entry>(8);
        private readonly List<CarryItemDebugBot> bots = new List<CarryItemDebugBot>(8);

        /// <summary>Состав под отправку в сеть. Переиспользуется — раунд не должен мусорить.</summary>
        private readonly List<CarryItemMemberNetState> rosterBuffer = new List<CarryItemMemberNetState>(8);

        /// <summary>
        /// Куда ушла вода за раунд: команда × причина, единиц.
        ///
        /// Нужен приёмке и плейтесту. Без разреза «расплескали» — это одно
        /// число, из которого не видно, что чинить: перекос от рассинхрона,
        /// балка над горлышком или пропорция тарана. Считается по тому же
        /// событию, что и сама потеря, поэтому разойтись со счётом не может.
        ///
        /// Живёт у авторитета: потери считает он один.
        /// </summary>
        private readonly int[,] spentByReason =
            new int[3, System.Enum.GetValues(typeof(WaterLossReason)).Length];

        private CarryItemState state;
        private CarryItemNetwork net;
        private Coroutine countdownRoutine;

        /// <summary>
        /// Раунд оборван уходом целой команды, и победитель назначен не по
        /// воде, а по этому факту. Отдельным флагом, а не значением
        /// <see cref="TeamSide.None"/>: «победителя нет» — это законный исход
        /// (обе команды с нулём), и спутать его с «никто не обрывал» нельзя.
        /// </summary>
        private bool winnerForced;
        private TeamSide forcedWinner = TeamSide.None;

        /// <summary>Числа игры. Нужны болванкам и предметам арены.</summary>
        public CarryItemConfig Config => config;

        /// <summary>Отметка пропасти. Нужна тележке, приехавшей из сети: в трафик её не гоняем.</summary>
        public float VoidLevel => voidLevel;

        /// <summary>Счёт раунда одной структурой. У клиента — то, что приехало от сервера.</summary>
        public CarryItemState State => state;

        /// <summary>
        /// Сколько участников в ростере сессии. По изменению этого числа
        /// сетевая половина понимает, что приехавший раньше состав пора
        /// разобрать заново: тела появляются не в тот же миг, что состав.
        /// </summary>
        public int RosterCount => Players.Count;

        protected override void Awake()
        {
            base.Awake();
            net = GetComponent<CarryItemNetwork>();
        }

        // ========== СТАРТ ==========

        protected override void OnPlayersReady()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: CarryItemMinigame без CarryItemConfig — числа брать неоткуда", this);
                return;
            }

            state = default;
            winnerForced = false;
            forcedWinner = TeamSide.None;
            System.Array.Clear(spentByReason, 0, spentByReason.Length);

            // Составы делит и разводит сервер. Клиент дождётся объявленного
            // состава: повторив деление у себя, он получил бы те же команды
            // только при точно совпавшем порядке ростера.
            if (HasAuthority)
            {
                AssignTeams();
                PlaceTeams();
                PublishRoster();
            }
            else
            {
                entries.Clear();
            }

            ConfigureRigs();

            if (HasAuthority)
            {
                SpawnCarts();
            }

            AttachBots();

            progressBar?.ResetBars(config.TankCapacity);
            RefreshLocalTeam();
            respawnPresentation?.Bind(Players);
            if (CoordinationHud == null)
            {
                var root = new GameObject("Cart coordination", typeof(RectTransform));
                root.transform.SetParent(transform, false);
                CoordinationHud = root.AddComponent<CartCoordinationHud>();
                var label = Hud != null ? Hud.GetComponentInChildren<TMPro.TMP_Text>(true) : null;
                CoordinationHud.Bind(this, label != null ? label.font : null);
            }
            if (GetComponentInChildren<CarryWaterHud>() == null)
            {
                var waterHud = new GameObject("Carry water HUD", typeof(RectTransform));
                waterHud.transform.SetParent(transform, false);
                waterHud.AddComponent<CarryWaterHud>().Bind(this, Hud, announcer);
            }

        }

        /// <summary>
        /// Делит сервер, детерминированно, по порядковому номеру в составе.
        /// Компенсации за неравенство нет: меньшая команда несёт устойчивее,
        /// зато у неё меньше рук на саботаж (спека 2.2).
        /// </summary>
        private void AssignTeams()
        {
            entries.Clear();

            for (int i = 0; i < Players.Count; i++)
            {
                SessionPlayer player = Players[i];
                entries.Add(new Entry
                {
                    PlayerId = player.Id,
                    Team = TeamAssignment.SideFor(i),
                    Avatar = player.Avatar,
                    OriginalRespawn = player.Avatar != null &&
                                      player.Avatar.TryGetComponent(out PlayerRespawner respawner)
                        ? respawner.RespawnPoint
                        : null
                });
            }

            Debug.Log($"🫙 [Переноска] составы: A — {TeamAssignment.TeamASize(Players.Count)}, " +
                      $"B — {TeamAssignment.TeamBSize(Players.Count)} из {Players.Count}");
        }

        /// <summary>Объявить состав всем. Один вызов на раунд плюс правка при уходе игрока.</summary>
        private void PublishRoster()
        {
            if (net == null)
            {
                return;
            }

            rosterBuffer.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                rosterBuffer.Add(new CarryItemMemberNetState
                {
                    PlayerId = entries[i].PlayerId,
                    Team = (byte)entries[i].Team,
                    Left = entries[i].Left,
                    RespawnAt = entries[i].RespawnAt
                });
            }

            net.PublishRoster(rosterBuffer);
        }

        /// <summary>
        /// Состав приехал с сервера. Персонажей ищем в ростере сессии: тела
        /// свои на каждой машине, а по сети едут только номера участников.
        /// </summary>
        public void ApplyNetworkRoster(IReadOnlyList<CarryItemMemberNetState> members)
        {
            var previous = entries.ToArray();
            bool compositionChanged = previous.Length != members.Count;
            entries.Clear();

            for (int i = 0; i < members.Count; i++)
            {
                PlayerController avatar = AvatarOf(members[i].PlayerId);
                var entry = new Entry
                {
                    PlayerId = members[i].PlayerId,
                    Team = (TeamSide)members[i].Team,
                    Left = members[i].Left,
                    Avatar = avatar,
                    OriginalRespawn = avatar != null && avatar.TryGetComponent(out PlayerRespawner respawner)
                        ? respawner.RespawnPoint
                        : null
                };
                for (int j = 0; j < previous.Length; j++)
                    if (previous[j].PlayerId == entry.PlayerId && previous[j].Avatar == avatar)
                    { entry = previous[j]; break; }
                if (i >= previous.Length || previous[i].PlayerId != entry.PlayerId ||
                    previous[i].Avatar != avatar || previous[i].Left != members[i].Left)
                    compositionChanged = true;
                entry.Left = members[i].Left;
                entry.RespawnAt = members[i].RespawnAt;
                SetFallWaiting(ref entry, entry.RespawnAt > 0 && !entry.Left);
                entries.Add(entry);
            }

            // Число поручней у тележки клиенту не считать: оно приезжает
            // состоянием самой тележки вместе с уровнем воды.
            RefreshLocalTeam();
            respawnPresentation?.Bind(Players);

            // Болванку автопрогона вешаем только теперь: на OnPlayersReady у
            // этой машины состава ещё не было, и вешать её было не на кого.
            // Без этого стенд простаивал бы у всех, кроме хоста, — а выглядело
            // бы это как «клиенты не играют», то есть как сетевой баг.
            if (compositionChanged) AttachBots();
        }

        /// <summary>Счёт приехал с сервера. Клиент только показывает — считать ему нечего.</summary>
        public void ApplyNetworkState(in CarryItemState value)
        {
            CarryItemState previous = state;
            state = value;
            AnnounceTrips(previous, state);

            teamA.Tank?.ApplyNetworkLevel(state.TeamA.Water);
            teamB.Tank?.ApplyNetworkLevel(state.TeamB.Water);

            progressBar?.SetValue(TeamSide.A, state.TeamA.Water, config.TankCapacity);
            progressBar?.SetValue(TeamSide.B, state.TeamB.Water, config.TankCapacity);
        }

        private PlayerController AvatarOf(int playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == playerId)
                {
                    return Players[i].Avatar;
                }
            }

            return null;
        }

        private void RefreshLocalTeam() =>
            progressBar?.SetLocalTeam(TeamOfPlayer(SessionScoreboard.Current?.LocalPlayer?.Id ?? -1));

        /// <summary>
        /// Развести по стартовым зонам. Спавнер раздаёт точки одной роли, а
        /// команд здесь две, поэтому расстановку делают правила игры — и они же
        /// переставляют точку респавна: свалившийся в пропасть обязан вернуться
        /// к своему штабелю, а не к чужому.
        /// </summary>
        private void PlaceTeams()
        {
            if (spawnPoints == null)
            {
                Debug.LogError($"{name}: не назначен SpawnPointSet — разводить команды не по чему", this);
                return;
            }

            int indexA = 0;
            int indexB = 0;
            int sizeA = TeamAssignment.TeamASize(entries.Count);
            int sizeB = TeamAssignment.TeamBSize(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Avatar == null)
                {
                    continue;
                }

                bool isA = entry.Team == TeamSide.A;
                SpawnRole role = isA ? teamA.SpawnRole : teamB.SpawnRole;
                int slot = isA ? indexA++ : indexB++;
                int count = isA ? sizeA : sizeB;

                SpawnPoint point = spawnPoints.GetSpreadPoint(role, slot, count);
                if (point == null)
                {
                    continue;
                }

                entry.Avatar.RequestTeleport(point.transform.position, point.transform.rotation);

                if (entry.Avatar.TryGetComponent(out PlayerRespawner respawner))
                {
                    respawner.SetRespawnPoint(point.transform);
                }
            }
        }

        private void ConfigureRigs()
        {
            SetUpRig(teamA, TeamSide.A, SizeOf(TeamSide.A));
            SetUpRig(teamB, TeamSide.B, SizeOf(TeamSide.B));

            ramDetector?.Configure(config);
        }

        /// <summary>
        /// Сколько человек в команде <b>сейчас</b>. Вышедшие не считаются: по
        /// этому числу раздаются ручки у бутыли, а призрак ручку не держит.
        /// В местах они при этом участвуют — там перебирается весь состав.
        /// </summary>
        private int SizeOf(TeamSide side)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Team == side && !entries[i].Left)
                {
                    count++;
                }
            }

            return count;
        }

        private void SetUpRig(TeamRig rig, TeamSide side, int teamSize)
        {
            if (rig.Tank != null)
            {
                rig.Tank.Configure(config, side);
                rig.Tank.Delivered += (amount, time) => OnDelivered(side, amount, time);
                rig.Tank.TripFinished += () => OnTripFinished(side);
            }

            if (rig.Tap == null)
            {
                Debug.LogError($"{name}: у команды {side} нет крана — набирать воду неоткуда", this);
                return;
            }

            rig.Tap.Configure(config, side);
        }

        // ========== ТЕЛЕЖКИ ==========

        /// <summary>
        /// Тележки спавнит сервер на стоянках кранов — по одной на команду и на
        /// весь раунд. Тара больше не выдаётся и не исчезает: слилась — стоит
        /// пустая, упала в пропасть — вернётся на стоянку сама.
        /// </summary>
        private void SpawnCarts()
        {
            SpawnCart(teamA, TeamSide.A);
            SpawnCart(teamB, TeamSide.B);
            ramDetector?.SetCarts(teamA.Cart, teamB.Cart);
        }

        private void SpawnCart(TeamRig rig, TeamSide side)
        {
            if (rig.Tap == null || rig.Cart != null)
            {
                return;
            }

            if (cartPrefab == null)
            {
                Debug.LogError($"{name}: не назначен префаб тележки — команде {side} возить нечего", this);
                return;
            }

            WaterCart cart = Instantiate(cartPrefab, rig.Tap.DockPosition, rig.Tap.DockRotation);
            cart.name = $"Cart_{side}";
            cart.Initialize(config, side, Mathf.Max(1, SizeOf(side)), voidLevel, rig.Tap.DockPosition, rig.Tap.DockRotation);
            AttachCart(rig, side, cart);

            if (WorldAuthority.IsNetworkSession && cart.TryGetComponent(out NetworkObject netObject))
            {
                netObject.Spawn();
            }
        }

        /// <summary>
        /// Записать тележку за командой: фильтр своих, кран, разрез потерь.
        /// Зовёт и сервер при спавне, и клиент — за тележку, приехавшую из сети.
        /// </summary>
        private void AttachCart(TeamRig rig, TeamSide side, WaterCart cart)
        {
            rig.Cart = cart;
            rig.Tap?.AttachCart(cart);
            cart.ConfigureClaims(TeamOfAvatar, SizeOf, player => CartCarriedBy(player) == null);
            cart.Stability.FirstSpill += AnnounceSpill;

            // Разрез потерь ведёт тот, кто их считает. У клиента ChangeWater
            // молчит, и подписка здесь дала бы вечные нули в отчёте.
            if (HasAuthority)
            {
                cart.WaterChanged += (amount, reason) => spentByReason[(int)side, (int)reason] += amount;
            }
        }

        /// <summary>
        /// Тележка приехала из сети: сервер заспавнил её на старте, а этой
        /// машине осталось прицепить к ней числа игры, стоянку и записать за
        /// краном своей команды.
        /// </summary>
        public bool AdoptNetworkCart(WaterCart cart)
        {
            if (cart == null || config == null)
            {
                return false;
            }

            TeamSide side = cart.Team;
            TeamRig rig = RigOf(side);
            if (rig == null || rig.Tap == null)
            {
                return false;
            }

            if (rig.Cart == cart) return true;
            cart.ApplyNetworkSetup(config, voidLevel, rig.Tap.DockPosition, rig.Tap.DockRotation);
            AttachCart(rig, side, cart);
            ramDetector?.SetCarts(teamA.Cart, teamB.Cart);
            return true;
        }

        /// <summary>Сколько воды команда потеряла по этой причине за раунд, единиц.</summary>
        public int SpentBy(TeamSide side, WaterLossReason reason) => spentByReason[(int)side, (int)reason];

        // ========== ПОТЕРЯ ТЕЛЕЖЕК И РЕСПАВН ==========

        private void FixedUpdate()
        {
            if (!HasAuthority || !Phase.IsGameplay() || config == null) return;
            if (!StartCountdownActive && FleetExhausted)
            {
                EndMinigame();
                return;
            }
            bool changed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Avatar == null || entry.Left) continue;
                // The owner applies RequestTeleport; until its transform arrives,
                // the server still sees the old fall position. Do not start a
                // second penalty for that same fall.
                if (entry.AwaitingRespawnPosition)
                {
                    if (entry.Avatar.Position.y < voidLevel) continue;
                    entry.AwaitingRespawnPosition = false;
                }
                if (entry.RespawnAt <= 0 && entry.Avatar.Position.y < voidLevel)
                {
                    CartCarriedBy(entry.Avatar)?.Carry.ReleaseFor(entry.Avatar, CarryReleaseReason.RoundEnded);
                    if (entry.Avatar.TryGetComponent(out PlayerCarryAbility carry)) carry.Drop();
                    entry.RespawnAt = NetworkClock.Now + config.RespawnDelaySeconds;
                    SetFallWaiting(ref entry, true);
                    Debug.Log($"[CarryRespawn] wait player={entry.PlayerId} duration={config.RespawnDelaySeconds:F1}");
                    changed = true;
                }
                else if (entry.RespawnAt > 0 && NetworkClock.Now >= entry.RespawnAt)
                {
                    SetFallWaiting(ref entry, false);
                    entry.RespawnAt = 0;
                    entry.AwaitingRespawnPosition = true;
                    if (entry.Avatar.TryGetComponent(out PlayerRespawner respawner)) respawner.Respawn();
                    Debug.Log($"[CarryRespawn] return player={entry.PlayerId}");
                    changed = true;
                }
                entries[i] = entry;
            }
            if (changed) PublishRoster();
        }

        private void OnDestroy()
        {
            respawnPresentation?.ResetPresentation();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                SetFallWaiting(ref entry, false);
            }
        }

        private static void SetFallWaiting(ref Entry entry, bool waiting)
        {
            if (entry.Avatar == null || entry.Waiting == waiting) return;
            var avatar = entry.Avatar;
            var body = avatar.GetComponent<Rigidbody>();
            var carry = avatar.GetComponent<PlayerCarryAbility>();
            if (waiting)
            {
                entry.MotorWasEnabled = avatar.enabled;
                entry.WasLocked = avatar.MovementLocked;
                entry.BodyConstraints = body != null ? body.constraints : RigidbodyConstraints.None;
                entry.HandsWereBlocked = carry != null && carry.HandsBlocked;
                avatar.MovementLocked = true;
                avatar.enabled = false;
                if (body != null) body.constraints = RigidbodyConstraints.FreezeAll;
                if (carry != null) carry.HandsBlocked = true;
            }
            else
            {
                if (body != null) body.constraints = entry.BodyConstraints;
                avatar.enabled = entry.MotorWasEnabled;
                avatar.MovementLocked = entry.WasLocked;
                if (carry != null) carry.HandsBlocked = entry.HandsWereBlocked;
            }
            entry.Waiting = waiting;
        }

        private void AnnounceSpill(CartTiltCause cause, int responsible)
        {
            string text = cause switch
            {
                CartTiltCause.Disagreement => "Тянете вразнобой — вода через край!",
                CartTiltCause.Brake => "Резко затормозили — вода через край!",
                CartTiltCause.Turn => "Занесло на повороте!",
                CartTiltCause.Road => "Тряский настил — вода через край!",
                _ => "Удар! Вода за бортом!"
            };
            announcer?.Announce(text, 3f);
        }

        // ========== РАУНД ==========

        protected override void OnRoundStarted()
        {
            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
            }

            // EnterRound has primed the full duration. Keep it intact until GO.
            if (HasAuthority) Timer?.StopTimer();
            countdownRoutine = StartCoroutine(CountdownThenGo());
        }

        /// <summary>
        /// Обратный отсчёт: ввод заморожен целиком, чтобы «управления нет»
        /// значило именно это, а не «двигаться нельзя, а толкать можно».
        ///
        /// Идёт на каждой машине по своим часам, и это верно: отсчёт объявляет
        /// фаза раунда, а она приезжает всем одним пакетом.
        /// </summary>
        private IEnumerator CountdownThenGo()
        {
            SetStartCountdownActive(true);

            float remaining = config.CountdownSeconds;
            while (remaining > 0f)
            {
                Hud?.ShowCountdown(remaining);
                yield return null;
                remaining -= Time.deltaTime;
            }

            Hud?.HideCountdown();
            if (HasAuthority && Phase == MinigamePhase.Round) Timer?.StartTimer(RoundDuration);
            SetStartCountdownActive(false);
            countdownRoutine = null;
        }

        private void OnDelivered(TeamSide side, int amount, double time)
        {
            if (!HasAuthority)
            {
                return;
            }

            state.Deliver(side, amount, config.TankCapacity, time, false);
            progressBar?.SetValue(side, state.Of(side).Water, config.TankCapacity);
            net?.PublishState(state);
        }

        private void OnTripFinished(TeamSide side)
        {
            if (!HasAuthority)
            {
                return;
            }

            CarryItemState previous = state;
            state.Deliver(side, 0, config.TankCapacity, 0d, true);
            net?.PublishState(state);
            AnnounceTrips(previous, state);

            Debug.Log($"🫙 [Переноска] команда {side} закрыла ходку №{state.Of(side).Deliveries}, " +
                      $"в баке {state.Of(side).Water} из {config.TankCapacity}");
        }

        /// <summary>
        /// Диктор объявляет закрытую ходку на каждой машине: число ходок едет
        /// счётом, и по его росту клиент говорит то же, что и хост, — без
        /// отдельного сообщения.
        /// </summary>
        private void AnnounceTrips(in CarryItemState previous, in CarryItemState current)
        {
            if (announcer == null)
            {
                return;
            }

            if (current.TeamA.Deliveries > previous.TeamA.Deliveries)
            {
                announcer.Announce($"Команда А: ходка {current.TeamA.Deliveries}, в баке {current.TeamA.Water}", 2.6f);
            }

            if (current.TeamB.Deliveries > previous.TeamB.Deliveries)
            {
                announcer.Announce($"Команда Б: ходка {current.TeamB.Deliveries}, в баке {current.TeamB.Water}", 2.6f);
            }
        }

        // ========== УХОД ИГРОКА ==========

        /// <summary>
        /// Участник вышел из матча (спека 10.1).
        ///
        /// Строку из состава <b>не удаляем</b>: место ему полагается наравне с
        /// остальными, по накопленному командой. Команды остальных тоже не
        /// трогаем — пересчёт по индексу перевёл бы половину лобби в другую
        /// команду посреди ходки.
        ///
        /// Что происходит на самом деле: ручка ушедшего освобождается, у бутыли
        /// становится на одну ручку меньше, и от несбалансированной тяги она
        /// начинает крениться — команда несёт дальше уже вкривь. Отдельного
        /// кода на это не нужно, так работает сама модель переноски.
        ///
        /// Зовёт сетевая половина, только у сервера.
        /// </summary>
        public void HandlePlayerLeft(int playerId)
        {
            if (!HasAuthority)
            {
                return;
            }

            int index = IndexOfPlayer(playerId);
            if (index < 0 || entries[index].Left)
            {
                return;
            }

            Entry entry = entries[index];

            // Поручень снимаем сами, не дожидаясь страховки: она сработает в
            // ближайший такт физики, но состав уже объявлен, и число поручней
            // должно сойтись с ним в тот же миг. Сначала снять ушедшего, потом
            // ужать число поручней — живые несущие при этом переезжают на
            // младшие слоты, а не срываются.
            WaterCart cart = CartCarriedBy(entry.Avatar);
            if (cart != null && entry.Avatar != null)
            {
                cart.Carry.ReleaseFor(entry.Avatar, CarryReleaseReason.RoundEnded);
            }

            entry.Left = true;
            entry.Avatar = null;
            entries[index] = entry;

            foreach (WaterCart owned in new[] { teamA.Cart, teamB.Cart })
                if (owned != null && owned.ControlTeam == entry.Team) owned.SetHandleCount(Mathf.Max(1, SizeOf(entry.Team)));
            PublishRoster();

            Debug.Log($"🫙 [Переноска] {playerId} вышел из матча, в команде {entry.Team} осталось " +
                      $"{SizeOf(entry.Team)}");

            EndIfTeamWipedOut();
        }

        /// <summary>
        /// Команда кончилась целиком — играть дальше нечем. Победа второй
        /// команде; если ушли обе, победителя нет и все получают последнее
        /// место общим механизмом.
        /// </summary>
        private void EndIfTeamWipedOut()
        {
            bool aliveA = SizeOf(TeamSide.A) > 0;
            bool aliveB = SizeOf(TeamSide.B) > 0;

            if (aliveA && aliveB)
            {
                return;
            }

            winnerForced = true;
            forcedWinner = aliveA ? TeamSide.A : aliveB ? TeamSide.B : TeamSide.None;

            Debug.Log($"🫙 [Переноска] раунд оборван: команда кончилась целиком, победитель — " +
                      $"{(forcedWinner == TeamSide.None ? "нет" : forcedWinner.ToString())}");

            EndMinigame();
        }

        /// <summary>
        /// Кто победил. Обычно решает вода, но ушедшая целиком команда отдаёт
        /// раунд сопернику независимо от того, сколько успела донести.
        /// </summary>
        private TeamSide ResolveWinner() => winnerForced ? forcedWinner : state.Winner();

        private int IndexOfPlayer(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Presentation reads the replicated deadline; it never starts or ends a penalty.</summary>
        public bool TryGetRespawnDeadline(int playerId, out double deadline)
        {
            int index = IndexOfPlayer(playerId);
            deadline = index >= 0 ? entries[index].RespawnAt : 0;
            return index >= 0 && !entries[index].Left;
        }

        // ========== КОНЕЦ ==========

        /// <summary>
        /// Снять с игроков всё, что игра на них вешала: привязку к ручке,
        /// потолок скорости, запрет удара и подбора.
        ///
        /// Правило, стоившее отдельного дня на «Ангелах»: персонаж уезжает в хаб
        /// живым, и незакрытая роль уезжает вместе с ним. Всё это снимает
        /// <c>MultiCarryObject.ReleaseAll</c> — и снимает на <b>каждой</b>
        /// машине, не дожидаясь пакета: потолок скорости живёт в моторе
        /// владельца, а мотор у него свой.
        /// </summary>
        protected override void OnRoundEnded()
        {
            respawnPresentation?.ResetPresentation();
            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }

            Hud?.HideCountdown();

            ReleaseTeam(teamA);
            ReleaseTeam(teamB);

            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i] != null)
                {
                    bots[i].enabled = false;
                }
            }

            bots.Clear();

            // Точка респавна вела внутрь этой сцены. Не вернув прежнюю, персонаж
            // уедет в хаб с точкой внутри уже выгруженной арены.
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                SetFallWaiting(ref entry, false);
                entry.RespawnAt = 0;
                entry.AwaitingRespawnPosition = false;
                entries[i] = entry;
                if (entry.Avatar != null && entry.Avatar.TryGetComponent(out PlayerRespawner respawner))
                {
                    respawner.SetRespawnPoint(entry.OriginalRespawn);
                }
            }

            SetStartCountdownActive(false);

            LogFinalTable();

            if (!HasAuthority)
            {
                return;
            }

            TeamSide winner = ResolveWinner();
            Debug.Log($"🫙 [Переноска] итог: A — {state.TeamA.Water} за {state.TeamA.Deliveries} ходок, " +
                      $"B — {state.TeamB.Water} за {state.TeamB.Deliveries}, " +
                      $"победитель — {(winner == TeamSide.None ? "нет" : winner.ToString())}");

            Debug.Log($"🫙 [Переноска] куда ушла вода — A: {LossReport(TeamSide.A)}");
            Debug.Log($"🫙 [Переноска] куда ушла вода — B: {LossReport(TeamSide.B)}");
        }

        /// <summary>
        /// Выписать итог в лог — на <b>каждой</b> машине, а не только у сервера.
        ///
        /// Стенд из восьми процессов проверяется сличением восьми логов между
        /// собой: разъехавшийся уровень бака у хоста и клиента иначе не поймать
        /// вовсе — у каждого своя картинка, и обе выглядят правдоподобно.
        /// Формат строки общий для проекта, его читает <c>autorun-report.sh</c>.
        /// </summary>
        private void LogFinalTable()
        {
            var table = new System.Text.StringBuilder(256);
            table.Append("📊 [Переноска] итог у ");
            table.Append(HasAuthority
                ? "хоста"
                : $"клиента id={SessionScoreboard.Current?.LocalPlayer?.Id}");
            table.Append(':');

            TeamSide winner = ResolveWinner();
            table.Append(" A=").Append(state.TeamA.Water).Append('/').Append(state.TeamA.Deliveries)
                 .Append(" B=").Append(state.TeamB.Water).Append('/').Append(state.TeamB.Deliveries)
                 .Append(" победитель=").Append(winner == TeamSide.None ? "нет" : winner.ToString());

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                table.Append(" | id=").Append(entry.PlayerId).Append(' ').Append(entry.Team);

                if (entry.Left)
                {
                    table.Append(" ВЫШЕЛ");
                }
            }

            Debug.Log(table.ToString(), this);
        }

        /// <summary>Разрез потерь одной строкой. Числа приёмки и плейтеста.</summary>
        private string LossReport(TeamSide side) =>
            $"набрано {SpentBy(side, WaterLossReason.Filled)}, " +
            $"переливы {SpentBy(side, WaterLossReason.Tilt)}, " +
            $"удары {SpentBy(side, WaterLossReason.Hit)}, " +
            $"толчки {SpentBy(side, WaterLossReason.Shove)}, " +
            $"таран {SpentBy(side, WaterLossReason.RamVictim) + SpentBy(side, WaterLossReason.RamAttacker)}, " +
            $"пропасть {SpentBy(side, WaterLossReason.Void)}, " +
            $"донесено {SpentBy(side, WaterLossReason.Poured)}";

        /// <summary>
        /// Снять всё с тележки команды и убрать её. Поручни отпускаются на
        /// <b>каждой</b> машине — потолок скорости и занятые руки живут в моторе
        /// владельца; саму тележку убирает авторитет, остальные узнают из despawn.
        /// </summary>
        private void ReleaseTeam(TeamRig rig)
        {
            WaterCart cart = rig.Cart;
            rig.Tap?.DetachCart();
            rig.Cart = null;

            if (cart == null)
            {
                return;
            }

            cart.Carry.ReleaseAll(CarryReleaseReason.RoundEnded);

            if (!HasAuthority)
            {
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.ShutdownInProgress)
            {
                return;
            }

            if (WorldAuthority.IsNetworkSession && cart.TryGetComponent(out NetworkObject netObject) && netObject.IsSpawned)
            {
                netObject.Despawn(true);
                return;
            }

            Destroy(cart.gameObject);
        }

        /// <summary>
        /// Места половинами: победившая команда делит верхние, проигравшая —
        /// нижние. Счёт 0 : 0 — победителя нет, и все получают последнее место
        /// и ноль очков: отдать всем первое означало бы выдать максимум за
        /// общий провал.
        ///
        /// Считает только сервер — клиенту приедут готовые места.
        /// </summary>
        public override string ResultMetricTitle => "ВОДА";
        public override bool ResultsAreTeams => true;
        public override RoundResultDetail GetResultDetail(int playerId)
        {
            TeamSide side = TeamOfPlayer(playerId);
            if (side == TeamSide.None) return new RoundResultDetail("—", "Вышел из раунда");
            bool a = side == TeamSide.A;
            return new RoundResultDetail((a ? state.TeamA.Water : state.TeamB.Water).ToString(),
                a ? "КОМАНДА А" : "КОМАНДА Б", a ? new Color(.18f, .52f, .78f) : new Color(.88f, .37f, .24f));
        }

        protected override void CollectResults(MinigameResults results)
        {
            rankingBuffer.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                rankingBuffer.Add(new TeamRanking.Entry(entries[i].PlayerId, entries[i].Team));
            }

            TeamRanking.Fill(rankingBuffer, ResolveWinner(), results);
        }

        // ========== СЛУЖЕБНОЕ ==========

        /// <summary>Команда участника по его номеру в сессии.</summary>
        public TeamSide TeamOfPlayer(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return entries[i].Team;
                }
            }

            return TeamSide.None;
        }

        /// <summary>Команда участника по его персонажу. Восемь записей — перебор дешевле словаря.</summary>
        public TeamSide TeamOfAvatar(PlayerController avatar)
        {
            if (avatar == null)
            {
                return TeamSide.None;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Avatar == avatar)
                {
                    return entries[i].Team;
                }
            }

            return TeamSide.None;
        }

        /// <summary>
        /// Следующая точка пути команды: доска, горлышко, доска — и только
        /// потом сама цель. Разбор, зачем это нужно, — в
        /// <see cref="CarryItemBotRoute"/>.
        /// </summary>
        public Vector3 NextWaypoint(TeamSide side, Vector3 from, Vector3 to, out bool isFinal)
        {
            CarryItemBotRoute route = side == TeamSide.A ? teamA.Route : teamB.Route;
            if (route == null)
            {
                isFinal = true;
                return to;
            }

            return route.NextPoint(from, to, out isFinal);
        }

        private TeamRig RigOf(TeamSide side) =>
            side == TeamSide.A ? teamA : side == TeamSide.B ? teamB : null;

        /// <summary>Кран этой команды. Нужен болванкам соло-теста и разбору сетевой тележки.</summary>
        public WaterTap TapOf(TeamSide side) => RigOf(side)?.Tap;

        /// <summary>Тележка этой команды. Пусто — ещё не приехала или раунд кончился.</summary>
        public WaterCart CartOf(TeamSide side) => RigOf(side)?.Cart;
        public bool FleetExhausted => teamA.Cart != null && teamB.Cart != null && teamA.Cart.IsDepleted && teamB.Cart.IsDepleted;

        public WaterCart CartCarriedBy(PlayerController player)
        {
            if (player == null) return null;
            if (teamA.Cart != null && teamA.Cart.Carry.IsCarriedBy(player)) return teamA.Cart;
            if (teamB.Cart != null && teamB.Cart.Carry.IsCarriedBy(player)) return teamB.Cart;
            return null;
        }
        public WaterCart CartForPlayer(int id)
        {
            WaterCart held = CartCarriedBy(AvatarOf(id));
            if (held != null) return held;
            TeamSide side = TeamOfPlayer(id);
            if (side == TeamSide.None) return null;
            WaterCart own = CartOf(side);
            if (own != null && !own.IsLost && own.ControlTeam == side) return own;
            WaterCart captured = CartOf(side == TeamSide.A ? TeamSide.B : TeamSide.A);
            if (captured != null && !captured.IsLost && captured.ControlTeam == side) return captured;
            return own;
        }

        public WaterCart PumpCart(TeamSide side)
        {
            WaterTank tank = TankOf(side);
            if (tank == null) return null;
            if (tank.CanReceive(teamA.Cart) && teamA.Cart.IsPouring) return teamA.Cart;
            if (tank.CanReceive(teamB.Cart) && teamB.Cart.IsPouring) return teamB.Cart;
            return null;
        }


        /// <summary>Бак этой команды. Нужен болванкам соло-теста.</summary>
        public WaterTank TankOf(TeamSide side) =>
            side == TeamSide.A ? teamA.Tank : side == TeamSide.B ? teamB.Tank : null;

        /// <summary>
        /// Повесить болванки.
        ///
        /// Вне сети они садятся на манекенов — на копии без локального
        /// управления, — а свой персонаж остаётся за человеком.
        ///
        /// 🔴 В сетевой катке манекенов не бывает: у каждой копии есть
        /// владелец-человек, и болванка играла бы за него. Единственное
        /// исключение — стенд автопрогона, и оно не по признаку, а
        /// <b>по аргументу запуска</b>: в процессе с <c>--bot</c> за
        /// клавиатурой нет никого. Там болванка ведёт ровно одного персонажа —
        /// своего; чужих ведут их машины. Тот же приём у «Рейса на память» и
        /// «Экзамена».
        ///
        /// Без этого стенд из четырёх процессов молча простоял бы весь раунд:
        /// баки остались бы по нулям, а выглядело бы это как сломанная игра,
        /// а не как отключённая болванка.
        /// </summary>
        private void AttachBots()
        {
            // Зовётся дважды: у авторитета сразу с составом, у остальных ещё
            // раз, когда состав приедет. Прежние болванки снимаем — иначе
            // после ухода игрока его болванка осталась бы рулить телом,
            // которого в составе больше нет.
            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i] != null)
                {
                    bots[i].enabled = false;
                }
            }

            bots.Clear();

            bool networked = WorldAuthority.IsNetworkSession;
            if (networked && !LaunchArguments.BotEnabled)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                PlayerController avatar = entries[i].Avatar;
                if (avatar == null || !avatar.TryGetComponent(out PlayerInputReader reader))
                {
                    continue;
                }

                if (networked)
                {
                    // Только своего: чужую копию ведёт её собственная машина.
                    if (!reader.LocallyControlled)
                    {
                        continue;
                    }

                    // Автопилот не добавляет второй источник ввода, а заменяет
                    // первый: пока он взведён, клавиатура не читается вовсе.
                    reader.EngageAutopilot();
                }
                else if (reader.LocallyControlled)
                {
                    continue;
                }

                if (!avatar.TryGetComponent(out CarryItemDebugBot bot))
                {
                    bot = avatar.gameObject.AddComponent<CarryItemDebugBot>();
                }

                bot.enabled = true;
                bot.Configure(this, entries[i].Team, botObstacles);
                bots.Add(bot);
            }

            if (networked && bots.Count > 0)
            {
                Debug.Log($"🫙 [Переноска] 🤖 сетевой автопрогон: болванка ведёт ТОЛЬКО персонажа " +
                          "этой машины, чужими не рулит");
            }
        }
    }
}
