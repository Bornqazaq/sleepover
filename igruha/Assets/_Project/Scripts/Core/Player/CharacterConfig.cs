using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Единый геймплейный конфиг персонажа. Общий для ВСЕХ персонажей —
    /// балансовых отличий между ними нет. Индивидуальны только размеры
    /// капсулы и Animator Controller (задаются на prefab-варианте).
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "Igruha/Character Config")]
    public sealed class CharacterConfig : ScriptableObject
    {
        [Header("Движение")]
        [Tooltip("Максимальная горизонтальная скорость, м/с")]
        [SerializeField] private float maxSpeed = 6.5f;
        [Tooltip("Ускорение разгона, м/с². Ниже — тяжелее ощущается разгон")]
        [SerializeField] private float acceleration = 28f;
        [Tooltip("Торможение при отпущенном вводе, м/с². Ниже — заметнее проскальзывание")]
        [SerializeField] private float deceleration = 20f;
        [Tooltip("Множитель управления в воздухе (0..1)")]
        [Range(0f, 1f)]
        [SerializeField] private float airControl = 0.55f;
        [Tooltip("Скорость поворота к направлению движения, °/с. Ниже — комичнее инерция разворота")]
        [SerializeField] private float rotationSpeed = 520f;
        [Tooltip("Мёртвая зона стика")]
        [SerializeField] private float inputDeadzone = 0.15f;

        [Header("Приседание")]
        [Tooltip("Множитель максимальной скорости в приседе")]
        [Range(0.1f, 1f)]
        [SerializeField] private float crouchSpeedMultiplier = 0.45f;
        [Tooltip("Множитель высоты капсулы в приседе. Ниже 2×радиуса капсула не сжимается — это предел Unity")]
        [Range(0.2f, 1f)]
        [SerializeField] private float crouchHeightMultiplier = 0.5f;
        [Tooltip("За сколько секунд капсула переходит между стойкой и приседом")]
        [SerializeField] private float crouchTransitionTime = 0.12f;

        [Header("Прыжок и гравитация")]
        [Tooltip("Вертикальная скорость прыжка, м/с")]
        [SerializeField] private float jumpSpeed = 8.4f;
        [Tooltip("Множитель гравитации на взлёте (1 = стандартные -9.81)")]
        [SerializeField] private float riseGravityMultiplier = 2.2f;
        [Tooltip("Множитель гравитации на падении — резче, чем взлёт, для читаемой дуги")]
        [SerializeField] private float fallGravityMultiplier = 3f;
        [Tooltip("Coyote time: сколько секунд после схода с края ещё можно прыгнуть")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("Буфер прыжка: за сколько секунд до приземления нажатие ещё засчитается")]
        [SerializeField] private float jumpBufferTime = 0.1f;
        [Tooltip("Длина спер-каста проверки земли ниже капсулы, м")]
        [SerializeField] private float groundCheckDistance = 0.2f;

        [Header("Тело (Rigidbody)")]
        [Tooltip("Масса. Влияет на обмен импульсами при толчках")]
        [SerializeField] private float mass = 2f;
        [Tooltip("Ненулевое linear damping гасит накопление микроимпульсов от толчков")]
        [SerializeField] private float linearDamping = 0.05f;
        [SerializeField] private float angularDamping = 0.05f;

        [Header("Удар (Cross Punch) и импульс-толчок")]
        [Tooltip("Сила удара, импульс (кг·м/с)")]
        [SerializeField] private float pushForce = 14f;
        [Tooltip("Радиус поражения удара, м")]
        [SerializeField] private float pushRadius = 1.6f;
        [Tooltip("Доля силы, уходящая вверх (подбрасывание)")]
        [Range(0f, 1f)]
        [SerializeField] private float pushUpward = 0.35f;
        [Tooltip("Кулдаун удара, с")]
        [SerializeField] private float pushCooldown = 0.9f;
        [Tooltip("Угол фронтального сектора удара, °")]
        [Range(30f, 360f)]
        [SerializeField] private float pushArcAngle = 140f;
        [Tooltip("Задержка от начала анимации удара до момента контакта, с — под замах Cross Punch")]
        [SerializeField] private float punchImpactDelay = 0.25f;
        [Tooltip("Множитель силы при ударе в лицо: отлёт назад должен быть заметно мощнее подсечки со спины")]
        [SerializeField] private float faceHitForceMultiplier = 1.35f;

        [Header("Падение от удара (knockdown)")]
        [Tooltip("Порог: изменение скорости (м/с) от импульса/удара, после которого персонаж падает")]
        [SerializeField] private float knockdownVelocityThreshold = 5f;
        [Tooltip("Удар в лицо: полёт назад + подъём со спины. Значение выставляется билдером аниматора по длине клипов")]
        [SerializeField] private float knockdownFrontDuration = 2.8f;
        [Tooltip("Удар со спины: падение вперёд + подъём с живота. Значение выставляется билдером аниматора")]
        [SerializeField] private float knockdownBackDuration = 2.2f;
        [Tooltip("Дополнительное торможение, пока персонаж лежит — чтобы не уезжал по полу во время подъёма")]
        [SerializeField] private float knockdownDrag = 4f;

        [Header("Блокировка движения (замри)")]
        [Tooltip("За сколько секунд гасится бег при включении блокировки. Ноль — мгновенно, и персонаж 'клинит' на ходу")]
        [SerializeField] private float lockStopTime = 0.08f;

        public float MaxSpeed => maxSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float AirControl => airControl;
        public float RotationSpeed => rotationSpeed;
        public float InputDeadzone => inputDeadzone;

        public float CrouchSpeedMultiplier => crouchSpeedMultiplier;
        public float CrouchHeightMultiplier => crouchHeightMultiplier;
        public float CrouchTransitionTime => crouchTransitionTime;

        public float JumpSpeed => jumpSpeed;
        public float RiseGravityMultiplier => riseGravityMultiplier;
        public float FallGravityMultiplier => fallGravityMultiplier;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;
        public float GroundCheckDistance => groundCheckDistance;

        public float Mass => mass;
        public float LinearDamping => linearDamping;
        public float AngularDamping => angularDamping;

        public float PushForce => pushForce;
        public float PushRadius => pushRadius;
        public float PushUpward => pushUpward;
        public float PushCooldown => pushCooldown;
        public float PushArcAngle => pushArcAngle;
        public float PunchImpactDelay => punchImpactDelay;
        public float FaceHitForceMultiplier => faceHitForceMultiplier;

        public float KnockdownVelocityThreshold => knockdownVelocityThreshold;
        public float KnockdownFrontDuration => knockdownFrontDuration;
        public float KnockdownBackDuration => knockdownBackDuration;
        public float KnockdownDrag => knockdownDrag;

        public float LockStopTime => lockStopTime;
    }
}
