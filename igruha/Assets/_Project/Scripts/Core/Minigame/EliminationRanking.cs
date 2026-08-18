using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Места по порядку вылета. Третья игра подряд считает одно и то же,
    /// и у «Ангелов» именно расстановка мест оказалась самой ошибкоёмкой
    /// частью каркаса — поэтому правило живёт здесь, а не в каждой игре.
    ///
    /// Правило одно: место = сколько игроков стоит выше, плюс один.
    /// Из него само собой следует и то, что вылетевшие в одном подраунде
    /// делят место, и то, что следующая группа получает место со сдвигом
    /// на размер предыдущей, а не на единицу.
    ///
    /// Переиспользуемый контейнер: между раундами не аллоцирует.
    /// </summary>
    public sealed class EliminationRanking
    {
        // Группы вылета хранятся плоско: копия входа плюс размеры групп.
        // Ссылки на списки вызывающего хранить нельзя — он их переиспользует.
        private readonly List<int> eliminated = new List<int>(8);
        private readonly List<int> groupSizes = new List<int>(8);
        private readonly List<int> survivors = new List<int>(8);
        private readonly HashSet<int> seen = new HashSet<int>();

        /// <summary>Сколько игроков уже выбыло.</summary>
        public int EliminatedCount => eliminated.Count;

        public void Clear()
        {
            eliminated.Clear();
            groupSizes.Clear();
            survivors.Clear();
            seen.Clear();
        }

        /// <summary>
        /// Очередная группа вылета. Порядок вызовов — хронологический:
        /// первый вызов — те, кто вылетел раньше всех.
        /// </summary>
        public void AddEliminationGroup(IReadOnlyList<int> playerIds)
        {
            if (playerIds == null || playerIds.Count == 0)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < playerIds.Count; i++)
            {
                if (!seen.Add(playerIds[i]))
                {
                    Debug.LogWarning($"EliminationRanking: игрок {playerIds[i]} вылетает второй раз — место ему уже посчитано, пропускаю");
                    continue;
                }

                eliminated.Add(playerIds[i]);
                added++;
            }

            if (added > 0)
            {
                groupSizes.Add(added);
            }
        }

        /// <summary>Игрок дожил до конца. Нескольких выживших даёт только таймаут.</summary>
        public void AddSurvivor(int playerId)
        {
            if (!seen.Add(playerId))
            {
                Debug.LogWarning($"EliminationRanking: игрок {playerId} уже посчитан выбывшим — выжившим его не добавляю");
                return;
            }

            survivors.Add(playerId);
        }

        /// <summary>
        /// Разложить места в <paramref name="results"/>.
        ///
        /// <paramref name="survivorComparison"/> сравнивает выживших между собой:
        /// отрицательное — первый лучше, ноль — полное равенство, и тогда они
        /// делят одно место. Нужен только при таймауте; при обычном финале
        /// выживший один, и компаратор не зовётся.
        /// </summary>
        public void Build(MinigameResults results, Comparison<int> survivorComparison = null)
        {
            if (results == null)
            {
                return;
            }

            int place = 1;

            if (survivors.Count > 0)
            {
                if (survivors.Count > 1 && survivorComparison != null)
                {
                    survivors.Sort(survivorComparison);
                }

                int index = 0;
                while (index < survivors.Count)
                {
                    // Полное равенство по компаратору — одно место на всех,
                    // следующий получает место со сдвигом на размер группы.
                    int tied = 1;
                    while (index + tied < survivors.Count &&
                           survivorComparison != null &&
                           survivorComparison(survivors[index], survivors[index + tied]) == 0)
                    {
                        tied++;
                    }

                    for (int i = 0; i < tied; i++)
                    {
                        results.Add(survivors[index + i], place);
                    }

                    index += tied;
                    place += tied;
                }
            }

            // Выбывшие — от последней группы к первой: кто продержался дольше,
            // тот выше. Внутри группы место общее.
            int cursor = eliminated.Count;
            for (int group = groupSizes.Count - 1; group >= 0; group--)
            {
                int size = groupSizes[group];
                cursor -= size;

                for (int i = 0; i < size; i++)
                {
                    results.Add(eliminated[cursor + i], place);
                }

                place += size;
            }
        }
    }
}
