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
    /// Состояние бутыли одной структурой: чья она, сколько у неё ручек и
    /// сколько осталось воды.
    ///
    /// Вместе, а не тремя каналами: команда и число ручек назначаются в тот же
    /// миг, что и первый уровень воды, — при выдаче со штабеля. Приехавший
    /// отдельно уровень воды по бутыли без команды разбирать было бы некуда.
    /// </summary>
    public struct WaterBottleNetState : INetworkSerializable, IEquatable<WaterBottleNetState>
    {
        /// <summary>Чья бутыль, <see cref="TeamSide"/> байтом.</summary>
        public byte Team;

        /// <summary>Сколько у бутыли ручек — размер команды на момент выдачи.</summary>
        public byte Handles;

        /// <summary>Остаток воды, единиц. Вместимость 100, в short помещается с запасом.</summary>
        public short Water;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Handles);
            serializer.SerializeValue(ref Water);
        }

        public bool Equals(WaterBottleNetState other) =>
            Team == other.Team && Handles == other.Handles && Water == other.Water;
    }

    /// <summary>
    /// Бутыль с водой: сколько в ней осталось и куда это утекает.
    ///
    /// Вся механика переноски — ручки, натяжение, наклон, срыв — живёт в
    /// <see cref="MultiCarryObject"/> и про воду не знает ничего. Здесь только
    /// вода: сколько её, по каким событиям она уходит и как это видно снаружи.
    ///
    /// <b>Одна точка расхода — <see cref="SpendWater"/>.</b> Через неё проходят
    /// все виды потерь без исключения: удар, наклон, падение, бросок, таран,
    /// слив в бак, пропасть. Считает её только авторитет: уровень воды — это
    /// счёт, и клиенту его не доверяют ни на кадр.
    ///
    /// Отсчёты — кулдаун удара, задержка перед утечкой, исчезновение пустой —
    /// тикают там же, у авторитета. Клиент видит результат уровнем воды и
    /// отыгрывает индикацию: подсветку и звук считает каждая машина сама, по
    /// наклону, который и так приезжает поворотом.
    /// </summary>
    [RequireComponent(typeof(MultiCarryObject))]
    public sealed class WaterBottle : NetworkBehaviour, ITrapImpactTarget
    {
        [SerializeField] private CarryItemConfig config;

        [Header("Вид")]
        [Tooltip("Меш воды внутри бутыли. Растягивается по уровню ступенями")]
        [SerializeField] private Transform waterMesh;
        [Tooltip("Рендерер, который подсвечивает наклон. Пусто — берётся с меша воды")]
        [SerializeField] private Renderer tiltIndicator;
        [Tooltip("Цвет спокойной бутыли")]
        [SerializeField] private Color calmColor = new Color(0.2f, 0.5f, 0.95f);
        [Tooltip("Цвет на подходе к порогу наклона")]
        [SerializeField] private Color alarmColor = new Color(0.95f, 0.75f, 0.15f);
        [Tooltip("Цвет, когда вода уже льётся")]
        [SerializeField] private Color pouringColor = new Color(0.95f, 0.2f, 0.15f);
        [Tooltip("С какой доли порога наклона начинается тревога. 0.5 — с половины")]
        [Range(0f, 1f)]
        [SerializeField] private float alarmStartFraction = 0.5f;
        [Tooltip("Струя из горлышка. Бьёт ровно тогда, когда вода уходит от перекоса")]
        [SerializeField] private ParticleSystem pourJet;
        [Tooltip("Что красится в цвет команды: крышка бутыли и прочие метки принадлежности")]
        [SerializeField] private Renderer[] teamTint;

        [Header("Звук")]
        [Tooltip("Нарастающий плеск: громкость и тон растут вместе с наклоном")]
        [SerializeField] private AudioSource sloshLoop;
        [Tooltip("Отдельный звук самой утечки")]
        [SerializeField] private AudioSource leakLoop;

        /// <summary>Вода ушла: сколько и почему. Под звук, VFX и счёт бака.</summary>
        public event Action<int, WaterLossReason> WaterSpent;

        /// <summary>Бутыль перестала существовать: слита, улетела в пропасть, пустую бросили.</summary>
        public event Action<WaterBottle> Gone;

        /// <summary>Чья бутыль, сколько ручек, сколько воды. Пишет сервер, читают все.</summary>
        private readonly NetworkVariable<WaterBottleNetState> netState =
            new NetworkVariable<WaterBottleNetState>();

        private MultiCarryObject carry;
        private MaterialPropertyBlock materialBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float hitCooldownTimer;
        private float tiltHoldTimer;
        private float leakAccumulator;
        private float despawnTimer;
        private bool despawnPending;
        private float voidLevel = float.NegativeInfinity;
        private int shownStep = -1;
        private bool gone;
        private bool pouringShown;
        private TeamSide paintedTeam = (TeamSide)byte.MaxValue;

        /// <summary>Клиент уже разобрал эту бутыль по штабелю своей команды.</summary>
        private bool adopted;

        /// <summary>Сколько воды осталось, единиц.</summary>
        public int Water { get; private set; }

        /// <summary>Чья это бутыль. Чужой за её ручку не возьмётся.</summary>
        public TeamSide Team { get; private set; } = TeamSide.None;

        /// <summary>Механика переноски этой бутыли.</summary>
        public MultiCarryObject Carry => carry;

        /// <summary>Бутыль в полёте после броска. На финальном свистке такая не засчитывается.</summary>
        public bool InFlight => carry != null && carry.InFlight;

        /// <summary>Бутыль уже списана и доживает кадр до уничтожения.</summary>
        public bool IsGone => gone;

        /// <summary>Вправе ли эта машина списывать воду. Вне сети — да, иначе только сервер.</summary>
        private bool HasAuthority => IsSpawned ? IsServer : WorldAuthority.HasAuthority;

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
            carry.Dropped += OnDropped;
            carry.Thrown += OnThrown;
        }

        private void OnDisable()
        {
            carry.Dropped -= OnDropped;
            carry.Thrown -= OnThrown;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged += OnNetStateChanged;

            if (IsServer)
            {
                // Штабель настроил бутыль до спавна — теперь её состояние можно
                // объявить. Раньше спавна NetworkVariable писать нечем: канал
                // ещё не заведён.
                PublishState();
                return;
            }

            ApplyNetState(netState.Value);
            TryAdopt();
        }

        /// <summary>
        /// Клиент получил чужую бутыль. Команду, ручки и уровень она везёт сама,
        /// а числа игры и отметку пропасти берёт у правил раунда — те лежат в
        /// сцене на каждой машине.
        ///
        /// Отложено до приезда команды намеренно: спавн и первое состояние —
        /// два разных сообщения, и в кадре спавна бутыль ещё ничья. Разобрать
        /// её тогда было бы не к какому штабелю.
        /// </summary>
        private void TryAdopt()
        {
            if (adopted || Team == TeamSide.None)
            {
                return;
            }

            if (MinigameControllerBase.Current is not CarryItemMinigame game)
            {
                Debug.LogWarning($"{name}: бутыль приехала в сцену без правил «Переноски» — настроить её нечем", this);
                return;
            }

            adopted = true;
            game.AdoptNetworkBottle(this);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;

            // Сервер объявил, что бутыли больше нет. Штабель ждёт ровно этого,
            // чтобы разрешить взять новую.
            if (!gone)
            {
                gone = true;
                Gone?.Invoke(this);
            }

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Выдать бутыль команде. Числа модели переноски уходят в
        /// <see cref="MultiCarryObject"/> отсюда: так все значения спеки живут
        /// в одном ассете и крутятся на плейтесте без пересборки.
        ///
        /// Зовёт только штабель на авторитете — и до <c>NetworkObject.Spawn</c>,
        /// чтобы состояние уехало вместе со спавном, а не догоняло его.
        /// </summary>
        public void Initialize(CarryItemConfig gameConfig, TeamSide team, int handleCount, float voidY)
        {
            config = gameConfig;
            Team = team;
            voidLevel = voidY;

            Water = config.BottleCapacity;
            shownStep = -1;
            gone = false;
            despawnPending = false;
            despawnTimer = 0f;
            tiltHoldTimer = 0f;
            leakAccumulator = 0f;
            hitCooldownTimer = 0f;

            carry.Configure(BuildCarrySettings(config));
            carry.SetHandleCount(handleCount);

            PublishState();
            ApplyLevelVisual();
            ApplyTeamTint();
        }

        /// <summary>
        /// Донастроить бутыль, приехавшую из сети. Команду, ручки и уровень она
        /// привезла сама — здесь только то, чего в трафике нет и быть не должно:
        /// общие числа игры и отметка пропасти.
        /// </summary>
        public void ApplyNetworkSetup(CarryItemConfig gameConfig, float voidY)
        {
            config = gameConfig;
            voidLevel = voidY;

            carry.Configure(BuildCarrySettings(config));
            carry.SetHandleCount(netState.Value.Handles);

            shownStep = -1;
            ApplyLevelVisual();
        }

        /// <summary>Числа переноски из конфига игры. Единственное место, где спека 8.4 превращается в модель.</summary>
        public static MultiCarrySettings BuildCarrySettings(CarryItemConfig config)
        {
            MultiCarrySettings settings = MultiCarrySettings.Default;
            settings.handleRadius = config.HandleRadius;
            settings.handleHeight = config.HandleHeight;
            settings.carrierStandoff = config.CarrierStandoff;
            settings.carryClearance = config.CarryClearance;
            settings.maxObjectSpeed = config.MaxObjectSpeed;
            settings.carrierSpeedCap = config.MaxCarrierSpeed;
            settings.breakDistance = config.BreakDistance;
            settings.tiltThreshold = config.TiltAngleThreshold;
            settings.maxTiltAngle = config.MaxTiltAngle;
            settings.pullToSpeed = config.PullToSpeed;
            settings.tiltFromTorque = config.TiltFromTorque;
            settings.tiltFromSupportLoss = config.TiltFromSupportLoss;
            settings.tiltDamping = config.TiltDamping;
            settings.tiltRestoring = config.TiltRestoring;
            settings.tensionDeadzone = config.TensionDeadzone;
            settings.tetherFreeSpeedPerMeter = config.TetherFreeSpeedPerMeter;
            settings.tetherGrip = config.TetherGrip;
            settings.throwImpulsePerCarrier = config.ThrowImpulsePerCarrier;
            settings.throwUpward = config.ThrowUpward;
            return settings;
        }

        // ========== ЕДИНСТВЕННАЯ ТОЧКА РАСХОДА ==========

        /// <summary>
        /// Списать воду. <b>Все виды потерь идут только сюда</b>, и считает их
        /// только авторитет: клиент видит результат уровнем воды.
        ///
        /// Возвращает, сколько реально списано: в бутыли могло остаться меньше,
        /// чем просили, и бак обязан долить ровно списанное, а не запрошенное.
        /// </summary>
        public int SpendWater(int amount, WaterLossReason reason)
        {
            if (!HasAuthority || gone || amount <= 0 || Water <= 0)
            {
                return 0;
            }

            int spent = Mathf.Min(amount, Water);
            Water -= spent;

            PublishState();
            ApplyLevelVisual();
            WaterSpent?.Invoke(spent, reason);

            // Уровень уедет сам, состоянием, — но по нему не видно, почему воды
            // стало меньше. Причину шлём отдельно и только под эффекты: плеск,
            // брызги и цифру над бутылью видят все, а не один виновник.
            if (IsSpawned && IsServer)
            {
                AnnounceLossRpc(spent, (byte)reason);
            }

            // Опустела, лёжа на земле, — пошёл отсчёт до исчезновения. Слив в
            // бак сюда не попадает: там бутыль убирают сразу, не дожидаясь.
            if (Water == 0 && reason != WaterLossReason.Poured)
            {
                StartDespawnIfEmpty();
            }

            return spent;
        }

        /// <summary>
        /// Удар по бутыли: игрок, брошенный предмет, ловушка. Все три — 20
        /// единиц с <b>общим</b> кулдауном: одно событие даёт один штраф, даже
        /// если по бутыли попало сразу два кирпича. Кулдаун тикает у авторитета.
        /// </summary>
        public bool TakeHit(WaterLossReason reason)
        {
            if (!HasAuthority || gone || hitCooldownTimer > 0f)
            {
                return false;
            }

            hitCooldownTimer = config.HitCooldown;
            SpendWater(config.HitLoss, reason);
            return true;
        }

        /// <summary>Ловушка ударила. Направление и импульс бутыли безразличны — цена удара одна.</summary>
        public void TakeTrapImpact(Vector3 direction, float force) => TakeHit(WaterLossReason.Hit);

        /// <summary>
        /// Убрать бутыль из игры. Через неё же проходит и слив, и пропасть, и
        /// исчезновение брошенной пустой: штабель ждёт ровно этого события,
        /// чтобы выдать новую. Решает авторитет, остальные узнают из despawn.
        /// </summary>
        public void Vanish()
        {
            if (gone || !HasAuthority)
            {
                return;
            }

            gone = true;
            carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            Gone?.Invoke(this);

            if (IsSpawned)
            {
                NetworkObject.Despawn(true);
                return;
            }

            Destroy(gameObject);
        }

        // ========== СЕТЕВОЕ СОСТОЯНИЕ ==========

        private void PublishState()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            netState.Value = new WaterBottleNetState
            {
                Team = (byte)Team,
                Handles = (byte)Mathf.Clamp(carry.HandleCount, 1, MultiCarryObject.MaxHandles),
                Water = (short)Mathf.Clamp(Water, 0, short.MaxValue)
            };
        }

        private void OnNetStateChanged(WaterBottleNetState previous, WaterBottleNetState current)
        {
            if (IsServer)
            {
                return;
            }

            ApplyNetState(current);
            TryAdopt();
            ApplyLevelVisual();
        }

        private void ApplyNetState(in WaterBottleNetState state)
        {
            Team = (TeamSide)state.Team;
            Water = state.Water;
            ApplyTeamTint();
        }

        /// <summary>
        /// Потеря случилась — отыграть её. Хосту не шлём: у него событие уже
        /// прошло на месте, в <see cref="SpendWater"/>.
        /// </summary>
        [Rpc(SendTo.NotServer)]
        private void AnnounceLossRpc(int amount, byte reason) =>
            WaterSpent?.Invoke(amount, (WaterLossReason)reason);

        // ========== ПОТЕРИ ==========

        private void OnDropped()
        {
            SpendWater(config.DropLoss, WaterLossReason.Drop);
            StartDespawnIfEmpty();
        }

        private void OnThrown(int throwers)
        {
            // Половина остатка, округляя вниз до целой единицы: остаток в
            // бутыли всегда целый, и половина от 5 не должна давать 2.5.
            SpendWater(Mathf.FloorToInt(Water * config.ThrowLossFraction), WaterLossReason.Throw);
        }

        private void Update()
        {
            if (gone || config == null)
            {
                return;
            }

            // Индикация — у каждого своя: подсветка и плеск читаются по наклону,
            // а наклон и так приезжает поворотом.
            UpdateIndication();

            if (!HasAuthority)
            {
                return;
            }

            float delta = Time.deltaTime;

            hitCooldownTimer = Mathf.Max(0f, hitCooldownTimer - delta);

            UpdateTiltLeak(delta);
            UpdateDespawn(delta);
            CheckVoid();
        }

        /// <summary>
        /// Утечка от наклона. Задержка перед началом — чтобы мгновенный клевок
        /// за порог и обратно не стоил ничего; расход считается за секунду, а не
        /// за кадр, поэтому три секунды крена дают ровно десять единиц:
        /// секунда задержки плюс два полных тика по пять.
        /// </summary>
        private void UpdateTiltLeak(float delta)
        {
            if (!carry.BeyondTiltThreshold)
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

            leakAccumulator += config.TiltLossPerSecond * delta;
            int whole = Mathf.FloorToInt(leakAccumulator);
            if (whole <= 0)
            {
                return;
            }

            leakAccumulator -= whole;
            SpendWater(whole, WaterLossReason.Tilt);
        }

        /// <summary>
        /// Брошенная пустая бутыль исчезает через отсчёт, а пустая <b>в руках</b>
        /// не исчезает: её видно, её обидно, и это часть шутки (спека 5.1).
        /// </summary>
        private void UpdateDespawn(float delta)
        {
            if (!despawnPending)
            {
                return;
            }

            if (carry.IsCarried || Water > 0)
            {
                despawnPending = false;
                return;
            }

            despawnTimer -= delta;
            if (despawnTimer <= 0f)
            {
                Vanish();
            }
        }

        private void StartDespawnIfEmpty()
        {
            if (Water > 0 || carry.IsCarried)
            {
                return;
            }

            despawnPending = true;
            despawnTimer = config.EmptyBottleDespawnSeconds;
        }

        /// <summary>Улетела в пропасть — теряется целиком вместе с остатком.</summary>
        private void CheckVoid()
        {
            if (transform.position.y > voidLevel)
            {
                return;
            }

            SpendWater(Water, WaterLossReason.Void);
            Vanish();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (gone || !HasAuthority)
            {
                return;
            }

            // Прилетевший предмет бьёт бутыль; лежащий под ногами — нет.
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
        /// Уровень воды ступенями по пять единиц: симуляции жидкости нет, меш
        /// опускается ступенькой. Правится только при смене ступени — иначе
        /// на каждый кадр слива уходил бы лишний расчёт.
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

            int totalSteps = Mathf.Max(1, config.BottleCapacity / config.WaterStep);
            float fraction = Mathf.Clamp01(step / (float)totalSteps);

            Vector3 scale = waterMesh.localScale;
            scale.y = Mathf.Max(0.001f, fraction);
            waterMesh.localScale = scale;
        }

        /// <summary>
        /// Индикация наклона: чем ближе к порогу, тем тревожнее бутыль. Читается
        /// <b>всеми</b> несущими, а не только тем, кто рванул: виноват один,
        /// платят все, и видеть это должны все. Свойство самой бутыли, а не
        /// подсказка своей команде, — соперник видит ровно то же.
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

            // Струя слышна там же, где видна: наклон за порогом и вода ещё есть.
            bool pouring = carry.BeyondTiltThreshold && Water > 0;
            SetPourJet(pouring);
            SetLeakSound(pouring);
        }

        private void ApplyIndicatorColor(Color color)
        {
            if (tiltIndicator == null)
            {
                return;
            }

            // Через PropertyBlock, а не через material: обращение к material
            // создаёт копию на каждую бутыль, и за раунд их набирается дюжина.
            tiltIndicator.GetPropertyBlock(materialBlock);
            materialBlock.SetColor(BaseColorId, color);
            materialBlock.SetColor(ColorId, color);
            tiltIndicator.SetPropertyBlock(materialBlock);
        }

        /// <summary>
        /// Струя из горлышка. Единственное, по чему видно <b>куда</b> уходит
        /// вода: цвет говорит «плохо», а льётся она вот отсюда и вот туда.
        /// Считает каждая машина сама — наклон приезжает поворотом, и повод
        /// для струи у всех одинаковый.
        /// </summary>
        private void SetPourJet(bool pouring)
        {
            if (pourJet == null || pouringShown == pouring)
            {
                return;
            }

            pouringShown = pouring;

            if (pouring)
            {
                pourJet.Play(true);
            }
            else
            {
                // Останавливаем только выпуск: уже вылетевшие капли обязаны
                // долететь и упасть, иначе струя пропадает в воздухе.
                pourJet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// Цвет команды на крышке. Своя бутыль от чужой иначе не отличается
        /// вовсе: в горлышке их две, и обе синие от воды.
        /// </summary>
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

            bool audible = carry.IsCarried && intensity > 0.01f;
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

        private void SetLeakSound(bool leaking)
        {
            if (leakLoop == null || leakLoop.isPlaying == leaking)
            {
                return;
            }

            if (leaking)
            {
                leakLoop.Play();
            }
            else
            {
                leakLoop.Stop();
            }
        }
    }
}
