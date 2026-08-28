using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

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
    public sealed class ThirdPersonCameraRig : MonoBehaviour
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
        [Tooltip("Слои сплошной геометрии: Ground, Cover, PlayerBarrier. Default сюда не входит намеренно — там триггеры чекпоинтов и ловушек и сами персонажи")]
        [SerializeField] private LayerMask occluders;
        [Tooltip("Насколько близко камера подходит к точке обхода, когда её прижало к стене")]
        [SerializeField] private float minDistanceFromTarget = 0.25f;
        [Tooltip("Радиус пробника камеры — на столько она держится от геометрии")]
        [SerializeField] private float probeRadius = 0.28f;
        [Tooltip("На сколько точка обхода выше макушки персонажа")]
        [SerializeField] private float pivotHeadroom = 0.25f;
        [Tooltip("Высота точки обхода, если у цели нет капсулы, — метры от её основания")]
        [SerializeField] private float fallbackPivotHeight = 1.9f;
        [Tooltip("С какой дистанции до точки обхода взгляд начинает подниматься к макушке")]
        [SerializeField] private float aimLiftDistance = 2.2f;

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

        private void Awake()
        {
            orbit = GetComponent<CinemachineOrbitalFollow>();
            deoccluder = GetComponent<CinemachineDeoccluder>();
            TryGetComponent(out cam);

            if (TryGetComponent(out composer))
            {
                restAimOffset = composer.TargetOffset;
            }

            EnforceFormat();
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
            // кадр в LateUpdate — она едет от приседа.
            avoidance.UseFollowTarget.Enabled = true;
            avoidance.UseFollowTarget.YOffset = fallbackPivotHeight;
            deoccluder.AvoidObstacles = avoidance;
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
        private void LateUpdate()
        {
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
                pivotTarget = target;
                target.TryGetComponent(out pivotCapsule);
            }

            // Присед меняет высоту капсулы, и точка обхода обязана ехать с ней:
            // постоянные два метра в лазу оказались бы внутри перекрытия, а луч
            // обхода — внутри геометрии, откуда он не видит ничего.
            float pivotHeight = pivotCapsule != null
                ? pivotCapsule.center.y + pivotCapsule.height * 0.5f + pivotHeadroom
                : fallbackPivotHeight;

            CinemachineDeoccluder.ObstacleAvoidance avoidance = deoccluder.AvoidObstacles;
            avoidance.UseFollowTarget.YOffset = pivotHeight;
            deoccluder.AvoidObstacles = avoidance;

            // Ближе этого камера не подходит физически: деокклюдер начинает
            // пробу от суммы минимальной дистанции и радиуса пробника.
            float closest = minDistanceFromTarget + probeRadius;
            float far = Mathf.Max(aimLiftDistance, closest + 0.01f);

            Vector3 pivot = target.position + target.rotation * (Vector3.up * pivotHeight);
            float distance = Vector3.Distance(cam.State.GetFinalPosition(), pivot);
            float lift = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(closest, far, distance));

            Vector3 aim = restAimOffset;
            aim.y = Mathf.Lerp(restAimOffset.y, pivotHeight, lift);
            composer.TargetOffset = aim;
        }

        private void OnEnable()
        {
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
