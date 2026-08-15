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
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>(8);

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
                if (hiddenRenderers[i].enabled)
                {
                    hiddenRenderers[i].enabled = false;
                }
                else
                {
                    // Выключенное не нашей рукой возвращать потом нельзя.
                    hiddenRenderers.RemoveAt(i);
                }
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
