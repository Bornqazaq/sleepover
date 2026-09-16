using System;
using System.Collections.Generic;
using Igruha.Core.Scenes;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>Host-owned finite playlist. The queue survives scenes and consumes each scene once.</summary>
    public static class PartySeries
    {
        private static readonly Queue<MinigameDefinition> remaining = new Queue<MinigameDefinition>();
        public static bool Active { get; private set; }
        public static int Total { get; private set; }
        public static int Round { get; private set; }
        public static string Summary { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            remaining.Clear(); Active = false; Total = Round = 0; Summary = string.Empty;
        }

        public static List<MinigameDefinition> BuildQueue(MinigameCatalog catalog, int count, System.Random random,
            Func<string, bool> available)
        {
            var list = new List<MinigameDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null) return list;
            foreach (var game in catalog.Games)
                if (game != null && !string.IsNullOrEmpty(game.SceneName) && count >= game.MinPlayers &&
                    count <= game.MaxPlayers && available(game.SceneName) && seen.Add(game.SceneName)) list.Add(game);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1); var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
            return list;
        }

        public static int AvailableCount(MinigameCatalog catalog, int count) =>
            BuildQueue(catalog, count, new System.Random(0), IsInBuild).Count;
        private static bool IsInBuild(string scene) => BuildSceneCatalog.TryResolvePath(scene, out _);

        public static bool Start(MinigameCatalog catalog, MinigameLoader loader)
        {
            var session = SessionScoreboard.Current;
            if (Active || loader == null || session == null || !session.HasAuthority) return false;
            foreach (var player in session.Players) if (player.Avatar == null) return false;
            var queue = BuildQueue(catalog, session.Players.Count, new System.Random(), IsInBuild);
            if (queue.Count == 0) return false;
            Reset(); foreach (var game in queue) remaining.Enqueue(game);
            Active = true; Total = remaining.Count;
            (session as ISessionScoreReset)?.ResetScores();
            return Advance(loader);
        }

        public static bool Advance(MinigameLoader loader)
        {
            if (!Active || SessionScoreboard.Current?.HasAuthority != true) return false;
            int count = SessionScoreboard.Current.Players.Count;
            while (remaining.Count > 0)
            {
                var game = remaining.Dequeue();
                // A disconnect can make a later game unsuitable. Do not repeat earlier games to fill it.
                if (count < game.MinPlayers || count > game.MaxPlayers) continue;
                if (!loader.TryLoad(game))
                {
                    Summary = "Серия остановлена: не удалось загрузить следующую игру.";
                    Active = false; remaining.Clear(); return false;
                }
                Round++;
                Debug.Log($"PARTY SERIES {Round}/{Total}: {game.SceneName}");
                return true;
            }
            Active = false;
            Summary = $"Серия завершена · сыграно {Round} из {Total}";
            Debug.Log("PARTY SERIES COMPLETE: " + Summary);
            return false;
        }
    }
}
