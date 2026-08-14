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

        public event Action Jumped;
        public event Action<KnockdownType> KnockdownStarted;
        public event Action KnockdownEnded;
        public event Action<bool> MovementLockChanged;

        /// <summary>Персонажа перенесли: респаун, старт мини-игры, смена арены.</summary>
        public event Action Teleported;

        public CharacterConfig Config => config;
        public bool IsGrounded { get; private set; }
        public bool IsKnockedDown => knockdownTimer > 0f;

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

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            worldEffectRelay = GetComponent<IWorldEffectRelay>();
            targetRotation = rb.rotation;
            standingHeight = Mathf.Max(capsule.height, capsule.radius * 2f);
            standingCenter = capsule.center;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            ApplyBodyConfig();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        /// <summary>Задать камеру-систему отсчёта для ввода (для персонажей, заспавненных в рантайме).</summary>
        public void SetCameraReference(Transform cameraToFollow) => cameraTransform = cameraToFollow;

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
        }

        private void FixedUpdate()
        {
            if (config == null)
            {
                return;
            }

            IsGrounded = CheckGrounded();
            UpdateTimers();
            ApplyExtraGravity();
            UpdateCrouch();

            if (IsKnockedDown)
            {
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
                NormalizedSpeed = 0f;
                return;
            }

            ReadJumpInput();
            ApplyLocomotion(ReadMoveInput());
            TryJump();
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
        /// Запросить приседание. Единая точка входа: сюда же придёт серверное
        /// решение в сетевой фазе, логика ниже от источника не зависит.
        /// </summary>
        public void SetCrouched(bool crouched) => crouchRequested = crouched;

        private void UpdateCrouch()
        {
            if (inputReader != null && !MovementLocked)
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
        private void ApplyCapsuleHeight()
        {
            float crouchedHeight = Mathf.Max(standingHeight * config.CrouchHeightMultiplier, capsule.radius * 2f);
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
            Vector3 desiredVelocity = Vector3.ClampMagnitude(desiredDirection, 1f) * maxSpeed;
            Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            bool accelerating = desiredVelocity.sqrMagnitude > 0.01f;
            float rate = accelerating ? config.Acceleration : config.Deceleration;
            if (!IsGrounded)
            {
                rate *= config.AirControl;
            }

            Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredVelocity, rate * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector3(newHorizontal.x, rb.linearVelocity.y, newHorizontal.z);
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
            if (cameraTransform == null)
            {
                return new Vector3(moveInput.x, 0f, moveInput.y);
            }

            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                // Камера смотрит строго вниз — берём её «верх» как направление вперёд.
                forward = cameraTransform.up;
                forward.y = 0f;
            }

            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            return right * moveInput.x + forward * moveInput.y;
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
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, config.JumpSpeed, rb.linearVelocity.z);
            Jumped?.Invoke();
        }

        private void ApplyExtraGravity()
        {
            float multiplier = rb.linearVelocity.y > 0f ? config.RiseGravityMultiplier : config.FallGravityMultiplier;
            if (multiplier > 1f)
            {
                rb.AddForce(Physics.gravity * (multiplier - 1f), ForceMode.Acceleration);
            }
        }

        private bool CheckGrounded()
        {
            Vector3 origin = capsule.bounds.center;
            float castDistance = capsule.bounds.extents.y - capsule.radius + config.GroundCheckDistance;
            return Physics.SphereCast(origin, capsule.radius * 0.95f, Vector3.down, out _, castDistance, groundLayer);
        }

        /// <summary>
        /// Импульс-толчок персонажу. Единая точка входа — позже станет
        /// server-authoritative (сервер валидирует и применяет).
        /// </summary>
        public void ApplyPush(Vector3 direction, float force)
        {
            if (config == null)
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
            if (config == null)
            {
                return;
            }

            rb.AddForce(impulse, ForceMode.Impulse);

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
                ? config.KnockdownFrontDuration
                : config.KnockdownBackDuration;

            // Пока лежит — гасим скольжение, иначе подъём проигрывается «на ходу».
            rb.linearDamping = config.KnockdownDrag;
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
            rb.position = position;
            rb.rotation = rotation;
            targetRotation = rotation;

            if (wasKnockedDown)
            {
                KnockdownEnded?.Invoke();
            }

            Teleported?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (config == null || IsKnockedDown)
            {
                return;
            }

            float deltaV = collision.impulse.magnitude / rb.mass;
            Vector3 impulseDirection = collision.impulse / Mathf.Max(collision.impulse.magnitude, 0.0001f);
            Vector3 horizontal = new Vector3(impulseDirection.x, 0f, impulseDirection.z);
            float horizontalDeltaV = deltaV * horizontal.magnitude;

            // Боковой удар валит с порога; вертикальный (жёсткое приземление) — с двойного,
            // чтобы обычные прыжки не роняли персонажа.
            if (horizontalDeltaV >= config.KnockdownVelocityThreshold ||
                deltaV >= config.KnockdownVelocityThreshold * 2f)
            {
                KnockdownType type = Vector3.Dot(horizontal.normalized, Facing) < 0f
                    ? KnockdownType.FlyBack
                    : KnockdownType.FallForward;
                Knockdown(type);
            }
        }
    }
}
