using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Фаза станции. Прицеливание сюда не входит намеренно: шкала силы —
    /// локальное дело того, кто целится, и гнать её по сети незачем.
    /// Общим становится только результат — состоявшийся бросок.
    /// </summary>
    public enum HubActivityPhase : byte
    {
        Free = 0,
        Occupied = 1,
        Launched = 2,
        Counting = 3,
    }

    /// <summary>
    /// Мебель в хабе, в которую можно поиграть, пока собирается компания:
    /// дорожка боулинга, бильярдный стол, дартс. Общая часть всех забав —
    /// занять место, прицелиться, бросить, увидеть результат, уйти.
    ///
    /// **Это не мини-игра.** Очков катки не даёт, в <c>SessionManager</c>
    /// не отчитывается, из каталога не запускается. Спека — `docs/hub-activities.md`.
    ///
    /// Авторитет как у остального мира: состояние живёт на сервере, клиент
    /// шлёт намерение. Занятие места идёт готовым путём взаимодействия
    /// (<see cref="PlayerInteractor"/> → сетевой релей → сервер), поэтому
    /// своего RPC ему не нужно — сервер получает уже проверенный по дистанции
    /// вызов <see cref="Interact"/>. Свой RPC есть ровно один: бросок.
    ///
    /// Тело занявшего фиксирует **его собственная машина**, а не сервер:
    /// позицию персонажа ведёт владелец (<c>ClientNetworkTransform</c>), и
    /// всё, что сервер напишет в чужую копию, тут же перетрёт транспорт.
    /// Поэтому сервер держит «кто занял», а каждая машина сама смотрит,
    /// не её ли это игрок.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class HubActivityStation : NetworkBehaviour, IInteractable, IPushButtonOverride
    {
        /// <summary>Станция свободна. Ноль занять нельзя: это законный clientId хоста.</summary>
        public const ulong NoOccupant = ulong.MaxValue;

        /// <summary>Лучшего результата ещё нет. Ноль занимать нельзя: это законный номер участника.</summary>
        private const int NoPlayer = -1;

        [Header("Место игрока")]
        [Tooltip("Куда встаёт занявший станцию. Его forward — направление броска по умолчанию")]
        [SerializeField] private Transform standPoint;

        [Tooltip("Название забавы для подсказки: «E — боулинг»")]
        [SerializeField] private string activityName = "играть";

        [Header("Дистанции, м")]
        [Tooltip("Дальше этого станцию не занять. Проверяется на сервере")]
        [SerializeField] private float takeRadius = 2.5f;

        [Tooltip("Ушёл дальше — место освобождается само")]
        [SerializeField] private float leaveRadius = 4f;

        [Header("Бросок")]
        [Tooltip("За сколько секунд шкала силы проходит от нуля до единицы")]
        [SerializeField] private float powerCycleSeconds = 1.2f;

        [Tooltip("На сколько градусов можно отвернуть бросок от направления метки")]
        [SerializeField] private float maxAimAngle = 60f;

        [Header("Показ")]
        [SerializeField] private HubActivityBoard board;
        [SerializeField] private HubActivityPowerGauge gauge;

        private readonly NetworkVariable<ulong> occupant = new NetworkVariable<ulong>(NoOccupant);
        private readonly NetworkVariable<HubActivityPhase> phase = new NetworkVariable<HubActivityPhase>(HubActivityPhase.Free);
        private readonly NetworkVariable<byte> lastScore = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> bestScore = new NetworkVariable<byte>();
        /// <summary>
        /// Кто держит лучший результат — номер участника, а не его имя.
        /// Имя живёт в табло сессии и берётся оттуда при показе: гнать его
        /// второй раз по сети незачем, а две копии имени рано или поздно
        /// разъедутся при переименовании на экране приставки.
        /// </summary>
        private readonly NetworkVariable<int> bestPlayerId = new NetworkVariable<int>(NoPlayer);

        /// <summary>
        /// Зеркала сетевого состояния для сцены, открытой без сети. Писать
        /// в <c>NetworkVariable</c> до спавна нельзя, а проверять забаву
        /// в редакторе в одиночку — обычное дело.
        /// </summary>
        private ulong offlineOccupant = NoOccupant;
        private HubActivityPhase offlinePhase = HubActivityPhase.Free;
        private byte offlineLastScore;
        private byte offlineBestScore;
        private int offlineBestPlayerId = NoPlayer;

        /// <summary>Кого посадили за станцию. Нужен там, где сети нет и искать игрока по clientId не у кого.</summary>
        private PlayerController occupantPlayer;

        private PlayerController boundPlayer;
        private PlayerInputReader boundInput;
        private PlayerPushAbility boundPush;

        private float powerTimer;
        private bool wasHolding;

        public ulong Occupant => IsSpawned ? occupant.Value : offlineOccupant;
        public HubActivityPhase Phase => IsSpawned ? phase.Value : offlinePhase;
        public byte LastScore => IsSpawned ? lastScore.Value : offlineLastScore;
        public byte BestScore => IsSpawned ? bestScore.Value : offlineBestScore;
        /// <summary>Имя держателя лучшего результата. Пусто, если его ещё нет или он ушёл из катки.</summary>
        public string BestName
        {
            get
            {
                int id = IsSpawned ? bestPlayerId.Value : offlineBestPlayerId;
                if (id == NoPlayer)
                {
                    return string.Empty;
                }

                SessionPlayer player = SessionScoreboard.Current?.FindPlayer(id);
                return player != null ? player.DisplayName : string.Empty;
            }
        }

        /// <summary>Сила, набранная прямо сейчас: 0…1. Локальная, по сети не идёт.</summary>
        public float Power { get; private set; }

        /// <summary>Вправе ли эта машина решать исход. Вне сети — всегда.</summary>
        protected bool HasAuthority => IsSpawned ? IsServer : WorldAuthority.HasAuthority;

        protected Transform StandPoint => standPoint;

        // ================== взаимодействие ==================

        public string InteractionPrompt
        {
            get
            {
                if (Occupant == NoOccupant)
                {
                    return $"E — {activityName}";
                }

                return IsOccupiedByLocalPlayer() ? "E — отойти" : string.Empty;
            }
        }

        public bool CanInteract(PlayerController player)
        {
            if (player == null || standPoint == null)
            {
                return false;
            }

            // Свободна — занимай. Занята тобой — уходи. Чужую не трогаем.
            return Occupant == NoOccupant || Occupant == ClientIdOf(player);
        }

        /// <summary>
        /// Исполняется на авторитете: сетевой релей доставляет сюда намерение,
        /// уже проверенное по дистанции в <see cref="PlayerInteractor"/>.
        /// </summary>
        public void Interact(PlayerController player)
        {
            if (!HasAuthority || player == null)
            {
                return;
            }

            ulong clientId = ClientIdOf(player);

            if (Occupant == NoOccupant)
            {
                if (!IsWithinTakeRadius(player))
                {
                    return;
                }

                Take(player, clientId);
                return;
            }

            if (Occupant == clientId)
            {
                Release();
            }
        }

        private void Take(PlayerController player, ulong clientId)
        {
            occupantPlayer = player;
            SetOccupant(clientId);
            SetPhase(HubActivityPhase.Occupied);
            OnTaken(player);
        }

        /// <summary>
        /// Освободить станцию и вернуть забаву в исходное. Вызывается по любому
        /// из поводов: ушёл сам, ушёл далеко, отключился, станция выгружается.
        /// </summary>
        public void Release()
        {
            if (!HasAuthority)
            {
                return;
            }

            occupantPlayer = null;
            SetOccupant(NoOccupant);
            SetPhase(HubActivityPhase.Free);
            ResetActivity();
        }

        // ================== перехват кнопки толчка ==================

        /// <summary>
        /// Пока игрок за станцией, ЛКМ не бьёт кулаком: она набирает силу
        /// броска. Само нажатие здесь не обрабатывается — шкалу ведёт
        /// <see cref="UpdateAiming"/> по удержанию, а этот метод только
        /// съедает нажатие, чтобы не прошёл удар.
        /// </summary>
        public bool HandlePushButton(PlayerController player) => true;

        // ================== жизненный цикл ==================

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            occupant.OnValueChanged += OnOccupantChanged;
            phase.OnValueChanged += OnPhaseChanged;
            lastScore.OnValueChanged += OnScoreChanged;
            bestScore.OnValueChanged += OnScoreChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            RefreshBoard();
        }

        public override void OnNetworkDespawn()
        {
            occupant.OnValueChanged -= OnOccupantChanged;
            phase.OnValueChanged -= OnPhaseChanged;
            lastScore.OnValueChanged -= OnScoreChanged;
            bestScore.OnValueChanged -= OnScoreChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            // Сцена уезжает в мини-игру, а персонаж переживает смену сцены:
            // не сняв блокировку здесь, мы отправим его в игру обездвиженным.
            UnbindLocalPlayer();

            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            UnbindLocalPlayer();
        }

        protected virtual void Start()
        {
            RefreshBoard();
        }

        private void Update()
        {
            ApplyLocalBinding();
            UpdateAiming();

            if (HasAuthority)
            {
                GuardOccupant();
                OnAuthorityUpdate();
            }
        }

        // ================== привязка своего тела ==================

        /// <summary>
        /// Каждая машина сама смотрит, не её ли игрок занял станцию, и только
        /// своему телу ставит блокировку и перехват кнопки. Чужое тело трогать
        /// нельзя: его ведёт владелец.
        /// </summary>
        private void ApplyLocalBinding()
        {
            bool mine = IsOccupiedByLocalPlayer();

            if (mine && boundPlayer == null)
            {
                BindLocalPlayer();
            }
            else if (!mine && boundPlayer != null)
            {
                UnbindLocalPlayer();
            }
        }

        private void BindLocalPlayer()
        {
            PlayerController player = ResolveLocalPlayer();
            if (player == null || standPoint == null)
            {
                return;
            }

            boundPlayer = player;
            boundInput = player.GetComponent<PlayerInputReader>();
            boundPush = player.GetComponent<PlayerPushAbility>();

            player.TeleportTo(standPoint.position, standPoint.rotation);
            player.MovementLocked = true;

            if (boundPush != null)
            {
                boundPush.ButtonOverride = this;
            }

            Power = 0f;
            powerTimer = 0f;
            wasHolding = false;

            if (gauge != null)
            {
                gauge.SetVisible(true);
                gauge.SetPower(0f);
            }
        }

        private void UnbindLocalPlayer()
        {
            if (boundPlayer != null)
            {
                boundPlayer.MovementLocked = false;
            }

            // Снимает тот же, кто вешал: чужую роль сбрасывать нельзя.
            if (boundPush != null && ReferenceEquals(boundPush.ButtonOverride, this))
            {
                boundPush.ButtonOverride = null;
            }

            boundPlayer = null;
            boundInput = null;
            boundPush = null;
            Power = 0f;
            powerTimer = 0f;
            wasHolding = false;

            if (gauge != null)
            {
                gauge.SetVisible(false);
            }
        }

        /// <summary>
        /// Тело локального игрока. В сети — своё же <c>PlayerObject</c>,
        /// вне сети — тот, кого посадили за станцию.
        /// </summary>
        private PlayerController ResolveLocalPlayer()
        {
            if (!IsSpawned || NetworkManager == null)
            {
                return occupantPlayer;
            }

            NetworkObject body = NetworkManager.LocalClient?.PlayerObject;
            return body != null ? body.GetComponent<PlayerController>() : null;
        }

        private bool IsOccupiedByLocalPlayer()
        {
            ulong current = Occupant;
            if (current == NoOccupant)
            {
                return false;
            }

            if (!IsSpawned || NetworkManager == null)
            {
                return occupantPlayer != null;
            }

            return current == NetworkManager.LocalClientId;
        }

        // ================== прицел и бросок ==================

        /// <summary>
        /// Шкала силы идёт треугольной волной, пока держат ЛКМ: 0 → 1 → 0 → 1.
        /// Промахнулся по силе — сам виноват, в этом половина веселья.
        /// </summary>
        private void UpdateAiming()
        {
            if (boundPlayer == null || boundInput == null || Phase != HubActivityPhase.Occupied)
            {
                return;
            }

            bool holding = boundInput.PushHeld;

            if (holding)
            {
                powerTimer += Time.deltaTime;
                Power = Triangle(powerTimer / Mathf.Max(0.05f, powerCycleSeconds));

                if (gauge != null)
                {
                    gauge.SetPower(Power);
                }
            }
            else if (wasHolding)
            {
                RequestLaunch(AimDirection(), Power);
                Power = 0f;
                powerTimer = 0f;

                if (gauge != null)
                {
                    gauge.SetPower(0f);
                }
            }

            wasHolding = holding;
        }

        /// <summary>Пила 0→1→0 без ветвлений на каждом кадре.</summary>
        private static float Triangle(float t) => Mathf.PingPong(t, 1f);

        /// <summary>
        /// Куда полетит: горизонтальная проекция взгляда камеры. Движение
        /// заблокировано, поэтому персонаж не поворачивается, и единственное,
        /// чем игрок целится, — камера.
        /// </summary>
        private Vector3 AimDirection()
        {
            Camera view = Camera.main;
            Vector3 raw = view != null ? view.transform.forward : standPoint.forward;
            raw.y = 0f;

            return raw.sqrMagnitude < 0.0001f ? standPoint.forward : raw.normalized;
        }

        private void RequestLaunch(Vector3 direction, float power)
        {
            if (IsSpawned)
            {
                RequestLaunchRpc(direction, power);
                return;
            }

            // Сети нет — сцену открыли в редакторе, исполняем на месте.
            ExecuteLaunch(direction, power);
        }

        /// <summary>
        /// Клиент просит бросить. Отправителя берём из <c>RpcParams</c>, а не из
        /// аргумента: иначе чужим намерением можно было бы бросить за занявшего.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestLaunchRpc(Vector3 direction, float power, RpcParams rpcParams = default)
        {
            if (Occupant != rpcParams.Receive.SenderClientId)
            {
                return;
            }

            ExecuteLaunch(direction, power);
        }

        private void ExecuteLaunch(Vector3 direction, float power)
        {
            if (Phase != HubActivityPhase.Occupied)
            {
                return;
            }

            SetPhase(HubActivityPhase.Launched);
            Launch(ClampAim(direction), Mathf.Clamp01(power));
        }

        /// <summary>
        /// Бросок не разворачивается назад, в комнату: направление зажимается
        /// в конус вокруг метки. Клиенту тут не доверяем — он мог прислать что угодно.
        /// </summary>
        private Vector3 ClampAim(Vector3 direction)
        {
            Vector3 forward = standPoint != null ? standPoint.forward : Vector3.forward;
            forward.y = 0f;
            forward.Normalize();

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return forward;
            }

            direction.Normalize();

            float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
            float clamped = Mathf.Clamp(angle, -maxAimAngle, maxAimAngle);

            return Quaternion.AngleAxis(clamped, Vector3.up) * forward;
        }

        // ================== сторож ==================

        /// <summary>
        /// Сервер следит, что занявший всё ещё тут. Ушёл, умер, телепортировался —
        /// место освобождается, иначе забава залипнет на ушедшем.
        /// </summary>
        private void GuardOccupant()
        {
            if (Occupant == NoOccupant)
            {
                return;
            }

            PlayerController player = ResolveOccupantBody();
            if (player == null)
            {
                Release();
                return;
            }

            if (Vector3.Distance(player.transform.position, standPoint.position) > leaveRadius)
            {
                Release();
            }
        }

        /// <summary>Тело занявшего. Спрашивать только на авторитете: у клиента чужого тела может не быть.</summary>
        protected PlayerController ResolveOccupantBody()
        {
            if (!IsSpawned || NetworkManager == null)
            {
                return occupantPlayer;
            }

            if (!NetworkManager.ConnectedClients.TryGetValue(Occupant, out NetworkClient client))
            {
                return null;
            }

            NetworkObject body = client.PlayerObject;
            return body != null ? body.GetComponent<PlayerController>() : null;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Occupant == clientId)
            {
                Release();
            }
        }

        private bool IsWithinTakeRadius(PlayerController player) =>
            standPoint != null &&
            Vector3.Distance(player.transform.position, standPoint.position) <= takeRadius;

        private static ulong ClientIdOf(PlayerController player)
        {
            var body = player.GetComponent<NetworkObject>();
            return body != null && body.IsSpawned ? body.OwnerClientId : 0ul;
        }

        // ================== состояние ==================

        protected void SetPhase(HubActivityPhase value)
        {
            if (IsSpawned && IsServer)
            {
                phase.Value = value;
                return;
            }

            if (!IsSpawned)
            {
                offlinePhase = value;
                RefreshBoard();
            }
        }

        private void SetOccupant(ulong value)
        {
            if (IsSpawned && IsServer)
            {
                occupant.Value = value;
                return;
            }

            if (!IsSpawned)
            {
                offlineOccupant = value;
            }
        }

        /// <summary>
        /// Записать результат броска. Лучший за вечер держится с именем и
        /// никуда не сохраняется: перезапустили хост — начали заново.
        /// </summary>
        protected void ReportScore(byte score, PlayerController player)
        {
            if (!HasAuthority)
            {
                return;
            }

            int playerId = PlayerIdOf(player);

            if (IsSpawned && IsServer)
            {
                lastScore.Value = score;

                if (score > bestScore.Value)
                {
                    bestScore.Value = score;
                    bestPlayerId.Value = playerId;
                }

                return;
            }

            offlineLastScore = score;

            if (score > offlineBestScore)
            {
                offlineBestScore = score;
                offlineBestPlayerId = playerId;
            }

            RefreshBoard();
        }

        /// <summary>
        /// Номер участника по его телу. Табло — единственное место, где тело
        /// и участник катки связаны, поэтому спрашиваем там.
        /// </summary>
        private static int PlayerIdOf(PlayerController player)
        {
            if (player == null)
            {
                return NoPlayer;
            }

            System.Collections.Generic.IReadOnlyList<SessionPlayer> players = SessionScoreboard.Current?.Players;
            if (players == null)
            {
                return NoPlayer;
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (ReferenceEquals(players[i].Avatar, player))
                {
                    return players[i].Id;
                }
            }

            return NoPlayer;
        }

        private void OnOccupantChanged(ulong before, ulong after) => RefreshBoard();
        private void OnPhaseChanged(HubActivityPhase before, HubActivityPhase after) => RefreshBoard();
        private void OnScoreChanged(byte before, byte after) => RefreshBoard();

        private void RefreshBoard()
        {
            if (board != null)
            {
                board.Render(this);
            }
        }

        // ================== что дописывает конкретная забава ==================

        /// <summary>Пустить снаряд. Исполняется только на авторитете.</summary>
        protected abstract void Launch(Vector3 direction, float power);

        /// <summary>Вернуть снаряды и мишени в исходное. Исполняется только на авторитете.</summary>
        protected abstract void ResetActivity();

        /// <summary>Игрок занял станцию. Забава выдаёт снаряд и готовит мишени.</summary>
        protected virtual void OnTaken(PlayerController player)
        {
        }

        /// <summary>
        /// Кадр на авторитете: забава досматривает свой бросок. Сюда не
        /// попадают клиенты — считать исход им нечем и незачем.
        /// </summary>
        protected virtual void OnAuthorityUpdate()
        {
        }
    }
}
