using System.Collections.Generic;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Таблица катки: места по сумме очков за все сыгранные игры.
    ///
    /// Правило то же, что у мест внутри раунда (<c>ScoreRanking</c>):
    /// <b>место = сколько игроков набрало строго больше, плюс один</b>.
    /// Равная сумма — общее место. Тай-брейк за корону по GDD 4.14 — бой
    /// подушками, он ещё не собран (IGR-81), поэтому пока делят и корону.
    ///
    /// Порядок строк на экране внутри общего места: больше побед — выше,
    /// дальше порядок ростера. На место это не влияет — только на то, кто
    /// в списке первым.
    ///
    /// Переиспользуемый контейнер: между показами не аллоцирует.
    /// </summary>
    public sealed class SessionStandings
    {
        public readonly struct Entry
        {
            public int PlayerId { get; }
            public int Place { get; }
            public int Score { get; }

            /// <summary>Сколько раз брал первое место за катку.</summary>
            public int Wins { get; }

            public Entry(int playerId, int place, int score, int wins)
            {
                PlayerId = playerId;
                Place = place;
                Score = score;
                Wins = wins;
            }
        }

        private readonly List<Entry> entries = new List<Entry>(8);
        private readonly List<int> scores = new List<int>(8);
        private readonly List<int> wins = new List<int>(8);
        private readonly List<int> ids = new List<int>(8);

        /// <summary>Строки по возрастанию места.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Сколько раундов катки засчитано.</summary>
        public int RoundsPlayed { get; private set; }

        /// <summary>Сколько участников делят первое место. Ноль — таблица пуста или никто не набрал очков.</summary>
        public int LeaderCount { get; private set; }

        /// <summary>За корону ничья: первое место делят.</summary>
        public bool IsTie => LeaderCount > 1;

        public void Clear()
        {
            entries.Clear();
            scores.Clear();
            wins.Clear();
            ids.Clear();
            RoundsPlayed = 0;
            LeaderCount = 0;
        }

        public void Rebuild(ISessionScoreboard scoreboard)
        {
            if (scoreboard == null)
            {
                Clear();
                return;
            }

            Rebuild(scoreboard.Players, scoreboard.History);
        }

        public void Rebuild(IReadOnlyList<SessionPlayer> players, IReadOnlyList<SessionRoundRecord> history)
        {
            Clear();
            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Count; i++)
            {
                ids.Add(players[i].Id);
                scores.Add(players[i].Score);
                wins.Add(0);
            }

            if (history != null)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    SessionRoundRecord record = history[i];
                    if (record.Round > RoundsPlayed)
                    {
                        RoundsPlayed = record.Round;
                    }

                    if (record.Place != 1)
                    {
                        continue;
                    }

                    int index = ids.IndexOf(record.PlayerId);
                    if (index >= 0)
                    {
                        wins[index]++;
                    }
                }
            }

            int count = ids.Count;
            for (int i = 0; i < count; i++)
            {
                int higher = 0;
                for (int j = 0; j < count; j++)
                {
                    if (scores[j] > scores[i])
                    {
                        higher++;
                    }
                }

                entries.Add(new Entry(ids[i], higher + 1, scores[i], wins[i]));
            }

            // Сортировка вставками: восемь строк, без аллокаций и без LINQ.
            for (int i = 1; i < entries.Count; i++)
            {
                Entry current = entries[i];
                int j = i - 1;
                while (j >= 0 && Compare(entries[j], current) > 0)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }

                entries[j + 1] = current;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Place == 1 && entries[i].Score > 0)
                {
                    LeaderCount++;
                }
            }
        }

        /// <summary>Делит ли участник первое место. Ложь, если очков нет ни у кого.</summary>
        public bool IsLeader(int playerId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId == playerId)
                {
                    return entries[i].Place == 1 && entries[i].Score > 0;
                }
            }

            return false;
        }

        private static int Compare(Entry a, Entry b)
        {
            if (a.Place != b.Place)
            {
                return a.Place.CompareTo(b.Place);
            }

            // Внутри общего места: больше побед — выше по списку.
            return b.Wins.CompareTo(a.Wins);
        }
    }
}
