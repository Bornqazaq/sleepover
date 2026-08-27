using System;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Физический мотор персонажа (Rigidbody, только FixedUpdate).
    /// Все игровые значения приходят из общего CharacterConfig.
    /// Публичные точки входа (ApplyPush, Knockdown, TeleportTo) — будущие
    /// server-authoritative вызовы; логика движения при переходе на NGO не меняется.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private CharacterConfig config;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private LayerMask groundLayer;
        [Tooltip("Во что упирается макушка при попытке встать из приседа, помимо земли: укрытия, платформы, декорации")]
        [SerializeField] private LayerMask crouchCeilingLayers;
        [Tooltip("Камера, относительно которой считается направление ввода. Пусто — берётся Camera.main при старте")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("Точка на уровне груди для привязки Cinemachine — у персонажей разного роста корень стоит на разной высоте, из-за чего камера кадрирует их по-разному. Выставляется CharacterPrefabBuilder пропорционально росту")]
        [SerializeField] private Transform cameraTarget;

        [Header("Длительности нокдауна (привязаны к клипам ЭТОГО персонажа)")]
        [Tooltip("Удар в лицо: полёт назад + подъём со спины. Выставляется билдером аниматора по длине клипов этого персонажа")]
        [SerializeField] private float knockdownFrontDuration = 2.8f;
        [Tooltip("Удар со спины: падение вперёд + подъём с живота. Выставляется билдером аниматора по длине клипов этого персонажа")]
        [SerializeField] private float knockdownBackDuration = 2.2f;

        public event Action Jumped;
        public event Action<KnockdownType> KnockdownStarted;
        public event Action KnockdownEnded;
        public event Action<bool> MovementLockChanged;

        /// <summary>Персонажа перенесли: респаун, старт мини-игры, смена арены.</summary>
        public event Action Teleported;

        public CharacterConfig Config => config;
        /// <summary>Куда должна целиться Cinemachine. Без назначенной точки — сам корень (запасной вариант для старых префабов).</summary>
        public Transform CameraTarget => cameraTarget != null ? cameraTarget : transform;
        public bool IsGrounded { get; private set; }
        public bool IsKnockedDown => knockdownTimer > 0f;

        /// <summary>
        /// Персонаж не принимает толчки и импульсы. Нужен ролям, которые обязаны
        /// стоять на своём месте весь раунд: Водящий «Плачущих ангелов» на
        /// постаменте, ведущий в любой асимметричной игре. Без этого его просто
        /// сшибают с точки, и раунд ломается.
        ///
        /// Не влияет на телепорт и респавн — те двигают персонажа адресно,
        /// а не через физику.
        /// </summary>
        public bool ImpulseImmune { get; set; }

        /// <summary>
        /// Персонаж сидит: капсула ниже, скорость меньше. Отличается от запроса —
        /// под низким потолком встать нельзя, и состояние держится дальше.
        /// </summary>
        public bool IsCrouched { get; private set; }

        /// <summary>
        /// «Замри»: ввод движения и прыжка обрублен, тело гасит бег и стоит.
        /// В отличие от нокдауна персонаж не падает и не отыгрывает клип, а
        /// камера остаётся управляемой — это состояние держит внешняя логика
        /// (в сетевой фазе владелец ставит его по реплицированному состоянию).
        ///
        /// Блокировка и нокдаун независимы: управление вернётся, когда сняты оба.
        /// Импульсы, толчки, нокдаун и телепорт продолжают работать.
        /// </summary>
        public bool MovementLocked
        {
            get => movementLocked;
            set
            {
                if (movementLocked == value)
                {
                    return;
                }

                movementLocked = value;

                // Нажатия, сделанные под блокировкой, не должны выстрелить в момент снятия.
                if (value)
                {
                    inputReader?.ConsumeJump();
                    jumpBufferTimer = 0f;
                }

                MovementLockChanged?.Invoke(value);
            }
        }
        /// <summary>0..1 — доля от максимальной скорости, для анимаций.</summary>
        public float NormalizedSpeed { get; private set; }

        /// <summary>
        /// Множитель разгона от поверхности под ногами. Единица — обычный пол.
        /// Скорость персонажа задаётся напрямую через linearVelocity, поэтому
        /// трение физматериала на горизонтальное движение не влияет вообще —
        /// лёд, грязь и масло делаются только этими двумя множителями.
        /// </summary>
        public float AccelerationMultiplier { get; private set; } = 1f;

        /// <summary>Множитель торможения от поверхности. Он и даёт скольжение: на льду тормозить дольше, чем разгоняться.</summary>
        public float DecelerationMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Направление взгляда по авторитетному повороту Rigidbody.
        /// Transform отстаёт от физики на кадр после MoveRotation/TeleportTo,
        /// а по этому направлению решается, в лицо прилетело или в спину.
        /// </summary>
        public Vector3 Facing => rb != null ? rb.rotation * Vector3.forward : transform.forward;

        /// <summary>
        /// Вправе ли эта машина решать, что произошло с персонажем. В сетевой игре
        /// true только на сервере, в одиночной — всегда.
        ///
        /// Мировые источники воздействий (ловушки, зоны смерти) обязаны спросить
        /// перед тем, как что-то решать: их триггеры срабатывают на каждой машине
        /// матча, и без проверки событие засчитается столько раз, сколько игроков.
        /// </summary>
        public bool HasWorldAuthority => worldEffectRelay == null || worldEffectRelay.HasAuthority;

        private Rigidbody rb;
        private CapsuleCollider capsule;
        private IWorldEffectRelay worldEffectRelay;
        private Quaternion targetRotation;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float knockdownTimer;
        private float standingHeight;
        private Vector3 standingCenter;
        private bool crouchRequested;
        private float crouchBlend;
        private bool movementLocked;
        private bool facingOverridden;
        private Component surfaceSource;
        private Component speedCapSource;

        /// <summary>Потолок скорости от внешней роли, м/с. Ноль — потолка нет.</summary>
        private float speedCap;
        private float fallSpeed;
        private bool wasGrounded = true;
        private float launchGraceTimer;
        private Vector3 groundNormal = Vector3.up;
        private bool standingOnGround;
        private float snapSuppressTimer;
        private bool interpolationSuppressed;

        /// <summary>Сколько секунд после импульса вверх лежачее демпфирование не включается: хватает пары шагов физики.</summary>
        private const float LaunchGraceTime = 0.1f;

        /// <summary>Насколько щуп всхождения смотрит вперёд за пределы капсулы, м.</summary>
        private const float StepProbeReach = 0.12f;

        /// <summary>Зазор щупов всхождения от пола и от верха ступени, м.</summary>
        private const float StepProbeClearance = 0.05f;

        /// <summary>
        /// На сколько капсула может не доставать до уступа и всё ещё считаться
        /// упёршейся в него, м. Без этого допуска персонаж всходил на ступень
        /// за десяток сантиметров до неё: щуп видел препятствие вперёд на
        /// радиус плюс вылет, тело подскакивало в воздух перед ступенью и тут
        /// же падало обратно. Это и читалось как «на лестнице колбасит».
        /// </summary>
        private const float StepContactTolerance = 0.06f;

        /// <summary>Сколько секунд после прыжка притяжение к опоре не работает: иначе прыжок гасится в первом же такте.</summary>
        private const float SnapSuppressAfterJump = 0.15f;

        /// <summary>Просадка, ниже которой персонаж считается уже стоящим на опоре и притягивать его некуда, м.</summary>
        private const float GroundSettleTolerance = 0.005f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            worldEffectRelay = GetComponent<IWorldEffectRelay>();
            targetRotation = rb.rotation;
            standingHeight = Mathf.Max(capsule.height, capsule.radius * 2f);
            standingCenter = capsule.center;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Физика идёт с шагом 50 Гц, кадры — чаще, и без интерполяции
            // персонаж дрожит на месте, а на лестнице дрожь складывается с
            // всхождением и притяжением, которые двигают тело напрямую.
            // Ставим кодом, а не в восьми префабах: значение одно на всех,
            // и разъезжаться ему незачем.
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            ApplyBodyConfig();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        /// <summary>Задать камеру-систему отсчёта для ввода (для персонажей, заспавненных в рантайме).</summary>
        public void SetCameraReference(Transform cameraToFollow) => cameraTransform = cameraToFollow;

        /// <summary>
        /// Развернуть тело внешним решением: риг от первого лица, скриптовая сцена.
        /// Поворот применяется в FixedUpdate, а не здесь — Rigidbody нельзя двигать
        /// вне физического тика, иначе появляется дрожание при интерполяции.
        /// </summary>
        public void SetFacing(float yawDegrees)
        {
            targetRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            facingOverridden = true;
        }

        private void ApplyBodyConfig()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: PlayerController без CharacterConfig — персонаж не двигается.", this);
                return;
            }

            rb.mass = config.Mass;
            rb.linearDamping = config.LinearDamping;
            rb.angularDamping = config.AngularDamping;
            WarnIfCrouchBlockedByRadius();
        }

        private void FixedUpdate()
        {
            if (config == null)
            {
                return;
            }

            if (interpolationSuppressed)
            {
                interpolationSuppressed = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            snapSuppressTimer = Mathf.Max(0f, snapSuppressTimer - Time.fixedDeltaTime);

            IsGrounded = CheckGrounded();

            // Притяжение считается до всего остального: сорвавшийся со ступени
            // должен снова стать стоящим ещё до того, как это увидят прыжок,
            // всхождение и учёт падения. Проверка идёт и у стоящего: опора
            // засчитывается в 18 см под ступнями, и без притяжения персонаж
            // так и висел бы над ней, удерживаемый на месте.
            if (TrySnapToGround())
            {
                IsGrounded = true;
            }

            UpdateGroundHold();
            TrackLanding();
            UpdateTimers();
            ApplyExtraGravity();
            UpdateCrouch();
            ApplyFacingOverride();

            if (IsKnockedDown)
            {
                // Вращение гасим каждый тик, а не только на входе в нокдаун.
                // Поворот по Y у тела свободен (заморожены только X и Z), и
                // любой контакт лежащего тела с геометрией раскручивает его:
                // клип падения играет как надо, а модель при этом наматывает
                // круги вокруг себя. Углового трения 0.05 на это не хватает.
                rb.angularVelocity = Vector3.zero;

                // Лежачее демпфирование — только когда персонаж и правда лежит.
                // Оно нужно, чтобы сбитый не скользил по полу, но те же 4 против
                // 0.05 в полёте гасят подброс: гейзер Duck Hunt терял две трети
                // высоты, потому что его импульс сам же и включал нокдаун.
                // Проверять одну землю мало — на старте подброса тело ещё
                // касается пола, и первые тики съедали пятую часть высоты.
                launchGraceTimer = Mathf.Max(0f, launchGraceTimer - Time.fixedDeltaTime);
                bool lyingStill = IsGrounded && rb.linearVelocity.y <= 0.1f && launchGraceTimer <= 0f;
                rb.linearDamping = lyingStill ? config.KnockdownDrag : config.LinearDamping;

                knockdownTimer -= Time.fixedDeltaTime;
                if (knockdownTimer <= 0f)
                {
                    knockdownTimer = 0f;
                    rb.linearDamping = config.LinearDamping;
                    KnockdownEnded?.Invoke();
                }

                NormalizedSpeed = 0f;
                return;
            }

            // Блокировка живёт рядом с нокдауном, а не внутри него: пока идёт
            // нокдаун, он главнее (персонаж и так не управляется), а когда
            // закончится — блокировка продолжит держать тело на месте.
            if (MovementLocked)
            {
                inputReader?.ConsumeJump();
                jumpBufferTimer = 0f;
                StopHorizontally();
                StopSpin();
                NormalizedSpeed = 0f;
                return;
            }

            ReadJumpInput();
            Vector2 moveInput = ReadMoveInput();
            ApplyLocomotion(moveInput);
            TryStepUp(ToCameraRelative(moveInput));
            TryJump();
        }

        /// <summary>
        /// Гасит вращение вокруг вертикали у заблокированного персонажа.
        ///
        /// Поворот по Y физике не заперт — иначе персонаж не смог бы поворачиваться,
        /// — и обычно его каждый такт перезадаёт <c>MoveRotation</c> из локомоции.
        /// Под блокировкой локомоция не выполняется, и остаточная угловая скорость
        /// от толчка или столкновения крутит тело дальше: замороженный вместо позы
        /// статуи вертится волчком на месте. Затухание у капсулы почти нулевое,
        /// само это не пройдёт.
        ///
        /// Нокдаун сюда не попадает — он обрабатывается раньше и возвращается
        /// до этой ветки, так что комичное падение с кувырком остаётся живым.
        /// </summary>
        private void StopSpin()
        {
            if (rb.angularVelocity.sqrMagnitude > 0f)
            {
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Гасит только горизонталь: вертикальная скорость и гравитация остаются,
        /// иначе заблокированный в воздухе завис бы вместо приземления.
        /// </summary>
        private void StopHorizontally()
        {
            Vector3 horizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            if (horizontal.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float rate = config.LockStopTime > 0f
                ? config.MaxSpeed / config.LockStopTime
                : horizontal.magnitude / Time.fixedDeltaTime;
            Vector3 stopped = Vector3.MoveTowards(horizontal, Vector3.zero, rate * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector3(stopped.x, rb.linearVelocity.y, stopped.z);
        }

        private void ReadJumpInput()
        {
            if (inputReader == null || !inputReader.JumpPressed)
            {
                return;
            }

            inputReader.ConsumeJump();
            jumpBufferTimer = config.JumpBufferTime;
        }

        /// <summary>
        /// Внешний разворот тела применяется и в нокдауне не применяется:
        /// упавший персонаж должен свободно кувыркаться.
        /// </summary>
        private void ApplyFacingOverride()
        {
            if (!facingOverridden)
            {
                return;
            }

            facingOverridden = false;

            if (!IsKnockedDown)
            {
                rb.MoveRotation(targetRotation);
            }
        }

        /// <summary>
        /// Запросить приседание. Единая точка входа: сюда же придёт серверное
        /// решение в сетевой фазе, логика ниже от источника не зависит.
        /// </summary>
        public void SetCrouched(bool crouched) => crouchRequested = crouched;

        /// <summary>
        /// Встать в полный рост немедленно, не дожидаясь ввода.
        ///
        /// Нужно тем, кто ставит персонажа в позу извне. Под блокировкой
        /// движения присед застывает как есть (см. <see cref="UpdateCrouch"/>) —
        /// для заморозки это правильно, а для постановки на место нет:
        /// «Экзамен» телепортирует Ведущего за кафедру и запирает его там,
        /// и присевший в этот момент так и вёл бы вопрос сидя.
        /// </summary>
        public void ForceStand()
        {
            crouchRequested = false;
            IsCrouched = false;
            crouchBlend = 0f;
            ApplyCapsuleHeight();
        }

        /// <summary>
        /// Встать на поверхность, меняющую управление (лёд, грязь, масло).
        /// Источник запоминается, чтобы вложенные и перекрывающиеся зоны не
        /// сбрасывали друг друга: вошёл во вторую, не выйдя из первой — правит
        /// вторая, и выход из первой уже ничего не трогает.
        ///
        /// По сети не гоняется: множитель детерминирован и одинаков на всех
        /// машинах, потому что зависит только от того, где стоит персонаж.
        /// </summary>
        public void ApplySurface(Component source, float accelerationMultiplier, float decelerationMultiplier)
        {
            surfaceSource = source;
            AccelerationMultiplier = Mathf.Max(0f, accelerationMultiplier);
            DecelerationMultiplier = Mathf.Max(0f, decelerationMultiplier);
        }

        /// <summary>Сойти с поверхности. Сбрасывает множители, только если их ставил этот же источник.</summary>
        public void ClearSurface(Component source)
        {
            if (surfaceSource != source)
            {
                return;
            }

            surfaceSource = null;
            AccelerationMultiplier = 1f;
            DecelerationMultiplier = 1f;
        }

        /// <summary>
        /// Опустить потолок скорости: роль мини-игры ограничивает бег, не трогая
        /// общий <c>CharacterConfig</c>. Несущий бутыль в «Переноске предмета»
        /// бежит 2.9 м/с вместо 6.5 — иначе он просто убегает от того, что несёт.
        ///
        /// Источник запоминается той же парой, что у поверхностей: две роли,
        /// повесившие потолок одновременно, не сбрасывают друг друга случайно.
        /// Потолок только опускает скорость и никогда не поднимает: значение
        /// выше <c>config.MaxSpeed</c> ничего не меняет.
        ///
        /// Разгон и торможение остаются прежними — меняется только предел.
        /// </summary>
        public void ApplySpeedCap(Component source, float maxSpeed)
        {
            speedCapSource = source;
            speedCap = Mathf.Max(0f, maxSpeed);
        }

        /// <summary>Снять потолок скорости. Срабатывает, только если его ставил этот же источник.</summary>
        public void ClearSpeedCap(Component source)
        {
            if (speedCapSource != source)
            {
                return;
            }

            speedCapSource = null;
            speedCap = 0f;
        }

        private void UpdateCrouch()
        {
            // Ввод читаем только у ридера, который реально управляет этой копией.
            // У выключенного CrouchHeld всегда false, и он затирал бы внешнее
            // решение — в сетевой фазе сервер не смог бы посадить персонажа.
            if (inputReader != null && inputReader.enabled && !MovementLocked)
            {
                SetCrouched(inputReader.CrouchHeld);
            }

            // В нокдауне персонаж и так лежит: приседание игнорируем. Под блокировкой
            // поза застывает как есть — иначе присевший за низким укрытием вставал бы
            // под луч ровно в тот момент, когда его заморозили.
            bool crouched = MovementLocked
                ? IsCrouched
                : crouchRequested && !IsKnockedDown;

            if (!crouched && IsCrouched && IsBlockedAbove())
            {
                crouched = true;
            }

            IsCrouched = crouched;

            float step = config.CrouchTransitionTime > 0f
                ? Time.fixedDeltaTime / config.CrouchTransitionTime
                : 1f;
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouched ? 1f : 0f, step);
            ApplyCapsuleHeight();
        }

        /// <summary>
        /// Капсула сжимается к полу, а не к центру: подошвы обязаны остаться
        /// на месте, иначе персонаж проваливается или подпрыгивает на присед.
        /// </summary>
        /// <summary>
        /// Применить присед, пришедший по сети, к чужой копии персонажа.
        ///
        /// У копий сам мотор выключен (ввод есть только у владельца), поэтому
        /// присед им никто не считает: капсула остаётся в полный рост и на
        /// сервере тоже. Для «Плачущих ангелов» это не косметика — по капсуле
        /// проверяется, видно ли игрока над низким укрытием, и без этого правило
        /// «присел — спрятался» на сервере просто не работает.
        ///
        /// Плавность здесь не нужна: у владельца присед уже сгладился, а к нам
        /// приезжает готовое состояние. Тянуть его ещё раз — значит отставать.
        /// </summary>
        public void ApplyReplicatedCrouch(bool crouched)
        {
            IsCrouched = crouched;
            crouchBlend = crouched ? 1f : 0f;
            ApplyCapsuleHeight();
        }

        private void ApplyCapsuleHeight()
        {
            float crouchedHeight = Mathf.Max(ResolveCrouchedHeight(), capsule.radius * 2f);
            float height = Mathf.Lerp(standingHeight, crouchedHeight, crouchBlend);
            if (Mathf.Approximately(capsule.height, height))
            {
                return;
            }

            capsule.height = height;
            capsule.center = new Vector3(
                standingCenter.x,
                standingCenter.y - (standingHeight - height) * 0.5f,
                standingCenter.z);
        }

        /// <summary>
        /// Высота капсулы в приседе. Абсолютная величина из конфига важнее
        /// множителя: там, где присед обязан прятать за укрытием известной
        /// высоты, доля от роста даёт разным персонажам разную макушку.
        /// </summary>
        private float ResolveCrouchedHeight() =>
            config.CrouchTargetHeight > 0f
                ? config.CrouchTargetHeight
                : standingHeight * config.CrouchHeightMultiplier;

        /// <summary>
        /// Радиус капсулы — жёсткий пол для приседа: ниже собственной толщины
        /// капсула не сжимается. Широкий персонаж молча не дотягивает до
        /// заданной высоты и торчит из-за укрытия — предупреждаем один раз
        /// на старте, а не ищем это потом на плейтесте.
        /// </summary>
        private void WarnIfCrouchBlockedByRadius()
        {
            if (config == null || capsule == null || config.CrouchTargetHeight <= 0f)
            {
                return;
            }

            float floor = capsule.radius * 2f;
            if (floor > config.CrouchTargetHeight)
            {
                Debug.LogWarning(
                    $"{name}: присед не дотягивает до {config.CrouchTargetHeight:F2} м — радиус капсулы {capsule.radius:F2} держит минимум {floor:F2} м. " +
                    "Персонаж будет торчать из-за низких укрытий. Уменьшить радиус капсулы или поднять укрытия.", this);
            }
        }

        private bool IsBlockedAbove()
        {
            float radius = capsule.radius;
            float currentTop = capsule.center.y + capsule.height * 0.5f;
            float standingTop = standingCenter.y + standingHeight * 0.5f;
            float distance = standingTop - currentTop;
            if (distance <= 0.001f)
            {
                return false;
            }

            Vector3 topSphere = transform.TransformPoint(new Vector3(capsule.center.x, currentTop - radius, capsule.center.z));
            return Physics.SphereCast(topSphere, radius * 0.95f, Vector3.up, out _, distance,
                groundLayer | crouchCeilingLayers, QueryTriggerInteraction.Ignore);
        }

        private Vector2 ReadMoveInput()
        {
            if (inputReader == null)
            {
                return Vector2.zero;
            }

            Vector2 raw = inputReader.MoveInput;
            return raw.sqrMagnitude < config.InputDeadzone * config.InputDeadzone ? Vector2.zero : raw;
        }

        private void ApplyLocomotion(Vector2 moveInput)
        {
            Vector3 desiredDirection = ToCameraRelative(moveInput);
            float maxSpeed = config.MaxSpeed * Mathf.Lerp(1f, config.CrouchSpeedMultiplier, crouchBlend);

            // Потолок роли только опускает предел и никогда не поднимает: присед
            // под ним остаётся приседом, а не становится бегом.
            if (speedCap > 0f)
            {
                maxSpeed = Mathf.Min(maxSpeed, speedCap);
            }

            Vector3 desiredVelocity = Vector3.ClampMagnitude(desiredDirection, 1f) * maxSpeed;
            Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            bool accelerating = desiredVelocity.sqrMagnitude > 0.01f;
            float rate = accelerating
                ? config.Acceleration * AccelerationMultiplier
                : config.Deceleration * DecelerationMultiplier;
            if (!IsGrounded)
            {
                rate *= config.AirControl;
            }

            Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredVelocity, rate * Time.fixedDeltaTime);

            // На опоре скорость задаётся целиком, вместе с вертикалью, и вдоль
            // самой опоры.
            //
            // Иначе гравитация каждый такт добавляет вниз, контакт превращает
            // это в движение вбок, и персонаж съезжает по ровному с виду полу:
            // трения у капсулы нет намеренно — скорость задаёт контроллер, и
            // трение мешало бы ему. Особенно заметно на лестнице, где капсула
            // шире проступи и стоит на носах ступеней: там сползание было
            // постоянным.
            //
            // Вверх летящего это не трогает: у прыгнувшего и подброшенного
            // гейзером скорость по Y положительная, и ветка не его.
            rb.linearVelocity = standingOnGround
                ? Vector3.ProjectOnPlane(newHorizontal, groundNormal)
                : new Vector3(newHorizontal.x, rb.linearVelocity.y, newHorizontal.z);

            NormalizedSpeed = config.MaxSpeed > 0f ? newHorizontal.magnitude / config.MaxSpeed : 0f;

            if (accelerating)
            {
                targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
            }

            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, config.RotationSpeed * Time.fixedDeltaTime));
        }

        /// <summary>
        /// Ввод в системе отсчёта камеры: W — от камеры, S — на камеру, A/D — вбок по экрану.
        /// Без этого экранное направление клавиш «плавает» вслед за разворотом камеры.
        /// </summary>
        private Vector3 ToCameraRelative(Vector2 moveInput)
        {
            if (!TryGetMoveBasis(out Vector3 forward, out Vector3 right))
            {
                return new Vector3(moveInput.x, 0f, moveInput.y);
            }

            return right * moveInput.x + forward * moveInput.y;
        }

        /// <summary>
        /// Перевести мировое направление во ввод этого персонажа — обратная
        /// сторона <see cref="ToCameraRelative"/>. Нужна всем, кто задаёт
        /// движение точкой в мире, а не нажатиями: болванке соло-теста,
        /// автопилоту, скриптовой сцене. Без пересчёта такой источник ведёт
        /// персонажа боком, как только у того появляется своя камера.
        /// </summary>
        public Vector2 WorldToMoveInput(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return Vector2.zero;
            }

            worldDirection.Normalize();

            if (!TryGetMoveBasis(out Vector3 forward, out Vector3 right))
            {
                return new Vector2(worldDirection.x, worldDirection.z);
            }

            return new Vector2(Vector3.Dot(worldDirection, right), Vector3.Dot(worldDirection, forward));
        }

        /// <summary>Базис ввода: куда для этого персонажа «вперёд» и «вправо». Ложь — камеры нет, ввод мировой.</summary>
        private bool TryGetMoveBasis(out Vector3 forward, out Vector3 right)
        {
            if (cameraTransform == null)
            {
                forward = Vector3.forward;
                right = Vector3.right;
                return false;
            }

            forward = cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                // Камера смотрит строго вниз — берём её «верх» как направление вперёд.
                forward = cameraTransform.up;
                forward.y = 0f;
            }

            forward.Normalize();
            right = new Vector3(forward.z, 0f, -forward.x);
            return true;
        }

        private void UpdateTimers()
        {
            coyoteTimer = IsGrounded ? config.CoyoteTime : Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.fixedDeltaTime);
        }

        private void TryJump()
        {
            if (jumpBufferTimer <= 0f || coyoteTimer <= 0f)
            {
                return;
            }

            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            snapSuppressTimer = SnapSuppressAfterJump;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, config.JumpSpeed, rb.linearVelocity.z);
            Jumped?.Invoke();
        }

        /// <summary>
        /// Утяжелённая гравитация — только в полёте.
        ///
        /// На земле она чистый вред: множитель падения втрое (29.4 вместо 9.81)
        /// на любом косом контакте — кромке ступени, скосе укрытия — становится
        /// боковым ускорением до 20 м/с², а торможение персонажа те же 20. Трения
        /// у капсулы нет намеренно (скорость задаёт контроллер), и удержать
        /// стоящего оказывалось нечем: он сползал по ровному с виду полу.
        /// </summary>
        private void ApplyExtraGravity()
        {
            if (IsGrounded && rb.linearVelocity.y <= 0f)
            {
                return;
            }

            float multiplier = rb.linearVelocity.y > 0f ? config.RiseGravityMultiplier : config.FallGravityMultiplier;
            if (multiplier > 1f)
            {
                rb.AddForce(Physics.gravity * (multiplier - 1f), ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Стоит ли персонаж на опоре — и на какой именно.
        ///
        /// Нормаль нужна не для красоты: по ней отличается пол от склона, а
        /// без этого различия притяжение к опоре приклеивало бы персонажа к
        /// отвесной стене. Триггеры отсекаются явно: <c>queriesHitTriggers</c>
        /// в проекте включён, и зона чекпоинта под ногами читалась бы как пол.
        /// </summary>
        private bool CheckGrounded()
        {
            Vector3 origin = capsule.bounds.center;
            float castDistance = capsule.bounds.extents.y - capsule.radius + config.GroundCheckDistance;

            if (!Physics.SphereCast(origin, capsule.radius * 0.95f, Vector3.down, out RaycastHit hit,
                    castDistance, groundLayer, QueryTriggerInteraction.Ignore))
            {
                groundNormal = Vector3.up;
                return false;
            }

            groundNormal = ResolveSurfaceNormal(hit.normal, castDistance);
            return true;
        }

        /// <summary>
        /// На чём персонаж стоит на самом деле.
        ///
        /// Сфера проверки цепляется за кромки: на лестнице она садится на нос
        /// ступени и отдаёт нормаль под 56°, хотя проступь под ногами ровная —
        /// и удержание на опоре, глядя на такую нормаль, отказывается работать
        /// ровно там, где нужнее всего. Луч по оси капсулы отвечает на вопрос
        /// честно. Промахнулся — персонаж и правда висит на самом краю, тогда
        /// остаётся нормаль сферы.
        /// </summary>
        private Vector3 ResolveSurfaceNormal(Vector3 sphereNormal, float castDistance)
        {
            return Physics.Raycast(capsule.bounds.center, Vector3.down, out RaycastHit straight,
                castDistance + capsule.radius, groundLayer, QueryTriggerInteraction.Ignore)
                ? straight.normal
                : sphereNormal;
        }

        /// <summary>Угол опоры под ногами, °. Без опоры — ноль.</summary>
        public float GroundAngle => Vector3.Angle(groundNormal, Vector3.up);

        /// <summary>Персонаж стоит на пригодной опоре и не летит вверх.</summary>
        public bool IsStandingOnGround => standingOnGround;

        /// <summary>
        /// Держать ли персонажа на опоре — и, если да, снять с него гравитацию.
        ///
        /// Обнулять скорость каждый такт мало: гравитация успевает разогнать
        /// тело внутри самого такта, солвер выталкивает капсулу из кромки вбок,
        /// и персонаж уезжает по семь миллиметров за шаг — метр за три секунды.
        /// Замерено на лестнице Duck Hunt, где капсула шире проступи и стоит на
        /// носах ступеней. Убирать нечего, кроме самой причины: у стоящего
        /// гравитации нет вовсе, и сползать ему не от чего. Трением это не
        /// лечится — капсула фрикционлесс намеренно, скорость задаёт контроллер.
        ///
        /// Как только опора пропала, стала слишком крутой или персонажа понесло
        /// вверх, гравитация возвращается тем же тактом.
        /// </summary>
        private void UpdateGroundHold()
        {
            standingOnGround = IsGrounded
                               && !IsKnockedDown
                               && rb.linearVelocity.y <= 0.01f
                               && GroundAngle <= config.MaxSlopeAngle;

            rb.useGravity = !standingOnGround;
        }

        /// <summary>Выключенному контроллеру гравитацию возвращаем: иначе тело зависнет в воздухе навсегда.</summary>
        private void OnDisable()
        {
            standingOnGround = false;
            if (rb != null)
            {
                rb.useGravity = true;
            }
        }

        /// <summary>
        /// Притянуть к опоре при спуске.
        ///
        /// Без этого лестница ломается дважды. Сходя со ступени, капсула
        /// уходит в свободное падение: земля под ней опускается разом на всю
        /// высоту ступени, <c>IsGrounded</c> мигает, и спуск превращается в
        /// череду мелких падений с потерей управления в каждом. А поднимаясь,
        /// капсула садится скруглённым низом на кромку ступени — нормаль там
        /// косая, трения у персонажа нет намеренно, и тело съезжает вниз. И то
        /// и другое лечится одним: не давать оторваться от опоры, если она
        /// рядом и по ней можно ходить.
        ///
        /// Прыжок и подброс не трогаются: вверх летящего не притягивает
        /// проверка скорости, а сразу после прыжка — ещё и таймер.
        /// </summary>
        private bool TrySnapToGround()
        {
            if (!wasGrounded || IsKnockedDown || snapSuppressTimer > 0f || rb.linearVelocity.y > 0.01f)
            {
                return false;
            }

            float snap = config.GroundSnapDistance;
            if (snap <= 0f)
            {
                return false;
            }

            Vector3 origin = capsule.bounds.center;
            float castDistance = capsule.bounds.extents.y - capsule.radius + snap;

            if (!Physics.SphereCast(origin, capsule.radius * 0.95f, Vector3.down, out RaycastHit hit,
                    castDistance, groundLayer, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Vector3.Angle(ResolveSurfaceNormal(hit.normal, castDistance), Vector3.up) > config.MaxSlopeAngle)
            {
                return false;
            }

            float feetY = rb.position.y + capsule.center.y - capsule.height * 0.5f;
            float drop = feetY - hit.point.y;
            if (drop <= GroundSettleTolerance)
            {
                return false;
            }

            rb.position += Vector3.down * drop;
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            groundNormal = hit.normal;
            return true;
        }

        /// <summary>
        /// Импульс-толчок персонажу. Единая точка входа — позже станет
        /// server-authoritative (сервер валидирует и применяет).
        /// </summary>
        public void ApplyPush(Vector3 direction, float force)
        {
            if (config == null || ImpulseImmune)
            {
                return;
            }

            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;

            // Куда летит персонаж относительно СВОЕГО взгляда: толчок против его
            // взгляда — значит прилетело в лицо; по взгляду — значит со спины.
            KnockdownType type = Vector3.Dot(flat, Facing) < 0f
                ? KnockdownType.FlyBack
                : KnockdownType.FallForward;

            // Удар в лицо отбрасывает заметно сильнее и выше, чем подсечка сзади.
            float appliedForce = type == KnockdownType.FlyBack ? force * config.FaceHitForceMultiplier : force;
            float lift = type == KnockdownType.FlyBack ? config.PushUpward : config.PushUpward * 0.5f;

            ApplyImpulse((flat + Vector3.up * lift).normalized * appliedForce, type);
        }

        /// <summary>
        /// Импульс от мира: пружина, взрыв, попадание снаряда. Это точка входа
        /// для всего, что находится вне персонажа — в сетевой игре решение
        /// принимает сервер, а применяет владелец.
        ///
        /// Мировым источникам следует вызывать этот метод, а не ApplyImpulse:
        /// прямой вызов на чужой копии персонажа будет перетёрт сетевым состоянием.
        /// </summary>
        public void ApplyWorldImpulse(Vector3 impulse)
        {
            if (worldEffectRelay != null && worldEffectRelay.TryRelayImpulse(impulse))
            {
                return;
            }

            ApplyImpulse(impulse);
        }

        /// <summary>
        /// Импульс произвольного направления (пружины, взрывы, попадания сверху).
        /// Применяется здесь и сейчас — сетевую маршрутизацию делает ApplyWorldImpulse.
        /// </summary>
        public void ApplyImpulse(Vector3 impulse) => ApplyImpulse(impulse, KnockdownType.FallForward);

        public void ApplyImpulse(Vector3 impulse, KnockdownType knockdownType)
        {
            if (config == null || ImpulseImmune)
            {
                return;
            }

            rb.AddForce(impulse, ForceMode.Impulse);

            // Импульс вверх ещё не превратился в скорость: сила ждёт ближайшего
            // шага физики. Без этой отметки нокдаун успевает выставить лежачее
            // демпфирование ровно на тот шаг, где импульс интегрируется, и
            // подброс теряет пятую часть высоты.
            if (impulse.y > 0f)
            {
                launchGraceTimer = LaunchGraceTime;
            }

            if (impulse.magnitude / rb.mass >= config.KnockdownVelocityThreshold)
            {
                Knockdown(knockdownType);
            }
        }

        /// <summary>
        /// Падение с потерей управления: персонаж отыгрывает клип падения,
        /// затем клип подъёма и возвращает контроль.
        /// </summary>
        public void Knockdown(KnockdownType type)
        {
            if (config == null || IsKnockedDown)
            {
                return;
            }

            knockdownTimer = type == KnockdownType.FlyBack
                ? knockdownFrontDuration
                : knockdownBackDuration;

            // Пока лежит — гасим скольжение, иначе подъём проигрывается «на ходу».
            rb.linearDamping = config.KnockdownDrag;

            // И вращение: тело валится от импульса, а закрутить его может любой
            // косой контакт по дороге.
            rb.angularVelocity = Vector3.zero;

            KnockdownStarted?.Invoke(type);
        }

        /// <summary>
        /// Перенос как решение мира: респаун, смена арены, старт мини-игры.
        /// В сетевой игре сервер поручает перенос владельцу, в одиночной
        /// применяется сразу. Мировому коду нужен этот метод, а не TeleportTo.
        /// </summary>
        public void RequestTeleport(Vector3 position, Quaternion rotation)
        {
            if (worldEffectRelay != null && worldEffectRelay.TryRelayTeleport(position, rotation))
            {
                return;
            }

            TeleportTo(position, rotation);
        }

        /// <summary>Мгновенный перенос (респаун). Сбрасывает скорость и нокдаун.</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            bool wasKnockedDown = IsKnockedDown;
            knockdownTimer = 0f;
            if (config != null)
            {
                rb.linearDamping = config.LinearDamping;
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Интерполяция сглаживает движение между тактами физики, и перенос
            // она сгладила бы тоже: респавн выглядел бы полётом через всю арену.
            // Гасим на один такт, обратно включает FixedUpdate.
            rb.interpolation = RigidbodyInterpolation.None;
            interpolationSuppressed = true;

            rb.position = position;
            rb.rotation = rotation;
            targetRotation = rotation;

            // Накопленную скорость падения сбрасываем вместе с телом: иначе
            // респавн посреди падения роняет персонажа сразу после переноса.
            fallSpeed = 0f;
            wasGrounded = true;

            if (wasKnockedDown)
            {
                KnockdownEnded?.Invoke();
            }

            Teleported?.Invoke();
        }

        /// <summary>
        /// Падение от приземления — только по скорости падения, и только по ней.
        ///
        /// Раньше это решалось в OnCollisionEnter по импульсу столкновения, и
        /// оттуда росли сразу четыре беды: персонаж падал, приземлившись после
        /// обычного прыжка; падал, задев другого игрока; падал, подойдя вплотную
        /// к стене; и падал на каждой ступеньке лестницы, отчего подняться по
        /// ней было нельзя вовсе. Импульс столкновения не различает удар,
        /// приземление и касание стены — все три для физики одно и то же.
        ///
        /// Теперь роняет только настоящий удар (через ApplyPush/ApplyImpulse) и
        /// падение с высоты. Порог берётся с запасом над обычным прыжком.
        /// </summary>
        private void TrackLanding()
        {
            if (!IsGrounded)
            {
                fallSpeed = Mathf.Max(fallSpeed, -rb.linearVelocity.y);
                wasGrounded = false;
                return;
            }

            if (!wasGrounded)
            {
                wasGrounded = true;

                if (config.HardLandingSpeed > 0f && fallSpeed >= config.HardLandingSpeed)
                {
                    Knockdown(KnockdownType.FallForward);
                }
            }

            fallSpeed = 0f;
        }

        /// <summary>
        /// Всхождение на низкую ступень. Без него капсула упирается в любой
        /// уступ и лестница проходится только прыжками — а прыжок на ступеньку
        /// это ещё и приземление, то есть риск упасть на каждой ступени.
        ///
        /// Два щупа: низкий ловит препятствие, верхний проверяет, что над ним
        /// свободно. Искать верх уступа лучом сверху вниз из точки ниже его
        /// верхушки нельзя — луч стартует внутри коллайдера, тот его не ловит,
        /// и стена читается как ровный пол.
        /// </summary>
        private void TryStepUp(Vector3 desiredDirection)
        {
            if (config.StepHeight <= 0f || !IsGrounded || IsKnockedDown)
            {
                return;
            }

            Vector3 direction = new Vector3(desiredDirection.x, 0f, desiredDirection.z);
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();

            float feetY = rb.position.y + capsule.center.y - capsule.height * 0.5f;
            float reach = capsule.radius + StepProbeReach;

            Vector3 low = new Vector3(rb.position.x, feetY + StepProbeClearance, rb.position.z);
            if (!Physics.Raycast(low, direction, out RaycastHit obstacle, reach, groundLayer, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            // В уступ надо упереться, а не увидеть его издали. Щуп смотрит
            // вперёд дальше края капсулы, и без этой проверки персонаж
            // взлетал на высоту ступени, ещё не дойдя до неё.
            if (obstacle.distance > capsule.radius + StepContactTolerance)
            {
                return;
            }

            Vector3 high = new Vector3(rb.position.x, feetY + config.StepHeight + StepProbeClearance, rb.position.z);
            if (Physics.Raycast(high, direction, reach, groundLayer, QueryTriggerInteraction.Ignore))
            {
                // На высоте ступени тоже занято — это стена, а не уступ.
                return;
            }

            Vector3 above = high + direction * reach;
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit surface,
                    config.StepHeight + StepProbeClearance, groundLayer, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            float rise = surface.point.y - feetY;
            if (rise <= StepProbeClearance || rise > config.StepHeight)
            {
                return;
            }

            rb.position += Vector3.up * (rise + StepProbeClearance);
        }
    }
}
