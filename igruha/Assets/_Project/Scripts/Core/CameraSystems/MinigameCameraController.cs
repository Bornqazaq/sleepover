using UnityEngine;
using Unity.Cinemachine;
using Igruha.Core.Minigame;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Переключение режима камеры под тип мини-игры (GDD 9.2).
    /// Сегодня реализован только ThirdPerson (party-риг из ядра);
    /// TopDown/Fixed — заготовки: добавить риг-ребёнка и включить в switch.
    /// </summary>
    public sealed class MinigameCameraController : MonoBehaviour
    {
        [Tooltip("3rd-person риг (PartyCameraRig) — единственный реализованный")]
        [SerializeField] private CinemachineCamera thirdPersonRig;
        [Tooltip("Заготовка под top-down риг")]
        [SerializeField] private CinemachineCamera topDownRig;
        [Tooltip("Заготовка под fixed-риг")]
        [SerializeField] private CinemachineCamera fixedRig;

        public void Apply(CameraMode mode, Transform followTarget)
        {
            CinemachineCamera rig = SelectRig(mode);
            if (rig == null)
            {
                Debug.LogWarning($"{name}: риг для режима {mode} не назначен — остаёмся на ThirdPerson", this);
                rig = thirdPersonRig;
            }

            SetRigActive(thirdPersonRig, rig == thirdPersonRig);
            SetRigActive(topDownRig, rig == topDownRig);
            SetRigActive(fixedRig, rig == fixedRig);

            if (rig != null && followTarget != null)
            {
                rig.Target.TrackingTarget = followTarget;
                SnapBehind(rig, followTarget);

                // Камера получает героя сразу после спавна, а спавн — это несколько
                // длинных кадров. Мусорная дельта от захвата курсора долетает уже
                // после них, поэтому защиту взводим здесь, а не только при включении рига.
                if (rig.TryGetComponent(out ThirdPersonCameraRig lookRig))
                {
                    lookRig.ArmLookGuard();
                }
            }
        }

        /// <summary>
        /// Поставить камеру за спину цели тем же кадром, без доводки из прежней позиции.
        /// Простой смены TrackingTarget мало: Cinemachine считает предыдущее состояние
        /// валидным и демпфирует переход, поэтому после выбора персонажа камера секунду
        /// ползёт к нему через всю комнату — и по дороге ныряет в лестницу и стены
        /// (deoccluder помогает только когда цель уже перекрыта, а не в полёте).
        /// </summary>
        private static void SnapBehind(CinemachineCamera rig, Transform followTarget)
        {
            if (rig.TryGetComponent(out CinemachineOrbitalFollow orbit))
            {
                // Орбита живёт в мировых координатах (BindingMode.WorldSpace), её угол
                // не знает про разворот героя. Один раз доворачиваем ось ему за спину:
                // это стартовая установка, а не привязка — дальше риг снова крутит камеру
                // сам, независимо от того, куда повернётся персонаж.
                InputAxis horizontal = orbit.HorizontalAxis;
                horizontal.Value = Mathf.Repeat(followTarget.eulerAngles.y + 180f, 360f) - 180f;
                orbit.HorizontalAxis = horizontal;

                InputAxis vertical = orbit.VerticalAxis;
                vertical.Value = Mathf.Clamp(vertical.Center, vertical.Range.x, vertical.Range.y);
                orbit.VerticalAxis = vertical;
            }

            // Сброс демпфирования: ближайший пересчёт риг сделает «с нуля» и встанет
            // в конечную точку сразу, а не поедет в неё.
            rig.PreviousStateIsValid = false;
        }

        private CinemachineCamera SelectRig(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.TopDown: return topDownRig;
                case CameraMode.Fixed: return fixedRig;
                default: return thirdPersonRig;
            }
        }

        private static void SetRigActive(CinemachineCamera rig, bool active)
        {
            if (rig != null)
            {
                rig.gameObject.SetActive(active);
            }
        }
    }
}
