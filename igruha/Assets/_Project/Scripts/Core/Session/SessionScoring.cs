using System;
using Igruha.Core.Minigame;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Единственная формула очков катки (GDD 3.1):
    /// <b>очки = число игроков − место</b>.
    ///
    /// Число игроков — состав на старте раунда, а не ростер на момент
    /// подсчёта. Раньше обе реализации табло считали по ростеру, и когда
    /// из матча выходили, победитель получал ноль: ростер сжимался до
    /// подсчёта (IGR-372). Теперь ушедшие свою долю не забирают — они
    /// просто проиграли.
    ///
    /// Формула живёт в одном месте нарочно: у табло две реализации
    /// (локальная и сетевая), и когда каждая считала сама, они разъезжались.
    /// </summary>
    public static class SessionScoring
    {
        /// <summary>Очки за место при данном числе участников. Первое из восьми — 7, последнее — 0.</summary>
        public static int PointsFor(int place, int playerCount) =>
            Math.Max(0, playerCount - Math.Max(1, place));

        /// <summary>
        /// По какому составу считать: старт раунда, если игра его сообщила;
        /// иначе больший из ростера и числа участников в итогах — чтобы
        /// сцена, открытая напрямую, тоже считала разумно.
        /// </summary>
        public static int PlayerCountFor(MinigameResults results, int rosterCount)
        {
            if (results == null)
            {
                return rosterCount;
            }

            return results.PlayerCount > 0
                ? results.PlayerCount
                : Math.Max(rosterCount, results.Entries.Count);
        }
    }
}
