using UnityEngine;
using Unity.Cinemachine;
using Igruha.Core.Minigame;

namespace Igruha.Core.CameraSystems
{
    /// <summary>
    /// Переключение режима камеры под тип мини-игры (GDD 9.2).
    /// Реализованы ThirdPerson (PartyCameraRig) и FirstPerson (риг ведущего);
    /// TopDown/Fixed — заготовки: добавить риг и включить в switch.
    /// </summary>
    public sealed class MinigameCameraController : MonoBehaviour
    {
        [Tooltip("3rd-person риг (PartyCameraRig) — режим по умолчанию")]
        [SerializeField] private CinemachineCamera thirdPersonRig;
        [Tooltip("1st-person риг ведущего (FirstPersonCameraRig)")]
        [SerializeField] private CinemachineCamera firstPersonRig;
        [Tooltip("Заготовка под top-down риг")]
        [SerializeField] private CinemachineCamera topDownRig;
        [Tooltip("Заготовка под fixed-риг")]
        [SerializeField] private CinemachineCamera fixedRig;

        /// <summary>Режим, включённый сейчас — чтобы было куда вернуться после спектатора.</summary>
        public CameraMode CurrentMode { get; private set; } = CameraMode.ThirdPerson;

        /// <summary>За кем камера следит сейчас.</summary>
        public Transform CurrentTarget { get; private set; }

        public void Apply(CameraMode mode, Transform followTarget)
        {
            CinemachineCamera rig = SelectRig(mode);
            if (rig == null)
            {
                Debug.LogWarning($"{name}: риг для режима {mode} не назначен — остаёмся на ThirdPerson", this);
                rig = thirdPersonRig;
            }

            // Цель ставится до включения рига: 1st-person при старте подхватывает
            // разворот персонажа, и без цели он смотрел бы в произвольную сторону.
            if (rig != null && followTarget != null)
            {
                rig.Target.TrackingTarget = followTarget;
                CurrentTarget = followTarget;
            }

            CurrentMode = mode;

            SetRigActive(thirdPersonRig, rig == thirdPersonRig);
            SetRigActive(firstPersonRig, rig == firstPersonRig);
            SetRigActive(topDownRig, rig == topDownRig);
            SetRigActive(fixedRig, rig == fixedRig);
        }

        private CinemachineCamera SelectRig(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.FirstPerson: return firstPersonRig;
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
