using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>Local presentation only: a sting confirms something already visible to the keeper.</summary>
    public struct KeeperSightConfirmation
    {
        private float visibleSeconds;
        private bool announced;

        public bool Tick(bool fullyVisible, float deltaTime, float requiredSeconds)
        {
            if (announced) return false;
            visibleSeconds = fullyVisible ? visibleSeconds + deltaTime : 0f;
            if (!fullyVisible || visibleSeconds < requiredSeconds) return false;
            announced = true;
            return true;
        }

        public static bool FullyVisible(Transform eye, float angle, float range, Collider target, int blockers)
        {
            if (eye == null || target == null || !target.enabled || !target.gameObject.activeInHierarchy) return false;
            Bounds bounds = target.bounds;
            Vector3 head = bounds.center + Vector3.up * bounds.extents.y * 0.8f;
            Vector3 body = bounds.center - Vector3.up * bounds.extents.y * 0.35f;
            Vector3 side = Vector3.Cross(Vector3.up, bounds.center - eye.position).normalized *
                Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.65f;
            return PointVisible(eye, angle, range, head, blockers) &&
                PointVisible(eye, angle, range, body, blockers) &&
                PointVisible(eye, angle, range, bounds.center + side, blockers) &&
                PointVisible(eye, angle, range, bounds.center - side, blockers);
        }

        private static bool PointVisible(Transform eye, float angle, float range, Vector3 point, int blockers)
        {
            Vector3 delta = point - eye.position;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon || distance > range || Vector3.Angle(eye.forward, delta) > angle * 0.5f)
                return false;
            return !Physics.Raycast(eye.position, delta / distance, distance, blockers, QueryTriggerInteraction.Ignore);
        }
    }
}
