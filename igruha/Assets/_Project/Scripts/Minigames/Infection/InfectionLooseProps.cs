using Unity.Netcode.Components;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Core.Session;

namespace Igruha.Minigames.Infection
{
    /// <summary>Six scene-owned pickups. Core owns pickup/throw; the server recovers escaped props.</summary>
    public sealed class InfectionLooseProps : MonoBehaviour
    {
        [SerializeField] private PickupItem[] items;
        [SerializeField] private float recoveryDepth = -4f;
        [SerializeField] private float recoveryRadius = 35f;
        private Vector3[] starts;
        private Quaternion[] rotations;
        private Rigidbody[] bodies;
        private NetworkTransform[] transports;

        private void Awake()
        {
            int count = items == null ? 0 : items.Length;
            starts = new Vector3[count]; rotations = new Quaternion[count];
            bodies = new Rigidbody[count]; transports = new NetworkTransform[count];
            for (int i = 0; i < count; i++)
            {
                if (items[i] == null) continue;
                starts[i] = items[i].transform.position; rotations[i] = items[i].transform.rotation;
                bodies[i] = items[i].GetComponent<Rigidbody>();
                transports[i] = items[i].GetComponent<NetworkTransform>();
            }
        }

        private void FixedUpdate()
        {
            if (!WorldAuthority.HasAuthority) return;
            for (int i = 0; i < starts.Length; i++)
            {
                if (items[i] == null || items[i].IsHeld) continue;
                Vector3 position = items[i].transform.position;
                if (position.y >= recoveryDepth && (position - starts[i]).sqrMagnitude < recoveryRadius * recoveryRadius) continue;
                if (items[i].IsSpawned && transports[i] != null)
                    transports[i].Teleport(starts[i], rotations[i], items[i].transform.localScale);
                else items[i].transform.SetPositionAndRotation(starts[i], rotations[i]);
                if (bodies[i] != null && !bodies[i].isKinematic)
                { bodies[i].position = starts[i]; bodies[i].linearVelocity = Vector3.zero; bodies[i].angularVelocity = Vector3.zero; }
            }
        }
    }
}
