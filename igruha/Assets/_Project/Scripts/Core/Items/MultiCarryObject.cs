using System;
using Unity.Netcode;
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
    /// Кто держит ручки и в полёте ли объект — <b>состояние, а не событие</b>
    /// (спека «Переноски предмета», 10). Слоты хранят <c>NetworkObjectId</c>
    /// несущих, как <c>holderObjectId</c> в <see cref="PickupItem"/>, только
    /// массивом: оно же защищает от двойного захвата и оно же догоняет
    /// подключившегося в середине раунда.
    ///
    /// Причина последнего срыва едет тем же каналом одним байтом. Отдельного
    /// канала под неё не заводим: срыв всегда меняет слот, а несколько ручек
    /// срываются разом ровно по одной причине — бросок или конец раунда.
    /// </summary>
    public struct MultiCarryNetState : INetworkSerializable, IEquatable<MultiCarryNetState>
    {
        /// <summary>Слот свободен. Идентификаторы сетевых объектов начинаются с единицы.</summary>
        public const ulong NoCarrier = 0UL;

        public ulong Handle0;
        public ulong Handle1;
        public ulong Handle2;
        public ulong Handle3;

        /// <summary>Объект брошен и ещё не коснулся земли.</summary>
        public bool InFlight;

        /// <summary>Причина последнего срыва, <see cref="CarryReleaseReason"/> байтом.</summary>
        public byte LastRelease;

        public readonly ulong Of(int slot)
        {
            switch (slot)
            {
                case 0: return Handle0;
                case 1: return Handle1;
                case 2: return Handle2;
                case 3: return Handle3;
                default: return NoCarrier;
            }
        }

        public void Set(int slot, ulong carrierId)
        {
            switch (slot)
            {
                case 0: Handle0 = carrierId; break;
                case 1: Handle1 = carrierId; break;
                case 2: Handle2 = carrierId; break;
                case 3: Handle3 = carrierId; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Handle0);
            serializer.SerializeValue(ref Handle1);
            serializer.SerializeValue(ref Handle2);
            serializer.SerializeValue(ref Handle3);
            serializer.SerializeValue(ref InFlight);
            serializer.SerializeValue(ref LastRelease);
        }

        public bool Equals(MultiCarryNetState other) =>
            Handle0 == other.Handle0 &&
            Handle1 == other.Handle1 &&
            Handle2 == other.Handle2 &&
            Handle3 == other.Handle3 &&
            InFlight == other.InFlight &&
            LastRelease == other.LastRelease;
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
    /// <b>Сеть.</b> Решения принимает сервер: кто взялся, кто сорвался, куда
    /// поехал объект и как он накренился. Позиция и поворот уезжают
    /// <c>NetworkTransform</c>, занятые ручки — <see cref="MultiCarryNetState"/>.
    /// Последствия захвата (потолок скорости, занятые руки, развязка коллизий)
    /// применяет <b>каждая машина у себя</b>: они живут в моторе владельца, и
    /// ждать пакета им нельзя.
    ///
    /// Упругая тяга к своей стоянке — единственное, что считает не сервер, а
    /// машина владельца несущего (см. <c>RidePlatform</c>): чужого игрока
    /// сервер двигать не вправе, это IGR-297.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MultiCarryObject : NetworkBehaviour, IInteractable, IPushButtonOverride
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
            public NetworkObject CarrierNetwork;
            public Action<KnockdownType> KnockdownHandler;

            /// <summary>Сколько секунд ручку ещё нельзя сорвать перерастяжением. См. <see cref="GrabGraceSeconds"/>.</summary>
            public float GraceTimer;

            /// <summary>
            /// Куда несущий просится идти, по его же словам. У своего берётся
            /// прямо из мотора, у чужого приезжает <c>CarrierIntentRpc</c>.
            /// </summary>
            public Vector2 Intent;

            /// <summary>
            /// Горизонтальная скорость несущего, посчитанная по его позиции, а
            /// не взятая из тела. У чужой копии тело ведёт <c>NetworkTransform</c>,
            /// и <c>linearVelocity</c> там пустой — ровно тот класс дыр, на
            /// котором «Рейс на память» отдал целую катку одному хосту.
            /// </summary>
            public Vector3 TrackedVelocity;

            public Vector3 LastPosition;
            public bool HasLastPosition;

            /// <summary>Сколько секунд несущий держится выше своего потолка скорости, прося при этом бежать.</summary>
            public float OverspeedTimer;

            /// <summary>
            /// Слот занят. Отдельным полем, а не проверкой <c>Carrier != null</c>:
            /// вышедший из матча уносит с собой свой объект, ссылка становится
            /// пустой — и слот, считай себя он свободным, молча освободился бы
            /// в обход единственной точки отцепления. Счётчик несущих при этом
            /// остался бы завышенным навсегда, а бутыль — висящей в руках у
            /// призрака.
            /// </summary>
            public bool Taken;

            public bool Occupied => Taken;

            /// <summary>Слот занят, и несущий на нём ещё существует.</summary>
            public bool Alive => Taken && Carrier != null;

            /// <summary>
            /// Ведёт ли несущего эта машина. Вне сети — всегда: мотор здесь же.
            /// В сети — только у владельца: сервер чужого игрока не двигает.
            /// </summary>
            public bool LocallyOwned =>
                CarrierNetwork == null || !CarrierNetwork.IsSpawned || CarrierNetwork.IsOwner;
        }

        /// <summary>
        /// Сколько секунд после захвата ручку не срывает перерастяжением, с.
        ///
        /// Взяться можно с вытянутой руки — радиус взаимодействия больше
        /// предела связи, — и без выдержки такой захват рвался бы в тот же
        /// кадр, в котором состоялся. Выдержка не поблажка: как только за
        /// ручку взялись, натяжение тянет объект к несущему, и разрыв
        /// закрывается сам за доли секунды.
        /// </summary>
        private const float GrabGraceSeconds = 1f;

        /// <summary>
        /// Во сколько раз несущему прощается превышение своего потолка
        /// скорости. Запас нужен честному игроку: спуск, доска под уклон и
        /// интерполяция чужой позиции дают короткие всплески выше потолка,
        /// а сорванная за них ручка читалась бы как случайный баг.
        /// </summary>
        private const float SpeedCapTolerance = 1.35f;

        /// <summary>
        /// Сколько секунд превышение терпится, прежде чем ручку снимут, с.
        /// Всплеск в пару кадров не наказывается, а снятый потолок держится
        /// столько, сколько игрок бежит.
        /// </summary>
        private const float OverspeedGraceSeconds = 0.75f;

        /// <summary>
        /// С какой длины вектор ввода считается «просит бежать». Ниже —
        /// персонажа несёт чужая сила, и превышение потолка не его вина.
        /// </summary>
        private const float ActiveIntent = 0.3f;

        /// <summary>
        /// На сколько должен измениться вектор ввода, чтобы уйти на сервер.
        /// Ввод шлётся по изменению, а не каждый кадр, — как ось лифта
        /// Охотника (<c>MoveElevatorServerRpc</c>).
        /// </summary>
        private const float IntentEpsilon = 0.1f;

        private readonly Handle[] handles = new Handle[MaxHandles];

        /// <summary>Занятые ручки и полёт. Пишет сервер, читают все.</summary>
        private readonly NetworkVariable<MultiCarryNetState> netState =
            new NetworkVariable<MultiCarryNetState>();

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
        private bool inFlight;

        /// <summary>
        /// Состояние приехало, а персонажа по идентификатору ещё нет. Так бывает
        /// у подключившегося в середине раунда: бутыль спавнится раньше, чем
        /// доедут тела. Разбираем повторно, пока не сойдётся.
        /// </summary>
        private bool handlesPending;

        /// <summary>Последний отправленный серверу вектор ввода и был ли он вообще отправлен.</summary>
        private Vector2 sentIntent;
        private bool intentSent;

        public int HandleCount => handleCount;
        public int CarrierCount { get; private set; }
        public bool IsCarried => CarrierCount > 0;

        /// <summary>
        /// Текущий наклон от вертикали, °.
        ///
        /// У авторитета — из самой модели. У остальных считается по
        /// реплицированному повороту: наклон и так приезжает
        /// <c>NetworkTransform</c>, и отдельный канал под то же число был бы
        /// вторым источником правды.
        /// </summary>
        public float TiltAngle => HasAuthority
            ? tiltRotation.magnitude * Mathf.Rad2Deg
            : Vector3.Angle(transform.up, Vector3.up);

        /// <summary>Наклон за порогом прямо сейчас.</summary>
        public bool BeyondTiltThreshold => beyondThreshold;

        /// <summary>Объект брошен и ещё не коснулся земли. На свистке такой не засчитывается.</summary>
        public bool InFlight => inFlight;

        public MultiCarrySettings Settings => settings;

        public string InteractionPrompt => interactionPrompt;

        /// <summary>
        /// Вправе ли эта машина решать судьбу объекта. Вне сетевой сессии — да,
        /// иначе сцена, открытая напрямую из редактора, не играла бы вовсе.
        /// </summary>
        private bool HasAuthority => IsSpawned ? IsServer : WorldAuthority.HasAuthority;

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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged += OnNetStateChanged;

            // Своя физика есть только у того, кто считает движение. У остальных
            // объект ведёт NetworkTransform, и вторые руки дают дрожь — та же
            // ловушка, что решена в PickupItem выключением физики у неавторитета.
            body.isKinematic = !IsServer;

            // Состояние могло приехать до подписки — разбираем то, что уже есть.
            ApplyNetState(netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
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

        /// <summary>
        /// Где должен стоять несущий на этом слоте — точка, к которой его тянет
        /// связь. Дальше ручки на своё же тело: в саму ручку несущий встать не
        /// может, там объект.
        ///
        /// Нужна снаружи всем, кто ведёт кого-то к объекту: болванке соло-теста,
        /// подсказке интерфейса, замеру натяжения на приёмке.
        /// </summary>
        public Vector3 StationOf(int slot)
        {
            Vector3 origin = BasePosition;
            if (slot < 0 || slot >= handleCount)
            {
                return origin;
            }

            return origin + HandleDirection(slot) * (settings.handleRadius + settings.carrierStandoff);
        }

        /// <summary>Натяжение связи на этом слоте, м. Ноль — слот свободен или несущий стоит на месте.</summary>
        public float StretchOf(int slot)
        {
            PlayerController carrier = CarrierAt(slot);
            if (carrier == null)
            {
                return 0f;
            }

            Vector3 offset = carrier.transform.position - StationOf(slot);
            offset.y = 0f;
            return offset.magnitude;
        }

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
        /// Занять свободную ручку. <b>Единственная точка входа на захват</b>, и
        /// решает её только авторитет: иначе двое возьмутся за одну ручку.
        ///
        /// Сам захват здесь не применяется — здесь публикуется состояние, а
        /// применяют его все машины разом в <see cref="ApplyNetState"/>.
        /// </summary>
        public bool TryGrab(PlayerController player)
        {
            if (!HasAuthority || player == null || InFlight)
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

            int slot = FindNearestFreeSlot(player.transform.position);
            if (slot < 0)
            {
                return false;
            }

            if (!IsSpawned)
            {
                AttachHandle(slot, player);
                return true;
            }

            NetworkObject carrierObject = player.GetComponent<NetworkObject>();
            if (carrierObject == null || !carrierObject.IsSpawned)
            {
                Debug.LogWarning($"{name}: у несущего нет заспавненного NetworkObject — ручка не выдана", this);
                return false;
            }

            MultiCarryNetState next = netState.Value;
            next.Set(slot, carrierObject.NetworkObjectId);
            netState.Value = next;
            return true;
        }

        /// <summary>
        /// <b>Единственная точка отцепления.</b> Сюда приходят все три причины
        /// срыва из спеки 9.2 — нажатие E, перерастяжение, сбитый несущий — плюс
        /// бросок и конец раунда. Решает авторитет; клиент только просит
        /// (см. <see cref="RequestReleaseRpc"/>).
        /// </summary>
        public bool ReleaseHandle(int slot, CarryReleaseReason reason)
        {
            if (slot < 0 || slot >= handles.Length || !HasAuthority)
            {
                return false;
            }

            if (!handles[slot].Occupied)
            {
                return false;
            }

            if (!IsSpawned)
            {
                DetachHandle(slot, reason);
                return true;
            }

            MultiCarryNetState next = netState.Value;
            next.Set(slot, MultiCarryNetState.NoCarrier);
            next.LastRelease = (byte)reason;
            netState.Value = next;
            return true;
        }

        /// <summary>Отцепить конкретного игрока, где бы он ни держал.</summary>
        public bool ReleaseFor(PlayerController player, CarryReleaseReason reason)
        {
            int slot = FindSlotOf(player);
            return slot >= 0 && ReleaseHandle(slot, reason);
        }

        /// <summary>
        /// Отцепить всех. Конец раунда обязан звать это сам — иначе роль уедет
        /// в хаб вместе с телом (спека 10.2).
        ///
        /// Локальные последствия снимаются на <b>каждой</b> машине, не дожидаясь
        /// пакета: потолок скорости и запрет удара живут в моторе владельца, и
        /// отдать их обратно обязана его же машина.
        /// </summary>
        public void ReleaseAll(CarryReleaseReason reason)
        {
            if (IsSpawned && IsServer)
            {
                MultiCarryNetState next = netState.Value;
                bool changed = false;

                for (int i = 0; i < handles.Length; i++)
                {
                    if (next.Of(i) == MultiCarryNetState.NoCarrier)
                    {
                        continue;
                    }

                    next.Set(i, MultiCarryNetState.NoCarrier);
                    changed = true;
                }

                if (changed)
                {
                    next.LastRelease = (byte)reason;
                    netState.Value = next;
                }
            }

            for (int i = 0; i < handles.Length; i++)
            {
                DetachHandle(i, reason);
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

            ApplyInFlight(false);

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
        ///
        /// Нажатие приходит из мотора владельца, а бросок — исход раунда,
        /// поэтому у клиента отсюда уходит намерение, а бросает сервер.
        /// </summary>
        public bool HandlePushButton(PlayerController player)
        {
            if (!IsCarriedBy(player))
            {
                return false;
            }

            if (HasAuthority)
            {
                ThrowByCarriers();
                return true;
            }

            RequestThrowRpc();
            return true;
        }

        /// <summary>
        /// Намерение бросить от машины несущего. Отправителя берём из
        /// <c>RpcParams</c>, а не из аргумента: иначе чужим намерением можно
        /// было бы выбить бутыль из рук соперника.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestThrowRpc(RpcParams rpcParams = default)
        {
            if (SlotOfSender(rpcParams.Receive.SenderClientId) < 0)
            {
                return;
            }

            ThrowByCarriers();
        }

        /// <summary>
        /// Намерение отпустить ручку от машины несущего. Нужно ровно одному
        /// случаю — <b>сбитому несущему</b>: нокдаун считает мотор владельца,
        /// у сервера чужой мотор выключен, и без этого сообщения ручка
        /// осталась бы в руках у лежащего. Нажатие E сюда не попадает: оно и
        /// так идёт через серверное взаимодействие.
        ///
        /// Подделать нечего: клиент вправе отпустить только свою ручку, а это
        /// он и так может нажатием.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestReleaseRpc(byte reason, RpcParams rpcParams = default)
        {
            int slot = SlotOfSender(rpcParams.Receive.SenderClientId);
            if (slot < 0)
            {
                return;
            }

            ReleaseHandle(slot, (CarryReleaseReason)reason);
        }

        /// <summary>Какую ручку держит игрок этого клиента. −1 — ни одной.</summary>
        private int SlotOfSender(ulong senderClientId)
        {
            for (int i = 0; i < handleCount; i++)
            {
                NetworkObject carrier = handles[i].CarrierNetwork;
                if (carrier != null && carrier.OwnerClientId == senderClientId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Совместный бросок. Направление — среднее из взглядов несущих,
        /// импульс — на каждого бросающего свой. Решает только авторитет.
        /// </summary>
        public void ThrowByCarriers()
        {
            if (!HasAuthority)
            {
                return;
            }

            int throwers = CarrierCount;
            if (throwers == 0)
            {
                return;
            }

            Vector3 aim = Vector3.zero;
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Alive)
                {
                    aim += handles[i].Carrier.Facing;
                }
            }

            aim.y = 0f;
            aim = aim.sqrMagnitude < 0.0001f ? transform.forward : aim.normalized;

            ReleaseAll(CarryReleaseReason.Thrown);

            PublishInFlight(true);
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.AddForce((aim + Vector3.up * settings.throwUpward).normalized *
                          (settings.throwImpulsePerCarrier * throwers), ForceMode.Impulse);

            Thrown?.Invoke(throwers);
        }

        // ========== СЕТЕВОЕ СОСТОЯНИЕ ==========

        private void OnNetStateChanged(MultiCarryNetState previous, MultiCarryNetState current) =>
            ApplyNetState(current);

        /// <summary>
        /// Разложить приехавшее состояние по ручкам. Зовётся на каждой машине,
        /// включая сервер: решение и его последствия разведены намеренно — иначе
        /// у хоста и у клиента были бы две разные ветки применения, и сходились
        /// бы они только на бумаге.
        /// </summary>
        private void ApplyNetState(in MultiCarryNetState state)
        {
            handlesPending = false;
            CarryReleaseReason reason = (CarryReleaseReason)state.LastRelease;

            for (int slot = 0; slot < handles.Length; slot++)
            {
                ulong wanted = slot < handleCount ? state.Of(slot) : MultiCarryNetState.NoCarrier;
                Handle handle = handles[slot];

                if (wanted == MultiCarryNetState.NoCarrier)
                {
                    DetachHandle(slot, reason);
                    continue;
                }

                if (handle.Occupied && handle.CarrierNetwork != null &&
                    handle.CarrierNetwork.NetworkObjectId == wanted)
                {
                    continue;
                }

                PlayerController carrier = ResolveCarrier(wanted);
                if (carrier == null)
                {
                    // Подключились в середине раунда: бутыль уже здесь, а тела
                    // несущих ещё едут. Разберём в следующем кадре.
                    handlesPending = true;
                    continue;
                }

                DetachHandle(slot, reason);
                AttachHandle(slot, carrier);
            }

            ApplyInFlight(state.InFlight);
        }

        private void PublishInFlight(bool value)
        {
            ApplyInFlight(value);

            if (!IsSpawned || !IsServer)
            {
                return;
            }

            MultiCarryNetState next = netState.Value;
            if (next.InFlight == value)
            {
                return;
            }

            next.InFlight = value;
            netState.Value = next;
        }

        private void ApplyInFlight(bool value)
        {
            if (inFlight == value)
            {
                return;
            }

            bool wasFlying = inFlight;
            inFlight = value;

            if (wasFlying)
            {
                Landed?.Invoke();
            }
        }

        private static PlayerController ResolveCarrier(ulong carrierObjectId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null)
            {
                return null;
            }

            return manager.SpawnManager.SpawnedObjects.TryGetValue(carrierObjectId, out NetworkObject carrierObject)
                ? carrierObject.GetComponent<PlayerController>()
                : null;
        }

        // ========== ПРИВЯЗКА НЕСУЩЕГО: ПРИМЕНЯЕТ КАЖДАЯ МАШИНА ==========

        /// <summary>
        /// Повесить последствия захвата на этой машине. Ничего не решает —
        /// решение уже принято авторитетом и приехало состоянием.
        /// </summary>
        private void AttachHandle(int slot, PlayerController player)
        {
            Handle handle = handles[slot];

            handle.Taken = true;
            handle.GraceTimer = GrabGraceSeconds;
            handle.OverspeedTimer = 0f;
            handle.Intent = Vector2.zero;
            handle.HasLastPosition = false;
            handle.TrackedVelocity = Vector3.zero;
            handle.Carrier = player;
            handle.CarrierBody = player.GetComponent<Rigidbody>();
            handle.CarrierCollider = player.GetComponent<CapsuleCollider>();
            handle.CarrierCarry = player.GetComponent<PlayerCarryAbility>();
            handle.CarrierPush = player.GetComponent<PlayerPushAbility>();
            handle.CarrierNetwork = player.GetComponent<NetworkObject>();

            // Сбитый несущий роняет ручку. Подписка на слот своя, чтобы снять
            // её потом ровно той же ссылкой.
            handle.KnockdownHandler = _ => OnCarrierKnockedDown(slot);
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
            ApplyInFlight(false);

            HandleTaken?.Invoke(slot, player);
            CarrierCountChanged?.Invoke(CarrierCount);
        }

        /// <summary>Снять последствия захвата на этой машине.</summary>
        private void DetachHandle(int slot, CarryReleaseReason reason)
        {
            Handle handle = handles[slot];
            if (!handle.Occupied)
            {
                return;
            }

            PlayerController carrier = handle.Carrier;

            // Несущего может уже не быть: вышедший из матча уносит с собой свой
            // объект. Слот при этом всё равно обязан освободиться — иначе
            // бутыль останется в руках у призрака, а ручка не достанется никому.
            if (carrier != null)
            {
                carrier.KnockdownStarted -= handle.KnockdownHandler;
                carrier.ClearSpeedCap(this);
            }

            handle.KnockdownHandler = null;
            SetCollisionsWithCarrier(handle, false);

            if (handle.CarrierCarry != null)
            {
                handle.CarrierCarry.HandsBlocked = false;
            }

            if (handle.CarrierPush != null && ReferenceEquals(handle.CarrierPush.ButtonOverride, this))
            {
                handle.CarrierPush.ButtonOverride = null;
            }

            handle.Taken = false;
            handle.Carrier = null;
            handle.CarrierBody = null;
            handle.CarrierCollider = null;
            handle.CarrierCarry = null;
            handle.CarrierPush = null;
            handle.CarrierNetwork = null;
            handle.HasLastPosition = false;
            handle.TrackedVelocity = Vector3.zero;
            handle.Intent = Vector2.zero;
            handle.OverspeedTimer = 0f;

            CarrierCount--;
            HandleReleased?.Invoke(slot, carrier, reason);
            CarrierCountChanged?.Invoke(CarrierCount);

            if (CarrierCount == 0)
            {
                OnLastHandleReleased(reason);
            }
        }

        /// <summary>
        /// Несущего сбили. Нокдаун считает мотор владельца, а у сервера чужой
        /// мотор выключен: поэтому у авторитета здесь решение, а у владельца —
        /// намерение.
        /// </summary>
        private void OnCarrierKnockedDown(int slot)
        {
            if (HasAuthority)
            {
                ReleaseHandle(slot, CarryReleaseReason.Knockdown);
                return;
            }

            if (IsSpawned && handles[slot].LocallyOwned)
            {
                RequestReleaseRpc((byte)CarryReleaseReason.Knockdown);
            }
        }

        // ========== МОДЕЛЬ ==========

        /// <summary>
        /// Основание объекта в мире. У авторитета — из физики, у остальных из
        /// трансформа: там тело кинематическое и его ведёт
        /// <c>NetworkTransform</c>, а <c>Rigidbody.position</c> догоняет
        /// трансформ только к ближайшему шагу физики.
        /// </summary>
        private Vector3 BasePosition =>
            HasAuthority && body != null ? body.position : transform.position;

        /// <summary>
        /// Шаг переноски разложен на три части, и разложен не по вкусу, а по
        /// тому, кто чем вправе распоряжаться:
        ///
        /// 1. <b>Скорости несущих</b> считает каждая машина по позициям — они
        ///    нужны и серверу (проверки), и владельцу (тяга);
        /// 2. <b>объект</b> — сумму натяжений, потолок и наклон — считает
        ///    только сервер, остальные видят результат <c>NetworkTransform</c>;
        /// 3. <b>упругую тягу к своей стоянке</b> применяет машина владельца
        ///    несущего: сервер чужого игрока двигать не вправе (IGR-297), и
        ///    попытка дала бы рывок с откатом. Тот же приём, что у
        ///    <c>RidePlatform</c>, — «пассажира везёт его собственная машина».
        /// </summary>
        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            DropGoneCarriers();
            TrackCarrierMotion(dt);

            if (HasAuthority)
            {
                if (CarrierCount > 0)
                {
                    StepCarried(dt);
                }

                StepTiltRelaxation(dt);
                ApplyRotation();
            }

            StepOwnedTethers();
            ReportOwnIntent();
        }

        /// <summary>
        /// Несущий вышел из матча. Проверяется каждый такт, как носитель в
        /// <see cref="PickupItem"/>: дисконнект не спрашивает разрешения и не
        /// проходит через отцепление, поэтому слот освобождает страховка.
        ///
        /// Решает авторитет: у него же снимается и сама ручка, а состояние
        /// разъедется остальным обычным путём.
        /// </summary>
        private void DropGoneCarriers()
        {
            if (!HasAuthority)
            {
                return;
            }

            for (int i = 0; i < handles.Length; i++)
            {
                Handle handle = handles[i];
                if (!handle.Taken || handle.Alive)
                {
                    continue;
                }

                ReleaseHandle(i, CarryReleaseReason.RoundEnded);
            }
        }

        /// <summary>
        /// Скорость каждого несущего по его же позиции.
        ///
        /// Не из <c>Rigidbody.linearVelocity</c> намеренно: у чужой копии
        /// персонажа тело ведёт <c>NetworkTransform</c>, скорость в нём пустая,
        /// и всё, что на неё опирается — тяга и таран, — на сервере молча
        /// перестало бы работать. Ровно этот класс дыр на одной машине не
        /// воспроизводится никогда.
        /// </summary>
        private void TrackCarrierMotion(float dt)
        {
            if (dt <= Mathf.Epsilon)
            {
                return;
            }

            for (int i = 0; i < handles.Length; i++)
            {
                Handle handle = handles[i];
                if (!handle.Alive)
                {
                    handle.HasLastPosition = false;
                    handle.TrackedVelocity = Vector3.zero;
                    continue;
                }

                Vector3 position = handle.Carrier.transform.position;
                if (!handle.HasLastPosition)
                {
                    handle.LastPosition = position;
                    handle.HasLastPosition = true;
                    handle.TrackedVelocity = Vector3.zero;
                    continue;
                }

                Vector3 step = position - handle.LastPosition;
                step.y = 0f;
                handle.LastPosition = position;
                handle.TrackedVelocity = step / dt;
            }
        }

        /// <summary>
        /// Горизонтальная скорость несущего на этом слоте, м/с. Считана по
        /// позиции, поэтому одинаково верна и у владельца, и у сервера.
        /// </summary>
        public Vector3 CarrierVelocityAt(int slot) =>
            slot >= 0 && slot < handles.Length ? handles[slot].TrackedVelocity : Vector3.zero;

        /// <summary>
        /// Упругая тяга к своей стоянке — только за своих несущих. Стоянка
        /// берётся от реплицированной позиции объекта, поэтому у владельца она
        /// та же самая, что у сервера, с точностью до задержки.
        /// </summary>
        private void StepOwnedTethers()
        {
            for (int i = 0; i < handleCount; i++)
            {
                Handle handle = handles[i];
                if (!handle.Alive || !handle.LocallyOwned || handle.CarrierBody == null)
                {
                    continue;
                }

                Vector3 stretch = handle.Carrier.transform.position - StationOf(i);
                stretch.y = 0f;

                float distance = stretch.magnitude;
                if (distance <= settings.tensionDeadzone)
                {
                    continue;
                }

                HoldCarrier(handle, stretch / distance, distance);
            }
        }

        // ========== ВВОД НЕСУЩЕГО ==========

        /// <summary>
        /// Отправить серверу свой вектор ввода — по изменению, а не каждый
        /// кадр. Прецедент тот же, что у лифта Охотника: ось едет событием,
        /// а не потоком.
        ///
        /// Серверу он нужен ровно для одного — <b>отличить рывок от полёта</b>.
        /// Скорость несущего сервер и так видит по позиции, но позиция не
        /// говорит, бежит человек сам или его несёт ловушка. Двигать бутыль по
        /// присланному вектору сервер не станет: тянет её натяжение связи, и
        /// числа приёмки каркаса выведены именно из него.
        /// </summary>
        private void ReportOwnIntent()
        {
            // Хосту слать себе нечего: он читает намерение своего несущего
            // прямо из мотора.
            if (!IsSpawned || IsServer)
            {
                return;
            }

            int slot = FindOwnedSlot();
            if (slot < 0)
            {
                intentSent = false;
                sentIntent = Vector2.zero;
                return;
            }

            Vector3 intent = handles[slot].Carrier.MoveIntent;
            Vector2 flat = new Vector2(intent.x, intent.z);

            if (intentSent && (flat - sentIntent).sqrMagnitude < IntentEpsilon * IntentEpsilon)
            {
                return;
            }

            sentIntent = flat;
            intentSent = true;
            CarrierIntentRpc(flat);
        }

        /// <summary>
        /// Ручка, которую держит персонаж этой машины. −1 — ни одной. Больше
        /// одной быть не может: тот же игрок за вторую ручку не возьмётся.
        /// </summary>
        private int FindOwnedSlot()
        {
            for (int i = 0; i < handleCount; i++)
            {
                Handle handle = handles[i];
                if (handle.Alive && handle.LocallyOwned)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Вектор ввода от машины несущего. Отправителя берём из
        /// <c>RpcParams</c>, длину обрезаем единицей: прислать «бегу втрое
        /// быстрее» нельзя, а прислать за чужого — некуда.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void CarrierIntentRpc(Vector2 intent, RpcParams rpcParams = default)
        {
            int slot = SlotOfSender(rpcParams.Receive.SenderClientId);
            if (slot < 0)
            {
                return;
            }

            handles[slot].Intent = Vector2.ClampMagnitude(intent, 1f);
        }

        /// <summary>Куда просится несущий: у своего — прямо из мотора, у чужого — из присланного.</summary>
        private Vector2 IntentOf(Handle handle)
        {
            if (!handle.LocallyOwned)
            {
                return handle.Intent;
            }

            Vector3 intent = handle.Carrier.MoveIntent;
            return new Vector2(intent.x, intent.z);
        }

        /// <summary>
        /// Держится ли несущий в своём потолке скорости. Проверка серверная:
        /// потолок ставится на <c>PlayerController</c>, то есть в моторе
        /// владельца, а мотор клиент волен и подменить.
        ///
        /// Быстрее потолка бывает и честно — толчок, пружина, струя из трубы, —
        /// поэтому одной скорости мало: наказываем только того, кто <b>сам
        /// просится</b> бежать быстрее, и только если держится так дольше
        /// выдержки. Наказание — сорванная ручка: бежать быстрее команды
        /// нельзя, а бутыль от этого всё равно быстрее не поедет.
        /// </summary>
        private bool HoldsSpeedCap(Handle handle, int slot, float dt)
        {
            float cap = settings.carrierSpeedCap * SpeedCapTolerance;
            if (handle.TrackedVelocity.sqrMagnitude <= cap * cap ||
                IntentOf(handle).sqrMagnitude < ActiveIntent * ActiveIntent)
            {
                handle.OverspeedTimer = 0f;
                return true;
            }

            handle.OverspeedTimer += dt;
            if (handle.OverspeedTimer < OverspeedGraceSeconds)
            {
                return true;
            }

            Debug.LogWarning($"{name}: несущий {handle.Carrier.name} держит " +
                             $"{handle.TrackedVelocity.magnitude:F1} м/с при потолке " +
                             $"{settings.carrierSpeedCap:F1} — ручка снята", this);

            ReleaseHandle(slot, CarryReleaseReason.Overstretched);
            return false;
        }

        private void LateUpdate()
        {
            // Порог наклона читают все: подсветка и звук бутыли живут на каждой
            // машине, а не только у того, кто считает модель.
            bool beyond = TiltAngle >= settings.tiltThreshold;
            if (beyond != beyondThreshold)
            {
                beyondThreshold = beyond;
                TiltThresholdCrossed?.Invoke(beyond);
            }

            if (handlesPending)
            {
                ApplyNetState(netState.Value);
            }
        }

        /// <summary>
        /// Один шаг переноски у авторитета: натяжения → скорость и момент,
        /// срыв перерастянутых ручек, проверка потолка скорости несущих.
        ///
        /// Обратной тяги здесь <b>нет</b>: её применяет машина владельца в
        /// <see cref="StepOwnedTethers"/>. Всё остальное осталось серверным
        /// ровно в том виде, в каком считалось в соло-каркасе, — числа приёмки
        /// выведены отсюда и меняться не должны.
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
                if (!handle.Alive)
                {
                    continue;
                }

                Vector3 handleDir = HandleDirection(i);
                Vector3 station = basePoint + handleDir * (settings.handleRadius + settings.carrierStandoff);
                Vector3 carrierPosition = handle.Carrier.transform.position;

                Vector3 stretch = carrierPosition - station;
                stretch.y = 0f;
                float distance = stretch.magnitude;

                handle.GraceTimer = Mathf.Max(0f, handle.GraceTimer - dt);

                // Перерастянутая ручка срывается до того, как её посчитают:
                // сорванная рука ни тянет, ни держит. Только что взявшемуся
                // дают выдержку — за неё натяжение подтягивает объект к нему.
                if (distance > settings.breakDistance && handle.GraceTimer <= 0f)
                {
                    ReleaseHandle(i, CarryReleaseReason.Overstretched);
                    continue;
                }

                // Потолок скорости несущего стоит в его моторе, а мотор живёт
                // у клиента. Проверяет его сервер — и снимает ручку тому, кто
                // потолок обошёл.
                if (!HoldsSpeedCap(handle, i, dt))
                {
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
        ///
        /// <b>Применяет машина владельца</b>, а не сервер: здесь правится
        /// скорость самого персонажа, а чужого персонажа сервер двигать не
        /// вправе — его позицией распоряжается владелец (IGR-297), и серверная
        /// правка была бы перетёрта следующим же пакетом. Позиция объекта, от
        /// которой считается стоянка, у владельца реплицированная — тот же
        /// приём, что у <c>RidePlatform</c>.
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
            // Приземление снимает полёт, а полёт — состояние: решает авторитет,
            // остальные узнают из него же.
            if (!InFlight || !HasAuthority)
            {
                return;
            }

            if ((groundLayers.value & (1 << collision.gameObject.layer)) == 0)
            {
                return;
            }

            PublishInFlight(false);
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

        /// <summary>
        /// Свободная ручка, ближайшая к подошедшему.
        ///
        /// Раздавать слоты по порядку нельзя: ручки расставлены по окружности,
        /// и подошедшему с одной стороны доставалась бы ручка с другой — то
        /// есть стоянка за спиной у объекта. Связь до неё сразу длиннее
        /// предела, и захват рвался бы в тот же кадр, в котором состоялся.
        /// </summary>
        private int FindNearestFreeSlot(Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < handleCount; i++)
            {
                if (handles[i].Occupied)
                {
                    continue;
                }

                Vector3 station = BasePosition +
                                  HandleDirection(i) * (settings.handleRadius + settings.carrierStandoff);
                station.y = position.y;

                float sqr = (station - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
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
