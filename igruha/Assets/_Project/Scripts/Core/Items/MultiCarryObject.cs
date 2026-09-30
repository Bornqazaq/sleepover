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
    /// Крен катящегося объекта для остальных машин: вектор «ось × угол» в
    /// градусах, по байту на горизонтальную ось. Тело физики у такого объекта
    /// стоит вертикально — крен показывает отдельный узел, — поэтому из
    /// поворота <c>NetworkTransform</c> его не прочитать, и он едет своим
    /// каналом: два байта, и только когда меняется целый градус.
    /// </summary>
    public struct MultiCarryTiltNetState : INetworkSerializable, IEquatable<MultiCarryTiltNetState>
    {
        public sbyte X;
        public sbyte Z;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Z);
        }

        public bool Equals(MultiCarryTiltNetState other) => X == other.X && Z == other.Z;
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
    /// <b>Катящийся груз</b> (тележка «Переноски» v2) в пунктах 3 и 4 отличается:
    /// скорость набирается и теряется с инерцией, а опора у него — колёса, а
    /// не руки. Ровная тяга и незанятые поручни такой объект не кренят вовсе;
    /// кренит его <b>рывок кузова</b> — изменение скорости за шаг: разгон,
    /// стена, толчок, ловушка, резкий поворот (<see cref="StepRollingTilt"/>).
    /// Тело физики при этом стоит вертикально, а крен показывает узел
    /// <c>tiltPivot</c>: наклонённый коллайдер зарывался углом в пол, и
    /// физика гасила ход трением — тележка вставала намертво.
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
    public sealed class MultiCarryObject : NetworkBehaviour, IHoldInteractable, IPushButtonOverride
    {
        /// <summary>Больше четырёх рук не бывает: столько человек в самой большой команде проекта.</summary>
        public const int MaxHandles = 4;

        [SerializeField] private MultiCarrySettings settings = MultiCarrySettings.Default;
        [Tooltip("Действие без клавиши: её добавляет InteractionPromptText")]
        [SerializeField] private string interactionPrompt = "взяться за бутыль";
        [Tooltip("Слои опоры. По ним объект понимает, что приземлился после броска")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [Tooltip("Что кренится у катящегося объекта. Тело физики остаётся вертикальным — коллайдер не зарывается углом в пол, — а крен показывает этот узел. Пусто — кренится всё тело, как у несомого груза")]
        [SerializeField] private Transform tiltPivot;

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
            public PlayerInteractor CarrierInteractor;
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

        /// <summary>Крен объекта с отдельным узлом крена. Пишет сервер, читают все.</summary>
        private readonly NetworkVariable<MultiCarryTiltNetState> netTilt =
            new NetworkVariable<MultiCarryTiltNetState>();

        private Rigidbody body;
        private Collider[] ownColliders;
        private Predicate<PlayerController> ownerFilter;

        private int handleCount = 1;

        /// <summary>Наклон как вектор «ось × угол», радианы. Ось всегда горизонтальна.</summary>
        private Vector3 tiltRotation;
        private Vector3 tiltAngularVelocity;

        private Quaternion baseRotation = Quaternion.identity;

        /// <summary>
        /// Курс кузова у авторитета в раскладке тележки, °. Остальные машины
        /// берут курс из реплицированного поворота — так стоянки у сервера и у
        /// владельца считаются от одного и того же числа.
        /// </summary>
        private float yawDegrees;

        /// <summary>Нагрузка 0…1, пустой → полный. Ставит игра; в режиме качения от неё зависят потолок и разгон.</summary>
        private float load;

        /// <summary>Крен, который держит игра, — «ось × угол» в радианах. Слив назад в бак; ноль — вертикаль.</summary>
        private Vector3 tiltTarget;

        /// <summary>
        /// Горизонтальная скорость, выставленная качению на прошлом шаге.
        /// Разница с текущей — то, что сделали с кузовом стены, толчки и
        /// ловушки между шагами: для крена это такой же рывок, как разгон.
        /// </summary>
        private Vector3 lastRollingVelocity;

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
        /// вторым источником правды. Исключение — объект с узлом крена: его
        /// тело вертикально, и крен у остальных догоняет свой канал
        /// (<see cref="FollowReplicatedTilt"/>) в том же <c>tiltRotation</c>.
        /// </summary>
        public float TiltAngle => HasAuthority || tiltPivot != null
            ? tiltRotation.magnitude * Mathf.Rad2Deg
            : Vector3.Angle(transform.up, Vector3.up);

        /// <summary>Наклон за порогом прямо сейчас.</summary>
        public bool BeyondTiltThreshold => beyondThreshold;

        /// <summary>Объект брошен и ещё не коснулся земли. На свистке такой не засчитывается.</summary>
        public bool InFlight => inFlight;

        public MultiCarrySettings Settings => settings;

        /// <summary>Катящийся объект: гравитация не выключается, скорость набирается и теряется с инерцией.</summary>
        public bool IsRolling => settings.motion == MultiCarryMotion.Rolling;

        /// <summary>Нагрузка 0…1. Читают показ и болванки.</summary>
        public float Load => load;

        /// <summary>
        /// Захват запрещён: объект вне игры — например, упал в пропасть и ждёт
        /// возврата. Ставит игра, снимает она же.
        /// </summary>
        public bool GrabLocked { get; set; }

        /// <summary>Сколько в объекте груза, 0…1. В режиме качения полный разгоняется и едет медленнее пустого.</summary>
        public void SetLoad(float value) => load = Mathf.Clamp01(value);

        /// <summary>
        /// Держать крен назад на столько градусов: перед поднимается, зад
        /// опускается — так тележка сливается в бак. Крен идёт через ту же
        /// модель наклона, что и рассинхрон несущих, поэтому он мягкий и
        /// не переворачивает тело физикой. Считает только авторитет.
        /// </summary>
        public void SetLeanBack(float degrees)
        {
            Vector3 axis = Heading * Vector3.right;
            tiltTarget = axis * (-degrees * Mathf.Deg2Rad);
        }

        /// <summary>Отпустить крен: объект возвращается к вертикали.</summary>
        public void ClearLean() => tiltTarget = Vector3.zero;

        /// <summary>
        /// Поставить объект заново: позиция, курс, ни скорости, ни крена.
        /// Возврат тележки из пропасти. Решает авторитет; клиентам положение
        /// довозит <c>NetworkTransform</c> телепортом, без интерполяции через
        /// всю арену.
        /// </summary>
        public void ResetPose(Vector3 position, Quaternion rotation)
        {
            if (!HasAuthority || body == null)
            {
                return;
            }

            ReleaseAll(CarryReleaseReason.RoundEnded);

            tiltRotation = Vector3.zero;
            tiltAngularVelocity = Vector3.zero;
            tiltTarget = Vector3.zero;
            lastRollingVelocity = Vector3.zero;
            yawDegrees = rotation.eulerAngles.y;
            baseRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.SetPositionAndRotation(position, body.rotation);

            if (tiltPivot != null)
            {
                tiltPivot.localRotation = Quaternion.identity;
                PublishTilt();
            }

            if (IsSpawned && TryGetComponent(out Unity.Netcode.Components.NetworkTransform networkTransform))
            {
                networkTransform.Teleport(position, body.rotation, transform.localScale);
            }
        }

        [System.NonSerialized] private string cachedPrompt;
        public string InteractionPrompt => cachedPrompt ??= "E — " + interactionPrompt;

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
            yawDegrees = transform.eulerAngles.y;

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

            // Ручек стало меньше — живые несущие со слотов, которых больше нет,
            // переезжают на свободные младшие слоты, а не срываются. Раньше
            // слоты освобождались с конца, и уход одного игрока сбрасывал ручку
            // другому: на одноразовой бутыли это терпели, у постоянной тележки
            // это читалось бы как «меня скинуло, когда вышел сосед».
            if (clamped < handleCount)
            {
                CompactHandles(clamped);
            }

            bool grew = clamped > handleCount;
            handleCount = clamped;

            // Ручек стало больше — разбираем состояние заново: занятый слот мог
            // не влезать в прежний предел и ждать ровно этого.
            if (grew && IsSpawned)
            {
                handlesPending = true;
            }
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

            Vector3 grip = HandleLocal(slot);
            grip.y = 0f;
            return origin + Heading * (grip + OutwardLocal(slot) * settings.carrierStandoff);
        }

        /// <summary>
        /// Где сама ручка — точка на объекте, за которую держатся. Не путать
        /// со <see cref="StationOf"/>: там стоит несущий, на полкорпуса
        /// дальше, а здесь берётся рука. Наклон учтён — на кренящейся бутыли
        /// ручки едут вместе с ней.
        ///
        /// Нужна показу: маркеру свободной ручки и подсказке, куда вставать.
        /// </summary>
        public Vector3 HandleAnchor(int slot)
        {
            Vector3 origin = BasePosition;
            return slot >= 0 && slot < handleCount ? origin + HandleOffsetWorld(slot) : origin;
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
            if (player == null || InFlight || GrabLocked)
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

        // Listen to the physical E edge, bypassing the shared InputAction's Hold delay.
        // Releasing the key does nothing; the next press toggles the attachment.
        public void HoldChanged(PlayerController player, bool held)
        {
            if (!held || player == null) return;
            if (HasAuthority)
            {
                if (player.TryGetComponent<PlayerInteractor>(out var interactor))
                    interactor.ExecuteInteraction(gameObject);
                return;
            }
            var actor = player.GetComponent<NetworkObject>();
            if (IsSpawned && actor != null && actor.IsSpawned && actor.IsOwner)
                RequestToggleRpc(actor.NetworkObjectId);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestToggleRpc(ulong actorId, RpcParams rpcParams = default)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(actorId, out var actor) ||
                actor.OwnerClientId != rpcParams.Receive.SenderClientId ||
                !actor.TryGetComponent<PlayerInteractor>(out var interactor)) return;
            interactor.ExecuteInteraction(gameObject);
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
            if (!HasAuthority || player == null || InFlight || GrabLocked)
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

            if (IsRolling)
            {
                // Тележку не швыряют, а толкают с разгона: импульс вдоль пола
                // поверх текущей скорости, полёта нет — она остаётся катящимся
                // объектом, и взяться за неё можно сразу, если догнал.
                body.useGravity = true;
                body.AddForce(aim * (settings.throwImpulsePerCarrier * throwers), ForceMode.Impulse);
                Thrown?.Invoke(throwers);
                return;
            }

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

            RelocateMovedHandles(state);

            for (int slot = 0; slot < handles.Length; slot++)
            {
                Handle handle = handles[slot];
                ulong wanted = state.Of(slot);

                // Число ручек приезжает своим каналом — на самом объекте, — и
                // может опоздать к состоянию ручек: тогда занятый слот выходит
                // за ещё не выросший предел. Не разбираем его, но и не забываем:
                // без отметки такая ручка потерялась бы навсегда, а несущий
                // остался бы у клиента стоять с пустыми руками.
                if (slot >= handleCount)
                {
                    if (wanted != MultiCarryNetState.NoCarrier)
                    {
                        handlesPending = true;
                    }

                    DetachHandle(slot, reason);
                    continue;
                }

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
            handle.CarrierInteractor = player.GetComponent<PlayerInteractor>();
            handle.CarrierNetwork = player.GetComponent<NetworkObject>();

            // Сбитый несущий роняет ручку. Подписка на слот своя, чтобы снять
            // её потом ровно той же ссылкой.
            // Замыкание держит саму ручку, а не номер слота: при уходе соседа
            // ручка может переехать на другой слот, и номер устарел бы.
            handle.KnockdownHandler = _ => OnCarrierKnockedDown(handle);
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

            if (handle.CarrierInteractor != null) handle.CarrierInteractor.ButtonOverride = this;

            player.ApplySpeedCap(this, settings.carrierSpeedCap);
            SetCollisionsWithCarrier(handle, true);

            CarrierCount++;
            hadCarriers = true;

            // Несомый объект держит модель, а не гравитация: иначе он волочится
            // по полу, а на доске над пропастью проваливается между несущими.
            // Катящийся стоит на колёсах и с гравитацией не расстаётся.
            body.useGravity = IsRolling;
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

            if (handle.CarrierInteractor != null && ReferenceEquals(handle.CarrierInteractor.ButtonOverride, this))
                handle.CarrierInteractor.ButtonOverride = null;

            handle.Taken = false;
            handle.Carrier = null;
            handle.CarrierBody = null;
            handle.CarrierCollider = null;
            handle.CarrierCarry = null;
            handle.CarrierPush = null;
            handle.CarrierInteractor = null;
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
        private void OnCarrierKnockedDown(Handle handle)
        {
            int slot = System.Array.IndexOf(handles, handle);
            if (slot < 0)
            {
                return;
            }

            if (HasAuthority)
            {
                ReleaseHandle(slot, CarryReleaseReason.Knockdown);
                return;
            }

            if (IsSpawned && handle.LocallyOwned)
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
                else if (IsRolling)
                {
                    StepFreeRolling(dt);
                }

                StepTiltRelaxation(dt);
                ApplyRotation();
                PublishTilt();
            }
            else if (tiltPivot != null)
            {
                FollowReplicatedTilt(dt);
                ApplyPivotTilt();
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

            for (int i = 0; i < handleCount; i++)
            {
                Handle handle = handles[i];
                if (!handle.Alive)
                {
                    continue;
                }

                Vector3 station = StationOf(i);
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
                supportSum += SupportPointWorld(i);

                if (distance > settings.tensionDeadzone)
                {
                    Vector3 direction = stretch / distance;
                    Vector3 tension = direction * (distance - settings.tensionDeadzone);

                    pullSum += tension;

                    // Плечо — от основания объекта до его ручки, вместе с высотой:
                    // именно высота ручки и превращает горизонтальную тягу в крен.
                    // Катящийся объект тяга не кренит: его держат колёса.
                    if (!IsRolling)
                    {
                        torqueSum += Vector3.Cross(HandleOffsetWorld(i), tension);
                    }
                }
            }

            if (occupied == 0)
            {
                return;
            }

            if (IsRolling)
            {
                StepRollingVelocity(pullSum, dt);
            }
            else
            {
                // Скорость: сумма натяжений с общим потолком. Тянущие вразнобой
                // гасят друг друга здесь же, векторно, — отдельного условия нет.
                Vector3 velocity = Vector3.ClampMagnitude(pullSum * settings.pullToSpeed, settings.maxObjectSpeed);

                // Высота: основание идёт над ступнями несущих. Так объект сам
                // поднимается на доску и не проваливается сквозь неё.
                float targetY = footSum / occupied + settings.carryClearance;
                float verticalRate = (targetY - body.position.y) / Mathf.Max(dt, Mathf.Epsilon);
                velocity.y = Mathf.Clamp(verticalRate, -settings.maxObjectSpeed * 2f, settings.maxObjectSpeed * 2f);

                body.linearVelocity = velocity;
            }

            if (IsRolling)
            {
                // Опора катящегося объекта — колёса, а не руки: незанятые
                // поручни его не валят. Крен качения считает StepRollingTilt.
                return;
            }

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
                                    (tiltRotation - tiltTarget) * settings.tiltRestoring) * dt;
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
            if (tiltPivot == null)
            {
                body.MoveRotation(Quaternion.AngleAxis(TiltDegrees(), TiltAxis()) * Heading);
                return;
            }

            // Тело стоит вертикально: наклонённый коллайдер зарывался бы углом
            // в пол, и физика гасила бы ход трением. Крен — на узле показа.
            body.MoveRotation(Heading);
            ApplyPivotTilt();
        }

        /// <summary>
        /// Крен на узле показа — в системе курса: узел дочерний, а курс телу
        /// выставляет физика на своём шаге, и мировой поворот узла отстал бы
        /// от него на шаг.
        /// </summary>
        private void ApplyPivotTilt()
        {
            Quaternion heading = Heading;
            tiltPivot.localRotation = Quaternion.Inverse(heading) * Quaternion.AngleAxis(TiltDegrees(), TiltAxis()) * heading;
        }

        /// <summary>
        /// Крен для остальных машин, целыми градусами по горизонтальным осям.
        /// Целыми — иначе канал шумел бы каждый шаг физики, пока кузов качается.
        /// </summary>
        private void PublishTilt()
        {
            if (tiltPivot == null || !IsSpawned || !IsServer)
            {
                return;
            }

            Vector3 degrees = tiltRotation * Mathf.Rad2Deg;
            var next = new MultiCarryTiltNetState
            {
                X = (sbyte)Mathf.Clamp(Mathf.RoundToInt(degrees.x), sbyte.MinValue, sbyte.MaxValue),
                Z = (sbyte)Mathf.Clamp(Mathf.RoundToInt(degrees.z), sbyte.MinValue, sbyte.MaxValue)
            };

            if (!next.Equals(netTilt.Value))
            {
                netTilt.Value = next;
            }
        }

        /// <summary>С какой скоростью показ у неавторитета догоняет реплицированный крен, рад/с. Быстрее, чем крен меняется, иначе показ отстаёт; медленнее шага физики, иначе целые градусы читаются ступеньками.</summary>
        private const float ReplicatedTiltRate = 6f;

        /// <summary>У неавторитета крен не считается, а догоняет реплицированный — в том же <c>tiltRotation</c>, чтобы <see cref="TiltAngle"/> читался одинаково на всех машинах.</summary>
        private void FollowReplicatedTilt(float dt)
        {
            MultiCarryTiltNetState state = netTilt.Value;
            Vector3 target = new Vector3(state.X, 0f, state.Z) * Mathf.Deg2Rad;
            tiltRotation = Vector3.MoveTowards(tiltRotation, target, ReplicatedTiltRate * dt);
        }

        // ========== КАЧЕНИЕ ==========

        /// <summary>Ниже этой скорости кузов не доворачивается: стоящую тележку не крутит от дрожи натяжений.</summary>
        private const float MinTurnSpeed = 0.25f;

        /// <summary>
        /// За этим углом между курсом и ходом тележку считают едущей задом:
        /// кузов не разворачивается на 180°, а катится назад. Иначе команда,
        /// сдавшая на метр назад, получала бы поручни с другой стороны и срыв
        /// всех ручек разом.
        /// </summary>
        private const float ReverseAngle = 100f;

        /// <summary>
        /// Шаг качения в руках: целевая скорость — те же натяжения, но
        /// объект тянется к ней с разгоном, а не берёт её сразу. Потолок и
        /// разгон зависят от нагрузки: полная тележка отстаёт от рванувших
        /// несущих сильнее пустой — и поручни у неё срываются чаще.
        /// </summary>
        private void StepRollingVelocity(Vector3 pullSum, float dt)
        {
            float cap = Mathf.Lerp(settings.maxSpeedEmpty, settings.maxSpeedFull, load);
            Vector3 target = Vector3.ClampMagnitude(pullSum * settings.pullToSpeed, cap);
            float acceleration = Mathf.Lerp(settings.accelerationEmpty, settings.accelerationFull, load);
            ApplyRollingVelocity(target, acceleration, dt);
        }

        /// <summary>Без рук катящийся объект докатывается и встаёт — так отпущенная на разгоне тележка уезжает сама.</summary>
        private void StepFreeRolling(float dt)
        {
            ApplyRollingVelocity(Vector3.zero, settings.rollingDeceleration, dt);
        }

        /// <summary>
        /// Горизонтальную скорость ведём сами, вертикаль оставляем гравитации:
        /// тележка стоит на колёсах, спускается с доски и падает в пропасть
        /// физикой, а не моделью.
        /// </summary>
        private void ApplyRollingVelocity(Vector3 target, float rate, float dt)
        {
            Vector3 current = body.linearVelocity;
            Vector3 flat = new Vector3(current.x, 0f, current.z);
            Vector3 next = Vector3.MoveTowards(flat, target, rate * dt);
            body.linearVelocity = new Vector3(next.x, current.y, next.z);
            StepRollingTilt(next - lastRollingVelocity);
            lastRollingVelocity = next;
            TurnTowardsMotion(next, dt);
        }

        /// <summary>Доля крена от рывка у пустого объекта: плескаться нечему, но кузов всё же качает.</summary>
        private const float EmptySloshFraction = 0.5f;

        /// <summary>
        /// Крен качения — от рывка кузова, а не от натяжений: ровная тяга
        /// полного кузова на колёсах его не валит, а разгон, торможение о
        /// стену, толчок с разгона, ловушка и резкий поворот — валят, и тем
        /// сильнее, чем он полнее. Разница берётся с прошлым выставленным
        /// значением, поэтому сюда попадает и то, что сделала с кузовом физика
        /// между шагами: удар о стену — тоже рывок. Знак — как у воды: разгон
        /// вперёд кладёт кузов назад, поворот — наружу.
        /// </summary>
        private void StepRollingTilt(Vector3 deltaVelocity)
        {
            float slosh = settings.sloshPerDeltaSpeed * Mathf.Lerp(EmptySloshFraction, 1f, load);
            tiltAngularVelocity += Vector3.Cross(deltaVelocity, Vector3.up) * slosh;
        }

        /// <summary>Кузов доворачивается к ходу; ход назад — задним ходом, без разворота.</summary>
        private void TurnTowardsMotion(Vector3 horizontalVelocity, float dt)
        {
            if (settings.handleLayout != MultiCarryHandleLayout.Cart ||
                horizontalVelocity.sqrMagnitude < MinTurnSpeed * MinTurnSpeed)
            {
                return;
            }

            float targetYaw = Mathf.Atan2(horizontalVelocity.x, horizontalVelocity.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(yawDegrees, targetYaw)) > ReverseAngle)
            {
                targetYaw += 180f;
            }

            yawDegrees = Mathf.MoveTowardsAngle(yawDegrees, targetYaw, settings.turnRate * dt);
        }

        /// <summary>
        /// Курс объекта — система координат ручек. Кольцо стоит на курсе,
        /// зафиксированном в <c>Awake</c>: рыскание у бутыли не меняется, и
        /// стоянки геометрически устойчивы. Тележка крутится: у авторитета курс
        /// ведёт модель, у остальных он читается из реплицированного поворота —
        /// один источник правды на всех, как <see cref="BasePosition"/>.
        /// </summary>
        private Quaternion Heading
        {
            get
            {
                if (settings.handleLayout == MultiCarryHandleLayout.Ring)
                {
                    return baseRotation;
                }

                Quaternion own = Quaternion.Euler(0f, yawDegrees, 0f);
                return HasAuthority ? own : YawOf(transform.rotation, own);
            }
        }

        /// <summary>Рыскание из полного поворота: горизонтальная проекция взгляда. Крен до 60° её не съедает.</summary>
        private static Quaternion YawOf(Quaternion rotation, Quaternion fallback)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : fallback;
        }

        /// <summary>
        /// Ручка в системе курса: горизонтальное смещение плюс высота.
        /// Кольцо — равномерно по окружности; тележка — по таблице
        /// <see cref="CartHandleLocal"/>.
        /// </summary>
        private Vector3 HandleLocal(int slot)
        {
            if (settings.handleLayout == MultiCarryHandleLayout.Ring)
            {
                float angle = Mathf.PI * 2f * slot / handleCount;
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * settings.handleRadius +
                       Vector3.up * settings.handleHeight;
            }

            return CartHandleLocal(slot) + Vector3.up * settings.handleHeight;
        }

        /// <summary>Куда от ручки «наружу», в системе курса: туда становится несущий.</summary>
        private Vector3 OutwardLocal(int slot)
        {
            if (settings.handleLayout == MultiCarryHandleLayout.Ring)
            {
                float angle = Mathf.PI * 2f * slot / handleCount;
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            return CartHandleLocal(slot).z < 0f ? Vector3.back : Vector3.forward;
        }

        /// <summary>
        /// Поручни тележки по числу рук. Один толкает сзади по центру; двое —
        /// сзади слева и справа; трое — двое сзади, один тянет спереди;
        /// четверо — двое сзади и двое спереди. Спереди тянут, сзади толкают:
        /// связь у обоих одна и та же, разница только в том, где стоянка.
        /// </summary>
        private Vector3 CartHandleLocal(int slot)
        {
            float side = settings.cartHandleSide;
            float back = -settings.cartHandleBack;
            float front = settings.cartHandleFront;

            switch (handleCount)
            {
                case 1:
                    return new Vector3(0f, 0f, back);
                case 2:
                    return new Vector3(slot == 0 ? -side : side, 0f, back);
                case 3:
                    return slot < 2
                        ? new Vector3(slot == 0 ? -side : side, 0f, back)
                        : new Vector3(0f, 0f, front);
                default:
                    return new Vector3(slot % 2 == 0 ? -side : side, 0f, slot < 2 ? back : front);
            }
        }

        /// <summary>Точка опоры ручки в мире, по горизонтали: по ней считается центр опоры и момент нехватки рук.</summary>
        private Vector3 SupportPointWorld(int slot)
        {
            Vector3 grip = HandleLocal(slot);
            grip.y = 0f;
            return Heading * grip;
        }

        /// <summary>
        /// Уместить занятые ручки в первые <paramref name="limit"/> слотов.
        /// Несущий со слота, которого больше нет, переезжает на свободный
        /// младший; кому места не хватило — тот срывается. У авторитета переезд
        /// уходит состоянием, и остальные повторяют его в
        /// <see cref="RelocateMovedHandles"/>, не отцепляя человека.
        /// </summary>
        private void CompactHandles(int limit)
        {
            MultiCarryNetState next = netState.Value;
            bool changed = false;

            for (int i = limit; i < handles.Length; i++)
            {
                if (!handles[i].Occupied)
                {
                    continue;
                }

                int free = -1;
                for (int j = 0; j < limit; j++)
                {
                    if (!handles[j].Occupied)
                    {
                        free = j;
                        break;
                    }
                }

                if (free < 0)
                {
                    if (!ReleaseHandle(i, CarryReleaseReason.RoundEnded))
                    {
                        DetachHandle(i, CarryReleaseReason.RoundEnded);
                    }

                    continue;
                }

                Handle moved = handles[i];
                handles[i] = handles[free];
                handles[free] = moved;

                if (IsSpawned && IsServer)
                {
                    next.Set(free, next.Of(i));
                    next.Set(i, MultiCarryNetState.NoCarrier);
                    changed = true;
                }
            }

            if (changed)
            {
                netState.Value = next;
            }
        }

        /// <summary>
        /// Несущий числится на другом слоте, чем приехало, — переставить ручку,
        /// а не отцеплять и цеплять заново: иначе на переезд соседа отвечал бы
        /// срыв с потерей потолка скорости и событием «уронили».
        /// </summary>
        private void RelocateMovedHandles(in MultiCarryNetState state)
        {
            for (int slot = 0; slot < handles.Length; slot++)
            {
                ulong wanted = state.Of(slot);
                if (wanted == MultiCarryNetState.NoCarrier || handles[slot].Occupied)
                {
                    continue;
                }

                int from = FindSlotByCarrierId(wanted);
                if (from < 0 || from == slot)
                {
                    continue;
                }

                Handle moved = handles[from];
                handles[from] = handles[slot];
                handles[slot] = moved;
            }
        }

        private int FindSlotByCarrierId(ulong carrierObjectId)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                NetworkObject carrier = handles[i].CarrierNetwork;
                if (handles[i].Occupied && carrier != null && carrier.NetworkObjectId == carrierObjectId)
                {
                    return i;
                }
            }

            return -1;
        }

        private float TiltDegrees() => tiltRotation.magnitude * Mathf.Rad2Deg;

        private Vector3 TiltAxis()
        {
            float angle = tiltRotation.magnitude;
            return angle > Mathf.Epsilon ? tiltRotation / angle : Vector3.up;
        }

        /// <summary>Плечо ручки от основания объекта: смещение по курсу плюс высота, повёрнутые вместе с наклоном.</summary>
        private Vector3 HandleOffsetWorld(int slot)
        {
            return Quaternion.AngleAxis(TiltDegrees(), TiltAxis()) * (Heading * HandleLocal(slot));
        }

        /// <summary>
        /// Центр опоры при полностью занятых ручках. У объекта с одной
        /// ручкой он совпадает с ней самой — поэтому одиночка и несёт ровно,
        /// а не валит объект набок.
        /// </summary>
        private Vector3 FullSupportCentroid()
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < handleCount; i++)
            {
                sum += SupportPointWorld(i);
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

                Vector3 station = StationOf(i);
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
