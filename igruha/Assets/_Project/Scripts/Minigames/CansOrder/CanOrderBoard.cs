using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Igruha.Core.UI;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Наполнение табло над ямой. Табло — единственный источник информации
    /// в игре, и почти всё смешное в ней растёт отсюда: три лучшие расстановки
    /// круга видят все и одновременно, поэтому через круг половина лобби стоит
    /// с расстановкой лидера, поправленной на одну банку.
    ///
    /// Три правила, которые здесь нельзя нарушить:
    /// — <b>расстановка собравшего не показывается никогда</b>, ни в тройке,
    ///   ни где-либо ещё: она и есть ответ;
    /// — <b>неподтвердившему не пишется ноль</b>, ему пишется «НЕ ПОДТВЕРДИЛ»;
    /// — <b>порядок строк детерминирован</b> (совпадения по убыванию, при
    ///   равенстве по идентификатору), поэтому табло одинаково на всех машинах
    ///   без синхронизации порядка.
    ///
    /// Строк всего по числу участников: три верхние несут полную расстановку,
    /// остальные — только счёт. При восьми игроках это ровно восемь строк,
    /// то есть штатная ёмкость <see cref="WorldScoreboardFace"/>. Цвет и символ
    /// банок рисуются разметкой TMP прямо в строке — своих граней городить
    /// не пришлось, и <c>Core/UI</c> остался нетронутым.
    /// </summary>
    public sealed class CanOrderBoard : MonoBehaviour
    {
        private const string LabelSolved = "СОБРАЛ";
        private const string LabelNotConfirmed = "НЕ ПОДТВЕРДИЛ";

        [SerializeField] private WorldScoreboard board;
        [SerializeField] private CansOrderConfig config;

        private readonly List<CansOrderEntry> ordered = new List<CansOrderEntry>(8);
        private readonly List<string> names = new List<string>(8);
        private readonly List<int> submitted = new List<int>(8);
        private readonly StringBuilder text = new StringBuilder(96);

        /// <summary>Табло найдено и в него есть что писать.</summary>
        public bool HasBoard => board != null;

        /// <summary>Брифинг: табло объявляет раунд и число банок, строк ещё нет.</summary>
        public void ShowTask(int roundNumber, int canCount)
        {
            if (board == null)
            {
                return;
            }

            board.SetHeader($"РАУНД {roundNumber}", $"БАНОК: {canCount}");
            board.BeginRows();
            board.EndRows();
        }

        /// <summary>
        /// Показ результатов круга. Данные берутся у контроллера, и он сам
        /// не отдаёт совпадения раньше этой стадии — прятать их здесь
        /// не требуется и было бы ненадёжно.
        /// </summary>
        public void ShowResults(CansOrderMinigame game)
        {
            if (board == null || config == null || game == null)
            {
                return;
            }

            CollectEntries(game);

            board.SetHeader($"РАУНД {game.Round.Round}", $"КРУГ {game.Round.Circle} · БАНОК: {game.Round.CanCount}");
            board.BeginRows();

            int shownArrangements = 0;
            int capacity = board.RowCapacity;

            for (int i = 0; i < ordered.Count && i < capacity; i++)
            {
                CansOrderEntry entry = ordered[i];
                string label = names[i];

                // Полную расстановку получают только первые несколько и только
                // среди тех, кто не собрал: расстановка собравшего — это ответ.
                bool showArrangement = shownArrangements < config.BoardTopRows
                                       && entry.Confirmed
                                       && !entry.Solved
                                       && game.TryGetSubmitted(IndexOf(game, entry.PlayerId), submitted);

                if (showArrangement)
                {
                    label = names[i] + "  " + Render(submitted);
                    shownArrangements++;
                }

                board.AddRow(label, ValueFor(entry));
            }

            board.EndRows();
        }

        public void Clear() => board?.Clear();

        /// <summary>
        /// Что стоит в правой колонке. Три разных состояния, и подменять одно
        /// другим нельзя: «НЕ ПОДТВЕРДИЛ» — это не ноль совпадений.
        /// </summary>
        private static string ValueFor(CansOrderEntry entry)
        {
            if (entry.SolvedThisCircle)
            {
                return LabelSolved;
            }

            if (!entry.Confirmed)
            {
                return LabelNotConfirmed;
            }

            return entry.Matches.ToString();
        }

        /// <summary>Расстановка цветными символами. Разметка TMP — цвет и символ на каждую позицию.</summary>
        private string Render(List<int> arrangement)
        {
            text.Clear();
            for (int i = 0; i < arrangement.Count; i++)
            {
                CansOrderConfig.CanKind kind = config.GetCanKind(arrangement[i]);
                text.Append("<color=#");
                text.Append(ColorUtility.ToHtmlStringRGB(kind.color));
                text.Append('>');
                text.Append(kind.symbol);
                text.Append("</color>");
            }

            return text.ToString();
        }

        /// <summary>
        /// Собрать участников и разложить в детерминированном порядке.
        /// Собравшие в этом круге и неподтвердившие сортируются вместе со всеми,
        /// но полной расстановки не получают — это решает <see cref="ShowResults"/>.
        /// </summary>
        private void CollectEntries(CansOrderMinigame game)
        {
            ordered.Clear();
            names.Clear();

            for (int i = 0; i < game.ContestantCount; i++)
            {
                if (game.TryGetEntry(i, out CansOrderEntry entry, out string displayName))
                {
                    ordered.Add(entry);
                    names.Add(displayName);
                }
            }

            // Имена сортируем вместе с записями: список параллельный, и после
            // Sort они разъехались бы. Поэтому сортируем индексы через ключ.
            for (int i = 1; i < ordered.Count; i++)
            {
                CansOrderEntry entry = ordered[i];
                string playerName = names[i];
                int j = i - 1;
                while (j >= 0 && Worse(ordered[j], entry))
                {
                    ordered[j + 1] = ordered[j];
                    names[j + 1] = names[j];
                    j--;
                }

                ordered[j + 1] = entry;
                names[j + 1] = playerName;
            }
        }

        /// <summary>Правило порядка строк — то же, что у <see cref="CanOrderRanking.SortForBoard"/>.</summary>
        /// <summary>
        /// Правило порядка строк живёт в <see cref="CanOrderRanking"/> и только там:
        /// вторая копия разъехалась бы с первой, а от детерминированности
        /// этого правила зависит, одинаково ли табло выглядит у всех.
        /// </summary>
        private static bool Worse(CansOrderEntry left, CansOrderEntry right)
        {
            return CanOrderRanking.CompareForBoard(left, right) > 0;
        }

        private static int IndexOf(CansOrderMinigame game, int playerId)
        {
            for (int i = 0; i < game.ContestantCount; i++)
            {
                if (game.TryGetEntry(i, out CansOrderEntry entry, out _) && entry.PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
