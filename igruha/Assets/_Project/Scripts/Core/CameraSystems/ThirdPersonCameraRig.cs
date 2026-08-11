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

        private CinemachineOrbitalFollow orbit;

        private void Awake()
        {
            orbit = GetComponent<CinemachineOrbitalFollow>();
        }

        private void OnEnable()
        {
            lookAction?.action.Enable();

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

        private void Update()
        {
            if (lookAction == null || orbit == null)
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

            InputAxis horizontal = orbit.HorizontalAxis;
            horizontal.Value = Mathf.Repeat(horizontal.Value + yawDelta + 180f, 360f) - 180f;
            orbit.HorizontalAxis = horizontal;

            InputAxis vertical = orbit.VerticalAxis;
            vertical.Value = Mathf.Clamp(vertical.Value + pitchDelta, minPitch, maxPitch);
            orbit.VerticalAxis = vertical;
        }

        private bool IsMouseLook()
        {
            InputControl control = lookAction.action.activeControl;
            return control == null || control.device is Mouse;
        }
    }
}
