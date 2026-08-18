using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.UI;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Наполнение мирового табло данными подраунда. Отдельно от контроллера:
    /// контроллер решает, что произошло, табло — как это назвать словами.
    ///
    /// Весь интерфейс игры — это табло. Экранного HUD нет намеренно:
    /// напряжение должно читаться по миру, а не по цифрам в углу.
    /// </summary>
    public sealed class StopwatchScoreboard : MonoBehaviour
    {
        [SerializeField] private WorldScoreboard board;

        private static readonly string[] TypeNames = { "ОТМЕРЬ РОВНО", "НЕ БОЛЬШЕ", "НЕ МЕНЬШЕ" };

        public bool HasBoard => board != null;

        /// <summary>Заголовок подраунда. Цель висит всю стадию отмера, а не только три секунды показа задания.</summary>
        public void ShowTask(int subround, StopwatchSubroundType type, float target)
        {
            if (board == null)
            {
                return;
            }

            board.SetHeader($"ПОДРАУНД {subround} — {TypeNames[(int)type]}", $"ЦЕЛЬ: {target:0.0} с");
        }

        /// <summary>
        /// Строки игроков. <paramref name="showTimes"/> — стадия показа
        /// результатов: до неё замеры не выводятся никому, иначе поздно
        /// нажимающие подстроились бы под уже известные цифры.
        /// </summary>
        public void ShowPlayers(IReadOnlyList<string> names, IReadOnlyList<float> times,
            IReadOnlyList<bool> completed, IReadOnlyList<int> errors, IReadOnlyList<bool> alive,
            IReadOnlyList<bool> faulted, bool showTimes)
        {
            if (board == null)
            {
                return;
            }

            board.BeginRows();
            for (int i = 0; i < names.Count; i++)
            {
                board.AddRow(names[i], FormatValue(times[i], completed[i], errors[i], alive[i], faulted[i], showTimes));
            }

            board.EndRows();
        }

        private static string FormatValue(float seconds, bool completed, int errors, bool alive, bool faulted, bool showTimes)
        {
            if (!alive)
            {
                return "выбыл";
            }

            string marks = errors > 0 ? new string('•', errors) : string.Empty;

            if (!showTimes)
            {
                return marks;
            }

            string value = completed ? seconds.ToString("0.00") : "—";
            // Только символы, которые точно есть в LiberationSans: экзотика
            // вроде ✕ подменяется на пустой квадрат и сыплет предупреждениями.
            string flag = faulted ? " !" : string.Empty;
            return string.IsNullOrEmpty(marks) ? value + flag : value + " " + marks + flag;
        }

        public void Clear() => board?.Clear();
    }
}
