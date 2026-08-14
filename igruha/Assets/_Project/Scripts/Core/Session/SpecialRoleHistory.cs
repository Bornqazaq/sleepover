using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Память катки о том, кто уже побывал в особой роли (Водящий, охотник,
    /// оператор). Чистый рандом выдаёт роль одному и тому же человеку трижды за
    /// вечер, а кому-то ни разу — это заметно портит вечеринку.
    ///
    /// Ключ роли — строка, поэтому истории разных игр независимы. Логика общая
    /// для локального и сетевого табло, отсюда отдельный класс, а не копия в двух.
    /// </summary>
    public sealed class SpecialRoleHistory
    {
        /// <summary>Выбрать некого: пустой ростер.</summary>
        public const int NoPlayer = -1;

        private readonly Dictionary<string, HashSet<int>> playedByRole = new Dictionary<string, HashSet<int>>(4);
        private readonly List<int> candidates = new List<int>(8);

        public bool HasPlayed(string roleKey, int playerId) =>
            playedByRole.TryGetValue(roleKey, out HashSet<int> played) && played.Contains(playerId);

        public void Mark(string roleKey, int playerId) => GetPlayed(roleKey).Add(playerId);

        /// <summary>Игрок ушёл из катки — вычищаем его из всех историй вместе с ростером.</summary>
        public void Forget(int playerId)
        {
            foreach (KeyValuePair<string, HashSet<int>> entry in playedByRole)
            {
                entry.Value.Remove(playerId);
            }
        }

        /// <summary>
        /// Случайный из тех, кто ещё не был в этой роли. Когда побывали все,
        /// история по ключу сбрасывается и круг начинается заново.
        /// Звать только там, где есть авторитет: рандом обязан быть серверным.
        /// </summary>
        public int Pick(string roleKey, IReadOnlyList<SessionPlayer> players)
        {
            if (players == null || players.Count == 0)
            {
                return NoPlayer;
            }

            HashSet<int> played = GetPlayed(roleKey);

            candidates.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                if (!played.Contains(players[i].Id))
                {
                    candidates.Add(players[i].Id);
                }
            }

            if (candidates.Count == 0)
            {
                played.Clear();
                for (int i = 0; i < players.Count; i++)
                {
                    candidates.Add(players[i].Id);
                }
            }

            int picked = candidates[Random.Range(0, candidates.Count)];
            played.Add(picked);
            return picked;
        }

        private HashSet<int> GetPlayed(string roleKey)
        {
            if (!playedByRole.TryGetValue(roleKey, out HashSet<int> played))
            {
                played = new HashSet<int>();
                playedByRole[roleKey] = played;
            }

            return played;
        }
    }
}
