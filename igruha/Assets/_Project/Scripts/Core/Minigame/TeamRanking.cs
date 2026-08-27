using System.Collections.Generic;
using Igruha.Core.Session;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Места по итогам командной игры: победившая команда делит верхнюю
    /// половину мест, проигравшая — нижнюю.
    ///
    /// Правило одно на все случаи: <b>место = сколько игроков стоит выше,
    /// плюс один</b>. Отсюда само собой следует и то, что вся команда делит
    /// одно место, и то, что проигравшие начинают со сдвигом на размер
    /// победившей команды. Очки начисляет <c>SessionManager</c> по общей
    /// формуле «очки = число игроков − место», и подгонять их здесь не надо.
    ///
    /// Пример на 4 игроках (2 на 2): победители — место 1 (3 очка),
    /// проигравшие — место 3 (1 очко). На 5 при победе троих: 1 и 4.
    /// На 8: 1 и 5 (7 и 3 очка).
    ///
    /// <c>BelieveRanking</c> считает то же правило, но со своей цепочкой
    /// личных тайбрейков и отменой деления при равенстве конов. Он здесь не
    /// годится и не трогается: рефакторинг «Верю / не верю» под общий помощник —
    /// отдельная задача.
    /// </summary>
    public static class TeamRanking
    {
        /// <summary>Участник и его команда. Всё, что нужно для мест половинами.</summary>
        public readonly struct Entry
        {
            public int PlayerId { get; }
            public TeamSide Team { get; }

            public Entry(int playerId, TeamSide team)
            {
                PlayerId = playerId;
                Team = team;
            }
        }

        /// <summary>
        /// Расставить места всем участникам, включая отключившихся: место
        /// считается по накопленному командой на момент конца раунда, а
        /// команда у вышедшего остаётся та же.
        ///
        /// <paramref name="winner"/> = <see cref="TeamSide.None"/> означает,
        /// что победителя нет, и разбирается тем же путём, что
        /// <see cref="FillNoWinner"/>.
        /// </summary>
        public static void Fill(IReadOnlyList<Entry> entries, TeamSide winner, MinigameResults results)
        {
            if (entries == null || results == null)
            {
                return;
            }

            if (winner == TeamSide.None)
            {
                FillNoWinner(entries, results);
                return;
            }

            results.Clear();

            int winners = CountOf(entries, winner);

            // Победителей нет ни одного — команда вышла целиком, а победа ей
            // всё равно записана. Отдавать оставшимся первое место нельзя:
            // они проиграли. Разбираем как «победителя нет».
            if (winners == 0)
            {
                FillNoWinner(entries, results);
                return;
            }

            int loserPlace = winners + 1;

            for (int i = 0; i < entries.Count; i++)
            {
                results.Add(entries[i].PlayerId, entries[i].Team == winner ? 1 : loserPlace);
            }
        }

        /// <summary>
        /// Победителя нет: все получают одно и то же последнее место и ноль
        /// очков. Так выглядит счёт 0 : 0 — не донесла ни одна команда.
        ///
        /// Отдать всем первое место означало бы выдать максимум очков за общий
        /// провал, поэтому место именно последнее: при формуле
        /// «очки = число игроков − место» оно и даёт ровно ноль.
        /// </summary>
        public static void FillNoWinner(IReadOnlyList<Entry> entries, MinigameResults results)
        {
            if (entries == null || results == null)
            {
                return;
            }

            results.Clear();

            int lastPlace = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                results.Add(entries[i].PlayerId, lastPlace);
            }
        }

        private static int CountOf(IReadOnlyList<Entry> entries, TeamSide side)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Team == side)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
