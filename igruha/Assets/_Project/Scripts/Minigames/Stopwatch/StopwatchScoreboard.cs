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

        /// <summary>
        /// Имена типов подраунда. Не «как называется правило», а «что делать»:
        /// вся тонкость игры в том, что осторожничать — тоже проигрыш.
        ///
        /// «Не больше 4 с» звучит как «жми и отпускай пораньше, там безопасно»,
        /// а правило ровно обратное: среди уложившихся хуже всех тот, кто
        /// остановился раньше прочих (<see cref="StopwatchRanking"/>,
        /// CompareCeiling). Приписка «но впритык» — единственное место, где
        /// это вообще сказано игроку.
        ///
        /// Длина имеет значение: заголовок грани табло автокеглем ужимается
        /// лишь до 45% кегля, дальше вылезает за панель. «ПОДРАУНД 8 —
        /// НЕ БОЛЬШЕ, НО ВПРИТЫК» — 34 знака против прежних 22, это в запасе.
        /// </summary>
        private static readonly string[] TypeNames =
        {
            "ОТМЕРЬ РОВНО",
            "НЕ БОЛЬШЕ, НО ВПРИТЫК",
            "НЕ МЕНЬШЕ, НО ВПРИТЫК"
        };

        /// <summary>
        /// Сколько ошибок игрок переживёт. Нужен, чтобы точки в строке читались
        /// как запас, а не как непонятная россыпь: две точки из трёх — это
        /// «остался один шанс», и это разное при лимите 2 и при лимите 3.
        /// </summary>
        private int errorLimit;

        public bool HasBoard => board != null;

        /// <summary>
        /// Лимит ошибок этого состава. Ставится один раз на старте игры:
        /// он зависит от числа игроков и внутри матча не меняется.
        /// </summary>
        public void SetErrorLimit(int limit) => errorLimit = Mathf.Max(0, limit);

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
                board.AddRow(names[i], FormatValue(times[i], completed[i], errors[i], alive[i], faulted[i], showTimes, errorLimit));
            }

            board.EndRows();
        }

        private static string FormatValue(float seconds, bool completed, int errors, bool alive, bool faulted,
            bool showTimes, int errorLimit)
        {
            if (!alive)
            {
                return "выбыл";
            }

            string marks = Marks(errors, errorLimit);

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

        /// <summary>
        /// Запас ошибок значками: истраченные — жирной точкой, оставшиеся —
        /// точкой средней. Раньше рисовались только истраченные, и строка
        /// «••» ничего не говорила о том, последняя это ошибка или предпоследняя,
        /// — а лимит в этой игре зависит от состава (2 при шести и больше,
        /// иначе 3) и на табло нигде не написан.
        ///
        /// Оба знака взяты из безопасного набора: LiberationSans их содержит.
        /// Экзотика вроде ✕ или ○ подменяется пустым квадратом и сыплет
        /// предупреждениями — на этом уже обжигались.
        /// </summary>
        private static string Marks(int errors, int errorLimit)
        {
            if (errorLimit <= 0)
            {
                return errors > 0 ? new string('•', errors) : string.Empty;
            }

            int used = Mathf.Clamp(errors, 0, errorLimit);
            return new string('•', used) + new string('·', errorLimit - used);
        }

        public void Clear() => board?.Clear();
    }
}
