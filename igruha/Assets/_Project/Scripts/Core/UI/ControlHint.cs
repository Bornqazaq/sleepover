using System.Collections.Generic;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Одна строка управления, разобранная на клавишу и действие.
    /// Клавиша может быть пустой — тогда строка показывается как обычный
    /// пункт списка, без капсулы.
    /// </summary>
    public readonly struct ControlHint
    {
        public readonly string Key;
        public readonly string Action;

        public ControlHint(string key, string action)
        {
            Key = key;
            Action = action;
        }
    }

    /// <summary>
    /// Разбор подсказок управления из <c>MinigameDefinition.ControlHints</c>.
    ///
    /// В ассетах подсказки лежат готовыми предложениями:
    /// «WASD — бежать, Space — прыжок». Экран правил показывал их как есть,
    /// одним абзацем, и получалась стена текста — ровно то, на что жаловался
    /// геймдизайнер. Здесь предложение режется на пары «клавиша → действие»,
    /// и каждая пара уезжает в свою строку с капсулой клавиши.
    ///
    /// Разбор нарочно осторожный. Пункт «За кафедрой: печатай вопрос,
    /// Tab — между полями» разбивать по запятой целиком нельзя: первая
    /// половина — это не клавиша, а условие. Поэтому пара засчитывается,
    /// только если слева от тире стоит короткое обозначение клавиши,
    /// а не фраза.
    ///
    /// Признак клавиши — отсутствие строчных русских букв. WASD, Space,
    /// Enter, Tab, ЛКМ, E проходят; «Подтвердил» из подсказки «Подтвердил —
    /// результат узнаешь вместе со всеми» не проходит, и вся строка остаётся
    /// пунктом списка. Без этого правила на карточке «Порядка банок»
    /// появлялась капсула с целым словом «Подтвердил».
    /// </summary>
    public static class ControlHintParser
    {
        /// <summary>Тире, которым в ассетах отделена клавиша от действия.</summary>
        private const string Dash = " — ";

        /// <summary>Длиннее этого левая часть — уже не клавиша, а условие.</summary>
        private const int MaxKeyLength = 16;

        /// <summary>Больше стольких слов слева — тоже не клавиша.</summary>
        private const int MaxKeyWords = 3;

        /// <summary>Разделители пунктов внутри одной подсказки.</summary>
        private static readonly char[] Separators = { ',', ';', '·' };

        /// <summary>
        /// Слова, которые считаются названием клавиши, хотя написаны строчными
        /// русскими буквами. Без списка «Пробел — прыжок» оставался бы пунктом
        /// списка, а «Space — прыжок» рядом получал капсулу: одно и то же
        /// действие выглядело бы в двух играх по-разному.
        /// </summary>
        private static readonly string[] KeyWords =
        {
            "пробел", "мышь", "стрелки", "колесо", "курсор", "лкм", "пкм", "скм", "стик"
        };

        /// <summary>
        /// Разобрать все подсказки игры в готовый список строк.
        /// Буфер переиспользуется вызывающим — экран правил показывается
        /// каждый раунд, и лишний мусор здесь ни к чему.
        /// </summary>
        public static void Parse(IReadOnlyList<string> hints, List<ControlHint> buffer)
        {
            buffer.Clear();
            if (hints == null)
            {
                return;
            }

            for (int i = 0; i < hints.Count; i++)
            {
                ParseLine(hints[i], buffer);
            }
        }

        private static void ParseLine(string line, List<ControlHint> buffer)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string[] parts = line.Split(Separators);
            var pending = new System.Text.StringBuilder();
            int lastWithKey = -1;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (TrySplit(part, out ControlHint hint))
                {
                    Flush(pending, buffer);
                    buffer.Add(hint);
                    lastWithKey = buffer.Count - 1;
                    continue;
                }

                // Кусок без клавиши прилипает к соседям: «За кафедрой: печатай
                // вопрос» — это одна фраза, а не две половины.
                if (pending.Length > 0)
                {
                    pending.Append(", ");
                }

                pending.Append(part);

                // Хвост после пары с клавишей — продолжение её же действия:
                // «Подтвердил — результат узнаешь вместе со всеми, не раньше»
                // это одна строка, а не строка и обрывок «не раньше».
                if (lastWithKey >= 0 && i > 0)
                {
                    ControlHint previous = buffer[lastWithKey];
                    buffer[lastWithKey] = new ControlHint(previous.Key, previous.Action + ", " + pending);
                    pending.Clear();
                }
            }

            Flush(pending, buffer);
        }

        private static void Flush(System.Text.StringBuilder pending, List<ControlHint> buffer)
        {
            if (pending.Length == 0)
            {
                return;
            }

            buffer.Add(new ControlHint(null, pending.ToString()));
            pending.Clear();
        }

        /// <summary>Есть ли в куске строчная русская буква — признак слова, а не клавиши.</summary>
        private static bool HasLowerCyrillic(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'а' && c <= 'я') || c == 'ё')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySplit(string part, out ControlHint hint)
        {
            hint = default;
            int dash = part.IndexOf(Dash, System.StringComparison.Ordinal);
            if (dash <= 0)
            {
                return false;
            }

            string key = part.Substring(0, dash).Trim();
            string action = part.Substring(dash + Dash.Length).Trim();

            // Уточнение в скобках — это про действие, а не про клавишу:
            // «Ctrl (удерживать) — присесть» даёт капсулу «Ctrl» и действие
            // «присесть (удерживать)». Иначе скобка ехала бы внутрь капсулы
            // и растягивала её на пол-карточки.
            int bracket = key.IndexOf('(');
            if (bracket > 0)
            {
                int close = key.IndexOf(')', bracket);
                string note = close > bracket
                    ? key.Substring(bracket + 1, close - bracket - 1).Trim()
                    : key.Substring(bracket + 1).Trim();
                key = key.Substring(0, bracket).Trim();
                if (note.Length > 0)
                {
                    action = $"{action} ({note})";
                }
            }

            if (key.Length == 0 || key.Length > MaxKeyLength || action.Length == 0)
            {
                return false;
            }

            string[] words = key.Split(' ');
            if (words.Length > MaxKeyWords)
            {
                return false;
            }

            if (!IsKey(key, words[0]))
            {
                return false;
            }

            hint = new ControlHint(key, action);
            return true;
        }

        /// <summary>
        /// Клавиша ли это. Решает первое слово: «WASD / стик» — клавиша,
        /// «Держать E у штабеля» — уже фраза, хотя латиница есть в обеих.
        /// </summary>
        private static bool IsKey(string key, string firstWord)
        {
            if (!HasLowerCyrillic(firstWord))
            {
                return true;
            }

            string lower = firstWord.ToLowerInvariant().Trim('.', ':', '/');
            for (int i = 0; i < KeyWords.Length; i++)
            {
                if (lower == KeyWords[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
