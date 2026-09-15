using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>Local identities and scores survive scene changes just like the network roster.</summary>
    public static class LocalPartyProfile
    {
        private struct Entry { public string Name; public int Character, Score; }
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => entries.Clear();
        public static int PlayerCount
        {
            get { int count = 0; while (count < 8 && entries.ContainsKey(count)) count++; return count; }
        }
        public static int CharacterOf(int id) => entries.TryGetValue(id, out var e) ? e.Character : -1;
        public static void Save(SessionPlayer player)
        {
            entries[player.Id] = new Entry { Name = player.DisplayName, Character = player.CharacterIndex, Score = player.Score };
        }
        public static void Restore(SessionPlayer player)
        {
            if (!entries.TryGetValue(player.Id, out var e)) return;
            player.DisplayName = e.Name; player.CharacterIndex = e.Character; player.Score = e.Score;
        }
    }
}
