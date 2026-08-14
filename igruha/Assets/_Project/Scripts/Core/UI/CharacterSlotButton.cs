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
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class CharacterSlotButton : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private Button button;
        private CharacterDefinition character;
        private CharacterSelectScreen screen;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(HandleClick);
        }

        public void Bind(CharacterDefinition definition, CharacterSelectScreen owner)
        {
            character = definition;
            screen = owner;
            button.interactable = definition != null && definition.IsAvailable;

            if (label != null)
            {
                label.text = definition != null ? definition.DisplayName : "???";
            }
        }

        private void HandleClick()
        {
            if (character == null || !character.IsAvailable)
            {
                return;
            }

            screen.OnSlotChosen(character);
        }
    }
}
