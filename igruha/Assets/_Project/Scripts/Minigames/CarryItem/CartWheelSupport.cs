using Igruha.Core.Items;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Four wheel contacts orient the physical chassis; airborne carts keep falling.</summary>
    [DefaultExecutionOrder(5), RequireComponent(typeof(MultiCarryObject), typeof(Rigidbody))]
    public sealed class CartWheelSupport : MonoBehaviour
    {
        public const float HalfTrack = 0.48f, HalfBase = 0.4f, Radius = 0.22f;
        private const float RayLift = 0.4f, SupportGap = 0.12f, MinNormalY = 0.8f;
        private const float LaunchSpeed = 1.5f;
        private readonly Vector3[] points = new Vector3[4];
        private readonly bool[] hits = new bool[4];
        private Rigidbody body;
        private MultiCarryObject carry;
        private WaterCart cart;
        private int groundMask;
        public bool Supported { get; private set; }
        public Vector3 Normal { get; private set; } = Vector3.up;

        private void Awake()
        {
            body = GetComponent<Rigidbody>(); carry = GetComponent<MultiCarryObject>();
            cart = GetComponent<WaterCart>();
            groundMask = LayerMask.GetMask("Ground");
        }

        private void FixedUpdate()
        {
            if (body.isKinematic || cart != null && !cart.IsAuthority) return;
            int count = 0;
            Vector3 normal = Vector3.zero;
            float clearance = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                var center = body.position + body.rotation * new Vector3(i % 2 == 0 ? -HalfTrack : HalfTrack,
                    Radius, i < 2 ? HalfBase : -HalfBase);
                hits[i] = Physics.Raycast(center + Vector3.up * RayLift, Vector3.down, out RaycastHit hit,
                    RayLift + Radius + SupportGap, groundMask, QueryTriggerInteraction.Ignore) && hit.normal.y >= MinNormalY;
                if (!hits[i]) continue;
                points[i] = hit.point; normal += hit.normal; count++;
                clearance = Mathf.Min(clearance, Vector3.Dot(center - hit.point, hit.normal) - Radius);
            }
            Supported = count >= 2 && !carry.InFlight;
            if (Supported)
            {
                normal.Normalize();
                if (count == 4)
                {
                    Vector3 forward = points[0] + points[1] - points[2] - points[3];
                    Vector3 right = points[1] + points[3] - points[0] - points[2];
                    Vector3 plane = Vector3.Cross(forward, right).normalized;
                    if (plane.y >= MinNormalY) normal = plane;
                }
                // A trap's upward impulse must not be absorbed by road adhesion.
                Supported = Vector3.Dot(body.linearVelocity, normal) < LaunchSpeed;
            }
            Normal = Supported ? normal : Vector3.up;
            carry.SetRollingSupport(Normal, Supported, clearance);
        }

        private void OnDisable()
        {
            if (carry != null) carry.SetRollingSupport(Vector3.up, false);
        }
    }
}
