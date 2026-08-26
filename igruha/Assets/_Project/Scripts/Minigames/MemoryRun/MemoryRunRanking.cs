using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.MemoryRun
{
    /// <summary>
    /// Места всем участникам «Рейса на память» — от 1 до числа игроков, без
    /// дырок и без дублей.
    ///
    /// <b>Почему не <see cref="EliminationRanking"/>.</b> Тот считает места по
    /// порядку вылета: кто вылетел раньше, тот ниже. Здесь ровно наоборот —
    /// смерть ничего не решает, игрок возвращается в очередь и продолжает,
    /// а место определяет прогресс. Общий класс тут дал бы неверный ответ,
    /// а не сэкономил работу.
    ///
    /// Переиспользуемый контейнер: между раундами не аллоцирует.
    /// </summary>
    public sealed class MemoryRunRanking
    {
        private readonly List<MemoryRunProgress> sorted = new List<MemoryRunProgress>(8);
        private System.Comparison<MemoryRunProgress> comparison;
        private TurnQueue queue;

        /// <summary>
        /// Расставить места. <paramref name="turnQueue"/> нужен только для
        /// последнего тайбрейка и может быть null — тогда при полном равенстве
        /// порядок определит сортировка, что для двух неходивших игроков
        /// всё равно даст разные места.
        /// </summary>
        public void Fill(MinigameResults results, IReadOnlyList<MemoryRunProgress> records, TurnQueue turnQueue)
        {
            results.Clear();
            if (records == null || records.Count == 0)
            {
                return;
            }

            queue = turnQueue;
            comparison ??= Compare;

            sorted.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                sorted.Add(records[i]);
            }

            sorted.Sort(comparison);

            // Место — просто позиция в отсортированном списке. Ничьих не бывает
            // по построению: четыре критерия подряд не могут совпасть у двоих,
            // последний из них — уникальная позиция в очереди.
            for (int i = 0; i < sorted.Count; i++)
            {
                results.Add(sorted[i].PlayerId, i + 1);
            }
        }

        /// <summary>
        /// Четыре критерия по приоритету:
        /// 1. дошёл до двери — выше любого недошедшего, между собой по прибытию;
        /// 2. не дошёл — по самому дальнему достигнутому шагу;
        /// 3. равный шаг — кто достиг его раньше по времени;
        /// 4. равное всё — по позиции в очереди.
        ///
        /// Четвёртый добавлен сверх LDD. Без него двое, до кого ход не дошёл
        /// из-за общего таймера, делят место — а движку нужно место каждому.
        /// </summary>
        private int Compare(MemoryRunProgress a, MemoryRunProgress b)
        {
            if (a.Finished != b.Finished)
            {
                return a.Finished ? -1 : 1;
            }

            if (a.Finished && b.Finished)
            {
                return a.ArrivalOrder.CompareTo(b.ArrivalOrder);
            }

            if (a.BestStep != b.BestStep)
            {
                return b.BestStep.CompareTo(a.BestStep);
            }

            // Оба на нуле — сравнивать время бессмысленно: его никто не ставил.
            if (a.BestStep > 0 && !Mathf.Approximately((float)a.BestStepTime, (float)b.BestStepTime))
            {
                return a.BestStepTime.CompareTo(b.BestStepTime);
            }

            int positionA = queue?.PositionOf(a.PlayerId) ?? 0;
            int positionB = queue?.PositionOf(b.PlayerId) ?? 0;
            if (positionA != positionB)
            {
                return positionA.CompareTo(positionB);
            }

            return a.PlayerId.CompareTo(b.PlayerId);
        }
    }
}
