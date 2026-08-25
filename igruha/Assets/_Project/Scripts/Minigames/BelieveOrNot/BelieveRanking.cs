using System.Collections.Generic;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Места по итогам матча.
    ///
    /// Правило места одно и то же для всех случаев: <b>место = сколько игроков
    /// стоит выше, плюс один</b>. Из него само собой следует и то, что равные
    /// делят место, и то, что следующий получает место со сдвигом на размер
    /// группы. То же правило живёт в <c>EliminationRanking</c>, но сам он тут
    /// не годится принципиально: он ранжирует по порядку вылета, а выбывания
    /// в этой игре нет вовсе — все играют до конца.
    ///
    /// <b>Пятого критерия «меньший номер в лобби», как в «Экзамене», здесь
    /// нет намеренно.</b> Там <c>ExamRanking</c> строит строгий порядок и обязан
    /// развести любую пару, отсюда и добавочный критерий. Здесь равные делят
    /// место, а дележ детерминирован сам по себе: он даёт одинаковый результат
    /// на хосте и на клиенте без всякой добавки. Урок «Экзамена» — «места
    /// обязаны совпадать на всех машинах», и дележ это условие выполняет.
    ///
    /// Коллизия по метке времени, из-за которой «Экзамен» и получил пятый
    /// критерий, здесь невозможна конструктивно: кон выигрывает ровно один
    /// игрок, и метка у каждой победы своя. Совпасть метки могут только у тех,
    /// кто не выиграл ни разу, — а у них совпадают и все прочие критерии,
    /// и делить место правильно.
    /// </summary>
    public static class BelieveRanking
    {
        private static readonly List<BelieveEntry> upper = new List<BelieveEntry>(8);
        private static readonly List<BelieveEntry> lower = new List<BelieveEntry>(8);

        /// <summary>
        /// Расставить места всем участникам, включая отключившихся: место
        /// считается по накопленному на момент выхода.
        ///
        /// При 4–8 игроках команда, выигравшая больше конов, занимает верхнюю
        /// половину мест. <b>При равенстве конов деление на половины
        /// отменяется</b> и все ранжируются одним пулом — иначе пришлось бы
        /// назначать «верхнюю» команду произвольно. При 2–3 игроках команд нет,
        /// и пул всегда один.
        /// </summary>
        public static void Fill(IReadOnlyList<BelieveEntry> entries, int teamAWins, int teamBWins,
            MinigameResults results)
        {
            if (entries == null || results == null)
            {
                return;
            }

            results.Clear();
            upper.Clear();
            lower.Clear();

            bool teamsPlay = HasTeams(entries) && teamAWins != teamBWins;
            TeamId winning = teamAWins > teamBWins ? TeamId.A : TeamId.B;

            for (int i = 0; i < entries.Count; i++)
            {
                BelieveEntry entry = entries[i];
                if (teamsPlay && entry.Team != winning)
                {
                    lower.Add(entry);
                }
                else
                {
                    upper.Add(entry);
                }
            }

            int place = AssignGroup(upper, results, 1);
            AssignGroup(lower, results, place);
        }

        /// <summary>
        /// Разложить места внутри одной группы и вернуть первое свободное место
        /// для следующей. Ничьи внутри группы делят место; между группами их
        /// быть не может — там решает результат команды, а не личные победы.
        /// </summary>
        private static int AssignGroup(List<BelieveEntry> group, MinigameResults results, int firstPlace)
        {
            if (group.Count == 0)
            {
                return firstPlace;
            }

            group.Sort(Compare);

            int place = firstPlace;
            int index = 0;

            while (index < group.Count)
            {
                int tied = 1;
                while (index + tied < group.Count && Compare(group[index], group[index + tied]) == 0)
                {
                    tied++;
                }

                for (int i = 0; i < tied; i++)
                {
                    results.Add(group[index + i].PlayerId, place);
                }

                index += tied;
                place += tied;
            }

            return place;
        }

        /// <summary>
        /// Цепочка тайбрейков. Каждый следующий критерий применяется, только
        /// если предыдущий не развёл:
        ///
        /// 1. больше личных побед;
        /// 2. больше побед в роли Решающего — выиграть вслепую заметно труднее;
        /// 3. <b>меньше</b> отсиженных конов — та же результативность за меньшее
        ///    число попыток весит больше;
        /// 4. раньше одержана последняя победа — кто пришёл к своему счёту
        ///    первым, держал его дольше под давлением.
        ///
        /// Ноль — полное равенство, и тогда игроки делят место.
        /// </summary>
        private static int Compare(BelieveEntry a, BelieveEntry b)
        {
            if (a.RoundsWon != b.RoundsWon)
            {
                return b.RoundsWon.CompareTo(a.RoundsWon);
            }

            if (a.DeciderWins != b.DeciderWins)
            {
                return b.DeciderWins.CompareTo(a.DeciderWins);
            }

            if (a.RoundsSeated != b.RoundsSeated)
            {
                return a.RoundsSeated.CompareTo(b.RoundsSeated);
            }

            // Метка нуля у тех, кто не выиграл ни разу, — но их уже развёл
            // (или признал равными) первый критерий, поэтому «раньше — выше»
            // здесь не даёт незаслуженного преимущества.
            if (!a.LastWonAt.Equals(b.LastWonAt))
            {
                return a.LastWonAt.CompareTo(b.LastWonAt);
            }

            return 0;
        }

        private static bool HasTeams(IReadOnlyList<BelieveEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Team != TeamId.None)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
