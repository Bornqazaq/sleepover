using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Player
{
    /// <summary>Owns temporary avatar parking; restoring twice is harmless.</summary>
    public sealed class PlayerAvatarPark
    {
        private readonly Dictionary<PlayerController, Snapshot> parked = new Dictionary<PlayerController, Snapshot>(8);

        private struct Snapshot
        {
            public bool Active, Locked, InputEnabled, Suspended, ControllerEnabled;
            public PlayerInputReader Input;
            public Renderer[] Renderers;
            public Collider[] Colliders;
            public bool[] RendererEnabled, ColliderEnabled;
            public Rigidbody Body;
            public bool Kinematic, Gravity;
            public Vector3 Velocity, AngularVelocity;
        }

        public int Count => parked.Count;

        public void Park(PlayerController avatar)
        {
            if (avatar == null || parked.ContainsKey(avatar)) return;
            avatar.TryGetComponent(out PlayerInputReader input);
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            var colliders = avatar.GetComponentsInChildren<Collider>(true);
            var body = avatar.GetComponent<Rigidbody>();
            var visible = new bool[renderers.Length]; var solid = new bool[colliders.Length];
            for (int i = 0; i < renderers.Length; i++) { visible[i] = renderers[i].enabled; renderers[i].enabled = false; }
            for (int i = 0; i < colliders.Length; i++) { solid[i] = colliders[i].enabled; colliders[i].enabled = false; }
            parked.Add(avatar, new Snapshot
            {
                Active = avatar.gameObject.activeSelf,
                Locked = avatar.MovementLocked,
                Input = input,
                InputEnabled = input != null && input.enabled,
                Suspended = input != null && input.Suspended,
                ControllerEnabled = avatar.enabled,
                Renderers = renderers, Colliders = colliders, RendererEnabled = visible, ColliderEnabled = solid,
                Body = body, Kinematic = body != null && body.isKinematic, Gravity = body != null && body.useGravity,
                Velocity = body != null ? body.linearVelocity : Vector3.zero, AngularVelocity = body != null ? body.angularVelocity : Vector3.zero
            });
            input?.SetSuspended(true);
            if (input != null) input.enabled = false;
            avatar.MovementLocked = true;
            avatar.enabled = false;
            if (body != null) body.isKinematic = true;
            // Keep the network root alive: NGO must deliver despawn/ownership callbacks during disconnects.
        }

        public void Release(PlayerController avatar)
        {
            if (ReferenceEquals(avatar, null) || !parked.TryGetValue(avatar, out Snapshot old)) return;
            parked.Remove(avatar);
            Restore(avatar, old);
        }

        public void ReleaseAll()
        {
            foreach (KeyValuePair<PlayerController, Snapshot> entry in parked) Restore(entry.Key, entry.Value);
            parked.Clear();
        }

        private static void Restore(PlayerController avatar, Snapshot old)
        {
            if (avatar == null) return;
            avatar.MovementLocked = old.Locked;
            avatar.enabled = old.ControllerEnabled;
            for (int i = 0; i < old.Renderers.Length; i++) if (old.Renderers[i] != null) old.Renderers[i].enabled = old.RendererEnabled[i];
            for (int i = 0; i < old.Colliders.Length; i++) if (old.Colliders[i] != null) old.Colliders[i].enabled = old.ColliderEnabled[i];
            if (old.Input != null)
            {
                old.Input.SetSuspended(old.Suspended);
                old.Input.enabled = old.InputEnabled;
            }
            avatar.gameObject.SetActive(old.Active);
            if (old.Body != null)
            {
                old.Body.isKinematic = old.Kinematic; old.Body.useGravity = old.Gravity;
                if (!old.Kinematic) { old.Body.linearVelocity = old.Velocity; old.Body.angularVelocity = old.AngularVelocity; }
            }
        }
    }
}
