using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Small turn-dependent offsets layered over the authored flexible gait.
    /// The head leads a turn while the chest and hips settle at different rates.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class CircusBearMotion : MonoBehaviour
    {
        [SerializeField] private PitBear bear;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform lumbar;
        private float previousYaw, headTurn, bodyLean;
        private bool initialized;

        private void LateUpdate()
        {
            if (bear == null || Time.deltaTime <= 0) return;
            Transform root = bear.transform;
            float yaw = root.eulerAngles.y;
            float turn = initialized ? Mathf.Clamp(Mathf.DeltaAngle(previousYaw, yaw) / Time.deltaTime, -150, 150) : 0;
            previousYaw = yaw;
            initialized = true;
            bool locomotion = bear.State == PitBear.BearState.Patrol || bear.State == PitBear.BearState.Chase;
            headTurn = Mathf.Lerp(headTurn, locomotion ? turn * .075f : 0, 1 - Mathf.Exp(-7 * Time.deltaTime));
            bodyLean = Mathf.Lerp(bodyLean, locomotion ? turn * -.027f : 0, 1 - Mathf.Exp(-4 * Time.deltaTime));
            if (!locomotion) return;
            if (lumbar != null) lumbar.rotation = Quaternion.AngleAxis(bodyLean * -.4f, root.forward) * lumbar.rotation;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(bodyLean, root.forward) * chest.rotation;
            if (neck != null) neck.rotation = Quaternion.AngleAxis(headTurn * .4f, Vector3.up) * neck.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(headTurn * .6f, Vector3.up) * head.rotation;
        }
    }
}
