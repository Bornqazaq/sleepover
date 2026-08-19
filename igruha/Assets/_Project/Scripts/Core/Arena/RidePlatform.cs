using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Вертикальная платформа, которая везёт пассажира: лифт Охотника в Duck Hunt,
    /// подъёмник или транспорт в любой другой игре.
    ///
    /// Платформа не читает ввод сама — ось приходит снаружи через <see cref="SetMoveAxis"/>.
    /// Это единственная точка, меняющая её состояние: в сетевой фазе вызов уходит
    /// за ServerRpc, высота становится NetworkVariable и применяется через
    /// <see cref="SetHeight"/>, а логика движения ниже не меняется.
    ///
    /// Пассажир возится явной дельтой, а не трением о площадку. Персонаж проекта —
    /// динамический Rigidbody, которому <see cref="PlayerController"/> каждый физический
    /// тик перезаписывает горизонтальную скорость и оставляет вертикальную. На подъёме
    /// его бы дотолкала сама площадка, а вот на спуске он отставал бы от неё и ехал
    /// серией мелких падений — заметная тряска там, где человек целится.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RidePlatform : MonoBehaviour
    {
        /// <summary>Пассажиров на платформе всегда единицы, но коллайдеров у каждого может быть несколько.</summary>
        private const int MaxOverlapResults = 16;

        [Header("Ход")]
        [Tooltip("Скорость подъёма и спуска, м/с. Там, где платформа задаёт позицию стрелка, это главный рычаг баланса — держать в конфиге игры, а не подбирать здесь")]
        [SerializeField] private float speed = 3.25f;
        [Tooltip("Нижняя граница хода — смещение от той высоты, на которой платформа стоит в сцене, м. Отрицательное опускает её ниже стартовой точки")]
        [SerializeField] private float minTravel = -1.44f;
        [Tooltip("Верхняя граница хода — смещение от стартовой высоты, м")]
        [SerializeField] private float maxTravel = 33.12f;

        [Header("Пассажиры")]
        [Tooltip("Центр зоны, внутри которой пассажир едет вместе с платформой. Локальные координаты относительно платформы")]
        [SerializeField] private Vector3 rideZoneCenter = new Vector3(0f, 1.2f, 0f);
        [Tooltip("Размер зоны пассажиров. Должна накрывать всю площадку и быть выше самого высокого персонажа")]
        [SerializeField] private Vector3 rideZoneSize = new Vector3(2.88f, 2.4f, 2.88f);
        [Tooltip("Слои, на которых лежат тела пассажиров")]
        [SerializeField] private LayerMask passengerLayers = ~0;
        [Tooltip("Удерживать пассажира в границах площадки. Снимать только там, где сойти на ходу — часть задумки")]
        [SerializeField] private bool confinePassengers = true;
        [Tooltip("Отступ от края площадки, на котором держится пассажир, м. Обычно радиус его капсулы — иначе он висит половиной тела за краем")]
        [SerializeField] private float confineMargin = 0.36f;

        private readonly List<Rigidbody> passengers = new List<Rigidbody>(4);
        private readonly Collider[] overlapResults = new Collider[MaxOverlapResults];

        private Rigidbody rb;
        private float startY;
        private float moveAxis;

        /// <summary>Текущее смещение от стартовой высоты, м. Это и есть состояние, которое поедет в NetworkVariable.</summary>
        public float Height { get; private set; }

        /// <summary>Платформа стоит в крайней нижней точке хода.</summary>
        public bool AtBottom => Height <= minTravel + Mathf.Epsilon;

        /// <summary>Платформа стоит в крайней верхней точке хода.</summary>
        public bool AtTop => Height >= maxTravel - Mathf.Epsilon;

        /// <summary>Платформа реально едет в этом тике. Стрелковым ролям нужен для повышенного разброса в движении.</summary>
        public bool IsMoving { get; private set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            startY = rb.position.y;

            if (minTravel > maxTravel)
            {
                Debug.LogError($"{name}: нижняя граница хода {minTravel} выше верхней {maxTravel} — платформа не сдвинется.", this);
            }
        }

        /// <summary>
        /// Задать ось движения: −1 вниз, +1 вверх, 0 стоять. Единственная точка
        /// входа для управления платформой — в сетевой фазе её зовёт сервер,
        /// получив ввод владельца через ServerRpc.
        /// </summary>
        public void SetMoveAxis(float axis) => moveAxis = Mathf.Clamp(axis, -1f, 1f);

        /// <summary>
        /// Поставить платформу на заданную высоту немедленно. Нужен клиенту
        /// сетевой фазы (применить реплицированную высоту) и мини-игре при
        /// старте раунда — вернуть лифт в исходную точку.
        /// </summary>
        public void SetHeight(float height)
        {
            Height = Mathf.Clamp(height, minTravel, maxTravel);
            rb.position = new Vector3(rb.position.x, startY + Height, rb.position.z);
        }

        private void FixedUpdate()
        {
            CollectPassengers();

            float target = Mathf.Clamp(Height + moveAxis * speed * Time.fixedDeltaTime, minTravel, maxTravel);
            float delta = target - Height;
            IsMoving = !Mathf.Approximately(delta, 0f);

            if (IsMoving)
            {
                Height = target;
                rb.MovePosition(new Vector3(rb.position.x, startY + Height, rb.position.z));
                CarryPassengers(delta);
            }

            if (confinePassengers)
            {
                ConfinePassengers();
            }
        }

        /// <summary>
        /// Кто едет прямо сейчас. Опрос объёма вместо OnTrigger-событий: он
        /// не зависит от порядка событий физики и сам подхватывает пассажира,
        /// которого телепортировали на площадку (спавн, респавн, старт раунда).
        /// </summary>
        private void CollectPassengers()
        {
            passengers.Clear();

            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(rideZoneCenter),
                rideZoneSize * 0.5f,
                overlapResults,
                transform.rotation,
                passengerLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerController passenger = overlapResults[i].GetComponentInParent<PlayerController>();
                if (passenger == null)
                {
                    continue;
                }

                // У персонажа несколько коллайдеров — тело найдётся столько же раз.
                Rigidbody body = passenger.GetComponent<Rigidbody>();
                if (body != null && !passengers.Contains(body))
                {
                    passengers.Add(body);
                }
            }
        }

        private void CarryPassengers(float delta)
        {
            Vector3 shift = new Vector3(0f, delta, 0f);
            for (int i = 0; i < passengers.Count; i++)
            {
                passengers[i].MovePosition(passengers[i].position + shift);
            }
        }

        /// <summary>
        /// Не дать пассажиру уйти с площадки. Геометрией это не закрыть до конца:
        /// бортики можно перепрыгнуть, а роль, привязанная к платформе на весь
        /// раунд, обязана оставаться на ней при любом вводе.
        /// </summary>
        private void ConfinePassengers()
        {
            Vector3 center = transform.TransformPoint(rideZoneCenter);
            float halfX = Mathf.Max(0f, rideZoneSize.x * 0.5f - confineMargin);
            float halfZ = Mathf.Max(0f, rideZoneSize.z * 0.5f - confineMargin);

            for (int i = 0; i < passengers.Count; i++)
            {
                Rigidbody body = passengers[i];
                Vector3 position = body.position;
                float clampedX = Mathf.Clamp(position.x, center.x - halfX, center.x + halfX);
                float clampedZ = Mathf.Clamp(position.z, center.z - halfZ, center.z + halfZ);

                if (Mathf.Approximately(clampedX, position.x) && Mathf.Approximately(clampedZ, position.z))
                {
                    continue;
                }

                body.position = new Vector3(clampedX, position.y, clampedZ);

                // Скорость гасим вместе с позицией: иначе пассажир каждый тик
                // упирается в невидимую границу, продолжая копить импульс.
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, velocity.y, 0f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(rideZoneCenter, rideZoneSize);

            // Ход рисуется от стартовой точки: в редакторе это та высота, на
            // которой платформа лежит в сцене, в игре — та же самая.
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
            float baseY = Application.isPlaying ? startY : transform.position.y;
            Vector3 bottom = new Vector3(transform.position.x, baseY + minTravel, transform.position.z);
            Vector3 top = new Vector3(transform.position.x, baseY + maxTravel, transform.position.z);
            Gizmos.DrawLine(bottom, top);
            Gizmos.DrawWireSphere(bottom, 0.2f);
            Gizmos.DrawWireSphere(top, 0.2f);
        }
    }
}
