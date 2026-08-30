using System.Collections.Generic;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Команда участника. <see cref="None"/> — команд в этой игре нет вовсе
    /// либо игрок вне составов.
    /// </summary>
    public enum TeamSide : byte
    {
        None = 0,
        A = 1,
        B = 2
    }

    /// <summary>
    /// Деление лобби на две команды. Общее: командных игр в проекте будет
    /// несколько (Переноска, Крокодил, Дырка в стене), а сейчас деление живёт
    /// внутри «Верю / не верю» и наружу не отдаётся.
    ///
    /// <b>Делит сервер, детерминированно.</b> Единственный вход — порядковый
    /// номер участника в составе раунда, а состав одинаков на всех машинах.
    /// Поэтому результат не зависит от того, где его посчитали, и сверять
    /// составы по сети не нужно.
    ///
    /// Раздаём через одного, а не первую половину в A и вторую в B. Так
    /// размеры команд сами собой ложатся в таблицу спеки —
    /// 4 → 2 + 2, 5 → 3 + 2, 6 → 3 + 3, 7 → 4 + 3, 8 → 4 + 4, — и отдельной
    /// таблицы в коде не заводится. Заодно соседи по лобби (а это обычно те,
    /// кто зашёл вместе) расходятся по разным сторонам.
    ///
    /// Компенсации за неравенство нет — это принятое решение: меньшая команда
    /// несёт устойчивее, зато у неё меньше рук на саботаж.
    /// </summary>
    public static class TeamAssignment
    {
        /// <summary>Команда участника с этим номером в составе раунда.</summary>
        public static TeamSide SideFor(int index) => index % 2 == 0 ? TeamSide.A : TeamSide.B;

        /// <summary>Сколько человек в команде A при таком составе.</summary>
        public static int TeamASize(int playerCount) => playerCount <= 0 ? 0 : (playerCount + 1) / 2;

        /// <summary>Сколько человек в команде B при таком составе.</summary>
        public static int TeamBSize(int playerCount) => playerCount <= 0 ? 0 : playerCount / 2;

        /// <summary>Сколько человек в этой команде при таком составе.</summary>
        public static int SizeOf(TeamSide side, int playerCount)
        {
            switch (side)
            {
                case TeamSide.A:
                    return TeamASize(playerCount);
                case TeamSide.B:
                    return TeamBSize(playerCount);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Разложить состав по командам. Заполняет переданный список, а не
        /// возвращает новый: делёж происходит раз в раунд, но плодить мусор
        /// незачем, а вызывающему список всё равно держать у себя.
        /// </summary>
        public static void Assign(IReadOnlyList<SessionPlayer> players, List<TeamSide> destination)
        {
            if (players == null || destination == null)
            {
                return;
            }

            destination.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                destination.Add(SideFor(i));
            }
        }

        /// <summary>Противоположная команда. Для None противоположной нет.</summary>
        public static TeamSide Opposite(TeamSide side)
        {
            switch (side)
            {
                case TeamSide.A:
                    return TeamSide.B;
                case TeamSide.B:
                    return TeamSide.A;
                default:
                    return TeamSide.None;
            }
        }
    }
}
