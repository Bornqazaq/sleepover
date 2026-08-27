using System;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Session;
using Igruha.Core.Traps;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Бутыль с водой: сколько в ней осталось и куда это утекает.
    ///
    /// Вся механика переноски — ручки, натяжение, наклон, срыв — живёт в
    /// <see cref="MultiCarryObject"/> и про воду не знает ничего. Здесь только
    /// вода: сколько её, по каким событиям она уходит и как это видно снаружи.
    ///
    /// <b>Одна точка расхода — <see cref="SpendWater"/>.</b> Через неё проходят
    /// все виды потерь без исключения: удар, наклон, падение, бросок, таран,
    /// слив в бак, пропасть. В фазе 3 она уйдёт за <c>IsServer</c>, и ни одно
    /// правило переписывать не придётся.
    /// </summary>
    [RequireComponent(typeof(MultiCarryObject))]
    public sealed class WaterBottle : MonoBehaviour, ITrapImpactTarget
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

        [Header("Звук")]
        [Tooltip("Нарастающий плеск: громкость и тон растут вместе с наклоном")]
        [SerializeField] private AudioSource sloshLoop;
        [Tooltip("Отдельный звук самой утечки")]
        [SerializeField] private AudioSource leakLoop;

        /// <summary>Вода ушла: сколько и почему. Под звук, VFX и счёт бака.</summary>
        public event Action<int, WaterLossReason> WaterSpent;

        /// <summary>Бутыль перестала существовать: слита, улетела в пропасть, пустую бросили.</summary>
        public event Action<WaterBottle> Gone;

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

        /// <summary>
        /// Выдать бутыль команде. Числа модели переноски уходят в
        /// <see cref="MultiCarryObject"/> отсюда: так все значения спеки живут
        /// в одном ассете и крутятся на плейтесте без пересборки.
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
        /// Списать воду. <b>Все виды потерь идут только сюда</b> — в фазе 3
        /// метод уйдёт за <c>IsServer</c> целиком, и логика правил не изменится.
        ///
        /// Возвращает, сколько реально списано: в бутыли могло остаться меньше,
        /// чем просили, и бак обязан долить ровно списанное, а не запрошенное.
        /// </summary>
        public int SpendWater(int amount, WaterLossReason reason)
        {
            if (gone || amount <= 0 || Water <= 0)
            {
                return 0;
            }

            int spent = Mathf.Min(amount, Water);
            Water -= spent;

            ApplyLevelVisual();
            WaterSpent?.Invoke(spent, reason);

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
        /// если по бутыли попало сразу два кирпича.
        /// </summary>
        public bool TakeHit(WaterLossReason reason)
        {
            if (gone || hitCooldownTimer > 0f)
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
        /// чтобы выдать новую.
        /// </summary>
        public void Vanish()
        {
            if (gone)
            {
                return;
            }

            gone = true;
            carry.ReleaseAll(CarryReleaseReason.RoundEnded);
            Gone?.Invoke(this);
            Destroy(gameObject);
        }

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

            float delta = Time.deltaTime;

            hitCooldownTimer = Mathf.Max(0f, hitCooldownTimer - delta);

            UpdateTiltLeak(delta);
            UpdateIndication();
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
                SetLeakSound(false);
                return;
            }

            tiltHoldTimer += delta;
            if (tiltHoldTimer < config.TiltGraceSeconds)
            {
                return;
            }

            SetLeakSound(Water > 0);

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
            if (gone || !WorldAuthority.HasAuthority)
            {
                return;
            }

            // Прилетевший предмет бьёт бутыль; лежащий под ногами — нет.
            if (collision.relativeVelocity.sqrMagnitude < config.ItemHitMinSpeed * config.ItemHitMinSpeed)
            {
                return;
            }

            if (collision.gameObject.GetComponentInParent<Igruha.Core.Items.PickupItem>() == null)
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
