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
        [Tooltip("Камера, относительно которой считается направление ввода. Пусто — берётся Camera.main при старте")]
        [SerializeField] private Transform cameraTransform;

        public event Action Jumped;
        public event Action<KnockdownType> KnockdownStarted;
        public event Action KnockdownEnded;

        public CharacterConfig Config => config;
        public bool IsGrounded { get; private set; }
        public bool IsKnockedDown => knockdownTimer > 0f;
        /// <summary>0..1 — доля от максимальной скорости, для анимаций.</summary>
        public float NormalizedSpeed { get; private set; }

        /// <summary>
        /// Направление взгляда по авторитетному повороту Rigidbody.
        /// Transform отстаёт от физики на кадр после MoveRotation/TeleportTo,
        /// а по этому направлению решается, в лицо прилетело или в спину.
        /// </summary>
        public Vector3 Facing => rb != null ? rb.rotation * Vector3.forward : transform.forward;

        private Rigidbody rb;
        private CapsuleCollider capsule;
        private Quaternion targetRotation;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float knockdownTimer;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            targetRotation = rb.rotation;
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

            ReadJumpInput();
            ApplyLocomotion(ReadMoveInput());
            TryJump();
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
            Vector3 desiredVelocity = Vector3.ClampMagnitude(desiredDirection, 1f) * config.MaxSpeed;
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
        /// Импульс произвольного направления (пружины, взрывы, попадания сверху).
        /// Тоже единая точка входа для будущего server-authority.
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
