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
        [Tooltip("Высота глаза над точкой слежения, м")]
        [SerializeField] private float eyeHeight = 0.55f;
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

        private void Awake()
        {
            cam = GetComponent<CinemachineCamera>();
        }

        private void OnEnable()
        {
            lookAction?.action.Enable();

            LensSettings lens = cam.Lens;
            lens.FieldOfView = fieldOfView;
            cam.Lens = lens;

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

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
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

        private void Update()
        {
            ResolveTarget();
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
                transform.position = trackedTarget.position + Vector3.up * eyeHeight;
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

            trackedTarget = target;
            trackedBody = target != null ? target.GetComponent<PlayerController>() : null;
        }

        private bool IsMouseLook()
        {
            InputControl control = lookAction.action.activeControl;
            return control == null || control.device is Mouse;
        }
    }
}
