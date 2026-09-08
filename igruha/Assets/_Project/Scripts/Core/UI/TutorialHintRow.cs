using TMPro;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Строка управления на экране правил: капсула с клавишей и действие
    /// рядом. Без клавиши строка показывается как пункт списка с точкой —
    /// так выглядят условия вроде «За кафедрой: печатай вопрос».
    /// </summary>
    public sealed class TutorialHintRow : MonoBehaviour
    {
        [SerializeField] private GameObject keyCap;
        [SerializeField] private TMP_Text keyText;
        [SerializeField] private TMP_Text actionText;

        /// <summary>Заполнить строку и показать её.</summary>
        public void Set(ControlHint hint)
        {
            bool hasKey = !string.IsNullOrEmpty(hint.Key);

            if (keyCap != null)
            {
                keyCap.SetActive(hasKey);
            }

            if (keyText != null && hasKey)
            {
                keyText.text = Capitalize(hint.Key);
            }

            if (actionText != null)
            {
                actionText.text = hasKey ? hint.Action : "• " + hint.Action;
            }

            gameObject.SetActive(true);
        }

        public void Clear()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Заглавная первая буква. В ассетах клавиша написана как придётся —
        /// «мышь» в одной подсказке и «Мышь» в другой, — а на капсуле рядом
        /// с «WASD» и «Ctrl» строчная выглядит опечаткой.
        /// </summary>
        private static string Capitalize(string key)
        {
            if (key.Length == 0 || !char.IsLower(key[0]))
            {
                return key;
            }

            return char.ToUpperInvariant(key[0]) + key.Substring(1);
        }
    }
}
