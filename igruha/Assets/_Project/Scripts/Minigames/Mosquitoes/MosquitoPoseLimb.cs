using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // Anatomical world-space targets, independent of each imported skeleton's local axes.
    public sealed class MosquitoPoseLimb
    {
        public readonly Transform Upper, Lower, End;
        private readonly Transform finger;
        public bool Valid => Upper != null && Lower != null && End != null;

        public MosquitoPoseLimb(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end)
        {
            Upper = animator.GetBoneTransform(upper);
            Lower = animator.GetBoneTransform(lower);
            End = animator.GetBoneTransform(end);
            if (end == HumanBodyBones.LeftHand) finger = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal)
                ?? animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            else if (end == HumanBodyBones.RightHand) finger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal)
                ?? animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        }

        public Quaternion SupportRotation(Vector3 direction) =>
            Quaternion.FromToRotation(finger != null ? finger.position - End.position : End.position - Lower.position, direction) * End.rotation;

        public void Reach(Vector3 target, Vector3 pole, Quaternion endRotation)
        {
            if (!Valid) return;
            float a = Vector3.Distance(Upper.position, Lower.position);
            float b = Vector3.Distance(Lower.position, End.position);
            Vector3 delta = target - Upper.position;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, (a + b) * .995f);
            Vector3 direction = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.down;
            Vector3 bend = Vector3.ProjectOnPlane(pole - Upper.position, direction).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.Cross(direction, Vector3.right).normalized;
            float along = (a * a + distance * distance - b * b) / (2 * distance);
            Vector3 joint = Upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Upper.rotation = Quaternion.FromToRotation(Lower.position - Upper.position, joint - Upper.position) * Upper.rotation;
            Lower.rotation = Quaternion.FromToRotation(End.position - Lower.position,
                Upper.position + direction * distance - Lower.position) * Lower.rotation;
            End.rotation = endRotation;
        }
    }
}
