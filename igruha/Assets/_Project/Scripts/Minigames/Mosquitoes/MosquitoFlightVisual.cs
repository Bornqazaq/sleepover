using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    /// <summary>Pose only the miniature copy; imported wing axes never define the flap plane.</summary>
    public sealed class MosquitoFlightVisual
    {
        private const float FollowRate = 14f;
        private readonly Transform root;
        private readonly Vector3 scale;
        private readonly List<Transform> wings = new List<Transform>(4);
        private readonly List<Quaternion> rest = new List<Quaternion>(4);
        private readonly List<float> side = new List<float>(4);
        private readonly Renderer[] renderers;
        private float phase, contact, recoil;
        private Vector3 previousVelocity;

        public MosquitoFlightVisual(Transform root, int seed)
        {
            this.root = root; scale = root.localScale; phase = seed * 1.7f;
            renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("WingPivot_")) continue;
                wings.Add(child); rest.Add(child.localRotation);
                // All four imported hinges share the same origin; their positions cannot
                // identify left/right. The exported names carry the wing side.
                side.Add(child.name.StartsWith("WingPivot_-1_") ? -1 : 1);
            }
        }
        public int VisibleCount
        {
            get { int count = 0; foreach (var r in renderers) if (r != null && r.enabled) count++; return count; }
        }
        public void Bite() => recoil = 1;
        public void Tick(Vector3 velocity, float maxSpeed, bool attached, bool quiet, bool visible, Vector3 surface, float dt)
        {
            foreach (var r in renderers) if (r != null) r.enabled = visible;
            float blend = 1 - Mathf.Exp(-FollowRate * dt);
            contact = Mathf.MoveTowards(contact, attached ? 1 : 0, dt * 9);
            recoil = Mathf.MoveTowards(recoil, 0, dt * 3);
            float speed = Mathf.Clamp01(velocity.magnitude / maxSpeed);
            Vector3 localVelocity = root.parent.InverseTransformDirection(velocity);
            Vector3 acceleration = root.parent.InverseTransformDirection(velocity - previousVelocity) / Mathf.Max(.001f, dt);
            previousVelocity = velocity;
            float bank = Mathf.Clamp(-acceleration.x * 1.4f, -22, 22);
            Quaternion pose = Quaternion.Euler(Mathf.Lerp(speed * 24 + localVelocity.y * 2, 70, contact), 0, bank * (1 - contact));
            root.localRotation = Quaternion.Slerp(root.localRotation, pose, blend);
            Vector3 hover = root.parent.position + Vector3.up * (Mathf.Sin(phase * .09f) * .009f + Mathf.Sin(recoil * Mathf.PI) * .03f);
            root.position = Vector3.Lerp(hover, surface + Vector3.up * .075f, contact);
            root.localScale = scale;
            phase += dt * Mathf.PI * 2 * (quiet ? 9 : Mathf.Lerp(12, 17, speed));
            for (int i = 0; i < wings.Count; i++)
            {
                // Rotate about the character's forward axis, expressed in each pivot's parent.
                // FBX pivots are rotated 270 degrees: their local Z was a sideways sweep.
                Vector3 axis = wings[i].parent.InverseTransformDirection(root.forward);
                float flap = Mathf.Sin(phase + (i % 2) * .35f) * (quiet ? 22 : 36);
                float angle = Mathf.Lerp(flap, 58, contact) * side[i];
                wings[i].localRotation = Quaternion.AngleAxis(angle, axis) * rest[i];
            }
        }
    }
}
