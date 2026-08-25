using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Player;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Один слот экрана выбора персонажа. Заглушка (без префаба в ростере)
    /// автоматически становится некликабельной — отдельного скрипта под
    /// заглушки не нужно, слот сам решает по данным CharacterDefinition.
    ///
    /// Занятый чужим игроком слот гаснет так же, как заглушка, но подписывается
    /// иначе: игрок должен видеть разницу между «персонаж не готов» и
    /// «персонажа уже взяли».
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class CharacterSlotButton : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [Tooltip("Цвет надписи занятого слота")]
        [SerializeField] private Color takenColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        private Button button;
        private CharacterDefinition character;
        private CharacterSelectScreen screen;
        private int index = -1;
        private bool available;
        private Color defaultColor;
        private bool defaultColorCached;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }

        public void Bind(int rosterIndex, CharacterDefinition definition, bool taken, CharacterSelectScreen owner)
        {
            index = rosterIndex;
            character = definition;
            screen = owner;
            available = definition != null && definition.IsAvailable && !taken;

            button.interactable = available;

            if (label == null)
            {
                return;
            }

            if (!defaultColorCached)
            {
                defaultColor = label.color;
                defaultColorCached = true;
            }

            if (definition == null)
            {
                label.text = "???";
                label.color = takenColor;
                return;
            }

            label.text = taken ? $"{definition.DisplayName} — занят" : definition.DisplayName;
            label.color = taken ? takenColor : defaultColor;
        }

        /// <summary>
        /// Погасить или вернуть кнопку, не трогая подпись: намерение уже
        /// отправлено и ждём ответа сервера.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable && available;
        }

        private void HandleClick()
        {
            if (!available || screen == null)
            {
                return;
            }

            screen.OnSlotChosen(index);
        }
    }
}
