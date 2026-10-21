using System;
using System.Collections.Generic;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>Серверная раздача разных ответов без повторов и подсчёт личных совпадений.</summary>
    public static class CansOrderSolutions
    {
        public const int MaxCans = 8;

        public static List<int>[] Deal(int canCount, int playerCount, Random random)
        {
            if (canCount < 1 || canCount > MaxCans) throw new ArgumentOutOfRangeException(nameof(canCount));
            int permutations = Factorial(canCount);
            if (playerCount < 0 || playerCount > permutations) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (random == null) throw new ArgumentNullException(nameof(random));

            // Номер однозначно задаёт перестановку. Берём номера без возвращения:
            // даже неудачная серия случайных чисел не даёт дублей или вечного цикла.
            var remaining = new List<int>(permutations);
            for (int i = 0; i < permutations; i++) remaining.Add(i);
            var answers = new List<int>[playerCount];
            for (int player = 0; player < playerCount; player++)
            {
                int index = random.Next(remaining.Count);
                int rank = remaining[index];
                remaining[index] = remaining[remaining.Count - 1];
                remaining.RemoveAt(remaining.Count - 1);
                var available = new List<int>(canCount);
                for (int i = 0; i < canCount; i++) available.Add(i);
                var answer = new List<int>(canCount);
                for (int slots = canCount; slots > 0; slots--)
                {
                    int block = Factorial(slots - 1);
                    int next = rank / block;
                    rank %= block;
                    answer.Add(available[next]);
                    available.RemoveAt(next);
                }
                answers[player] = answer;
            }
            return answers;
        }

        public static int CountMatches(IReadOnlyList<int> arrangement, IReadOnlyList<int> answer)
        {
            int matches = 0;
            for (int i = 0; i < Math.Min(arrangement.Count, answer.Count); i++)
                if (arrangement[i] == answer[i]) matches++;
            return matches;
        }

        private static int Factorial(int count)
        {
            int result = 1;
            for (int i = 2; i <= count; i++) result *= i;
            return result;
        }
    }
}
