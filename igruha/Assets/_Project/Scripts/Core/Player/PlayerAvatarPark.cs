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
            public bool Active, Locked, InputEnabled, Suspended;
            public PlayerInputReader Input;
        }

        public int Count => parked.Count;

        public void Park(PlayerController avatar)
        {
            if (avatar == null || parked.ContainsKey(avatar)) return;
            avatar.TryGetComponent(out PlayerInputReader input);
            parked.Add(avatar, new Snapshot
            {
                Active = avatar.gameObject.activeSelf,
                Locked = avatar.MovementLocked,
                Input = input,
                InputEnabled = input != null && input.enabled,
                Suspended = input != null && input.Suspended
            });
            input?.SetSuspended(true);
            avatar.MovementLocked = true;
            avatar.gameObject.SetActive(false);
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
            if (old.Input != null)
            {
                old.Input.SetSuspended(old.Suspended);
                old.Input.enabled = old.InputEnabled;
            }
            avatar.gameObject.SetActive(old.Active);
        }
    }
}
