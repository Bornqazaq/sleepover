using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>A visible transverse seam, sampled by the cart's four wheel paths.
    /// No trigger volumes or physical steps: the smooth chassis collider cannot snag.</summary>
    public sealed class CartRoadJoint : MonoBehaviour
    {
        private const float ContactHeightTolerance=.38f;
        private static readonly List<CartRoadJoint> active = new List<CartRoadJoint>();
        [SerializeField, Min(.1f)] private float width = 5.4f;
        [SerializeField, Range(.1f, 2f)] private float severity = 1f;
        public float Width => width;
        public float Severity => severity;
        public static IReadOnlyList<CartRoadJoint> Active => active;
        public void Configure(float span, float strength) { width = span; severity = strength; }
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        // Local X is travel across the seam, Z runs along its visible metal lip.
        // Half-open crossing handles a wheel exactly on the line only once.
        public bool Crossed(Vector3 from, Vector3 to, out float approach)
        {
            approach = 0f;
            var a = transform.InverseTransformPoint(from);
            var b = transform.InverseTransformPoint(to);
            if (!((a.x < 0f && b.x >= 0f) || (a.x >= 0f && b.x < 0f))) return false;
            float t = -a.x / (b.x - a.x);
            Vector3 hit = Vector3.Lerp(a, b, t);
            if (Mathf.Abs(hit.z) > width * .5f || Mathf.Abs(hit.y) > ContactHeightTolerance) return false;
            approach = Mathf.Abs(Vector3.Dot((to - from).normalized, transform.right));
            return true;
        }
    }
}
