using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Player;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local visuals follow replicated position and load; no gameplay authority.</summary>
    [RequireComponent(typeof(WaterCart)), DefaultExecutionOrder(-5)]
    public sealed class WaterCartPresentation : MonoBehaviour
    {
        [SerializeField] private Transform tub;
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float wheelRadius = 0.22f;
        [SerializeField] private float loadedSag = 0.025f;
        [SerializeField] private ParticleSystem wheelDust;
        [SerializeField] private AudioSource strainLoop;
        [SerializeField] private Transform[] handleSupports;
        private WaterCart cart;
        private MultiCarryObject carry;
        private readonly WaterCartGripPose[] poses = new WaterCartGripPose[MultiCarryObject.MaxHandles];
        private Vector3 previousPosition;
        private Vector3 tubRest;
        private float angle;
        private Transform visualFrame;
        public Quaternion Heading => visualFrame != null ? visualFrame.rotation : transform.rotation;

        private void Awake()
        {
            cart = GetComponent<WaterCart>();
            carry = GetComponent<MultiCarryObject>();
            previousPosition = transform.position;
            if (tub != null) tubRest = tub.localPosition;
            // Graphics use the carrier's simulation clock. Keep the network object,
            // collider and authoritative rigidbody on their original root.
            visualFrame = new GameObject("CartVisualFrame").transform;
            visualFrame.SetParent(transform, false);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != visualFrame) child.SetParent(visualFrame, true);
            }
            carry.SetPresentationFrame(visualFrame);
            // Fixed handles belong to the wheeled chassis, not to the suspended tank.
            // Tilting their meshes would leave the players holding empty air.
            if (tub != null)
            {
                Transform handles = tub.Find("Handles");
                if (handles != null) handles.SetParent(visualFrame, true);
            }
            if (strainLoop != null) { strainLoop.Stop(); strainLoop.enabled = false; }
        }

        private void OnEnable()
        {
            carry.HandleTaken += OnTaken;
            carry.HandleReleased += OnReleased;
            for (int i = 0; i < carry.HandleCount; i++)
                if (carry.CarrierAt(i) != null) OnTaken(i, carry.CarrierAt(i));
        }

        private void OnTaken(int slot, PlayerController player)
        {
            if (!player.TryGetComponent<WaterCartGripPose>(out var pose))
                pose = player.gameObject.AddComponent<WaterCartGripPose>();
            poses[slot] = pose;
            pose.Hold(carry);
        }

        private void OnReleased(int slot, PlayerController player, CarryReleaseReason reason)
        {
            if (player != null && player.TryGetComponent<WaterCartGripPose>(out var pose)) pose.Release(carry);
            poses[slot] = null;
        }

        private void OnDisable()
        {
            carry.HandleTaken -= OnTaken;
            carry.HandleReleased -= OnReleased;
            for (int i = 0; i < poses.Length; i++)
                if (poses[i] != null) { poses[i].Release(carry); poses[i] = null; }
        }

        private void LateUpdate()
        {
            if (carry.TryGetOwnedPresentationPose(out Vector3 position, out Quaternion rotation))
                visualFrame.SetPositionAndRotation(position, rotation);
            else
            {
                float blend = 1f - Mathf.Exp(-Time.deltaTime * 20f);
                visualFrame.localPosition = Vector3.Lerp(visualFrame.localPosition, Vector3.zero, blend);
                visualFrame.localRotation = Quaternion.Slerp(visualFrame.localRotation, Quaternion.identity, blend);
            }
            Vector3 delta = visualFrame.position - previousPosition;
            previousPosition = visualFrame.position;
            // Respawn teleports must not spin the wheels at impossible speeds.
            if (delta.sqrMagnitude < 1f)
                angle = Mathf.Repeat(angle + Vector3.Dot(delta, visualFrame.forward) / wheelRadius * Mathf.Rad2Deg, 360f);
            bool straining = cart.Stability.IsStraining && delta.sqrMagnitude > 0.000001f && !cart.IsLost;
            if (straining) angle = Mathf.Repeat(angle + 160f * Time.deltaTime, 360f);
            if (wheelDust != null && wheelDust.isEmitting != straining)
            {
                if (straining) wheelDust.Play();
                else wheelDust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (tub != null)
            {
                tub.localRotation = CartWaterSurface.BodyRotation(cart.Stability.State.BodySlope, Heading);
                Vector3 pivot = Vector3.up * 0.44f;
                tub.localPosition = tubRest + pivot - tub.localRotation * pivot - Vector3.up * (loadedSag * cart.Load);
            }
            if (handleSupports == null) return;
            for (int i = 0; i < handleSupports.Length; i++)
            {
                Transform support = handleSupports[i];
                if (support == null) continue;
                int slot = i / 2;
                bool visible = slot < carry.HandleCount;
                if (support.gameObject.activeSelf != visible) support.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 anchor = carry.HandleAnchor(slot);
                Vector3 inward = Vector3.ProjectOnPlane(anchor - carry.VisualStationOf(slot), Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, inward);
                Vector3 top = anchor + right * (i % 2 == 0 ? -0.21f : 0.21f) - Vector3.up * 0.1f;
                Vector3 foot = visualFrame.InverseTransformPoint(top);
                foot = new Vector3(Mathf.Clamp(foot.x, -0.36f, 0.36f), 0.32f, Mathf.Clamp(foot.z, -0.47f, 0.47f));
                Vector3 bottom = visualFrame.TransformPoint(foot);
                support.SetPositionAndRotation((top + bottom) * 0.5f, Quaternion.FromToRotation(Vector3.up, top - bottom));
                support.localScale = new Vector3(0.035f, Vector3.Distance(top, bottom) * 0.5f, 0.035f);
            }
        }
    }
}
