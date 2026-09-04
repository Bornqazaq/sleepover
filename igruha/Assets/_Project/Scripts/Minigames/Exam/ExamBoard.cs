using TMPro;
using UnityEngine;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Классная доска — единственный источник информации в игре. Всё решение
    /// Ученика строится на том, что здесь написано, поэтому читаемость важнее
    /// красоты: текст обязан разбираться с обеих платформ и из дальних углов
    /// зала, а это около двадцати пяти метров по диагонали.
    ///
    /// <b>Текст вопроса появляется только в фазе показа.</b> Это не косметика:
    /// в фазе 3 он поедет <c>ClientRpc</c> ровно в этот момент, и доска,
    /// показавшая его раньше, означала бы утечку.
    /// </summary>
    public sealed class ExamBoard : MonoBehaviour
    {
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private TMP_Text optionAText;
        [SerializeField] private TMP_Text optionBText;

        [Header("Подсветка верного варианта")]
        [SerializeField] private Color neutralColor = Color.white;
        [SerializeField] private Color correctColor = new Color(0.4f, 1f, 0.4f);
        [SerializeField] private Color wrongColor = new Color(1f, 0.4f, 0.4f);

        private void Awake() => ShowWaiting(0, 0, 0f);

        /// <summary>
        /// Фаза печати: вопроса ещё нет ни у кого. Пустая доска читается как
        /// баг, поэтому говорим прямо, чего ждём.
        /// </summary>
        public void ShowWaiting(int questionNumber, int totalQuestions, float secondsLeft)
        {
            // Отсчёт — в шапку, а не в строку вопроса: приклеенный к тексту,
            // он читался как часть фразы и растягивал строку, из-за чего
            // автоподбор кегля мельчил её на ровном месте.
            SetHeader(questionNumber, totalQuestions, 0);
            if (headerText != null && secondsLeft > 0f)
            {
                headerText.text += $"   •   {Mathf.CeilToInt(secondsLeft)} с";
            }

            SetText(questionText, "Ведущий готовит вопрос…");
            SetText(optionAText, string.Empty);
            SetText(optionBText, string.Empty);
            ResetColors();
        }

        /// <summary>Фаза показа: вопрос и варианты выходят на доску.</summary>
        public void ShowQuestion(int questionNumber, int totalQuestions, int questionValue,
            string question, string optionA, string optionB)
        {
            SetHeader(questionNumber, totalQuestions, questionValue);
            SetText(questionText, question);
            SetText(optionAText, $"А)  {optionA}");
            SetText(optionBText, $"Б)  {optionB}");
            ResetColors();
        }

        /// <summary>Створки раскрылись — можно наконец назвать верный вариант.</summary>
        public void HighlightCorrect(ExamSide correct)
        {
            if (optionAText != null)
            {
                optionAText.color = correct == ExamSide.A ? correctColor : wrongColor;
            }

            if (optionBText != null)
            {
                optionBText.color = correct == ExamSide.B ? correctColor : wrongColor;
            }
        }

        /// <summary>Вопрос не состоялся: Ведущий не успел напечатать.</summary>
        /// <summary>
        /// Вопрос не состоялся. Локальное событие: доска показывает это
        /// у всех — и у сервера, и у клиента, — поэтому звуку хватает его
        /// и своего пакета не нужно (подфаза 4.5).
        /// </summary>
        public event System.Action Skipped;

        public void ShowSkipped(int questionNumber, int totalQuestions)
        {
            Skipped?.Invoke();
            SetHeader(questionNumber, totalQuestions, 0);
            SetText(questionText, "Вопрос не состоялся");
            SetText(optionAText, string.Empty);
            SetText(optionBText, string.Empty);
            ResetColors();
        }

        private void SetHeader(int questionNumber, int totalQuestions, int questionValue)
        {
            if (headerText == null)
            {
                return;
            }

            if (questionNumber <= 0)
            {
                headerText.text = string.Empty;
                return;
            }

            headerText.text = questionValue > 0
                ? $"Вопрос {questionNumber} из {totalQuestions}   •   {questionValue} очк."
                : $"Вопрос {questionNumber} из {totalQuestions}";
        }

        private void ResetColors()
        {
            if (optionAText != null)
            {
                optionAText.color = neutralColor;
            }

            if (optionBText != null)
            {
                optionBText.color = neutralColor;
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
