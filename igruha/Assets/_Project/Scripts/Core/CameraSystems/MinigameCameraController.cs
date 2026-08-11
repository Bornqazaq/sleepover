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
            }
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
