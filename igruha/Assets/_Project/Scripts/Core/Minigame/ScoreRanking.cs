using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Места по числу набранных очков. Четвёртая игра подряд считает одно и
    /// то же — <c>CanOrderRanking</c>, <c>ExamRanking</c>,
    /// <c>StopwatchRanking</c> написали это каждая у себя, — а у «Ангелов»
    /// именно расстановка мест оказалась самой ошибкоёмкой частью каркаса.
    /// Правило переезжает сюда.
    ///
    /// Правило одно: <b>место = сколько игроков набрало строго больше,
    /// плюс один.</b> Из него само собой следует и то, что равные счета делят
    /// место, и то, что следующая группа получает место со сдвигом на размер
    /// предыдущей, а не на единицу.
    ///
    /// Очки начисляет <c>SessionManager</c> по общей формуле
    /// «очки = число игроков − место», и подгонять их здесь не надо.
    ///
    /// <c>EliminationRanking</c> не подходит: он считает места по порядку
    /// вылета, а здесь выбывания может не быть вовсе.
    ///
    /// Переиспользуемый контейнер: между раундами не аллоцирует.
    /// </summary>
    public sealed class ScoreRanking
    {
        // Плоские параллельные списки, а не список пар: между раундами они
        // только чистятся и заполняются заново, то есть мусора не дают.
        private readonly List<int> playerIds = new List<int>(8);
        private readonly List<int> scores = new List<int>(8);
        private readonly HashSet<int> seen = new HashSet<int>();

        /// <summary>Сколько участников уже учтено.</summary>
        public int Count => playerIds.Count;

        public void Clear()
        {
            playerIds.Clear();
            scores.Clear();
            seen.Clear();
        }

        /// <summary>
        /// Учесть участника с его счётом. Отключившиеся добавляются наравне
        /// со всеми, по счёту на момент выхода: место им тоже нужно.
        /// </summary>
        public void Add(int playerId, int score)
        {
            if (!seen.Add(playerId))
            {
                Debug.LogWarning($"ScoreRanking: игрок {playerId} добавлен второй раз — место ему уже посчитано, пропускаю");
                return;
            }

            playerIds.Add(playerId);
            scores.Add(score);
        }

        /// <summary>
        /// Разложить места в <paramref name="results"/>.
        ///
        /// <b>Отдельный случай — когда лучший счёт равен нулю:</b> все получают
        /// место, равное числу игроков, и ноль очков сессии. Отдать всем первое
        /// место за общий провал нельзя — по формуле <c>SessionManager</c> это
        /// выдало бы максимум очков. Тот же приём, что
        /// <c>TeamRanking.FillNoWinner</c>.
        /// </summary>
        public void Build(MinigameResults results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();

            int count = playerIds.Count;
            if (count == 0)
            {
                return;
            }

            int best = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (scores[i] > best)
                {
                    best = scores[i];
                }
            }

            if (best <= 0)
            {
                for (int i = 0; i < count; i++)
                {
                    results.Add(playerIds[i], count);
                }

                return;
            }

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

                results.Add(playerIds[i], higher + 1);
            }
        }
    }
}
