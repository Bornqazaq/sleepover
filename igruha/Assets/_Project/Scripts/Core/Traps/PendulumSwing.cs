using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Маятник в вертикальной плоскости: качели, ядро на тросе, било.
    /// Ходит от края к краю с постоянным периодом и сшибает всех, кого задел.
    ///
    /// Отличается от <see cref="SwingingBeamTrap"/> плоскостью хода: та балка
    /// описывает круг по горизонтали вокруг крана, а этот маятник качается
    /// вверх-вниз вокруг перекладины. Качели, собранные из горизонтальной
    /// балки, читались бы как вертушка.
    ///
    /// <b>Ход считается от нуля общих часов</b>, а не от момента включения:
    /// сцена грузится у каждого своё время, и маятник, отсчитывающий фазу от
    /// собственного старта, стоял бы у всех в разных местах. От нуля часов
    /// формула сходится у всех и у любого опоздавшего — синхронизировать
    /// нечего, трафика нет.
    ///
    /// Сбивание — исход, поэтому его считает авторитет: иначе удар засчитается
    /// столько раз, сколько в матче машин.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PendulumSwing : MonoBehaviour
    {
        [Tooltip("Перекладина, вокруг которой качается. Пусто — точка над объектом на длину подвеса")]
        [SerializeField] private Transform pivot;

        [Tooltip("Длина подвеса, м")]
        [SerializeField] private float length = 2.6f;

        [Tooltip("Размах в каждую сторону, градусов")]
        [SerializeField] private float amplitudeDegrees = 55f;

        [Tooltip("Период полного колебания (туда и обратно), с")]
        [SerializeField] private float period = 3.2f;

        [Tooltip("Сдвиг фазы, с. Двум сиденьям на одной раме нужен разный, иначе они ходят как одно")]
        [SerializeField] private float phaseOffset;

        [Tooltip("Импульс, которым сшибает игрока. Должен превышать порог нокдауна, иначе только толкнёт")]
        [SerializeField] private float impactForce = 16f;

        [Tooltip("Доля импульса вверх — чтобы сбитый улетал, а не проезжал по полу")]
        [Range(0f, 1f)]
        [SerializeField] private float impactUpward = 0.3f;

        private Vector3 center;
        private Vector3 swingAxis = Vector3.right;
        private Quaternion baseRotation;
        private Vector3 previousPosition;

        /// <summary>Скорость сиденья в мире, м/с. По ней считается направление удара.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// Кого-то снесло. Событие для игры: реплика диктора, звук, счётчик.
        /// Приходит только у авторитета — как и сам удар.
        /// </summary>
        public event System.Action<PlayerController> Hit;

        public float Period
        {
            get => period;
            set => period = Mathf.Max(0.01f, value);
        }

        public float PhaseOffset
        {
            get => phaseOffset;
            set => phaseOffset = value;
        }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;

            center = pivot != null ? pivot.position : transform.position + Vector3.up * length;
            swingAxis = pivot != null ? pivot.right : transform.right;
            baseRotation = transform.rotation;
            previousPosition = transform.position;
        }

        /// <summary>
        /// Задать перекладину и ось качания в мире. Нужна билдеру арены,
        /// который ставит качели по числам конфига, а не рукой в сцене.
        /// </summary>
        public void Configure(Vector3 worldPivot, Vector3 worldAxis)
        {
            center = worldPivot;
            swingAxis = worldAxis.sqrMagnitude > 0.0001f ? worldAxis.normalized : Vector3.right;
        }

        private void FixedUpdate()
        {
            if (period <= 0f)
            {
                return;
            }

            float phase = (float)((NetworkClock.Now - phaseOffset) / period) * Mathf.PI * 2f;
            float angle = amplitudeDegrees * Mathf.Sin(phase);

            Quaternion swing = Quaternion.AngleAxis(angle, swingAxis);
            Vector3 next = center + swing * (Vector3.down * length);

            Velocity = (next - previousPosition) / Time.fixedDeltaTime;
            previousPosition = next;

            transform.SetPositionAndRotation(next, swing * baseRotation);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }

            Vector3 direction = Velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = other.transform.position - center;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
            }

            Vector3 impulse = (direction.normalized + Vector3.up * impactUpward).normalized * impactForce;
            player.ApplyWorldImpulse(impulse);
            Hit?.Invoke(player);
        }
    }
}
