using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Player;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local visuals follow replicated position and load; no gameplay authority.</summary>
    [RequireComponent(typeof(WaterCart))]
    public sealed class WaterCartPresentation : MonoBehaviour
    {
        [SerializeField] private Transform tub;
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float wheelRadius = 0.22f;
        [SerializeField] private float loadedSag = 0.025f;
        private WaterCart cart;
        private MultiCarryObject carry;
        private readonly WaterCartGripPose[] poses = new WaterCartGripPose[MultiCarryObject.MaxHandles];
        private Vector3 previousPosition;
        private Vector3 tubRest;
        private float angle;

        private void Awake()
        {
            cart = GetComponent<WaterCart>();
            carry = GetComponent<MultiCarryObject>();
            previousPosition = transform.position;
            if (tub != null) tubRest = tub.localPosition;
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
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            // Respawn teleports must not spin the wheels at impossible speeds.
            if (delta.sqrMagnitude < 1f)
                angle = Mathf.Repeat(angle + Vector3.Dot(delta, transform.forward) / wheelRadius * Mathf.Rad2Deg, 360f);
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (tub != null) tub.localPosition = tubRest + Vector3.down * (loadedSag * cart.Load);
        }
    }
}
