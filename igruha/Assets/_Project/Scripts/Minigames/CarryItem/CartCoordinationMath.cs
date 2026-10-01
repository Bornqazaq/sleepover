using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    public enum CartInputRelation { Idle, Together, Diverging, Opposing }

    /// <summary>Compare world commands, never keys from differently rotated cameras.</summary>
    public static class CartCoordinationMath
    {
        public const float DeadZone = 0.12f;
        private const float ParallelDot = 0.7071f;
        private const float OpposingDot = -0.25f;

        public static CartInputRelation Relation(Vector3 input, Vector3 others)
        {
            if (input.sqrMagnitude < DeadZone * DeadZone) return CartInputRelation.Idle;
            if (others.sqrMagnitude < DeadZone * DeadZone) return CartInputRelation.Together;
            float dot = Vector3.Dot(input.normalized, others.normalized);
            return dot < OpposingDot ? CartInputRelation.Opposing :
                dot < ParallelDot ? CartInputRelation.Diverging : CartInputRelation.Together;
        }

        public static Vector2 OnScreen(Vector3 world, Vector3 cameraForward, Vector3 cameraRight)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
            // Looking almost vertically down must not collapse the compass.
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.Cross(cameraRight, Vector3.up);
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, forward));
        }
    }
}
