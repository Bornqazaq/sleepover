using System;
using System.Collections.Generic;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>Restrictions belong to this circus round, including every remote avatar on the server.</summary>
    public sealed class CircusActionRestrictions : IDisposable
    {
        private struct Entry
        {
            public PlayerController Player;
            public PlayerPushAbility Push;
            public bool JumpWasSuppressed, PushWasSuppressed;
        }
        private readonly List<Entry> entries = new List<Entry>(8);

        public void Bind(PlayerController player)
        {
            if (player == null) return;
            foreach (var entry in entries) if (entry.Player == player) return;
            var push = player.GetComponent<PlayerPushAbility>();
            entries.Add(new Entry { Player = player, Push = push,
                JumpWasSuppressed = player.JumpInputSuppressed,
                PushWasSuppressed = push != null && push.InputSuppressed });
            player.JumpInputSuppressed = true;
            if (push != null) push.InputSuppressed = true;
        }

        public void Dispose()
        {
            foreach (var entry in entries)
            {
                if (entry.Player != null) entry.Player.JumpInputSuppressed = entry.JumpWasSuppressed;
                if (entry.Push != null) entry.Push.InputSuppressed = entry.PushWasSuppressed;
            }
            entries.Clear();
        }
    }
}
