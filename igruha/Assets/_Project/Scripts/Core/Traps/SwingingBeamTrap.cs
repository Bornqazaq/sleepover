using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Балка на кране: ходит по кругу вокруг заданной точки с постоянным
    /// периодом и сшибает всех подряд без разбора — и игроков, и то, что они
    /// несут.
    ///
    /// <b>Ход считается от общих часов</b>, как и у <see cref="PeriodicTrapDriver"/>:
    /// каждая машина берёт положение балки по одной формуле от одного момента,
    /// поэтому в сетевой фазе балка сойдётся у всех без трафика, а подключившийся
    /// в середине раунда сразу увидит её там же, где остальные.
    ///
    /// Висит на самой балке — там, где коллайдер-триггер: только объект с
    /// коллайдером получает <c>OnTriggerEnter</c>. Точка вращения задаётся
    /// отдельным трансформом, чтобы балку можно было двигать по арене, не
    /// пересобирая иерархию.
    ///
    /// Не наследник <see cref="TrapBase"/> намеренно: у той ловушки есть момент
    /// срабатывания и кулдаун, а здесь нет ни того, ни другого — балка опасна
    /// непрерывно и всё время, пока идёт раунд.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SwingingBeamTrap : MonoBehaviour
    {
        [Tooltip("Вокруг чего ходит балка. Пусто — вокруг точки, где она стояла на старте")]
        [SerializeField] private Transform pivot;
        [Tooltip("Радиус хода от точки вращения, м")]
        [SerializeField] private float radius = 3f;
        [Tooltip("Период полного оборота, с")]
        [SerializeField] private float period = 6f;
        [Tooltip("Сдвиг фазы, с")]
        [SerializeField] private float phaseOffset;
        [Tooltip("Импульс, которым балка сшибает игрока. Должен превышать порог нокдауна, иначе она только толкает")]
        [SerializeField] private float impactForce = 16f;
        [Tooltip("Доля импульса вверх — чтобы сбитый улетал, а не проезжал по полу")]
        [Range(0f, 1f)]
        [SerializeField] private float impactUpward = 0.3f;

        private Vector3 center;
        private Quaternion baseRotation;
        private double originTime;
        private Vector3 previousPosition;

        /// <summary>Скорость балки в мире, м/с. По ней считается направление удара.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Период полного оборота, с. Задаётся мини-игрой из её конфига.</summary>
        public float Period
        {
            get => period;
            set => period = Mathf.Max(0.01f, value);
        }

        /// <summary>Радиус хода, м. Им подгоняют перекрытие прохода под правило «не больше половины ширины».</summary>
        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            center = pivot != null ? pivot.position : transform.position;
            baseRotation = transform.rotation;
            previousPosition = transform.position;
            originTime = NetworkClock.Now + phaseOffset;
        }

        private void OnEnable()
        {
            // Фазу отсчитываем от включения: балка, поднятая правилами раунда
            // посреди игры, не должна прыгать в случайное положение.
            originTime = NetworkClock.Now + phaseOffset;
        }

        /// <summary>Задать точку вращения в мире. Нужна билдеру арены, который ставит балку по числам конфига.</summary>
        public void SetCenter(Vector3 worldCenter) => center = worldCenter;

        private void FixedUpdate()
        {
            if (period <= 0f)
            {
                return;
            }

            float angle = (float)((NetworkClock.Now - originTime) / period) * 360f;
            Quaternion swing = Quaternion.Euler(0f, angle, 0f);

            Vector3 next = center + swing * (Vector3.forward * radius);
            Velocity = (next - previousPosition) / Time.fixedDeltaTime;
            previousPosition = next;

            transform.SetPositionAndRotation(next, swing * baseRotation);
        }

        private void OnTriggerEnter(Collider other)
        {
            // Удар — исход, решает авторитет: иначе он засчитается столько раз,
            // сколько в матче машин.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            Vector3 direction = Velocity;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyWorldImpulse((direction + Vector3.up * impactUpward).normalized * impactForce);
                return;
            }

            // Не игрок — значит, что-то из мини-игры. Что именно значит удар,
            // решает сама цель: балка про воду и очки не знает.
            ITrapImpactTarget target = other.GetComponentInParent<ITrapImpactTarget>();
            target?.TakeTrapImpact(direction, impactForce);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 gizmoCenter = Application.isPlaying
                ? center
                : (pivot != null ? pivot.position : transform.position);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(gizmoCenter, radius);
        }
    }
}
