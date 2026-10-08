using UnityEngine;

namespace Igruha.Core.Items
{
    /// <summary>Continuous sweep for small, harmless props. Does not push bodies or apply damage.</summary>
    public static class BouncingBallistics
    {
        private const float Skin = .002f;
        private const float RestSpeed = .4f;
        private const int MaxContactsPerStep = 4;

        // Call from the authority's physics tick; replicas only present the resulting state.
        public static bool Step(ref Vector3 position, ref Vector3 velocity, float delta,
            float radius, float bounce, float drag, int mask, out RaycastHit contact, out float impactSpeed)
        {
            contact = default; impactSpeed = 0;
            if (velocity == Vector3.zero) return false;
            velocity += Physics.gravity * delta;
            bool collided = false;
            for (int i = 0; i < MaxContactsPerStep && delta > 0; i++)
            {
                float speed = velocity.magnitude;
                if (speed < .001f) break;
                Vector3 direction = velocity / speed;
                if (!Physics.SphereCast(position, radius, direction, out var hit, speed * delta,
                        mask, QueryTriggerInteraction.Ignore))
                { position += velocity * delta; break; }
                position += direction * Mathf.Max(0, hit.distance - Skin);
                position += hit.normal * Skin;
                delta -= hit.distance / speed;
                float normalSpeed = Mathf.Max(0, -Vector3.Dot(velocity, hit.normal));
                if (normalSpeed > impactSpeed) { contact = hit; impactSpeed = normalSpeed; }
                collided = true;
                Vector3 tangent = Vector3.ProjectOnPlane(velocity, hit.normal) * (1 - drag);
                velocity = tangent + hit.normal * normalSpeed * bounce;
                if (hit.normal.y > .7f && velocity.sqrMagnitude < RestSpeed * RestSpeed)
                { velocity = Vector3.zero; break; }
            }
            return collided;
        }
    }
}
