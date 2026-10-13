using System;
using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Одна клетка на цепи. Главный носитель напряжения: весь статус игрока
    /// читается по высоте клетки, без единой цифры на экране.
    ///
    /// Про содержимое слота реквизита не знает ничего — туда встаёт кнопка
    /// «Секундомера» или полка с банками «Порядка банок». Про правила игры
    /// тоже не знает: ей говорят «спустись на ступень» и «открой дно».
    ///
    /// Уровень задаётся числом ступеней над нижней, а высота считается формулой
    /// из <see cref="CircusArenaConfig.GetCageBottomHeight"/>. Правила игры
    /// считают ступень как «лимит ошибок − сделано ошибок», поэтому последняя
    /// ступень перед вылетом — всегда нижняя, при любом лимите.
    /// </summary>
    [RequireComponent(typeof(RidePlatform))]
    [DefaultExecutionOrder(80)] // Constrain the passenger after the normal player motor.
    public sealed class CageStation : MonoBehaviour
    {
        [Tooltip("Слот реквизита в центре клетки")]
        [SerializeField] private Transform propSlot;
        [Tooltip("Точка респавна внутри клетки — едет вниз вместе с ней")]
        [SerializeField] private Transform respawnPoint;
        [Tooltip("Пол на створках. Общий компонент Core — та же механика служит платформам «Экзамена»")]
        [SerializeField] private HingedFloorHatch hatch;
        [SerializeField] private AnimationClip fallingClip;

        [Tooltip("На какой слой возвращать спрятанную от камеры стену, когда клетка опустела")]
        [SerializeField] private string cameraBlockingLayerName = "Ground";

        /// <summary>Клетка приехала на заданный уровень.</summary>
        public event Action<CageStation> Arrived;

        /// <summary>Створки распахнулись — пассажир полетел вниз.</summary>
        public event Action<CageStation> DoorsOpened;

        private RidePlatform platform;
        private CircusArenaConfig config;
        private PlayerController occupant;
        private Transform previousRespawnPoint;
        private bool lockedByDescent;
        private Collider closedFloor;
        private Collider[] leafColliders = Array.Empty<Collider>();
        private Rigidbody occupantBody;
        private CapsuleCollider occupantCapsule;

        /// <summary>Детектор застревания пассажира и его состояние до посадки.</summary>
        private StuckDetector occupantStuckDetector;
        private bool stuckDetectorWasEnabled;

        /// <summary>
        /// Коллайдеры, спрятанные от камеры на слое Ignore Raycast. Собираются
        /// при старте — см. <see cref="SetCameraBlocking"/>.
        /// </summary>
        private readonly List<Collider> cameraHiddenColliders = new List<Collider>(4);

        private int ignoreRaycastLayer;
        private int cameraBlockingLayer;

        /// <summary>
        /// Ступеней над нижней. Ноль — последняя ступень перед вылетом.
        ///
        /// Это целочисленный путь: его ведут <see cref="DescendTo(int, float)"/>
        /// и <see cref="SnapToLevel"/>. Непрерывный ход <see cref="MoveToFraction"/>
        /// поле не трогает — доля 0…1 на ступени не ложится. Игра, которая
        /// двигает клетку долей, обязана и «кто на нижней» определять долей,
        /// а не этим полем: здесь останется значение с последнего целого хода.
        /// </summary>
        public int Level { get; private set; }

        public bool DoorsOpen => hatch != null && hatch.DoorsOpen;

        public bool Descending => platform != null && platform.Moving;

        public PlayerController Occupant => occupant;

        /// <summary>Куда «Секундомер» ставит кнопку, а «Порядок банок» — полку с банками.</summary>
        public Transform PropSlot => propSlot;

        private void Awake()
        {
            platform = GetComponent<RidePlatform>();
            platform.Mode = RidePlatform.DriveMode.Scripted;
            platform.StabilizeScriptedPassenger = true;
            platform.EaseScriptedMotion = true;
            platform.Arrived += HandleArrived;
            CacheCameraHiddenColliders();
            var support = transform.Find("Floor/ClosedFloorSupport");
            closedFloor = support != null ? support.GetComponent<Collider>() : null;
            if (closedFloor != null)
            {
                var leaves = new List<Collider>();
                foreach (string side in new[] { "Floor/DoorLeft", "Floor/DoorRight" })
                {
                    var door = transform.Find(side);
                    if (door != null) leaves.AddRange(door.GetComponentsInChildren<Collider>(true));
                }
                leafColliders = leaves.ToArray();
            }

            if (hatch == null)
            {
                Debug.LogError($"{name}: не назначен HingedFloorHatch — дно клетки не откроется", this);
                return;
            }

            // Момент падения решает люк: он отпускает пассажира на пороге угла,
            // а не в конце анимации. Клетка только пересказывает это своим
            // подписчикам, чтобы игры не знали про устройство пола.
            hatch.Released += HandleHatchReleased;
        }

        private void HandleHatchReleased(HingedFloorHatch _)
        {
            if (closedFloor != null) closedFloor.enabled = false;
            LockOccupant(false);
            platform.SetPassenger(null);
            if (occupant != null && fallingClip != null)
            {
                var fall = occupant.GetComponent<CircusFallPose>();
                if (fall == null) fall = occupant.gameObject.AddComponent<CircusFallPose>();
                fall.Begin(fallingClip, transform.position.y);
            }
            DoorsOpened?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (platform != null)
            {
                platform.Arrived -= HandleArrived;
            }

            if (hatch != null)
            {
                hatch.Released -= HandleHatchReleased;
            }
        }

        /// <summary>
        /// Запомнить коллайдеры, которые сцена спрятала от камеры.
        ///
        /// Внешняя стена клетки лежит на Ignore Raycast намеренно: деоклюдер
        /// камеры видел в ней препятствие и вжимал камеру пассажиру в ноги.
        /// </summary>
        private void CacheCameraHiddenColliders()
        {
            ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            cameraBlockingLayer = LayerMask.NameToLayer(cameraBlockingLayerName);

            cameraHiddenColliders.Clear();
            if (cameraBlockingLayer < 0)
            {
                Debug.LogError($"{name}: слоя '{cameraBlockingLayerName}' нет в проекте — " +
                               "стена клетки останется невидимой для камеры", this);
                return;
            }

            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                if (c.gameObject.layer == ignoreRaycastLayer)
                {
                    cameraHiddenColliders.Add(c);
                }
            }
        }

        /// <summary>
        /// Вернуть спрятанные коллайдеры камере или спрятать снова.
        ///
        /// Прятать их нужно, только пока в клетке кто-то сидит. Как только дно
        /// распахнулось и пассажир полетел вниз, спрятанная стена превращается
        /// в проблему: камера едет за падающим снаружи клетки, стена оказывается
        /// ровно между ней и игроком, а деоклюдер её не видит и обойти не может.
        /// Замерено 20.08: в стадии открытия дна между камерой и игроком стоял
        /// Collider внешней стены, а сама камера была исправна — держала
        /// дистанцию 3.17 и ни во что не упиралась.
        /// </summary>
        private void SetCameraBlocking(bool blocking)
        {
            if (cameraBlockingLayer < 0)
            {
                return;
            }

            int layer = blocking ? cameraBlockingLayer : ignoreRaycastLayer;
            for (int i = 0; i < cameraHiddenColliders.Count; i++)
            {
                if (cameraHiddenColliders[i] != null)
                {
                    cameraHiddenColliders[i].gameObject.layer = layer;
                }
            }
        }

        /// <summary>
        /// Подготовить клетку к матчу: границы хода — от нижней ступени
        /// до верхней, клетка встаёт на верхнюю.
        /// </summary>
        public void Configure(CircusArenaConfig arenaConfig, int errorLimit)
        {
            config = arenaConfig;
            if (config == null)
            {
                Debug.LogError($"{name}: не задан CircusArenaConfig — уровни считать нечем", this);
                return;
            }

            platform.SetLimits(config.GetCageBottomHeight(0), config.GetCageBottomHeight(config.MaxLevelSteps));
            CloseDoors();
            SnapToLevel(errorLimit);
        }

        /// <summary>Кто сидит в этой клетке. Ставит точку респавна внутрь клетки.</summary>
        public void SetOccupant(PlayerController player)
        {
            occupant = player;
            occupantBody = player != null ? player.GetComponent<Rigidbody>() : null;
            occupantCapsule = player != null ? player.GetComponent<CapsuleCollider>() : null;
            platform.SetPassenger(player);

            if (player == null || respawnPoint == null)
            {
                return;
            }

            // Респавн внутри клетки: застрявший вернётся в центр, а не улетит
            // на стартовую точку арены. Точка — ребёнок клетки, поэтому едет
            // вниз вместе с ней сама собой.
            if (player.TryGetComponent(out PlayerRespawner respawner))
            {
                previousRespawnPoint = respawner.RespawnPoint;
                respawner.SetRespawnPoint(respawnPoint);
            }

            SuspendStuckDetector(player);
        }

        private void FixedUpdate()
        {
            if (closedFloor == null || !closedFloor.enabled || occupant == null || !occupant.enabled ||
                occupantBody == null || occupantBody.isKinematic || occupantCapsule == null) return;
            // A changing crouch capsule can receive a downward ground-snap at
            // the thin shelf/floor contact. A closed cage is a solid support
            // plane: enforce it after the motor, only for its simulated passenger.
            // No correction survives hatch release, and remote copies are untouched.
            Bounds floor = closedFloor.bounds;
            Vector3 position = occupantBody.position;
            if (position.x < floor.min.x || position.x > floor.max.x || position.z < floor.min.z || position.z > floor.max.z) return;
            Vector3 sole = occupant.transform.TransformVector(occupantCapsule.center - Vector3.up * (occupantCapsule.height * .5f));
            float penetration = floor.max.y - (position.y + sole.y);
            if (penetration <= .001f) return;
            occupantBody.position = position + Vector3.up * penetration;
            Vector3 velocity = occupantBody.linearVelocity;
            if (velocity.y < 0) { velocity.y = 0; occupantBody.linearVelocity = velocity; }
        }

        public void SnapToLevel(int levelSteps)
        {
            Level = Mathf.Max(0, levelSteps);
            platform.SnapTo(config.GetCageBottomHeight(Level));
        }

        /// <summary>
        /// Спуститься на заданную ступень. Управление на время спуска отключено:
        /// две секунды игрок только смотрит, как медведь становится ближе, —
        /// это и есть смешное, а не помеха.
        /// </summary>
        public void DescendTo(int levelSteps, float duration)
        {
            if (config == null)
            {
                return;
            }

            Level = Mathf.Max(0, levelSteps);
            LockOccupant(true);
            platform.MoveTo(config.GetCageBottomHeight(Level), duration);
        }

        /// <summary>
        /// Тот же спуск, но отсчитанный от общего момента. По сети клетка обязана
        /// быть на одной высоте у всех, а команда на разные машины приходит
        /// не одновременно: считая от момента, а не от своего первого кадра,
        /// опоздавшая машина сразу встаёт на верную высоту.
        /// </summary>
        public void DescendTo(int levelSteps, float duration, double startTime)
        {
            if (config == null)
            {
                return;
            }

            Level = Mathf.Max(0, levelSteps);
            LockOccupant(true);
            platform.MoveTo(config.GetCageBottomHeight(Level), duration, startTime);
        }

        /// <summary>
        /// Встать на высоту по доле <paramref name="t"/>: 0 — нижняя ступень,
        /// 1 — верхняя. Между ними линейно.
        ///
        /// Ступеней в конфиге всего <see cref="CircusArenaConfig.MaxLevelSteps"/>,
        /// и «Секундомеру» их хватает: там высота означает накопленные ошибки,
        /// а лимит ошибок 2–3. Там же, где высота означает долю справившихся,
        /// ступени кончаются: при восьми игроках справляются шестеро, и шесть
        /// разных высот на четыре ступени не ложатся.
        ///
        /// Работает в обе стороны — подъём клеток в брифинге это тот же вызов
        /// с большей долей, отдельного метода для него нет.
        ///
        /// <paramref name="startTime"/> — момент начала хода на общих часах,
        /// как у <see cref="DescendTo(int, float, double)"/>: каждая машина
        /// считает высоту по одной формуле от одного момента, поэтому
        /// опоздавшая сразу встаёт на верную высоту, а не догоняет.
        ///
        /// <see cref="Level"/> при этом не обновляется — доля на ступени
        /// не ложится.
        /// </summary>
        public void MoveToFraction(float t, float duration, double startTime)
        {
            if (config == null)
            {
                return;
            }

            float bottom = config.GetCageBottomHeight(0);
            float top = config.GetCageBottomHeight(config.MaxLevelSteps);
            LockOccupant(true);
            platform.MoveTo(Mathf.Lerp(bottom, top, Mathf.Clamp01(t)), duration, startTime);
        }

        private void HandleArrived()
        {
            LockOccupant(false);
            Arrived?.Invoke(this);
        }

        /// <summary>
        /// Распахнуть дно. Клетка при этом не разрушается и не падает — она
        /// остаётся висеть пустой на своём уровне, и это единственный смысл
        /// пустой клетки на арене: отсюда уже кто-то выпал.
        /// </summary>
        public void OpenDoors(float duration, float elapsedSeconds = 0f)
        {
            if (DoorsOpen)
            {
                return;
            }

            // Пассажир уходит вниз — прятать от камеры больше нечего, а вот
            // мешает спрятанное сильно: см. SetCameraBlocking.
            SetCameraBlocking(true);

            hatch.OpenDoors(duration, elapsedSeconds);
        }

        public void CloseDoors()
        {
            // Клетка снова целая и в ней снова сидят — прячем стену от камеры.
            SetCameraBlocking(false);

            hatch.CloseDoors();
            if (closedFloor != null)
            {
                // One continuous support avoids ground-snap hits on the seam
                // and an inclined collider squeezing the passenger downward.
                closedFloor.enabled = true;
                foreach (var collider in leafColliders) if (collider != null) collider.enabled = false;
            }
            platform.SetPassenger(occupant);
        }

        private void LockOccupant(bool locked)
        {
            if (occupant == null)
            {
                return;
            }

            // Клетка снимает ровно ту блокировку, которую сама поставила:
            // если игрока держит что-то ещё, чужое состояние трогать нельзя.
            if (locked)
            {
                lockedByDescent = true;
                occupant.MovementLocked = true;
                return;
            }

            if (!lockedByDescent)
            {
                return;
            }

            lockedByDescent = false;
            occupant.MovementLocked = false;
        }

        /// <summary>
        /// Отпустить игрока: снять блокировку и вернуть точку респавна.
        /// Зовётся в конце раунда — персонаж переезжает между сценами живым,
        /// и незакрытая блокировка уедет в хаб вместе с ним.
        /// </summary>
        public void ReleaseOccupant()
        {
            if (occupant != null) occupant.GetComponent<CircusFallPose>()?.Clear();
            LockOccupant(false);
            RestoreStuckDetector();

            if (occupant != null && occupant.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(previousRespawnPoint);
            }

            occupant = null;
            platform.SetPassenger(null);
        }

        /// <summary>
        /// Снять с пассажира детектор застревания на время сидения в клетке.
        ///
        /// Детектор считает застреванием «ввод есть, а хода нет» — на открытой
        /// арене это верно, там упор в геометрию и правда означает зажатого.
        /// В клетке 2.88 м это происходит с любой стороны и означает ровно
        /// ничего: держать игрока в тесноте — её работа. Хуже того, респавн
        /// возвращает его в ту же клетку, откуда он снова упрётся, — рескью,
        /// которое ничего не спасает, а по сети превращается в поток
        /// TeleportRpc и топит клиенту очередь приёма (IGR-346).
        /// </summary>
        private void SuspendStuckDetector(PlayerController player)
        {
            RestoreStuckDetector();

            if (player == null || !player.TryGetComponent(out StuckDetector detector))
            {
                return;
            }

            occupantStuckDetector = detector;
            stuckDetectorWasEnabled = detector.enabled;
            detector.enabled = false;
        }

        /// <summary>Вернуть детектор как было: персонаж переезжает между сценами живым.</summary>
        private void RestoreStuckDetector()
        {
            if (occupantStuckDetector == null)
            {
                return;
            }

            occupantStuckDetector.enabled = stuckDetectorWasEnabled;
            occupantStuckDetector = null;
        }
    }
}
