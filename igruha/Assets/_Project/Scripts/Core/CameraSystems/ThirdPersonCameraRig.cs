using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Управление орбитой 3rd-person камеры мышью/правым стиком.
    /// Камера — независимая система отсчёта: персонаж поворачивается по
    /// направлению движения, а камера НЕ привязана к его развороту.
    /// Иначе получается петля «камера крутит ввод → ввод крутит персонажа →
    /// персонаж крутит камеру» и управление уезжает.
    /// </summary>
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    [RequireComponent(typeof(CinemachineDeoccluder))]
    public sealed class ThirdPersonCameraRig : CinemachineExtension
    {
        [SerializeField] private InputActionReference lookAction;

        [Header("Чувствительность")]
        [Tooltip("Мышь: градусов на пиксель смещения")]
        [SerializeField] private float mouseYawSensitivity = 0.16f;
        [SerializeField] private float mousePitchSensitivity = 0.11f;
        [Tooltip("Стик: градусов в секунду при полном отклонении")]
        [SerializeField] private float stickYawSpeed = 200f;
        [SerializeField] private float stickPitchSpeed = 130f;
        [SerializeField] private bool invertPitch;

        [Header("Ограничение наклона")]
        [Tooltip("Минимальный угол: отрицательный — камера ниже персонажа")]
        [SerializeField] private float minPitch = -12f;
        [SerializeField] private float maxPitch = 55f;

        [Header("Курсор")]
        [Tooltip("Захватывать курсор в центре экрана, как в 3rd-person играх (Esc освобождает)")]
        [SerializeField] private bool lockCursor = true;

        [Header("Формат камеры — орбита от третьего лица, как в GTA 5. Не менять")]
        [Tooltip("Сплошная геометрия: Ground, Cover, PlayerBarrier. CameraOnly добавляется кодом для отделки без контактов с игроком. Default исключён: там триггеры и персонажи")]
        [SerializeField] private LayerMask occluders;
        [Tooltip("Насколько близко камера подходит к точке обхода, когда её прижало к стене")]
        [SerializeField] private float minDistanceFromTarget = 0.25f;
        [Tooltip("Радиус пробника камеры — на столько она держится от геометрии")]
        [SerializeField] private float probeRadius = 0.28f;
        [Tooltip("На сколько точка обхода выше макушки персонажа")]
        [SerializeField] private float pivotHeadroom = 0.25f;
        [Tooltip("Высота точки обхода над привязкой, если у цели нет капсулы. Привязка — грудь персонажа, отсюда полметра, а не полный рост")]
        [SerializeField] private float fallbackPivotHeight = 0.5f;
        [Tooltip("С какой дистанции до точки обхода взгляд начинает подниматься к макушке")]
        [SerializeField] private float aimLiftDistance = 2.2f;

        [Header("Кадр — один на все сцены, прописывается кодом поверх YAML")]
        [Tooltip("Дистанция орбиты от точки привязки (грудь персонажа)")]
        [SerializeField] private float orbitRadius = 4.2f;
        [Tooltip("Угол камеры над точкой привязки в спокойном положении: с него начинается каждая сцена")]
        [SerializeField] private float restPitch = 20f;
        [Tooltip("Смягчение слежения за целью по мировым осям. По Y больше: прыжки и ступеньки не должны трясти кадр")]
        [SerializeField] private Vector3 followDamping = new Vector3(0.12f, 0.25f, 0.12f);
        [Tooltip("Смягчение доводки взгляда")]
        [SerializeField] private Vector2 aimDamping = new Vector2(0.1f, 0.1f);
        [Tooltip("Где цель стоит в кадре: доли экрана от центра. Отрицательный Y опускает персонажа и открывает вид вперёд")]
        [SerializeField] private Vector2 screenPosition = new Vector2(-0.05f, -0.04f);
        [Tooltip("Точка взгляда относительно привязки: вверх от груди к шее")]
        [SerializeField] private Vector3 aimOffset = new Vector3(0f, 0.15f, 0f);
        [Tooltip("Угол обзора по вертикали")]
        [SerializeField] private float fieldOfView = 55f;

        [Header("Обход геометрии — доводка деокклюдера")]
        [Tooltip("Как быстро камера подтягивается вперёд, когда цель закрыли")]
        [SerializeField] private float occlusionPullDamping = 0.1f;
        [Tooltip("Как быстро камера возвращается назад, когда помеха ушла")]
        [SerializeField] private float occlusionReturnDamping = 0.35f;
        [Tooltip("Сколько держать подтянутое положение, чтобы камера не дребезжала на столбах и перилах")]
        [SerializeField] private float occlusionSmoothing = 0.15f;

        /// <summary>
        /// Телепорт цели: респавн, старт мини-игры, смена арены. Смягчение
        /// слежения рассчитано на бег, а не на переброс через всю карту — без
        /// сброса камера едет к новому месту через всю геометрию сцены.
        /// Порог с большим запасом: за кадр персонаж проходит сантиметры.
        /// </summary>
        private const float TeleportStep = 2.5f;

        /// <summary>
        /// Захват курсора телепортирует его в центр экрана, и следом приходит
        /// огромная дельта мыши — камеру швыряет в упор клампа, вплоть до
        /// «камера внутри стены». Гасим ввод на пару кадров после включения
        /// рига (старт сцены, закрытие модалки выбора персонажа).
        /// </summary>
        private const int LookWarmupFrames = 2;

        /// <summary>
        /// Одних кадров мало: спавн персонажей после выбора растягивает кадр
        /// на секунды, и мусорная дельта прилетает уже после прогрева. Но она
        /// всегда ПЕРВАЯ ненулевая после захвата курсора — её и выбрасываем,
        /// сколько бы кадров до неё ни прошло. Дальше ввод идёт как обычно:
        /// никаких порогов и таймеров, обычные движения мышью не режутся.
        /// </summary>
        private bool discardFirstLook;

        private CinemachineOrbitalFollow orbit;
        private CinemachineCamera cam;
        private CinemachineDeoccluder deoccluder;
        private CinemachineRotationComposer composer;
        private int warmupFramesLeft;
        private bool lookSuspended;

        /// <summary>Точка взгляда в кадре без помех — та, что выставил геймдизайнер. К ней камера возвращается, отойдя от стены.</summary>
        private Vector3 restAimOffset;

        private Transform pivotTarget;
        private CapsuleCollider pivotCapsule;
        private Vector3 collisionPivot;
        private bool hasCollisionPivot;
        private Vector3 previousCameraPosition;
        private bool hasPreviousCameraPosition;
        private Vector3 lastTargetPosition;
        private bool hasLastTargetPosition;
        private const float CollisionPadding = 0.01f;
        private const string CameraOnlyLayer = "CameraOnly";

        /// <summary>
        /// Накопленное смещение мыши, ещё не отданное камере.
        /// Дельта мыши — величина событийная: она существует ровно один раз,
        /// на своё событие. Опрашивать её каждый кадр через ReadValue нельзя —
        /// пока новых движений нет, в контроле остаётся ПОСЛЕДНЕЕ значение,
        /// и камера крутится от него бесконечно, пока не упрётся в ограничитель
        /// (в Game видно застывшую картинку под нелепым углом, в Scene — что риг
        /// «живой»). Поэтому копим по событию и применяем один раз.
        /// </summary>
        private Vector2 pendingMouseLook;

        /// <summary>Взвести защиту заново — на каждом событии, после которого прилетает мусорная дельта.</summary>
        public void ArmLookGuard()
        {
            warmupFramesLeft = LookWarmupFrames;
            discardFirstLook = true;
        }

        /// <summary>
        /// Приостановить вращение камеры, не трогая курсор: той же дельтой мыши
        /// в этот момент водят курсор колеса эмоций, и камера крутиться не должна.
        /// В отличие от выключения всего рига (так делает экран выбора персонажа),
        /// курсор остаётся захваченным — модалки здесь нет, игрок продолжает играть.
        /// </summary>
        public void SetLookSuspended(bool suspended)
        {
            lookSuspended = suspended;

            if (!suspended)
            {
                // За время удержания накопилась дельта — гасим её той же защитой,
                // иначе камеру швырнёт на первом же кадре после отпускания.
                ArmLookGuard();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            orbit = GetComponent<CinemachineOrbitalFollow>();
            deoccluder = GetComponent<CinemachineDeoccluder>();
            TryGetComponent(out cam);

            TryGetComponent(out composer);

            EnforceFormat();
            restAimOffset = aimOffset;
        }

        /// <summary>
        /// Приводит Cinemachine к формату проекта: камера не заходит за геометрию,
        /// а упёршись в стену подтягивается к персонажу вплоть до затылка — так же,
        /// как в GTA 5.
        ///
        /// Значения живут здесь, а не только в компонентах Cinemachine, и
        /// переписываются на старте намеренно: настройки Cinemachine лежат в YAML
        /// префаба, а YAML не переживает слияние веток — чужая правка выигрывает
        /// молча, и камера начинает проходить сквозь стены. Код переживает.
        /// </summary>
        private void EnforceFormat()
        {
            if (deoccluder == null)
            {
                Debug.LogError($"{name}: на риге нет CinemachineDeoccluder — камера будет проходить сквозь стены", this);
                return;
            }

            // Декоративные балки могут иметь отдельные прокси без контактов
            // с игроками. Остальные сцены сохраняют прежнюю маску геометрии.
            occluders |= LayerMask.GetMask(CameraOnlyLayer);
            deoccluder.CollideAgainst = occluders;
            deoccluder.MinimumDistanceFromTarget = minDistanceFromTarget;

            CinemachineDeoccluder.ObstacleAvoidance avoidance = deoccluder.AvoidObstacles;
            avoidance.Enabled = true;
            avoidance.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            avoidance.CameraRadius = probeRadius;
            // Ноль — «тянуть, сколько нужно». Любой предел оставляет камеру в стене
            // ровно тогда, когда персонаж прижался к этой стене вплотную.
            avoidance.DistanceLimit = 0f;
            // Ждать перед реакцией нельзя: за время ожидания стена уже в кадре.
            avoidance.MinimumOcclusionTime = 0f;

            // Подтягивается камера к точке над макушкой, а не к точке взгляда.
            // На этапе Body, когда работает деокклюдер, точкой взгляда ещё
            // служит корень персонажа — то есть его ступни. Камера съезжала
            // к ним по лучу и упиралась в ноги с полуметра: кадр занимали
            // голени и пол, обзор пропадал целиком. Высота уточняется каждый
            // кадр перед расчётом Cinemachine — она едет от приседа.
            avoidance.UseFollowTarget.Enabled = true;
            avoidance.UseFollowTarget.YOffset = fallbackPivotHeight;
            avoidance.SmoothingTime = occlusionSmoothing;
            avoidance.Damping = occlusionReturnDamping;
            avoidance.DampingWhenOccluded = occlusionPullDamping;
            deoccluder.AvoidObstacles = avoidance;

            EnforceFraming();
        }

        /// <summary>
        /// Приводит кадр к единому формату: дистанция, угол над персонажем,
        /// смягчение слежения, место цели в кадре, угол обзора.
        ///
        /// Раньше эти значения жили только в YAML, и к десятой мини-игре кадр
        /// разъехался: в «Секундомере» и «Порядке банок» камеру руками подняли
        /// к груди и придвинули, в «Плачущих ангелах» подняли только взгляд,
        /// в остальных восьми она осталась на высоте пояса и смотрела в ноги.
        /// Формат обязан быть одним на все сцены, поэтому живёт здесь и
        /// переписывает инстансы префаба на старте — как маска препятствий.
        ///
        /// Сцене, которой действительно нужна другая дистанция, поле
        /// <c>orbitRadius</c> правится на самом риге: оно переживёт этот вызов,
        /// в отличие от правки компонентов Cinemachine.
        /// </summary>
        private void EnforceFraming()
        {
            if (orbit != null)
            {
                // Высота привязки живёт в CameraTarget персонажа, а не в смещении
                // орбиты: у восьми персонажей разный рост, и сдвиг метрами кадрирует
                // низких иначе, чем высоких.
                orbit.TargetOffset = Vector3.zero;
                orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
                orbit.Radius = orbitRadius;

                TrackerSettings tracker = orbit.TrackerSettings;
                tracker.BindingMode = BindingMode.WorldSpace;
                tracker.PositionDamping = followDamping;
                orbit.TrackerSettings = tracker;

                InputAxis vertical = orbit.VerticalAxis;
                vertical.Range = new Vector2(minPitch, maxPitch);
                vertical.Center = Mathf.Clamp(restPitch, minPitch, maxPitch);
                vertical.Value = Mathf.Clamp(vertical.Value, minPitch, maxPitch);
                orbit.VerticalAxis = vertical;

                InputAxis horizontal = orbit.HorizontalAxis;
                horizontal.Range = new Vector2(-180f, 180f);
                horizontal.Wrap = true;
                orbit.HorizontalAxis = horizontal;
            }

            if (composer != null)
            {
                composer.TargetOffset = aimOffset;
                composer.Damping = aimDamping;

                ScreenComposerSettings composition = composer.Composition;
                composition.ScreenPosition = screenPosition;
                composer.Composition = composition;
            }

            if (cam != null)
            {
                cam.Lens.FieldOfView = fieldOfView;
            }
        }

        /// <summary>
        /// Защита от геометрии: держать камеру над макушкой и поднимать к ней
        /// взгляд по мере того, как камеру прижимает к персонажу.
        ///
        /// Одной точки обхода мало. Камера перестаёт съезжать в ноги, но взгляд
        /// остаётся на прежней точке у пола — прижатая камера смотрит с макушки
        /// почти отвесно вниз, и обзор теряется ровно так же. Поэтому точка
        /// взгляда едет вверх вместе с сокращением дистанции: на полном отлёте
        /// кадр в точности прежний, вплотную — камера стоит над головой и
        /// смотрит вперёд, а персонаж уходит под нижний край кадра.
        /// </summary>
        public override void PrePipelineMutateCameraStateCallback(
            CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
        {
            hasCollisionPivot = false;
            if (cam == null || composer == null || deoccluder == null)
            {
                return;
            }

            Transform target = cam.Follow;
            if (target == null)
            {
                return;
            }

            if (!ReferenceEquals(target, pivotTarget))
            {
                hasPreviousCameraPosition = false;
                pivotTarget = target;
                // Hub передаёт дочерний CameraTarget на высоте груди, сеть —
                // корень игрока. Капсула и мировая макушка одинаковы в обоих случаях.
                pivotCapsule = target.GetComponentInParent<CapsuleCollider>();
            }

            Vector3 up = target.up;
            Vector3 origin = target.position;
            float pivotHeight = fallbackPivotHeight;
            if (pivotCapsule != null)
            {
                origin = pivotCapsule.transform.TransformPoint(pivotCapsule.center);
                Vector3 head = pivotCapsule.transform.TransformPoint(
                    pivotCapsule.center + Vector3.up * (pivotCapsule.height * 0.5f));
                pivotHeight = Vector3.Dot(head - target.position, up) + pivotHeadroom;
            }

            // Начинаем внутри свободной капсулы, а не над головой: на лестнице
            // и в прыжке даже правильная макушка с запасом может уйти в потолок.
            // Проверки здесь только читают физику для текущего кадра камеры.
            collisionPivot = ConstrainToGeometry(origin, target.position + up * pivotHeight);
            pivotHeight = Vector3.Dot(collisionPivot - target.position, up);
            hasCollisionPivot = true;

            CinemachineDeoccluder.ObstacleAvoidance avoidance = deoccluder.AvoidObstacles;
            avoidance.UseFollowTarget.YOffset = pivotHeight;
            deoccluder.AvoidObstacles = avoidance;

            // Здесь штатный деокклюдер уже прижал камеру. Последняя проверка
            // может подтянуть её ещё ближе; подъём взгляда при этом сохраняется.
            float closest = minDistanceFromTarget + probeRadius;
            float far = Mathf.Max(aimLiftDistance, closest + 0.01f);

            // В PrePipeline cam.State уже сброшен к сырой орбите. Берём
            // сохранённый итог прошлого кадра, иначе подъём взгляда у стены
            // никогда не включается, даже когда камера вплотную к голове.
            Vector3 cameraPosition = hasPreviousCameraPosition && cam.PreviousStateIsValid
                ? previousCameraPosition : state.GetFinalPosition();
            float distance = Vector3.Distance(cameraPosition, collisionPivot);
            float lift = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(closest, far, distance));

            Vector3 aim = restAimOffset;
            aim.y = Mathf.Lerp(restAimOffset.y, pivotHeight, lift);
            composer.TargetOffset = aim;
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || !hasCollisionPivot)
            {
                return;
            }

            // Деокклюдер пропускает начало луча на MinimumDistance + CameraRadius,
            // а затем сглаживает приближение. В тесном углу начало бывает уже
            // за стеной. Последний sweep без пропуска удерживает итоговый кадр
            // внутри комнаты, сохраняя обычное плавное отдаление Cinemachine.
            Vector3 previousPosition = state.GetFinalPosition();
            Vector3 safePosition = ConstrainToGeometry(collisionPivot, previousPosition);
            previousCameraPosition = safePosition;
            hasPreviousCameraPosition = true;
            Vector3 correction = safePosition - previousPosition;
            if (correction.sqrMagnitude < Epsilon * Epsilon)
            {
                return;
            }

            state.PositionCorrection += correction;
            if (state.HasLookAt()
                && (state.ReferenceLookAt - safePosition).sqrMagnitude > Epsilon * Epsilon)
            {
                // Сохраняем смещение цели в кадре, уже рассчитанное композером.
                Vector2 screenOffset = state.RawOrientation.GetCameraRotationToTarget(
                    state.ReferenceLookAt - previousPosition, state.ReferenceUp);
                Quaternion look = Quaternion.LookRotation(
                    state.ReferenceLookAt - safePosition, state.ReferenceUp);
                state.RawOrientation = look.ApplyCameraRotation(-screenOffset, state.ReferenceUp);
            }
        }

        private Vector3 ConstrainToGeometry(Vector3 origin, Vector3 destination)
        {
            Vector3 offset = destination - origin;
            float distance = offset.magnitude;
            if (distance > Epsilon && Physics.SphereCast(
                origin, probeRadius, offset / distance, out RaycastHit hit,
                distance, occluders, QueryTriggerInteraction.Ignore))
            {
                return origin + offset / distance * Mathf.Max(0f, hit.distance - CollisionPadding);
            }

            return destination;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (lookAction != null)
            {
                lookAction.action.performed += OnLookPerformed;
                lookAction.action.Enable();
            }

            pendingMouseLook = Vector2.zero;
            ArmLookGuard();

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            if (lookAction != null)
            {
                lookAction.action.performed -= OnLookPerformed;
            }

            lookAction?.action.Disable();
            pendingMouseLook = Vector2.zero;

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            DropDampingAfterTeleport();

            if (lookAction == null || orbit == null || lookSuspended)
            {
                return;
            }

            if (warmupFramesLeft > 0)
            {
                warmupFramesLeft--;
                return;
            }

            // Мышь: копилка событий, забираем и обнуляем — каждое смещение
            // применяется ровно один раз. Стик: обычный опрос, его отклонение
            // держится всё время, пока стик отклонён.
            Vector2 mouseLook = pendingMouseLook;
            pendingMouseLook = Vector2.zero;
            Vector2 stickLook = IsStickLook() ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;

            if (mouseLook.sqrMagnitude <= 0f && stickLook.sqrMagnitude <= 0f)
            {
                return;
            }

            // Первое же смещение после захвата курсора — это его прыжок в центр
            // экрана, а не движение руки. Гасим ровно его, дальше не вмешиваемся.
            if (discardFirstLook)
            {
                discardFirstLook = false;
                return;
            }

            // Мышь даёт смещение в пикселях, стик — отклонение -1..1:
            // первое нельзя умножать на deltaTime, второе — обязательно.
            float yawDelta = mouseLook.x * mouseYawSensitivity + stickLook.x * stickYawSpeed * Time.deltaTime;
            float pitchDelta = mouseLook.y * mousePitchSensitivity + stickLook.y * stickPitchSpeed * Time.deltaTime;

            if (!invertPitch)
            {
                pitchDelta = -pitchDelta;
            }

            InputAxis horizontal = orbit.HorizontalAxis;
            horizontal.Value = Mathf.Repeat(horizontal.Value + yawDelta + 180f, 360f) - 180f;
            orbit.HorizontalAxis = horizontal;

            InputAxis vertical = orbit.VerticalAxis;
            vertical.Value = Mathf.Clamp(vertical.Value + pitchDelta, minPitch, maxPitch);
            orbit.VerticalAxis = vertical;
        }

        /// <summary>
        /// Персонажа перебросило — снять смягчение на один кадр, чтобы камера
        /// встала на новое место сразу.
        ///
        /// Респавн, старт мини-игры и возврат в хаб двигают тело мгновенно, а
        /// смягчение слежения рассчитано на бег: камера отправлялась догонять
        /// через всю сцену, по дороге ныряя в стены и пол. Отличить переброс от
        /// бега можно по одному шагу: на скорости персонаж проходит за кадр
        /// сантиметры, а переброс — это метры.
        /// </summary>
        private void DropDampingAfterTeleport()
        {
            Transform target = cam != null ? cam.Follow : null;
            if (target == null)
            {
                hasLastTargetPosition = false;
                return;
            }

            Vector3 position = target.position;
            if (hasLastTargetPosition
                && (position - lastTargetPosition).sqrMagnitude > TeleportStep * TeleportStep)
            {
                cam.PreviousStateIsValid = false;
                hasPreviousCameraPosition = false;
            }

            lastTargetPosition = position;
            hasLastTargetPosition = true;
        }

        /// <summary>Смещение мыши приходит событием и копится до ближайшего кадра.</summary>
        private void OnLookPerformed(InputAction.CallbackContext context)
        {
            if (context.control != null && context.control.device is Mouse)
            {
                pendingMouseLook += context.ReadValue<Vector2>();
            }
        }

        private bool IsStickLook()
        {
            InputControl control = lookAction.action.activeControl;
            return control != null && !(control.device is Mouse);
        }
    }
}
