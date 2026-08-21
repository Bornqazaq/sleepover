using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>
    /// Болванка соло-теста: ведёт персонажа к точке на арене, огибая геометрию
    /// и беря низкие ступени прыжком.
    ///
    /// Нужна потому, что половину правил мини-игры одному игроку проверить
    /// нечем: порядок финиша, досрочный конец раунда, переключение наблюдателя
    /// между живыми — всё это требует, чтобы до цели дошёл кто-то ещё. Двумя
    /// персонажами руками не поуправляешь, а сети на фазе каркаса ещё нет.
    ///
    /// Это отладочный инструмент, а не игровой ИИ: болванка не прячется, не
    /// блефует и не оценивает опасность. Ввод она подаёт через тот же
    /// <see cref="PlayerInputReader"/>, что и человек, поэтому заморозка,
    /// нокдаун, присед, толчки и авто-респавн застрявшего действуют на неё
    /// сами собой — отдельных веток в этих системах не появляется.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class DebugPlayerBot : MonoBehaviour
    {
        [Tooltip("Ближе этого расстояния до цели болванка стоит, юниты")]
        [SerializeField] private float arriveRadius = 0.4f;
        [Tooltip("На сколько вперёд болванка щупает геометрию, юниты")]
        [SerializeField] private float probeDistance = 1.8f;
        [Tooltip("Зазор между щупом и полом: без него щуп цепляет пол и болванка считает себя запертой, юниты")]
        [SerializeField] private float probeClearance = 0.1f;
        [Tooltip("Препятствие ниже этого — ступень, её берём прыжком. Выше — обходим. Юниты")]
        [SerializeField] private float stepHeight = 0.6f;
        [Tooltip("Шаг перебора направлений обхода, °")]
        [SerializeField] private float avoidStep = 25f;
        [Tooltip("Сколько шагов перебирается в каждую сторону")]
        [SerializeField] private int avoidSteps = 5;

        private PlayerController motor;
        private PlayerInputReader reader;
        private LayerMask obstacles;
        private Vector3 target;
        private float probeRadius;
        private float footOffset;
        private bool hasTarget;
        private bool stopOnArrival = true;

        /// <summary>
        /// Сторона обхода прошлого решения. Без памяти о ней болванка на
        /// симметричном камне каждый тик выбирает то левый, то правый путь
        /// и топчется на месте вместо обхода.
        /// </summary>
        private bool preferRight = true;

        /// <summary>Куда идём. Пусто — болванка стоит.</summary>
        public bool HasTarget => hasTarget;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
            reader = GetComponent<PlayerInputReader>();

            // Щуп должен быть толщиной с персонажа, а высоты считаться от ступней:
            // у пятерых персонажей разный рост, и числами это не задать.
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                probeRadius = capsule.radius;
                footOffset = capsule.center.y - capsule.height * 0.5f;
            }
            else
            {
                probeRadius = 0.36f;
                footOffset = 0f;
            }
        }

        private void OnDisable()
        {
            // Снятая болванка обязана бросить ввод: иначе она уходит из-под
            // управления с зажатым «вперёд» и уезжает в стену до конца раунда.
            reader?.DriveMove(Vector2.zero);
        }

        /// <summary>Какая геометрия считается препятствием. Задаёт мини-игра: слои у каждой арены свои.</summary>
        public void Configure(LayerMask obstacleLayers) => obstacles = obstacleLayers;

        /// <summary>
        /// То же плюс порог перешагивания: препятствие ниже него берётся
        /// прыжком, выше — обходится. Значение по умолчанию рассчитано на
        /// ровную арену; там, где подъём собран из площадок под высоту прыжка,
        /// болванка с ним считает каждую ступень стеной и встаёт у лестницы.
        /// </summary>
        public void Configure(LayerMask obstacleLayers, float maxStepHeight)
        {
            obstacles = obstacleLayers;
            stepHeight = Mathf.Max(0f, maxStepHeight);
        }

        /// <summary>
        /// Идти к точке в мире.
        ///
        /// <paramref name="stopOnArrival"/> различает конечную цель и путевую
        /// точку. У конечной болванка останавливается, дойдя до неё; путевая —
        /// лишь поворот маршрута, и останавливаться на ней нельзя. Ступени
        /// лестницы стоят плотнее радиуса «дошёл», поэтому болванка считала себя
        /// прибывшей на середине подъёма и замирала там до конца раунда.
        /// </summary>
        public void SetTarget(Vector3 worldPoint, bool stopOnArrival = true)
        {
            target = worldPoint;
            hasTarget = true;
            this.stopOnArrival = stopOnArrival;
        }

        /// <summary>Забыть цель и остановиться.</summary>
        public void Stop()
        {
            hasTarget = false;
            reader?.DriveMove(Vector2.zero);
        }

        private void FixedUpdate()
        {
            if (!hasTarget || reader == null || motor == null)
            {
                return;
            }

            // Заморозка и нокдаун болванку не касаются напрямую: она просто
            // перестаёт жать «вперёд». Держать ввод под блокировкой нельзя —
            // детектор застревания принял бы это за реальное застревание.
            if (motor.MovementLocked || motor.IsKnockedDown)
            {
                reader.DriveMove(Vector2.zero);
                return;
            }

            Vector3 offset = target - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (stopOnArrival && distance <= arriveRadius)
            {
                reader.DriveMove(Vector2.zero);
                return;
            }

            // Дальше цели щупать нечего: за ней поворот маршрута, и помеха там
            // не мешает дойти. Щуп на всю длину в комнате с укрытиями почти
            // всегда во что-нибудь упирается, и болванка топчется вместо шага.
            float range = Mathf.Min(probeDistance, Mathf.Max(distance, probeRadius));
            Vector3 course = ChooseCourse(offset / distance, range);
            reader.DriveMove(motor.WorldToMoveInput(course));
        }

        /// <summary>
        /// Куда шагнуть на самом деле. Прямо, если путь свободен; иначе — под
        /// наименьшим углом от прямого, начиная с той стороны, которую выбрали
        /// в прошлый раз.
        /// </summary>
        private Vector3 ChooseCourse(Vector3 desired, float range)
        {
            if (IsPassable(desired, range, out float bestClearance))
            {
                return desired;
            }

            Vector3 roomiest = desired;

            for (int i = 1; i <= avoidSteps; i++)
            {
                float angle = avoidStep * i;

                Vector3 preferred = Rotate(desired, preferRight ? angle : -angle);
                if (IsPassable(preferred, range, out float preferredClearance))
                {
                    return preferred;
                }

                if (preferredClearance > bestClearance)
                {
                    bestClearance = preferredClearance;
                    roomiest = preferred;
                }

                Vector3 opposite = Rotate(desired, preferRight ? -angle : angle);
                if (IsPassable(opposite, range, out float oppositeClearance))
                {
                    preferRight = !preferRight;
                    return opposite;
                }

                if (oppositeClearance > bestClearance)
                {
                    bestClearance = oppositeClearance;
                    roomiest = opposite;
                }
            }

            // Свободного направления нет — идём туда, где до помехи дальше
            // всего, а не упираемся в прямое.
            //
            // Щуп меряет 1.8 м вперёд, и в комнате с укрытиями «занято» почти
            // всегда: помеха в полутора метрах не мешает пройти метр и свернуть.
            // Пока болванка стояла, ждать оставалось только StuckDetector,
            // который вытаскивает её на чекпоинт — то есть отменяет весь путь.
            return roomiest;
        }

        /// <summary>
        /// Путь свободен или перегорожен ступенью, которую можно перепрыгнуть.
        /// Прыжок отсюда и заказывается: решение «перешагнуть, а не обходить»
        /// принимается там же, где меряется высота помехи.
        ///
        /// Высота помехи берётся по её собственным габаритам, а не вторым щупом
        /// на фиксированной высоте. Второй щуп годится, пока препятствия стоят
        /// поодиночке на ровном полу, но на лестнице он ловит не ту ступень,
        /// перед которой болванка стоит, а следующую за ней — и любая лестница
        /// целиком читается как глухая стена.
        /// </summary>
        private bool IsPassable(Vector3 direction, float range, out float clearance)
        {
            Vector3 origin = transform.position + Vector3.up * (footOffset + probeClearance + probeRadius);
            if (!Physics.SphereCast(origin, probeRadius, direction, out RaycastHit hit, range,
                    obstacles, QueryTriggerInteraction.Ignore))
            {
                clearance = range;
                return true;
            }

            float feetY = transform.position.y + footOffset;
            if (hit.collider.bounds.max.y - feetY > stepHeight)
            {
                clearance = hit.distance;
                return false;
            }

            if (motor.IsGrounded)
            {
                reader.DriveJump();
            }

            clearance = range;
            return true;
        }

        private static Vector3 Rotate(Vector3 direction, float degrees) =>
            Quaternion.AngleAxis(degrees, Vector3.up) * direction;
    }
}
