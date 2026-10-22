#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using Igruha.Minigames.CansOrder;

namespace Igruha.Tests
{
    /// <summary>Только для явно включённых сетевых проверок. Обычная игра ответ не раскрывает.</summary>
    internal static class CansSolutionFixture
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Players = 8;
        public static List<int> Known(int id, int count) => CansOrderSolutions.Deal(count, Players, new Random(73021))[id];

        public static List<int> Answer(CansOrderMinigame game, int id)
        {
            object player = typeof(CansOrderMinigame).GetMethod("Find", Private).Invoke(game, new object[] { id });
            return (List<int>)player.GetType().GetField("Solution").GetValue(player);
        }

        public static void SetKnown(CansOrderMinigame game)
        {
            for (int i = 0; i < game.ContestantCount; i++)
            {
                game.TryGetEntry(i, out var entry, out _);
                var answer = Answer(game, entry.PlayerId);
                answer.Clear(); answer.AddRange(Known(entry.PlayerId, game.Round.CanCount));
            }
        }

        public static bool HasUniqueAnswers(CansOrderMinigame game)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < game.ContestantCount; i++)
            {
                game.TryGetEntry(i, out var entry, out _);
                var answer = Answer(game, entry.PlayerId);
                if (answer.Count != game.Round.CanCount || !seen.Add(string.Join(",", answer))) return false;
            }
            return true;
        }
    }
}
#endif
