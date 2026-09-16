using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
    public sealed class CharacterSlotButton : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Image portrait, border, selectionMark;
        private Color accent = Color.white;
        private bool focused;
        public bool IsAvailable => available;
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
            if (button == null) button = GetComponent<Button>();
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

            label.text = taken && statusLabel == null ? $"{definition.DisplayName} — занят" : definition.DisplayName;
            label.color = taken ? takenColor : defaultColor;
            if (statusLabel != null) statusLabel.text = taken ? "УЖЕ В КОМПАНИИ" : available ? "ВЫБРАТЬ" : "НЕДОСТУПЕН";
            if (portrait != null) portrait.color = available ? Color.white : new Color(.45f, .45f, .45f, .6f);
            SetFocused(focused);
        }

        public void SetPortrait(CharacterSelectionView.Portrait art)
        {
            if (portrait != null) { portrait.sprite = art?.Face; portrait.enabled = portrait.sprite != null; }
            accent = art?.Accent ?? Color.white;
        }

        public void SetFocused(bool value)
        {
            focused = value && available;
            if (border != null) border.color = focused ? accent : new Color(.2f, .29f, .30f, 1);
            if (selectionMark != null) { selectionMark.enabled = focused; selectionMark.color = accent; }
            if (statusLabel != null && available) { statusLabel.text = focused ? "ТВОЙ ВЫБОР" : "ВЫБРАТЬ"; statusLabel.color = focused ? accent : new Color(.64f, .72f, .71f); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!available || button == null || !button.interactable) return;
            button.Select();
            screen?.PreviewCharacter(index);
        }
        public void OnSelect(BaseEventData eventData) { if (available) screen?.PreviewCharacter(index); }

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
