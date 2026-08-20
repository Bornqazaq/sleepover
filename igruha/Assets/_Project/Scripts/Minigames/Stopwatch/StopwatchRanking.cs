using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Замер одного игрока за подраунд.
    /// </summary>
    public struct StopwatchEntry
    {
        public int PlayerId;
        /// <summary>Отмеренный интервал, с. Осмысленно только при <see cref="Completed"/>.</summary>
        public float Seconds;
        /// <summary>Успел ли игрок нажать и старт, и стоп до конца окна.</summary>
        public bool Completed;
    }

    /// <summary>
    /// Рейтинг худших за подраунд и раздача ошибок. Вынесено из контроллера
    /// отдельно: это единственная часть правил, которую можно и нужно
    /// прогонять подготовленными наборами, не запуская игру.
    ///
    /// Подсчёт относительный, а не абсолютный: ошибку получают K худших,
    /// а не те, кто вышел за какой-то порог. Это и гарантирует, что игра
    /// всегда движется к финалу, как бы точны ни были игроки в этом лобби.
    /// </summary>
    public static class StopwatchRanking
    {
        /// <summary>
        /// Отсортировать от худшего к лучшему по правилу типа подраунда.
        /// Список меняется на месте.
        ///
        /// Не завершившие всегда хуже всех: они не просто плохо отмерили —
        /// они не отмеряли вовсе.
        /// </summary>
        public static void SortWorstFirst(List<StopwatchEntry> entries, StopwatchSubroundType type, float target)
        {
            entries.Sort((a, b) => CompareWorstFirst(a, b, type, target));
        }

        private static int CompareWorstFirst(StopwatchEntry a, StopwatchEntry b, StopwatchSubroundType type, float target)
        {
            if (a.Completed != b.Completed)
            {
                return a.Completed ? 1 : -1;
            }

            if (!a.Completed)
            {
                return 0;
            }

            switch (type)
            {
                case StopwatchSubroundType.Ceiling:
                    return CompareCeiling(a.Seconds, b.Seconds, target);
                case StopwatchSubroundType.Floor:
                    return CompareFloor(a.Seconds, b.Seconds, target);
                default:
                    // А — Точность: хуже тот, у кого больше модуль отклонения.
                    return Mathf.Abs(b.Seconds - target).CompareTo(Mathf.Abs(a.Seconds - target));
            }
        }

        /// <summary>
        /// Б — Потолок, «не больше N». Сначала нарушители по убыванию превышения,
        /// затем уложившиеся по возрастанию времени: кто остановился раньше всех,
        /// тот хуже — он перестраховался сильнее прочих.
        /// </summary>
        private static int CompareCeiling(float a, float b, float target)
        {
            bool aOver = a > target;
            bool bOver = b > target;
            if (aOver != bOver)
            {
                return aOver ? -1 : 1;
            }

            return aOver
                ? (b - target).CompareTo(a - target)
                : a.CompareTo(b);
        }

        /// <summary>
        /// В — Пол, «не меньше N». Сначала нарушители по убыванию недобора,
        /// затем перебравшие по убыванию времени: кто перебрал сильнее, тот хуже.
        /// </summary>
        private static int CompareFloor(float a, float b, float target)
        {
            bool aUnder = a < target;
            bool bUnder = b < target;
            if (aUnder != bUnder)
            {
                return aUnder ? -1 : 1;
            }

            return aUnder
                ? (target - b).CompareTo(target - a)
                : b.CompareTo(a);
        }

        /// <summary>
        /// Кто получает ошибку в этом подраунде. <paramref name="sorted"/> —
        /// уже отсортированный от худшего к лучшему список.
        ///
        /// Три поправки к таблице, без которых она ломается на краях:
        /// не завершившие получают ошибку всегда, вне зависимости от K;
        /// ничья на границе K раздаёт ошибку всем с одинаковым результатом;
        /// в лобби из двух порог ничьей отдельный и грубее — иначе на двоих
        /// матч решает разница в единицы миллисекунд.
        /// </summary>
        public static void PickFaulted(List<StopwatchEntry> sorted, StopwatchSubroundType type, float target,
            int worstCount, float twoPlayerTieThreshold, List<int> faulted)
        {
            faulted.Clear();
            if (sorted.Count == 0)
            {
                return;
            }

            // Не завершившие — всегда, сколько бы их ни было. Это же закрывает
            // вопрос «может ли игрок отсидеться»: молчун вылетает за два-три
            // подраунда.
            int incomplete = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Completed)
                {
                    break;
                }

                faulted.Add(sorted[i].PlayerId);
                incomplete++;
            }

            int remaining = worstCount - incomplete;
            if (remaining <= 0)
            {
                return;
            }

            int cursor = incomplete;
            int taken = 0;
            while (cursor < sorted.Count && taken < remaining)
            {
                faulted.Add(sorted[cursor].PlayerId);
                cursor++;
                taken++;
            }

            // Ничья на границе: следующий за границей с тем же результатом
            // получает ошибку наравне — иначе исход решал бы порядок в списке.
            while (cursor < sorted.Count && sorted[cursor].Completed &&
                   IsTie(sorted[cursor - 1], sorted[cursor], type, target, sorted.Count, twoPlayerTieThreshold))
            {
                faulted.Add(sorted[cursor].PlayerId);
                cursor++;
            }
        }

        /// <summary>
        /// Считать ли двоих равными на границе K. В лобби из двух порог грубее
        /// (по умолчанию 50 мс): без него матч на двоих решала бы разница
        /// в единицы миллисекунд, которую человек не контролирует. Тогда же
        /// возможен сценарий, где оба падают в одном подраунде и получают
        /// одинаковые очки.
        /// </summary>
        private static bool IsTie(StopwatchEntry a, StopwatchEntry b, StopwatchSubroundType type, float target,
            int playerCount, float twoPlayerTieThreshold)
        {
            if (playerCount == 2)
            {
                // Сравнивать по величине можно только внутри одной категории:
                // на «Потолке» нарушитель с превышением 0.1 и уложившийся
                // с запасом 0.1 дают одинаковую величину, но нарушитель хуже
                // всегда, и ничьей между ними быть не может.
                if (IsViolator(a.Seconds, type, target) != IsViolator(b.Seconds, type, target))
                {
                    return false;
                }

                return Mathf.Abs(Metric(a.Seconds, type, target) - Metric(b.Seconds, type, target)) < twoPlayerTieThreshold;
            }

            return CompareWorstFirst(a, b, type, target) == 0;
        }

        /// <summary>Нарушил ли игрок условие подраунда. На «Точности» нарушителей нет.</summary>
        private static bool IsViolator(float seconds, StopwatchSubroundType type, float target)
        {
            switch (type)
            {
                case StopwatchSubroundType.Ceiling:
                    return seconds > target;
                case StopwatchSubroundType.Floor:
                    return seconds < target;
                default:
                    return false;
            }
        }

        /// <summary>Величина, по которой игроки сравниваются между собой в этом типе подраунда.</summary>
        private static float Metric(float seconds, StopwatchSubroundType type, float target)
        {
            switch (type)
            {
                case StopwatchSubroundType.Ceiling:
                    return seconds > target ? seconds - target : target - seconds;
                case StopwatchSubroundType.Floor:
                    return seconds < target ? target - seconds : seconds - target;
                default:
                    return Mathf.Abs(seconds - target);
            }
        }
    }
}
