using System.Collections.Generic;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Сортировки «Порядка банок». Вынесены из контроллера, потому что правил
    /// сортировки в игре три и путать их нельзя: одно решает, кого показать
    /// на табло, второе — кого выбить при равенстве, третье — как разложить
    /// выживших при жёстком таймауте.
    ///
    /// Все правила <b>детерминированы</b>: при равенстве решает идентификатор
    /// игрока. Поэтому табло выглядит одинаково на всех машинах без отдельной
    /// синхронизации порядка строк (спека 5.3).
    /// </summary>
    public static class CanOrderRanking
    {
        /// <summary>
        /// Порядок для табло: больше совпадений — выше, при равенстве — меньший
        /// идентификатор игрока.
        ///
        /// В выборку попадают только те, кто <b>подтвердил и не собрал</b>.
        /// Собравшие не попадают в тройку никогда, и их расстановка не
        /// показывается вообще: она и есть ответ (спека 5.3).
        /// </summary>
        public static int CompareForBoard(CansOrderEntry a, CansOrderEntry b)
        {
            int byMatches = b.Matches.CompareTo(a.Matches);
            return byMatches != 0 ? byMatches : a.PlayerId.CompareTo(b.PlayerId);
        }

        /// <summary>
        /// Кого выбить, когда собрали все в одном круге: хуже тот, кто потратил
        /// больше попыток; при равенстве — тот, кто подтвердил позже
        /// (спека 5.6). Худшие идут первыми.
        /// </summary>
        public static void SortWorstFirstBySpentAttempts(List<CansOrderEntry> entries)
        {
            entries.Sort(CompareWorstByAttempts);
        }

        private static int CompareWorstByAttempts(CansOrderEntry a, CansOrderEntry b)
        {
            int byAttempts = b.Attempts.CompareTo(a.Attempts);
            if (byAttempts != 0)
            {
                return byAttempts;
            }

            int byTime = b.ConfirmTime.CompareTo(a.ConfirmTime);
            return byTime != 0 ? byTime : a.PlayerId.CompareTo(b.PlayerId);
        }

        /// <summary>
        /// Кого выбить при исчерпании потолка кругов: хуже тот, у кого меньше
        /// лучшее достигнутое за раунд число совпадений; при равенстве — тот,
        /// кто достиг его позже (спека 5.6). Худшие идут первыми.
        ///
        /// Лучший счёт −1 означает «не подтвердил ни разу»: такой хуже любого,
        /// кто подтверждал, — включая подтвердившего с нулём попаданий. Ноль
        /// это результат, отсутствие результата — нет.
        /// </summary>
        public static void SortWorstFirstByBestMatches(List<CansOrderEntry> entries)
        {
            entries.Sort(CompareWorstByBest);
        }

        private static int CompareWorstByBest(CansOrderEntry a, CansOrderEntry b)
        {
            int byBest = a.BestMatches.CompareTo(b.BestMatches);
            if (byBest != 0)
            {
                return byBest;
            }

            int byCircle = b.BestCircle.CompareTo(a.BestCircle);
            return byCircle != 0 ? byCircle : a.PlayerId.CompareTo(b.PlayerId);
        }

        /// <summary>
        /// Сравнение выживших при жёстком таймауте мини-игры, сверху вниз
        /// (спека 6.5):
        /// 1. собрал расстановку — выше не собравшего;
        /// 2. среди собравших: меньше попыток — выше, при равенстве раньше
        ///    подтвердил — выше;
        /// 3. среди не собравших: больше лучшее число совпадений — выше,
        ///    при равенстве раньше достиг — выше;
        /// 4. полное равенство — ноль, и они делят место.
        ///
        /// Возвращает отрицательное, если первый лучше. Именно такой контракт
        /// ждёт <c>EliminationRanking.Build</c>.
        /// </summary>
        public static int CompareSurvivors(CansOrderEntry a, CansOrderEntry b)
        {
            if (a.Solved != b.Solved)
            {
                return a.Solved ? -1 : 1;
            }

            if (a.Solved)
            {
                int byAttempts = a.Attempts.CompareTo(b.Attempts);
                if (byAttempts != 0)
                {
                    return byAttempts;
                }

                return a.ConfirmTime.CompareTo(b.ConfirmTime);
            }

            int byBest = b.BestMatches.CompareTo(a.BestMatches);
            if (byBest != 0)
            {
                return byBest;
            }

            return a.BestCircle.CompareTo(b.BestCircle);
        }
    

        /// <summary>
        /// Полное равенство по попыткам и времени подтверждения.
        ///
        /// Идентификатор игрока сюда <b>не входит</b> намеренно: в сортировке
        /// он разводит любую ничью ради детерминированности, а вот решать
        /// им чью-то судьбу нельзя. При полном равенстве выбывают все, кто
        /// на линии отсечения, даже если их больше квоты (спека 5.6 и LDD 14).
        /// </summary>
        public static bool FullyTiedByAttempts(CansOrderEntry a, CansOrderEntry b)
        {
            return a.Attempts == b.Attempts && a.ConfirmTime.Equals(b.ConfirmTime);
        }

        /// <summary>Полное равенство по лучшему счёту и кругу, в котором он достигнут.</summary>
        public static bool FullyTiedByBestMatches(CansOrderEntry a, CansOrderEntry b)
        {
            return a.BestMatches == b.BestMatches && a.BestCircle == b.BestCircle;
        }
}
}
