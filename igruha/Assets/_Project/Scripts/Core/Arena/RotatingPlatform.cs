using UnityEngine;
using Unity.Netcode;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Горизонтально вращающаяся платформа, которая везёт стоящих на ней.
    /// Карусель «Заражения», поворотный круг, диск-ловушка — всё, где пол
    /// уходит из-под ног по кругу.
    ///
    /// <b>Угол — чистая функция от общих часов, а не накопленная сумма кадров.</b>
    /// Каждая машина считает <c>angle = start + speed × NetworkClock.Now</c> и
    /// приходит к одному и тому же числу сама, поэтому платформу не нужно
    /// реплицировать вовсе: ни NetworkVariable, ни RPC, ни коррекции. Сумма
    /// кадров так не умеет — у клиента с просадкой она отстаёт навсегда.
    ///
    /// <b>Пассажира везёт та машина, которой он принадлежит.</b> Авторитет над
    /// позицией персонажа у владельца (ClientNetworkTransform), и сдвинуть чужую
    /// копию нельзя — её перетрёт сетевое состояние. Работает это только потому,
    /// что платформа у всех в одном и том же положении: каждый довозит своего.
    ///
    /// <b>Разворачивается только положение, но не взгляд.</b> Доворачивать тело
    /// вместе с платформой — значит крутить персонажа под игроком: управление
    /// камерное, и «вперёд» уезжало бы из-под пальцев. Смысл карусели в том,
    /// что сходишь не там, где рассчитывал преследователь, а не в том, чтобы
    /// отобрать управление.
    ///
    /// <see cref="RidePlatform"/> для этого не годится: он ходит по вертикали
    /// и управляется осью ввода пассажира.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RotatingPlatform : MonoBehaviour
    {
        /// <summary>Сколько пассажиров ищем за такт. Лобби не больше восьми, запас — на реквизит в той же зоне.</summary>
        private const int MaxOverlaps = 16;

        [Tooltip("Скорость вращения, градусов в секунду. Знак задаёт сторону")]
        [SerializeField] private float degreesPerSecond = 30f;

        [Tooltip("Радиус платформы, м. По нему ищутся пассажиры")]
        [SerializeField] private float radius = 4f;

        [Tooltip("Слои, на которых живут пассажиры (персонажи)")]
        [SerializeField] private LayerMask passengerLayers = ~0;

        [Tooltip("На сколько метров выше настила ищем ноги пассажира. Меньше — пассажиров теряем на кочках, больше — везём пробегающих рядом")]
        [SerializeField] private float rideHeight = 1.2f;

        [Tooltip("На сколько метров ниже настила ещё считаем пассажира стоящим. Нужен запас на просадку капсулы в опору")]
        [SerializeField] private float rideDepth = 0.35f;

        private readonly Collider[] overlaps = new Collider[MaxOverlaps];
        private Rigidbody body;
        private float startYaw;
        private float topLocalY;

        /// <summary>Текущий угол платформы, градусов. Одинаков на всех машинах.</summary>
        public float Angle => startYaw + (float)(NetworkClock.Now * degreesPerSecond % 360.0);

        /// <summary>Скорость вращения — арт и звук скрипа берут её отсюда, а не заводят своё число.</summary>
        public float DegreesPerSecond
        {
            get => degreesPerSecond;
            set => degreesPerSecond = value;
        }

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0.1f, value);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;

            // Интерполяция кинематического тела: без неё настил дёргается
            // между тактами физики, и на нём дёргается пассажир.
            body.interpolation = RigidbodyInterpolation.Interpolate;

            startYaw = transform.eulerAngles.y;
            topLocalY = MeasureTop();
        }

        private void FixedUpdate()
        {
            float target = Angle;
            float current = body.rotation.eulerAngles.y;
            float delta = Mathf.DeltaAngle(current, target);
            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }

            Vector3 center = body.position;
            body.MoveRotation(Quaternion.Euler(0f, target, 0f));
            CarryPassengers(center, delta);
        }

        /// <summary>
        /// Повернуть пассажиров вокруг оси платформы на тот же угол.
        /// Позиция меняется через <c>MovePosition</c>, а не присвоением: тело
        /// пассажира не кинематическое, и телепорт на каждом такте ломает ему
        /// столкновения с бортом.
        /// </summary>
        private void CarryPassengers(Vector3 center, float deltaDegrees)
        {
            float topY = center.y + topLocalY;
            Vector3 probe = new Vector3(center.x, topY + (rideHeight - rideDepth) * 0.5f, center.z);
            float probeRadius = radius + 0.2f;
            float probeHeight = (rideHeight + rideDepth) * 0.5f;

            int count = Physics.OverlapCapsuleNonAlloc(
                probe + Vector3.up * probeHeight,
                probe - Vector3.up * probeHeight,
                probeRadius,
                overlaps,
                passengerLayers,
                QueryTriggerInteraction.Ignore);

            Quaternion turn = Quaternion.Euler(0f, deltaDegrees, 0f);

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlaps[i];
                if (hit == null)
                {
                    continue;
                }

                PlayerController player = hit.GetComponentInParent<PlayerController>();
                if (player == null || !player.TryGetComponent(out Rigidbody passenger))
                {
                    continue;
                }

                // Чужую копию персонажа двигать нельзя: её позицию решает владелец.
                if (player.TryGetComponent(out NetworkObject netObject) &&
                    netObject.IsSpawned && !netObject.IsOwner)
                {
                    continue;
                }

                Vector3 position = passenger.position;
                if (position.y < topY - rideDepth || position.y > topY + rideHeight)
                {
                    continue;
                }

                Vector3 offset = position - center;
                offset.y = 0f;
                if (offset.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                Vector3 turned = turn * offset;
                passenger.MovePosition(new Vector3(center.x + turned.x, position.y, center.z + turned.z));
            }
        }

        /// <summary>
        /// Высота настила над центром объекта. Берётся из коллайдера, а не
        /// из числа в инспекторе: настил собирает билдер арены, и разъехаться
        /// эти два значения обязаны были бы при первой же правке толщины.
        /// </summary>
        private float MeasureTop()
        {
            if (TryGetComponent(out Collider deck))
            {
                return deck.bounds.max.y - transform.position.y;
            }

            return 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
