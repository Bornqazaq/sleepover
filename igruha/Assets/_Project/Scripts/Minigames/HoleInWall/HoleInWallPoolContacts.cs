using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Owner-side clearance for the animated body outside the frozen upright capsule.</summary>
    internal sealed class HoleInWallPoolContacts
    {
        private const float Skin = .045f;
        private const float TeleportDistance = 3f;
        private const int SolverPasses = 3;
        private readonly Probe[] probes;
        private readonly Vector3[] peerPoints;
        private Collider[] supports;
        private bool hasPrevious;

        private struct Probe
        {
            internal Transform A, B;
            internal float Blend, Radius;
            internal Vector3 Previous;
            internal Vector3 Position => B == null ? A.position : Vector3.Lerp(A.position, B.position, Blend);
        }

        internal HoleInWallPoolContacts(Animator animator, float bodyRadius)
        {
            var list = new List<Probe>();
            if (animator != null && animator.isHuman)
            {
                Add(list, animator, HumanBodyBones.Head, HumanBodyBones.Head, bodyRadius);
                Add(list, animator, HumanBodyBones.Hips, HumanBodyBones.Chest, bodyRadius);
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    Add(list, animator, left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                        left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, bodyRadius * .4f);
                    Add(list, animator, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm,
                        left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, bodyRadius * .32f);
                    Add(list, animator, left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                        left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, bodyRadius * .5f);
                    Add(list, animator, left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                        left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, bodyRadius * .42f);
                    Add(list, animator, left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot,
                        left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes, bodyRadius * .4f);
                }
            }
            probes = list.ToArray();
            peerPoints = new Vector3[probes.Length];
        }

        private static void Add(List<Probe> list, Animator animator, HumanBodyBones a, HumanBodyBones b, float radius)
        {
            var first = animator.GetBoneTransform(a);
            var second = animator.GetBoneTransform(b);
            if (first == null) return;
            int count = second == null || first == second ? 1 : 3;
            for (int i = 0; i < count; i++)
                list.Add(new Probe { A = first, B = second, Blend = i * .5f, Radius = radius });
        }

        internal void Configure(Collider[] colliders) { supports = colliders; Reset(); }
        internal void Reset() => hasPrevious = false;

        // The upright motor capsule cannot cover the sideways fall animation.
        // Resolve the visible body against the other visible body, on its owner only.
        internal void ResolvePeer(Rigidbody body, HoleInWallPoolContacts peer, Rigidbody other, bool first)
        {
            if (peer == null || other == null || (body.position - other.position).sqrMagnitude > 16f) return;
            Vector3 shift = Vector3.zero;
            Vector3 ownOffset = body.position - body.transform.position;
            Vector3 peerOffset = other.position - other.transform.position;
            Vector3 velocity = body.linearVelocity;
            for (int i = 0; i < probes.Length; i++) peerPoints[i] = probes[i].Position + ownOffset;
            for (int i = 0; i < peer.probes.Length; i++) peer.peerPoints[i] = peer.probes[i].Position + peerOffset;
            for (int pass = 0; pass < SolverPasses; pass++)
            {
                float deepest = 0;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < probes.Length; i++)
                    for (int j = 0; j < peer.probes.Length; j++)
                    {
                        Vector3 delta = peerPoints[i] + shift - peer.peerPoints[j];
                        float radius = probes[i].Radius + peer.probes[j].Radius + Skin;
                        if (Mathf.Abs(delta.y) >= radius) continue;
                        float horizontalRadius = Mathf.Sqrt(radius * radius - delta.y * delta.y);
                        delta.y = 0;
                        float distance = delta.magnitude;
                        float depth = horizontalRadius - distance;
                        if (depth <= deepest) continue;
                        deepest = depth;
                        if (distance > .001f) normal = delta / distance;
                        else
                        {
                            normal = body.position - other.position;
                            normal.y = 0;
                            if (normal.sqrMagnitude < .000001f)
                                normal = first ? Vector3.left : Vector3.right;
                            else normal.Normalize();
                        }
                    }
                if (deepest <= 0) break;
                // Half on each owner: no force or transform write on a remote replica.
                shift += normal * (deepest * .5f + .002f);
                float approach = Vector3.Dot(velocity - other.linearVelocity, normal);
                if (approach < 0) velocity -= normal * (approach * .5f);
            }
            if (shift.sqrMagnitude > 0)
            {
                body.position += shift;
                body.linearVelocity = velocity;
            }
        }

        internal void Resolve(Rigidbody body)
        {
            if (supports == null || probes.Length == 0) return;
            Vector3 shift = Vector3.zero;
            Vector3 velocity = body.linearVelocity;
            // Animated bones follow the interpolated render transform. Resolve against
            // the current physics position so multiple fixed ticks never reuse a stale body.
            Vector3 physicsOffset = body.position - body.transform.position;
            for (int pass = 0; pass < SolverPasses; pass++)
                for (int i = 0; i < probes.Length; i++)
                {
                    Probe probe = probes[i];
                    Vector3 point = probe.Position + physicsOffset + shift;
                    foreach (var support in supports)
                    {
                        if (support == null || !support.enabled || !support.gameObject.activeInHierarchy) continue;
                        // Supports are vertical square piers. A conservative box around
                        // each body sample also catches fast animated limb sweeps at corners.
                        Bounds bounds = support.bounds;
                        bounds.Expand((probe.Radius + Skin) * 2f);
                        Vector3 from = probe.Previous;
                        Vector3 travel = point - from;
                        if (pass == 0 && hasPrevious && !bounds.Contains(from) &&
                            travel.sqrMagnitude > .000001f && travel.sqrMagnitude < TeleportDistance * TeleportDistance &&
                            bounds.IntersectRay(new Ray(from, travel.normalized), out float distance) && distance < travel.magnitude)
                        {
                            Vector3 entry = from + travel.normalized * Mathf.Max(0, distance - Skin);
                            // Only horizontal correction: floor support remains the buoyancy/capsule's job.
                            Vector3 correction = new Vector3(entry.x - point.x, 0, entry.z - point.z);
                            shift += correction; point += correction;
                            if (correction.sqrMagnitude > .000001f)
                            {
                                Vector3 sweepNormal = correction.normalized;
                                float sweepInward = Vector3.Dot(velocity, sweepNormal);
                                if (sweepInward < 0) velocity -= sweepNormal * sweepInward;
                            }
                        }
                        if (!bounds.Contains(point)) continue;
                        float left = point.x - bounds.min.x, right = bounds.max.x - point.x;
                        float near = point.z - bounds.min.z, far = bounds.max.z - point.z;
                        float depth = Mathf.Min(Mathf.Min(left, right), Mathf.Min(near, far));
                        Vector3 normal = depth == left ? Vector3.left : depth == right ? Vector3.right :
                            depth == near ? Vector3.back : Vector3.forward;
                        Vector3 push = normal * (depth + Skin);
                        shift += push; point += push;
                        float inward = Vector3.Dot(velocity, normal);
                        if (inward < 0) velocity -= normal * inward;
                    }
                }
            for (int i = 0; i < probes.Length; i++) probes[i].Previous = probes[i].Position + physicsOffset + shift;
            if (shift.sqrMagnitude > 0)
            {
                body.position += shift;
                body.linearVelocity = velocity;
            }
            hasPrevious = true;
        }
    }
}
