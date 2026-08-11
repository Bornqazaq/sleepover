using System.Collections.Generic;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Итог мини-игры: место каждого игрока. Очки по местам считает SessionManager.
    /// Переиспользуемый контейнер (без аллокаций между раундами).
    /// </summary>
    public sealed class MinigameResults
    {
        public readonly struct PlayerResult
        {
            public int PlayerId { get; }
            /// <summary>Место, начиная с 1. Ничьи (одинаковое место) — позже, структура не закрыта.</summary>
            public int Place { get; }

            public PlayerResult(int playerId, int place)
            {
                PlayerId = playerId;
                Place = place;
            }
        }

        private readonly List<PlayerResult> entries = new List<PlayerResult>(8);

        public IReadOnlyList<PlayerResult> Entries => entries;

        public void Clear() => entries.Clear();

        public void Add(int playerId, int place) => entries.Add(new PlayerResult(playerId, place));
    }
}
