using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using Igruha.Core.Player;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Обзор от первого лица с потолком угловой скорости. Мышь и стик задают
    /// желаемое направление, а камера доезжает до него с постоянной скоростью
    /// maxTurnSpeed — это и есть главный рычаг баланса ведущего в асимметричных
    /// играх: он физически не может развернуться мгновенно и переловить всех.
    ///
    /// Процедурных компонентов Cinemachine на риге нет: позицию и поворот пишет
    /// этот скрипт в Update, а мозг камеры читает их в LateUpdate — порядок
    /// гарантирован, ничего не перетирается.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class FirstPersonCameraRig : MonoBehaviour
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

        [Header("Ограничение обзора")]
        [Tooltip("Потолок угловой скорости поворота по горизонтали, °/с")]
        [SerializeField] private float maxTurnSpeed = 90f;
        [Tooltip("Насколько низко можно опустить взгляд, °")]
        [SerializeField] private float minPitch = -20f;
        [Tooltip("Насколько высоко можно поднять взгляд, °")]
        [SerializeField] private float maxPitch = 20f;

        [Header("Тело и глаз")]
        [Tooltip("Разворачивать тело персонажа вслед за обзором")]
        [SerializeField] private bool lockBodyRotation = true;
        [Tooltip("Запасная высота глаза, м. Берётся только если у персонажа нет капсулы")]
        [SerializeField] private float eyeHeight = 1.5f;
        [Tooltip("На сколько глаз ниже макушки капсулы, м. Персонажи разного роста получают свою высоту")]
        [SerializeField] private float eyeDropFromTop = 0.15f;
        [Tooltip("Прятать собственную модель: иначе камера стоит внутри головы и видно изнанку текстур")]
        [SerializeField] private bool hideOwnModel = true;
        [Tooltip("Поле зрения, °. Вместе с потолком скорости снижает риск укачивания")]
        [Range(40f, 110f)]
        [SerializeField] private float fieldOfView = 75f;

        [Header("Курсор")]
        [Tooltip("Захватывать курсор в центре экрана (Esc освобождает)")]
        [SerializeField] private bool lockCursor = true;

        /// <summary>Куда камера смотрит сейчас — уже с учётом потолка скорости.</summary>
        public float Yaw => yaw;

        /// <summary>Наклон взгляда. Чисто визуальный: на игровые проверки не влияет.</summary>
        public float Pitch => pitch;

        private CinemachineCamera cam;
        private Transform trackedTarget;
        private PlayerController trackedBody;
        private float yaw;
        private float desiredYaw;
        private float pitch;
        private float resolvedEyeHeight;
        private float horizontalFieldOfView;
        private float appliedAspect;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>(8);

        private void Awake()
        {
            EnsureCamera();
        }

        /// <summary>
        /// Достать камеру, не дожидаясь Awake. Настройки рига задаёт мини-игра
        /// при раздаче ролей, а риг в этот момент ещё выключен — Awake у него
        /// не отработал, и ссылка пустая. Компонент лежит на том же объекте,
        /// так что это разовый GetComponent, а не поиск по сцене.
        /// </summary>
        private bool EnsureCamera()
        {
            if (cam == null)
            {
                cam = GetComponent<CinemachineCamera>();
            }

            return cam != null;
        }

        private void OnEnable()
        {
            lookAction?.action.Enable();

            LensSettings lens = cam.Lens;
            lens.FieldOfView = fieldOfView;
            cam.Lens = lens;

            // Обзор по горизонтали, если игра его задала, обязан пересчитаться
            // заново: строкой выше поле зрения только что вернули к вертикальному.
            appliedAspect = 0f;
            ApplyHorizontalFieldOfView();

            // Риг включается уже наведённым на своего персонажа: подхватываем его
            // разворот, иначе камера стартует со случайного направления.
            ResolveTarget();
            if (trackedTarget != null)
            {
                yaw = desiredYaw = trackedTarget.eulerAngles.y;
            }

            pitch = 0f;
            ApplyTransform();

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            lookAction?.action.Disable();
            RestoreOwnModel();

            // Сбрасываем цель, иначе при следующем включении ResolveTarget решит,
            // что она не менялась, и модель останется видимой.
            trackedTarget = null;

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// Потолок угловой скорости под правила конкретной мини-игры: в
        /// асимметричных играх он зависит от числа убегающих и потому не может
        /// быть зашит в риг. Значение ≤ 0 игнорируется — риг остаётся на своём.
        /// </summary>
        public void SetMaxTurnSpeed(float degreesPerSecond)
        {
            if (degreesPerSecond > 0f)
            {
                maxTurnSpeed = degreesPerSecond;
            }
        }

        /// <summary>
        /// Поставить обзор напрямую, минуя ввод и потолок скорости. Через это
        /// в сетевой фазе серверное значение yaw затирает локальное.
        /// </summary>
        public void SetYaw(float value)
        {
            yaw = desiredYaw = value;
            ApplyTransform();
        }

        /// <summary>
        /// Пределы наклона взгляда под конкретную игру. Стандартные ±20° годятся
        /// для ровной арены, но роль, которая обязана простреливать башню от
        /// первого этажа до крыши, ими не обходится.
        /// </summary>
        public void SetPitchLimits(float min, float max)
        {
            minPitch = Mathf.Min(min, max);
            maxPitch = Mathf.Max(min, max);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        /// <summary>
        /// Держать заданный обзор по ГОРИЗОНТАЛИ, °, независимо от соотношения
        /// сторон экрана. Cinemachine задаёт поле зрения по вертикали, поэтому
        /// на ультрашироком мониторе горизонтальный обзор молча вырастает —
        /// а там, где ширина кадра и есть баланс роли (сколько арены видно
        /// разом), это отдаёт игроку с широким экраном чужое преимущество.
        ///
        /// Ноль возвращает риг к обычному вертикальному полю зрения.
        /// </summary>
        public void SetHorizontalFieldOfView(float degrees)
        {
            horizontalFieldOfView = Mathf.Max(0f, degrees);
            appliedAspect = 0f;
            ApplyHorizontalFieldOfView();
        }

        /// <summary>
        /// Пересчёт вертикального поля зрения под текущее соотношение сторон.
        /// Аспект меняется при смене размера окна, поэтому проверяется каждый
        /// кадр, а сама тригонометрия считается только когда он реально поехал.
        /// </summary>
        private void ApplyHorizontalFieldOfView()
        {
            if (horizontalFieldOfView <= 0f || !EnsureCamera())
            {
                return;
            }

            float aspect = cam.Lens.Aspect;
            if (aspect <= 0f || Mathf.Approximately(aspect, appliedAspect))
            {
                return;
            }

            appliedAspect = aspect;

            float halfHorizontal = horizontalFieldOfView * 0.5f * Mathf.Deg2Rad;
            float vertical = 2f * Mathf.Atan(Mathf.Tan(halfHorizontal) / aspect) * Mathf.Rad2Deg;

            LensSettings lens = cam.Lens;
            lens.FieldOfView = vertical;
            cam.Lens = lens;
        }

        private void Update()
        {
            ResolveTarget();
            ApplyHorizontalFieldOfView();
            ReadLook();

            // Желаемое направление копится без ограничений, а камера доезжает до него
            // с постоянной скоростью. Мгновенный рывок мышью превращается в доводку,
            // и разворот на 180° честно занимает своё время.
            yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, maxTurnSpeed * Time.deltaTime);
            ApplyTransform();

            if (lockBodyRotation && trackedBody != null)
            {
                trackedBody.SetFacing(yaw);
            }
        }

        private void ReadLook()
        {
            if (lookAction == null)
            {
                return;
            }

            Vector2 look = lookAction.action.ReadValue<Vector2>();
            if (look.sqrMagnitude <= 0f)
            {
                return;
            }

            // Мышь даёт смещение в пикселях за кадр, стик — отклонение -1..1:
            // первое нельзя умножать на deltaTime, второе — обязательно.
            float yawDelta, pitchDelta;
            if (IsMouseLook())
            {
                yawDelta = look.x * mouseYawSensitivity;
                pitchDelta = look.y * mousePitchSensitivity;
            }
            else
            {
                yawDelta = look.x * stickYawSpeed * Time.deltaTime;
                pitchDelta = look.y * stickPitchSpeed * Time.deltaTime;
            }

            if (!invertPitch)
            {
                pitchDelta = -pitchDelta;
            }

            desiredYaw = Mathf.Repeat(desiredYaw + yawDelta + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);
        }

        private void ApplyTransform()
        {
            if (trackedTarget != null)
            {
                transform.position = trackedTarget.position + Vector3.up * resolvedEyeHeight;
            }

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Цель ставит MinigameCameraController, а в сетевой сцене переставляет
        /// LocalPlayerCameraBinder после загрузки. GetComponent дёргается только
        /// в момент смены цели, а не каждый кадр.
        /// </summary>
        private void ResolveTarget()
        {
            Transform target = cam.Target.TrackingTarget;
            if (target == trackedTarget)
            {
                return;
            }

            RestoreOwnModel();

            trackedTarget = target;
            trackedBody = target != null ? target.GetComponent<PlayerController>() : null;
            resolvedEyeHeight = ResolveEyeHeight(target);
            HideOwnModel(target);
        }

        /// <summary>
        /// Высота глаза считается от капсулы персонажа, а не задаётся числом:
        /// точка персонажа лежит на уровне ступней, а ростом персонажи
        /// различаются заметно. Фиксированная высота ставила бы камеру одному
        /// в глаза, другому в живот.
        /// </summary>
        private float ResolveEyeHeight(Transform target)
        {
            if (target == null || !target.TryGetComponent(out CapsuleCollider capsule))
            {
                return eyeHeight;
            }

            float top = capsule.center.y + capsule.height * 0.5f;
            return Mathf.Max(top - eyeDropFromTop, capsule.radius);
        }

        /// <summary>
        /// Спрятать модель того, чьими глазами смотрим. Камера стоит внутри
        /// головы, и без этого в кадр лезет изнанка собственных текстур.
        /// Прячем только у себя: остальные игроки видят это тело как обычно.
        /// </summary>
        private void HideOwnModel(Transform target)
        {
            if (!hideOwnModel || target == null)
            {
                return;
            }

            target.GetComponentsInChildren(true, hiddenRenderers);
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = hiddenRenderers[i];

                // Выключенное не нашей рукой возвращать потом нельзя, а
                // помеченное как «оставить видимым» гасить нельзя вовсе:
                // так на персонаже живёт фонарь ведущего, который ему и нужен.
                if (!renderer.enabled || renderer.GetComponentInParent<KeepVisibleInFirstPerson>() != null)
                {
                    hiddenRenderers.RemoveAt(i);
                    continue;
                }

                renderer.enabled = false;
            }
        }

        private void RestoreOwnModel()
        {
            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                if (hiddenRenderers[i] != null)
                {
                    hiddenRenderers[i].enabled = true;
                }
            }

            hiddenRenderers.Clear();
        }

        private bool IsMouseLook()
        {
            InputControl control = lookAction.action.activeControl;
            return control == null || control.device is Mouse;
        }
    }
}
