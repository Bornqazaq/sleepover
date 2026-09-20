using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Локальная катка без сети: имена, облики, счёт, журнал раундов и
    /// чемпионы переживают смену сцен так же, как сетевой ростер. Сетевая
    /// катка сюда не пишет — у неё всё это в NetworkList.
    /// </summary>
    public static class LocalPartyProfile
    {
        private struct Entry { public string Name; public int Character, Score; }
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private static readonly List<SessionRoundRecord> history = new List<SessionRoundRecord>(64);
        private static readonly List<int> champions = new List<int>(8);

        /// <summary>Журнал катки локального режима. Пишет только SessionManager.</summary>
        public static List<SessionRoundRecord> History => history;

        /// <summary>Чемпионы последней доигранной серии локального режима.</summary>
        public static List<int> Champions => champions;

        /// <summary>Сколько игр засчитано в локальной катке.</summary>
        public static int RoundsPlayed { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            entries.Clear();
            history.Clear();
            champions.Clear();
            RoundsPlayed = 0;
        }

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
