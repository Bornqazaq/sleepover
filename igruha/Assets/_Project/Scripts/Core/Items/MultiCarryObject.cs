using System;
using UnityEngine;
using Igruha.Core.Interaction;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Items
{
    /// <summary>Почему ручка освободилась. Нужна тем, кто отыгрывает звук и потери.</summary>
    public enum CarryReleaseReason
    {
        /// <summary>Игрок нажал E ещё раз.</summary>
        LetGo,
        /// <summary>Связь растянулась дальше предела.</summary>
        Overstretched,
        /// <summary>Несущего сбили: удар, толчок, ловушка, нокдаун.</summary>
        Knockdown,
        /// <summary>Совместный бросок: отцепились все разом.</summary>
        Thrown,
        /// <summary>Раунд кончился, роль снимается с игрока.</summary>
        RoundEnded
    }

    /// <summary>
    /// Объект, за который держатся несколько человек и который едет туда, куда
    /// его тянут все вместе. Про воду, баки и команды не знает ничего — этим
    /// занимается тот, кто его настраивает.
    ///
    /// <b>Модель одна на все случаи</b> (спека «Переноски предмета», 9.1):
    ///
    /// 1. У объекта N ручек, равномерно по окружности радиуса
    ///    <c>handleRadius</c> на высоте <c>handleHeight</c>. Число ручек задаёт
    ///    снаружи мини-игра — обычно по размеру команды.
    /// 2. Каждый несущий притянут к своей стоянке упругой связью. Отстал или
    ///    рванул — связь натянулась, и это натяжение и есть единственный вход
    ///    модели.
    /// 3. <b>Движение:</b> объект едет под суммой натяжений, скорость ограничена
    ///    потолком. Отсюда «скорость не зависит от числа несущих»: потолок один,
    ///    а тянущие вразнобой гасят друг друга векторно.
    /// 4. <b>Наклон:</b> момент относительно основания — сумма
    ///    (плечо ручки × натяжение), плюс смещение опоры, когда занята не вся
    ///    окружность. Демпфер и возврат к вертикали не дают объекту падать от
    ///    каждого шага.
    ///
    /// Что из этого следует само, без единого добавочного параметра:
    ///
    /// — <b>четверо нестабильнее двоих:</b> четыре независимых натяжения чаще
    ///   не складываются, чем два, и остаточный момент у них больше даже там,
    ///   где сумма для движения обнулилась;
    /// — <b>ушёл один из четверых:</b> занятые ручки перестают быть
    ///   симметричными, центр опоры уезжает в сторону оставшихся, и вес объекта
    ///   валит его в пустую сторону;
    /// — <b>одиночка несёт ровно:</b> у объекта с одной ручкой центр опоры
    ///   совпадает с этой ручкой, смещения нет вовсе.
    ///
    /// Скорость и наклон считает авторитет — в сетевой фазе сервер, вне сети
    /// эта же машина. Остальные увидят результат через NetworkTransform.
    /// Упругая тяга к своей стоянке в фазе 3 переедет на машину владельца
    /// (см. <c>RidePlatform</c>): чужого игрока сервер двигать не вправе.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MultiCarryObject : MonoBehaviour, IInteractable, IPushButtonOverride
    {
        /// <summary>Больше четырёх рук не бывает: столько человек в самой большой команде проекта.</summary>
        public const int MaxHandles = 4;

        [SerializeField] private MultiCarrySettings settings = MultiCarrySettings.Default;
        [Tooltip("Подсказка над объектом. Одна на оба действия: E и берёт, и отпускает")]
        [SerializeField] private string interactionPrompt = "Взяться за бутыль (E)";
        [Tooltip("Слои опоры. По ним объект понимает, что приземлился после броска")]
        [SerializeField] private LayerMask groundLayers = ~0;

        /// <summary>Ручку заняли: номер слота и кто занял.</summary>
        public event Action<int, PlayerController> HandleTaken;

        /// <summary>Ручку освободили: номер слота, кто держал, почему отпустил.</summary>
        public event Action<int, PlayerController, CarryReleaseReason> HandleReleased;

        /// <summary>Число несущих изменилось.</summary>
        public event Action<int> CarrierCountChanged;

        /// <summary>Наклон перешёл порог (true) или вернулся под него (false).</summary>
        public event Action<bool> TiltThresholdCrossed;

        /// <summary>Сорвались все ручки — объект упал и стоит там, где был.</summary>
        public event Action Dropped;

        /// <summary>Объект швырнули. Аргумент — сколько человек бросало.</summary>
        public event Action<int> Thrown;

        /// <summary>Брошенный объект коснулся земли.</summary>
        public event Action Landed;

        /// <summary>
        /// Одна ручка. Классом, а не структурой: держит подписку на нокдаун
        /// своего несущего, а её надо снимать той же ссылкой, которой вешали.
        /// </summary>
        private sealed class Handle
        {
            public PlayerController Carrier;
            public Rigidbody CarrierBody;
            public CapsuleCollider CarrierCollider;
            public PlayerCarryAbility CarrierCarry;
            public PlayerPushAbility CarrierPush;
            public Action<KnockdownType> KnockdownHandler;

            public bool Occupied => Carrier != null;
        }

        private readonly Handle[] handles = new Handle[MaxHandles];
        private Rigidbody body;
        private Collider[] ownColliders;
        private Predicate<PlayerController> ownerFilter;

        private int handleCount = 1;

        /// <summary>Наклон как вектор «ось × угол», радианы. Ось всегда горизонтальна.</summary>
        private Vector3 tiltRotation;
        private Vector3 tiltAngularVelocity;

        private Quaternion baseRotation = Quaternion.identity;
        private bool beyondThreshold;
        private bool hadCarriers;

        public int HandleCount => handleCount;
        public int CarrierCount { get; private set; }
        public bool IsCarried => CarrierCount > 0;

        /// <summary>Текущий наклон от вертикали, °.</summary>
        public float TiltAngle => tiltRotation.magnitude * Mathf.Rad2Deg;

        /// <summary>Наклон за порогом прямо сейчас.</summary>
        public bool BeyondTiltThreshold => beyondThreshold;

        /// <summary>Объект брошен и ещё не коснулся земли. На свистке такой не засчитывается.</summary>
        public bool InFlight { get; private set; }

        public MultiCarrySettings Settings => settings;

        public string InteractionPrompt => interactionPrompt;

        private void Reset()
        {
            settings = MultiCarrySettings.Default;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ownColliders = GetComponentsInChildren<Collider>();

            // Поворот считает модель наклона, а не физика: иначе объект
            // кувыркается от любого касания, и «стоит вертикально после
            // приземления» перестаёт выполняться.
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            baseRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = new Handle();
            }
        }

        private void OnDisable()
        {
            ReleaseAll(CarryReleaseReason.RoundEnded);
        }

        /// <summary>
        /// Числа модели. Зовёт мини-игра из своего конфига — так все значения
        /// живут в одном ассете и крутятся на плейтесте без пересборки.
        /// </summary>
        public void Configure(in MultiCarrySettings value)
        {
            settings = value;
        }

        /// <summary>
        /// Сколько ручек у объекта. Для «Переноски предмета» — размер команды:
        /// лишних ручек нет, пятый подойти и взяться не может.
        ///
        /// Смена числа ручек освобождает те, что вышли за новый предел: иначе
        /// у объекта остался бы несущий на ручке, которой больше не существует.
        /// </summary>
        public void SetHandleCount(int count)
        {
            int clamped = Mathf.Clamp(count, 1, MaxHandles);
            if (clamped == handleCount)
            {
                return;
            }

            for (int i = clamped; i < handles.Length; i++)
            {
                ReleaseHandle(i, CarryReleaseReason.RoundEnded);
            }

            handleCount = clamped;
        }

        /// <summary>
        /// Кому разрешено браться. Пусто — всем. «Переноска предмета» ставит
        /// сюда проверку своей команды: чужой за ручку не возьмётся.
        /// </summary>
        public void SetOwnerFilter(Predicate<PlayerController> filter) => ownerFilter = filter;

        /// <summary>Держит ли этот игрок какую-нибудь ручку.</summary>
        public bool IsCarriedBy(PlayerController player) => FindSlotOf(player) >= 0;

        /// <summary>Несущий на этом слоте. Null — слот свободен.</summary>
        public PlayerController CarrierAt(int slot) =>
            slot >= 0 && slot < handles.Length ? handles[slot].Carrier : null;

        /// <summary>
        /// Тело несущего на этом слоте. Кэшировано в момент захвата: тем, кто
        /// читает скорости несущих каждый такт физики, звать GetComponent нельзя.
        /// </summary>
        public Rigidbody CarrierBodyAt(int slot) =>
            slot >= 0 && slot < handles.Length ? handles[slot].CarrierBody : null;

        // ========== ВЗАИМОДЕЙСТВИЕ ==========

        public bool CanInteract(PlayerController player)
        {
            if (player == null || InFlight)
            {
                return false;
            }

            if (IsCarriedBy(player))
            {
                return true;
            }

            return HasFreeHandle() && (ownerFilter == null || ownerFilter(player));
        }

        /// <summary>
        /// E: взяться за свободную ручку или отпустить свою. По сети сюда
        /// приводит серверное взаимодействие <c>PlayerInteractor</c>, то есть
        /// решение уже принято авторитетом.
        /// </summary>
        public void Interact(PlayerController player)
        {
            int slot = FindSlotOf(player);
            if (slot >= 0)
            {
                ReleaseHandle(slot, CarryReleaseReason.LetGo);
                return;
            }

            TryGrab(player);
        }

        /// <summary>
        /// Занять свободную ручку. Единственная точка входа на захват — в фазе
        /// сети она уйдёт за <c>IsServer</c> без переписывания логики.
        /// </summary>
        public bool TryGrab(PlayerController player)
        {
            if (player == null || InFlight)
            {
                return false;
            }

            if (ownerFilter != null && !ownerFilter(player))
            {
                return false;
            }

            if (FindSlotOf(player) >= 0)
            {
                return false;
            }

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                return false;
            }

            Handle handle = handles[slot];
            handle.Carrier = player;
            handle.CarrierBody = player.GetComponent<Rigidbody>();
            handle.CarrierCollider = player.GetComponent<CapsuleCollider>();
            handle.CarrierCarry = player.GetComponent<PlayerCarryAbility>();
            handle.CarrierPush = player.GetComponent<PlayerPushAbility>();

            // Сбитый несущий роняет ручку. Подписка на слот своя, чтобы снять
            // её потом ровно той же ссылкой.
            handle.KnockdownHandler = _ => ReleaseHandle(slot, CarryReleaseReason.Knockdown);
            player.KnockdownStarted += handle.KnockdownHandler;

            // Руки заняты: ни ударить, ни подобрать, ни бросить предмет.
            // Кнопку толчка забираем себе — ей теперь швыряют сам объект.
            if (handle.CarrierCarry != null)
            {
                handle.CarrierCarry.Drop();
                handle.CarrierCarry.HandsBlocked = true;
            }

            if (handle.CarrierPush != null)
            {
                handle.CarrierPush.ButtonOverride = this;
            }

            player.ApplySpeedCap(this, settings.carrierSpeedCap);
            SetCollisionsWithCarrier(handle, true);

            CarrierCount++;
            hadCarriers = true;

            // Объект в руках держит модель, а не гравитация: иначе он волочится
            // по полу, а на доске над пропастью проваливается между несущими.
            body.useGravity = false;
            InFlight = false;

            HandleTaken?.Invoke(slot, player);
            CarrierCountChanged?.Invoke(CarrierCount);
            return true;
        }

        /// <summary>
        /// <b>Единственная точка отцепления.</b> Сюда приходят все три причины
        /// срыва из спеки 9.2 — нажатие E, перерастяжение, сбитый несущий — плюс
        /// бросок и конец раунда. В фазе сети её обернут проверкой
        /// <c>IsServer</c>, и больше ничего менять не придётся.
        /// </summary>
        public bool ReleaseHandle(int slot, CarryReleaseReason reason)
        {
            if (slot < 0 || slot >= handles.Length)
            {
                return false;
            }

            Handle handle = handles[slot];
            if (!handle.Occupied)
            {
                return false;
            }

            PlayerController carrier = handle.Carrier;

            carrier.KnockdownStarted -= handle.KnockdownHandler;
            handle.KnockdownHandler = null;

            carrier.ClearSpeedCap(this);
            SetCollisionsWithCarrier(handle, false);

            if (handle.CarrierCarry != null)
            {
                handle.CarrierCarry.HandsBlocked = false;
            }

            if (handle.CarrierPush != null && ReferenceEquals(handle.CarrierPush.ButtonOverride, this))
            {
                handle.CarrierPush.ButtonOverride = null;
            }

            handle.Carrier = null;
            handle.CarrierBody = null;
            handle.CarrierCollider = null;
            handle.CarrierCarry = null;
            handle.CarrierPush = null;

            CarrierCount--;
            HandleReleased?.Invoke(slot, carrier, reason);
            CarrierCountChanged?.Invoke(CarrierCount);

            if (CarrierCount == 0)
            {
                OnLastHandleReleased(reason);
            }

            return true;
        }

        /// <summary>Отцепить конкретного игрока, где бы он ни держал.</summary>
        public bool ReleaseFor(PlayerController player, CarryReleaseReason reason)
        {
            int slot = FindSlotOf(player);
            return slot >= 0 && ReleaseHandle(slot, reason);
        }

        /// <summary>Отцепить всех. Конец раунда обязан звать это сам — иначе роль уедет в хаб вместе с телом.</summary>
        public void ReleaseAll(CarryReleaseReason reason)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                ReleaseHandle(i, reason);
            }
        }

        private void OnLastHandleReleased(CarryReleaseReason reason)
        {
            // Отпущенный объект снова живёт под физикой: падает, катится,
            // проваливается в пропасть.
            body.useGravity = true;

            if (reason == CarryReleaseReason.Thrown)
            {
                return;
            }

            InFlight = false;

            if (hadCarriers)
            {
                Dropped?.Invoke();
            }
        }

        // ========== КНОПКА ТОЛЧКА: СОВМЕСТНЫЙ БРОСОК ==========

        /// <summary>
        /// Держащий бутыль не бьёт — он швыряет её. Бросают <b>все несущие
        /// разом</b>, поэтому решение принимает объект, а не персонаж:
        /// дальность растёт с числом бросающих.
        /// </summary>
        public bool HandlePushButton(PlayerController player)
        {
            if (!IsCarriedBy(player))
            {
                return false;
            }

            ThrowByCarriers();
            return true;
        }

        /// <summary>
        /// Совместный бросок. Направление — среднее из взглядов несущих,
        /// импульс — на каждого бросающего свой. Единственная точка входа:
        /// в фазе сети она уйдёт за <c>IsServer</c>.
        /// </summary>
        public void ThrowByCarriers()
        {
            int throwers = CarrierCount;
            if (throwers == 0)
            {
                return;
            }

            Vector3 aim = Vector3.zero;
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Occupied)
                {
                    aim += handles[i].Carrier.Facing;
                }
            }

            aim.y = 0f;
            aim = aim.sqrMagnitude < 0.0001f ? transform.forward : aim.normalized;

            ReleaseAll(CarryReleaseReason.Thrown);

            InFlight = true;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.AddForce((aim + Vector3.up * settings.throwUpward).normalized *
                          (settings.throwImpulsePerCarrier * throwers), ForceMode.Impulse);

            Thrown?.Invoke(throwers);
        }

        // ========== МОДЕЛЬ ==========

        private void FixedUpdate()
        {
            // Движение и наклон — исход, а исход считает авторитет. Вне сети
            // авторитет здесь же, поэтому одиночный тест сцены не меняется.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            if (CarrierCount > 0)
            {
                StepCarried(dt);
            }

            StepTiltRelaxation(dt);
            ApplyRotation();
        }

        /// <summary>
        /// Один шаг переноски: натяжения → скорость и момент, обратная тяга
        /// несущим, срыв перерастянутых ручек.
        /// </summary>
        private void StepCarried(float dt)
        {
            Vector3 pullSum = Vector3.zero;
            Vector3 torqueSum = Vector3.zero;
            Vector3 supportSum = Vector3.zero;
            float footSum = 0f;
            int occupied = 0;

            Vector3 basePoint = body.position;

            for (int i = 0; i < handleCount; i++)
            {
                Handle handle = handles[i];
                if (!handle.Occupied)
                {
                    continue;
                }

                Vector3 handleDir = HandleDirection(i);
                Vector3 station = basePoint + handleDir * (settings.handleRadius + settings.carrierStandoff);
                Vector3 carrierPosition = handle.Carrier.transform.position;

                Vector3 stretch = carrierPosition - station;
                stretch.y = 0f;
                float distance = stretch.magnitude;

                // Перерастянутая ручка срывается до того, как её посчитают:
                // сорванная рука ни тянет, ни держит.
                if (distance > settings.breakDistance)
                {
                    ReleaseHandle(i, CarryReleaseReason.Overstretched);
                    continue;
                }

                footSum += carrierPosition.y;
                occupied++;
                supportSum += handleDir * settings.handleRadius;

                if (distance > settings.tensionDeadzone)
                {
                    Vector3 direction = stretch / distance;
                    Vector3 tension = direction * (distance - settings.tensionDeadzone);

                    pullSum += tension;

                    // Плечо — от основания объекта до его ручки, вместе с высотой:
                    // именно высота ручки и превращает горизонтальную тягу в крен.
                    torqueSum += Vector3.Cross(HandleOffsetWorld(i), tension);

                    HoldCarrier(handle, direction, distance);
                }
            }

            if (occupied == 0)
            {
                return;
            }

            // Скорость: сумма натяжений с общим потолком. Тянущие вразнобой
            // гасят друг друга здесь же, векторно, — отдельного условия нет.
            Vector3 velocity = Vector3.ClampMagnitude(pullSum * settings.pullToSpeed, settings.maxObjectSpeed);

            // Высота: основание идёт над ступнями несущих. Так объект сам
            // поднимается на доску и не проваливается сквозь неё.
            float targetY = footSum / occupied + settings.carryClearance;
            float verticalRate = (targetY - body.position.y) / Mathf.Max(dt, Mathf.Epsilon);
            velocity.y = Mathf.Clamp(verticalRate, -settings.maxObjectSpeed * 2f, settings.maxObjectSpeed * 2f);

            body.linearVelocity = velocity;

            // Момент опоры: занята не вся окружность — центр опоры уезжает к
            // оставшимся рукам, и вес валит объект в пустую сторону.
            Vector3 supportOffset = supportSum / occupied - FullSupportCentroid();
            supportOffset.y = 0f;

            Vector3 supportTorque = Vector3.Cross(Vector3.up, -supportOffset) * settings.tiltFromSupportLoss;
            Vector3 tensionTorque = new Vector3(torqueSum.x, 0f, torqueSum.z) * settings.tiltFromTorque;

            tiltAngularVelocity += (tensionTorque + supportTorque) * dt;
        }

        /// <summary>
        /// Связь не пускает несущего дальше, чем осталось запаса. Забирает
        /// только уход <b>наружу</b>: вернуться к объекту можно всегда и полным
        /// ходом, иначе отставший не догнал бы свою команду никогда.
        ///
        /// Забирает не всё превышение, а долю: упрямый бегун всё-таки дотягивает
        /// связь до предела и срывает ручку — это и есть наказание за рывок.
        /// </summary>
        private void HoldCarrier(Handle handle, Vector3 outward, float distance)
        {
            if (handle.CarrierBody == null)
            {
                return;
            }

            Vector3 velocity = handle.CarrierBody.linearVelocity;
            float outwardSpeed = Vector3.Dot(velocity, outward);
            float freeSpeed = Mathf.Max(0f, settings.breakDistance - distance) * settings.tetherFreeSpeedPerMeter;

            if (outwardSpeed <= freeSpeed)
            {
                return;
            }

            velocity -= outward * ((outwardSpeed - freeSpeed) * settings.tetherGrip);
            handle.CarrierBody.linearVelocity = velocity;
        }

        /// <summary>Демпфер и возврат к вертикали. Работают всегда, в том числе у брошенного объекта.</summary>
        private void StepTiltRelaxation(float dt)
        {
            tiltAngularVelocity -= (tiltAngularVelocity * settings.tiltDamping +
                                    tiltRotation * settings.tiltRestoring) * dt;
            tiltRotation += tiltAngularVelocity * dt;

            float maxRadians = settings.maxTiltAngle * Mathf.Deg2Rad;
            float angle = tiltRotation.magnitude;
            if (angle > maxRadians && angle > Mathf.Epsilon)
            {
                Vector3 axis = tiltRotation / angle;
                tiltRotation = axis * maxRadians;

                // Гасим только ту часть скорости, что продолжала бы валить
                // дальше предела: раскачку вдоль оси оставляем.
                float along = Vector3.Dot(tiltAngularVelocity, axis);
                if (along > 0f)
                {
                    tiltAngularVelocity -= axis * along;
                }
            }

            bool beyond = TiltAngle >= settings.tiltThreshold;
            if (beyond != beyondThreshold)
            {
                beyondThreshold = beyond;
                TiltThresholdCrossed?.Invoke(beyond);
            }
        }

        private void ApplyRotation()
        {
            body.MoveRotation(Quaternion.AngleAxis(TiltDegrees(), TiltAxis()) * baseRotation);
        }

        private float TiltDegrees() => tiltRotation.magnitude * Mathf.Rad2Deg;

        private Vector3 TiltAxis()
        {
            float angle = tiltRotation.magnitude;
            return angle > Mathf.Epsilon ? tiltRotation / angle : Vector3.up;
        }

        /// <summary>Горизонтальное направление ручки в мире. Рыскание объекта не меняется, поэтому стоянки геометрически устойчивы.</summary>
        private Vector3 HandleDirection(int slot)
        {
            float angle = Mathf.PI * 2f * slot / handleCount;
            return baseRotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>Плечо ручки от основания объекта: радиус вбок плюс высота вверх, повёрнутые вместе с наклоном.</summary>
        private Vector3 HandleOffsetWorld(int slot)
        {
            Vector3 local = HandleDirection(slot) * settings.handleRadius + Vector3.up * settings.handleHeight;
            return Quaternion.AngleAxis(TiltDegrees(), TiltAxis()) * local;
        }

        /// <summary>
        /// Центр опоры при полностью занятой окружности. У объекта с одной
        /// ручкой он совпадает с ней самой — поэтому одиночка и несёт ровно,
        /// а не валит объект набок.
        /// </summary>
        private Vector3 FullSupportCentroid()
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < handleCount; i++)
            {
                sum += HandleDirection(i) * settings.handleRadius;
            }

            return sum / handleCount;
        }

        // ========== ПОЛЁТ И ПРИЗЕМЛЕНИЕ ==========

        private void OnCollisionEnter(Collision collision)
        {
            if (!InFlight)
            {
                return;
            }

            if ((groundLayers.value & (1 << collision.gameObject.layer)) == 0)
            {
                return;
            }

            InFlight = false;
            Landed?.Invoke();
        }

        // ========== СЛУЖЕБНОЕ ==========

        private bool HasFreeHandle() => FindFreeSlot() >= 0;

        private int FindFreeSlot()
        {
            for (int i = 0; i < handleCount; i++)
            {
                if (!handles[i].Occupied)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindSlotOf(PlayerController player)
        {
            if (player == null)
            {
                return -1;
            }

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Carrier == player)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Свой несущий объекту не помеха: без этого команда бульдозерит саму
        /// себя собственной тарой, и на месте стоянки несущего оказывается
        /// коллайдер того, что он несёт.
        /// </summary>
        private void SetCollisionsWithCarrier(Handle handle, bool ignore)
        {
            if (handle.CarrierCollider == null || ownColliders == null)
            {
                return;
            }

            for (int i = 0; i < ownColliders.Length; i++)
            {
                if (ownColliders[i] != null && ownColliders[i].enabled)
                {
                    Physics.IgnoreCollision(ownColliders[i], handle.CarrierCollider, ignore);
                }
            }
        }
    }
}
