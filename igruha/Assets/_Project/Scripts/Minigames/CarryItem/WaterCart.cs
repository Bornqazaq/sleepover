using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.Traps;
using Igruha.Core.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Состояние тележки одной структурой: чья она, сколько у неё поручней,
    /// сколько воды и что с ней происходит.
    ///
    /// Вместе, а не четырьмя каналами: команда назначается в тот же миг, что и
    /// первый уровень воды, — при спавне на старте раунда. Флаги — состояние,
    /// а не события: «наполняется» и «сливается» длятся секундами, и клиент
    /// рисует по ним струю, не получая сообщения на каждую единицу.
    /// </summary>
    public struct WaterCartNetState : INetworkSerializable, IEquatable<WaterCartNetState>
    {
        public const byte FillingFlag = 1;
        public const byte PouringFlag = 2;
        public const byte LostFlag = 4;

        /// <summary>Чья тележка, <see cref="TeamSide"/> байтом.</summary>
        public byte Team;

        /// <summary>Сколько у тележки поручней — размер команды сейчас.</summary>
        public byte Handles;

        /// <summary>Воды, единиц. Вместимость 150, в short помещается с запасом.</summary>
        public short Water;

        /// <summary>Наполняется / сливается / улетела в пропасть.</summary>
        public byte Flags;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Handles);
            serializer.SerializeValue(ref Water);
            serializer.SerializeValue(ref Flags);
        }

        public bool Equals(WaterCartNetState other) =>
            Team == other.Team && Handles == other.Handles && Water == other.Water && Flags == other.Flags;
    }

    /// <summary>
    /// Тележка с водой: сколько в ней, откуда прибывает и куда утекает.
    ///
    /// Вся механика переноски — поручни, натяжение, качение, крен, срыв — живёт
    /// в <see cref="MultiCarryObject"/> и про воду не знает ничего. Здесь только
    /// вода: сколько её, по каким событиям она меняется и как это видно снаружи.
    ///
    /// <b>Одна точка изменения уровня — <see cref="ChangeWater"/>.</b> Через неё
    /// проходят и наполнение под краном, и все виды потерь без исключения:
    /// удар, крен, толчок, таран, слив в бак, пропасть. Считает её только
    /// авторитет: уровень воды — это счёт, и клиенту его не доверяют ни на кадр.
    ///
    /// <b>Тележка постоянна.</b> Она не выдаётся и не исчезает: слилась — стоит
    /// пустая, упала в пропасть — теряет воду и через отсчёт возвращается на
    /// стоянку к крану. Отсюда и публикация уровня <b>ступенями</b>: наполнение
    /// и слив непрерывны, и запись состояния на каждую единицу дала бы десятки
    /// пакетов в секунду.
    /// </summary>
    [RequireComponent(typeof(MultiCarryObject))]
    public sealed class WaterCart : NetworkBehaviour, ITrapImpactTarget
    {
        [SerializeField] private CarryItemConfig config;

        [Header("Вид")]
        [Tooltip("Меш воды в баке тележки. Растягивается по уровню ступенями")]
        [SerializeField] private Transform waterMesh;
        [Tooltip("Рендерер, который подсвечивает крен. Пусто — берётся с меша воды")]
        [SerializeField] private Renderer tiltIndicator;
        [Tooltip("Цвет спокойной тележки")]
        [SerializeField] private Color calmColor = new Color(0.2f, 0.5f, 0.95f);
        [Tooltip("Цвет на подходе к порогу крена")]
        [SerializeField] private Color alarmColor = new Color(0.95f, 0.75f, 0.15f);
        [Tooltip("Цвет, когда вода уже льётся через борт")]
        [SerializeField] private Color pouringColor = new Color(0.95f, 0.2f, 0.15f);
        [Tooltip("С какой доли порога крена начинается тревога. 0.5 — с половины")]
        [Range(0f, 1f)]
        [SerializeField] private float alarmStartFraction = 0.5f;
        [Tooltip("Струя через борт. Бьёт ровно тогда, когда вода уходит от крена")]
        [SerializeField] private ParticleSystem leakJet;
        [Tooltip("Дуга слива в бак. Идёт, пока тележка сливается")]
        [SerializeField] private ParticleSystem pourJet;
        [Tooltip("Брызги наполнения в баке тележки. Идут, пока тележка под краном")]
        [SerializeField] private ParticleSystem fillSplash;
        [Tooltip("Что красится в цвет команды: обод, поручни и прочие метки принадлежности")]
        [SerializeField] private Renderer[] teamTint;

        [Header("Звук")]
        [Tooltip("Нарастающий плеск: громкость и тон растут вместе с креном")]
        [SerializeField] private AudioSource sloshLoop;
        [Tooltip("Отдельный звук утечки через борт")]
        [SerializeField] private AudioSource leakLoop;
        [Tooltip("Плеск набирающейся воды под краном")]
        [SerializeField] private AudioSource fillLoop;

        /// <summary>
        /// Уровень изменился: на сколько и почему. Под звук, VFX и счёт бака.
        /// Наполнение и слив у клиентов сюда <b>не</b> приходят — они длятся
        /// секундами и видны флагами; приходят дискретные потери и крен.
        /// </summary>
        public event Action<int, WaterLossReason> WaterChanged;

        /// <summary>Тележка улетела в пропасть и ждёт возврата.</summary>
        public event Action<WaterCart> Lost;

        /// <summary>Тележка вернулась на стоянку у крана.</summary>
        public event Action<WaterCart> Returned;

        /// <summary>Чья тележка, сколько поручней, сколько воды, что с ней. Пишет сервер, читают все.</summary>
        private readonly NetworkVariable<WaterCartNetState> netState =
            new NetworkVariable<WaterCartNetState>();

        private MultiCarryObject carry;
        private MaterialPropertyBlock materialBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float hitCooldownTimer;
        private float tiltHoldTimer;
        private float leakAccumulator;
        private double returnAt;
        private float voidLevel = float.NegativeInfinity;
        private Vector3 homePosition;
        private Quaternion homeRotation = Quaternion.identity;
        private int shownStep = -1;
        private int publishedWater = -1;
        private byte flags;
        private bool leakShown;
        private bool pourShown;
        private bool fillShown;
        private TeamSide paintedTeam = (TeamSide)byte.MaxValue;

        /// <summary>Клиент уже разобрал эту тележку по крану своей команды.</summary>
        private bool adopted;

        /// <summary>Сколько воды в тележке, единиц.</summary>
        public int Water { get; private set; }

        /// <summary>Чья это тележка. Чужой за её поручень не возьмётся.</summary>
        public TeamSide Team { get; private set; } = TeamSide.None;

        /// <summary>Механика переноски этой тележки.</summary>
        public MultiCarryObject Carry => carry;

        /// <summary>Набирает воду под краном прямо сейчас.</summary>
        public bool IsFilling => (flags & WaterCartNetState.FillingFlag) != 0;

        /// <summary>Сливается в бак прямо сейчас.</summary>
        public bool IsPouring => (flags & WaterCartNetState.PouringFlag) != 0;

        /// <summary>Улетела в пропасть и ждёт возврата на стоянку. Взяться за неё нельзя.</summary>
        public bool IsLost => (flags & WaterCartNetState.LostFlag) != 0;

        /// <summary>Заполнение 0…1. От него зависят потолок скорости, разгон и утечка от крена.</summary>
        public float Load => config != null && config.CartCapacity > 0
            ? Mathf.Clamp01(Water / (float)config.CartCapacity)
            : 0f;

        /// <summary>Стоянка у крана: сюда тележка возвращается из пропасти.</summary>
        public Vector3 HomePosition => homePosition;
        public Quaternion HomeRotation => homeRotation;

        /// <summary>Вправе ли эта машина менять тележку: вне сети или на работающем сервере.</summary>
        private bool HasAuthority
        {
            get
            {
                var manager = NetworkManager;
                // При shutdown поручни ещё снимаются, а IsServer/IsSpawned ещё true.
                // Списание воды здесь вызвало бы RPC через уже разобранную сеть (IGR-664).
                if (manager != null && manager.ShutdownInProgress) return false;
                return IsSpawned
                    ? IsServer && manager != null && manager.IsListening
                    : WorldAuthority.HasAuthority;
            }
        }

        private void Awake()
        {
            carry = GetComponent<MultiCarryObject>();
            materialBlock = new MaterialPropertyBlock();

            if (tiltIndicator == null && waterMesh != null)
            {
                tiltIndicator = waterMesh.GetComponent<Renderer>();
            }
        }

        private void OnEnable()
        {
            carry.Thrown += OnShoved;
        }

        private void OnDisable()
        {
            carry.Thrown -= OnShoved;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged += OnNetStateChanged;

            if (IsServer)
            {
                // Правила настроили тележку до спавна — теперь её состояние можно
                // объявить. Раньше спавна NetworkVariable писать нечем.
                PublishState(true);
                return;
            }

            ApplyNetState(netState.Value);
            TryAdopt();
        }

        /// <summary>
        /// Клиент получил чужую тележку. Команду, поручни и уровень она везёт
        /// сама, а числа игры, отметку пропасти и стоянку берёт у правил раунда —
        /// те лежат в сцене на каждой машине.
        ///
        /// Отложено до приезда команды намеренно: спавн и первое состояние —
        /// два разных сообщения, и в кадре спавна тележка ещё ничья.
        /// </summary>
        private void TryAdopt()
        {
            if (adopted || Team == TeamSide.None)
            {
                return;
            }

            if (MinigameControllerBase.Current is not CarryItemMinigame game)
            {
                Debug.LogWarning($"{name}: тележка приехала в сцену без правил «Переноски» — настроить её нечем", this);
                return;
            }

            adopted = true;
            game.AdoptNetworkCart(this);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Выдать тележку команде. Числа модели переноски уходят в
        /// <see cref="MultiCarryObject"/> отсюда: так все значения спеки живут
        /// в одном ассете и крутятся на плейтесте без пересборки.
        ///
        /// Зовут правила раунда на авторитете — и до <c>NetworkObject.Spawn</c>,
        /// чтобы состояние уехало вместе со спавном, а не догоняло его.
        /// </summary>
        public void Initialize(CarryItemConfig gameConfig, TeamSide team, int handleCount, float voidY,
            Vector3 home, Quaternion homeFacing)
        {
            config = gameConfig;
            Team = team;
            voidLevel = voidY;
            homePosition = home;
            homeRotation = homeFacing;

            Water = 0;
            flags = 0;
            shownStep = -1;
            publishedWater = -1;
            tiltHoldTimer = 0f;
            leakAccumulator = 0f;
            hitCooldownTimer = 0f;
            returnAt = 0d;

            carry.Configure(BuildCarrySettings(config));
            carry.SetHandleCount(handleCount);
            carry.SetLoad(Load);
            carry.GrabLocked = false;

            PublishState(true);
            ApplyLevelVisual();
            ApplyTeamTint();
        }

        /// <summary>
        /// Донастроить тележку, приехавшую из сети. Команду, поручни и уровень
        /// она привезла сама — здесь только то, чего в трафике нет и быть не
        /// должно: общие числа игры, отметка пропасти и стоянка.
        /// </summary>
        public void ApplyNetworkSetup(CarryItemConfig gameConfig, float voidY, Vector3 home, Quaternion homeFacing)
        {
            config = gameConfig;
            voidLevel = voidY;
            homePosition = home;
            homeRotation = homeFacing;

            carry.Configure(BuildCarrySettings(config));
            carry.SetHandleCount(netState.Value.Handles);
            carry.SetLoad(Load);

            shownStep = -1;
            ApplyLevelVisual();
        }

        /// <summary>Числа переноски из конфига игры. Единственное место, где спека 8.4 превращается в модель.</summary>
        public static MultiCarrySettings BuildCarrySettings(CarryItemConfig config)
        {
            MultiCarrySettings settings = MultiCarrySettings.Default;
            settings.motion = MultiCarryMotion.Rolling;
            settings.handleLayout = MultiCarryHandleLayout.Cart;
            settings.handleHeight = config.HandleHeight;
            settings.cartHandleBack = config.CartHandleBack;
            settings.cartHandleFront = config.CartHandleFront;
            settings.cartHandleSide = config.CartHandleSide;
            settings.carrierStandoff = config.CarrierStandoff;
            settings.carrierSpeedCap = config.MaxCarrierSpeed;
            settings.maxSpeedEmpty = config.MaxSpeedEmpty;
            settings.maxSpeedFull = config.MaxSpeedFull;
            settings.accelerationEmpty = config.AccelerationEmpty;
            settings.accelerationFull = config.AccelerationFull;
            settings.rollingDeceleration = config.RollingDeceleration;
            settings.turnRate = config.TurnRate;
            settings.pullToSpeed = config.PullToSpeed;
            settings.tensionDeadzone = config.TensionDeadzone;
            settings.breakDistance = config.BreakDistance;
            settings.tetherFreeSpeedPerMeter = config.TetherFreeSpeedPerMeter;
            settings.tetherGrip = config.TetherGrip;
            settings.tiltThreshold = config.TiltAngleThreshold;
            settings.maxTiltAngle = config.MaxTiltAngle;
            settings.sloshPerDeltaSpeed = config.SloshPerDeltaSpeed;
            settings.tiltDamping = config.TiltDamping;
            settings.tiltRestoring = config.TiltRestoring;
            settings.throwImpulsePerCarrier = config.ShoveImpulsePerCarrier;
            settings.throwUpward = 0f;
            return settings;
        }

        /// <summary>Сколько поручней у тележки — размер команды. Меняется, когда команда теряет игрока.</summary>
        public void SetHandleCount(int count)
        {
            carry.SetHandleCount(count);
            PublishState(true);
        }

        // ========== ЕДИНСТВЕННАЯ ТОЧКА ИЗМЕНЕНИЯ УРОВНЯ ==========

        /// <summary>
        /// Изменить уровень воды. <b>Все наполнения и потери идут только сюда</b>,
        /// и считает их только авторитет: клиент видит результат уровнем.
        ///
        /// Возвращает, сколько реально изменилось, со знаком: в тележке могло
        /// остаться меньше, чем просили списать, или влезть меньше, чем налили,
        /// и бак обязан долить ровно списанное, а не запрошенное.
        /// </summary>
        public int ChangeWater(int delta, WaterLossReason reason)
        {
            if (!HasAuthority || config == null || delta == 0 || IsLost && delta > 0)
            {
                return 0;
            }

            int before = Water;
            Water = Mathf.Clamp(Water + delta, 0, config.CartCapacity);
            int applied = Water - before;
            if (applied == 0)
            {
                return 0;
            }

            carry.SetLoad(Load);
            PublishState(false);
            ApplyLevelVisual();
            WaterChanged?.Invoke(Mathf.Abs(applied), reason);

            // Уровень уедет сам, состоянием, — но по нему не видно, почему воды
            // стало меньше. Дискретные причины шлём отдельно и только под
            // эффекты; наполнение и слив длятся секундами и видны флагами.
            if (IsSpawned && IsServer && AnnouncesReason(reason))
            {
                AnnounceLossRpc(Mathf.Abs(applied), (byte)reason);
            }

            return applied;
        }

        private static bool AnnouncesReason(WaterLossReason reason) =>
            reason == WaterLossReason.Hit || reason == WaterLossReason.Tilt || reason == WaterLossReason.Shove ||
            reason == WaterLossReason.RamVictim || reason == WaterLossReason.RamAttacker ||
            reason == WaterLossReason.Void;

        /// <summary>
        /// Удар по тележке: брошенный предмет или ловушка. Все — 20 единиц с
        /// <b>общим</b> кулдауном: одно событие даёт один штраф, даже если по
        /// тележке попало сразу два кирпича. Кулдаун тикает у авторитета.
        /// </summary>
        public bool TakeHit(WaterLossReason reason)
        {
            if (!HasAuthority || IsLost || hitCooldownTimer > 0f)
            {
                return false;
            }

            hitCooldownTimer = config.HitCooldown;
            ChangeWater(-config.HitLoss, reason);
            return true;
        }

        /// <summary>Ловушка ударила. Направление и импульс тележки безразличны — цена удара одна.</summary>
        public void TakeTrapImpact(Vector3 direction, float force) => TakeHit(WaterLossReason.Hit);

        /// <summary>Кран говорит, набирается ли вода. Решает авторитет; флаг едет состоянием.</summary>
        public void SetFilling(bool value) => SetFlag(WaterCartNetState.FillingFlag, value);

        /// <summary>
        /// Бак говорит, сливается ли тележка. На время слива тележка держит крен
        /// назад — это вид, воду переливает сам бак.
        /// </summary>
        public void SetPouring(bool value)
        {
            if (!SetFlag(WaterCartNetState.PouringFlag, value))
            {
                return;
            }

            if (value)
            {
                carry.SetLeanBack(config.PourTiltAngle);
            }
            else
            {
                carry.ClearLean();
            }
        }

        private bool SetFlag(byte flag, bool value)
        {
            if (!HasAuthority)
            {
                return false;
            }

            bool had = (flags & flag) != 0;
            if (had == value)
            {
                return false;
            }

            flags = value ? (byte)(flags | flag) : (byte)(flags & ~flag);
            PublishState(true);
            return true;
        }

        // ========== СЕТЕВОЕ СОСТОЯНИЕ ==========

        /// <summary>
        /// Опубликовать состояние. Уровень уезжает ступенями по
        /// <c>waterStep</c> и на краях (пусто, полно); команда, поручни и флаги —
        /// сразу. Так наполнение под краном стоит пяти записей в секунду, а не
        /// двадцати пяти.
        /// </summary>
        private void PublishState(bool force)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (!force && publishedWater >= 0 && config != null)
            {
                int step = Mathf.Max(1, config.WaterStep);
                bool edge = Water == 0 || Water == config.CartCapacity;
                if (!edge && Water / step == publishedWater / step)
                {
                    return;
                }
            }

            publishedWater = Water;
            netState.Value = new WaterCartNetState
            {
                Team = (byte)Team,
                Handles = (byte)Mathf.Clamp(carry.HandleCount, 1, MultiCarryObject.MaxHandles),
                Water = (short)Mathf.Clamp(Water, 0, short.MaxValue),
                Flags = flags
            };
        }

        private void OnNetStateChanged(WaterCartNetState previous, WaterCartNetState current)
        {
            if (IsServer)
            {
                return;
            }

            ApplyNetState(current);
            TryAdopt();
        }

        private void ApplyNetState(in WaterCartNetState state)
        {
            Team = (TeamSide)state.Team;
            Water = state.Water;
            flags = state.Flags;
            carry.GrabLocked = IsLost;
            carry.SetLoad(Load);
            if (carry.HandleCount != state.Handles && state.Handles > 0)
            {
                carry.SetHandleCount(state.Handles);
            }

            ApplyTeamTint();
            ApplyLevelVisual();
        }

        /// <summary>
        /// Потеря случилась — отыграть её. Хосту не шлём: у него событие уже
        /// прошло на месте, в <see cref="ChangeWater"/>.
        /// </summary>
        [Rpc(SendTo.NotServer)]
        private void AnnounceLossRpc(int amount, byte reason) =>
            WaterChanged?.Invoke(amount, (WaterLossReason)reason);

        // ========== ПОТЕРИ ==========

        /// <summary>Толкнули с разгона — доля остатка расплёскивается, округляя вниз до целой единицы.</summary>
        private void OnShoved(int shovers)
        {
            ChangeWater(-Mathf.FloorToInt(Water * config.ShoveLossFraction), WaterLossReason.Shove);
        }

        private void Update()
        {
            if (config == null)
            {
                return;
            }

            // Индикация — у каждого своя: подсветка, струи и плеск читаются по
            // крену и флагам, а те приезжают поворотом и состоянием.
            UpdateIndication();

            if (!HasAuthority)
            {
                return;
            }

            float delta = Time.deltaTime;
            hitCooldownTimer = Mathf.Max(0f, hitCooldownTimer - delta);

            if (IsLost)
            {
                UpdateReturn();
                return;
            }

            UpdateTiltLeak(delta);
            CheckVoid();
        }

        /// <summary>
        /// Утечка от крена. Задержка перед началом — чтобы мгновенный клевок
        /// за порог и обратно не стоил ничего; темп растёт с заполнением:
        /// полная плещет через борт вдвое сильнее полупустой.
        /// </summary>
        private void UpdateTiltLeak(float delta)
        {
            if (!carry.BeyondTiltThreshold || Water <= 0)
            {
                tiltHoldTimer = 0f;
                leakAccumulator = 0f;
                return;
            }

            tiltHoldTimer += delta;
            if (tiltHoldTimer < config.TiltGraceSeconds)
            {
                return;
            }

            leakAccumulator += config.TiltLossPerSecond * (config.TiltLossLoadFloor + Load) * delta;
            int whole = Mathf.FloorToInt(leakAccumulator);
            if (whole <= 0)
            {
                return;
            }

            leakAccumulator -= whole;
            ChangeWater(-whole, WaterLossReason.Tilt);
        }

        /// <summary>
        /// Улетела в пропасть — теряется весь остаток, а сама тележка через
        /// отсчёт вернётся на стоянку. Оставить её на дне значило бы оставить
        /// команду без тары до конца раунда (спека v2, 5.1).
        /// </summary>
        private void CheckVoid()
        {
            if (transform.position.y > voidLevel)
            {
                return;
            }

            ChangeWater(-Water, WaterLossReason.Void);
            carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            carry.GrabLocked = true;
            SetFlag(WaterCartNetState.FillingFlag, false);
            SetPouring(false);
            SetFlag(WaterCartNetState.LostFlag, true);
            returnAt = NetworkClock.Now + config.CartRespawnSeconds;
            Lost?.Invoke(this);
        }

        private void UpdateReturn()
        {
            if (NetworkClock.Now < returnAt)
            {
                return;
            }

            carry.ResetPose(homePosition, homeRotation);
            carry.GrabLocked = false;
            SetFlag(WaterCartNetState.LostFlag, false);
            Returned?.Invoke(this);
        }

        /// <summary>Вернуть тележку на стоянку немедленно — конец раунда, сброс.</summary>
        public void ReturnHome()
        {
            if (!HasAuthority)
            {
                return;
            }

            carry.ResetPose(homePosition, homeRotation);
            carry.GrabLocked = false;
            flags = 0;
            carry.ClearLean();
            PublishState(true);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!HasAuthority || IsLost)
            {
                return;
            }

            // Прилетевший предмет бьёт тележку; лежащий под колесом — нет.
            if (collision.relativeVelocity.sqrMagnitude < config.ItemHitMinSpeed * config.ItemHitMinSpeed)
            {
                return;
            }

            if (collision.gameObject.GetComponentInParent<PickupItem>() == null)
            {
                return;
            }

            TakeHit(WaterLossReason.Hit);
        }

        // ========== ВИД ==========

        /// <summary>
        /// Уровень воды ступенями: симуляции жидкости нет, меш опускается и
        /// поднимается ступенькой. Правится только при смене ступени.
        /// </summary>
        private void ApplyLevelVisual()
        {
            if (waterMesh == null || config == null)
            {
                return;
            }

            int step = Mathf.CeilToInt(Water / (float)config.WaterStep);
            if (step == shownStep)
            {
                return;
            }

            shownStep = step;

            int totalSteps = Mathf.Max(1, config.CartCapacity / config.WaterStep);
            float fraction = Mathf.Clamp01(step / (float)totalSteps);

            Vector3 scale = waterMesh.localScale;
            scale.y = Mathf.Max(0.001f, fraction);
            waterMesh.localScale = scale;
        }

        /// <summary>
        /// Индикация крена: чем ближе к порогу, тем тревожнее тележка. Читается
        /// <b>всеми</b>, а не только тем, кто рванул: виноват один, платят все.
        /// Струи и плеск — по крену и флагам, которые у всех одинаковые.
        /// </summary>
        private void UpdateIndication()
        {
            float threshold = config.TiltAngleThreshold;
            float alarmStart = threshold * alarmStartFraction;
            float tilt = carry.TiltAngle;

            Color color;
            if (carry.BeyondTiltThreshold)
            {
                color = pouringColor;
            }
            else
            {
                float t = threshold > alarmStart
                    ? Mathf.Clamp01((tilt - alarmStart) / (threshold - alarmStart))
                    : 0f;
                color = Color.Lerp(calmColor, alarmColor, t);
            }

            ApplyIndicatorColor(color);
            UpdateSloshSound(Mathf.Clamp01(tilt / Mathf.Max(threshold, 0.01f)));

            bool leaking = carry.BeyondTiltThreshold && Water > 0 && !IsLost;
            SetParticles(leakJet, ref leakShown, leaking);
            SetLoop(leakLoop, leaking);

            SetParticles(pourJet, ref pourShown, IsPouring);
            SetParticles(fillSplash, ref fillShown, IsFilling);
            SetLoop(fillLoop, IsFilling);
        }

        private void ApplyIndicatorColor(Color color)
        {
            if (tiltIndicator == null)
            {
                return;
            }

            // Через PropertyBlock, а не через material: обращение к material
            // создаёт копию на каждую тележку.
            tiltIndicator.GetPropertyBlock(materialBlock);
            materialBlock.SetColor(BaseColorId, color);
            materialBlock.SetColor(ColorId, color);
            tiltIndicator.SetPropertyBlock(materialBlock);
        }

        private static void SetParticles(ParticleSystem system, ref bool shown, bool playing)
        {
            if (system == null || shown == playing)
            {
                return;
            }

            shown = playing;
            if (playing)
            {
                system.Play(true);
            }
            else
            {
                // Останавливаем только выпуск: уже вылетевшие капли обязаны
                // долететь и упасть, иначе струя пропадает в воздухе.
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static void SetLoop(AudioSource source, bool playing)
        {
            if (source == null || source.isPlaying == playing)
            {
                return;
            }

            if (playing)
            {
                source.Play();
            }
            else
            {
                source.Stop();
            }
        }

        /// <summary>Цвет команды на ободе и поручнях. Своя тележка от чужой иначе не отличается: обе синие от воды.</summary>
        private void ApplyTeamTint()
        {
            if (teamTint == null || teamTint.Length == 0 || paintedTeam == Team)
            {
                return;
            }

            paintedTeam = Team;
            Color color = TeamPalette.ColorOf(Team);

            for (int i = 0; i < teamTint.Length; i++)
            {
                Renderer target = teamTint[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(materialBlock);
                materialBlock.SetColor(BaseColorId, color);
                materialBlock.SetColor(ColorId, color);
                target.SetPropertyBlock(materialBlock);
            }
        }

        private void UpdateSloshSound(float intensity)
        {
            if (sloshLoop == null)
            {
                return;
            }

            bool audible = carry.IsCarried && Water > 0 && intensity > 0.01f;
            if (sloshLoop.isPlaying != audible)
            {
                if (audible)
                {
                    sloshLoop.Play();
                }
                else
                {
                    sloshLoop.Stop();
                }
            }

            if (audible)
            {
                sloshLoop.volume = intensity;
                sloshLoop.pitch = Mathf.Lerp(0.9f, 1.35f, intensity);
            }
        }
    }
}
