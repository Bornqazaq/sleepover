using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Один сектор колеса эмоций. Знает только про свой вид: занят/пуст и
    /// наведён/нет. Кто под курсором — решает PlayerEmoteAbility, слот лишь
    /// подсвечивается по команде EmoteWheel.
    /// </summary>
    public sealed class EmoteWheelSlot : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;

        [Header("Цвета")]
        [SerializeField] private Color idleColor = new Color(0.09f, 0.09f, 0.12f, 0.85f);
        [SerializeField] private Color hoveredColor = new Color(0.98f, 0.75f, 0.15f, 0.95f);
        [Tooltip("Сектор без клипа: у персонажа эта эмоция ещё не завезена")]
        [SerializeField] private Color emptyColor = new Color(0.09f, 0.09f, 0.12f, 0.35f);

        private bool isAvailable;

        /// <summary>Заполнить сектор: пустое имя — эмоции нет, сектор гаснет.</summary>
        public void Bind(string emoteName)
        {
            isAvailable = !string.IsNullOrEmpty(emoteName);

            if (label != null)
            {
                label.text = isAvailable ? emoteName : "—";
                label.alpha = isAvailable ? 1f : 0.4f;
            }

            SetHovered(false);
        }

        public void SetHovered(bool hovered)
        {
            if (background == null)
            {
                return;
            }

            background.color = !isAvailable ? emptyColor : hovered ? hoveredColor : idleColor;
        }
    }
}
