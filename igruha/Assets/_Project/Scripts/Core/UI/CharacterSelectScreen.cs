using System;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Экран выбора персонажа перед спавном в хабе (HubBootstrap вызывает Show
    /// до PlayerSpawner.SpawnPlayers). Слоты — по индексу CharacterRoster.
    /// </summary>
    public sealed class CharacterSelectScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private CharacterSlotButton[] slots;
        [Tooltip("Риг 3rd-person камеры: он держит курсор залоченным, поэтому на время модалки его глушим — иначе по слотам нечем кликать")]
        [SerializeField] private ThirdPersonCameraRig cameraRig;

        private Action<CharacterDefinition> onChosen;

        public void Show(CharacterRoster roster, Action<CharacterDefinition> chosenCallback)
        {
            onChosen = chosenCallback;

            if (panel == null || roster == null)
            {
                Debug.LogError($"{name}: экран выбора персонажа не настроен (panel/roster)", this);
                Close(null);
                return;
            }

            // Курсор освобождает сам риг в OnDisable — единая политика курсора
            // остаётся в одном месте, здесь мы только приостанавливаем риг.
            if (cameraRig != null)
            {
                cameraRig.enabled = false;
            }
            else
            {
                Debug.LogError($"{name}: не назначен cameraRig — курсор останется залоченным и по слотам нельзя будет кликнуть", this);
            }

            // Активируем панель ДО Bind: пока она выключена, Awake() слотов ещё не
            // отработал (кэш Button), и Bind() упадёт на null-компоненте.
            panel.SetActive(true);

            var characters = roster.Characters;
            for (int i = 0; i < slots.Length; i++)
            {
                CharacterDefinition character = i < characters.Count ? characters[i] : null;
                slots[i].Bind(character, this);
            }
        }

        /// <summary>Вызывается CharacterSlotButton при клике по доступному слоту.</summary>
        public void OnSlotChosen(CharacterDefinition character) => Close(character);

        private void Close(CharacterDefinition character)
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }

            // Возвращаем ригу управление камерой и курсором.
            if (cameraRig != null)
            {
                cameraRig.enabled = true;
            }

            Action<CharacterDefinition> callback = onChosen;
            onChosen = null;
            callback?.Invoke(character);
        }
    }
}
