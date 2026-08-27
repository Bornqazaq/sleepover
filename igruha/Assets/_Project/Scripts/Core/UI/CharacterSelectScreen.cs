using System;
using TMPro;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Экран выбора персонажа перед спавном в хабе (HubBootstrap вызывает Show
    /// до появления тела). Слоты — по индексу CharacterRoster.
    ///
    /// В сетевой катке экран живёт до тех пор, пока сервер не подтвердит
    /// выбор: клик отправляет намерение и гасит слоты, а закрывается экран,
    /// только когда персонаж действительно закреплён. Иначе двое, кликнувшие
    /// в один кадр, оба увидели бы «выбрано», а тело получил бы один.
    ///
    /// Занятые слоты приходят с сервера и обновляются на лету: чужой выбор
    /// гасит кнопку прямо под курсором.
    /// </summary>
    public sealed class CharacterSelectScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private CharacterSlotButton[] slots;
        [Tooltip("Риг 3rd-person камеры: он держит курсор залоченным, поэтому на время модалки его глушим — иначе по слотам нечем кликать")]
        [SerializeField] private ThirdPersonCameraRig cameraRig;
        [Tooltip("Строка обратного отсчёта. Пусто — отсчёт просто не показывается")]
        [SerializeField] private TMP_Text countdownLabel;

        private Action<int> onChosen;
        private CharacterRoster roster;
        private ICharacterSelection selection;

        /// <summary>Экран открыт и ждёт решения.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Показать экран.
        /// </summary>
        /// <param name="characterRoster">Кого показывать.</param>
        /// <param name="networkSelection">
        /// Сетевой выбор: гасит занятые и ведёт отсчёт. Пусто — одиночный
        /// режим, где занятых не бывает и торопиться некуда.
        /// </param>
        /// <param name="chosenCallback">Индекс выбранного персонажа в ростере.</param>
        public void Show(CharacterRoster characterRoster, ICharacterSelection networkSelection, Action<int> chosenCallback)
        {
            onChosen = chosenCallback;
            roster = characterRoster;
            selection = networkSelection;

            if (panel == null || roster == null)
            {
                Debug.LogError($"{name}: экран выбора персонажа не настроен (panel/roster)", this);
                Close(-1);
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
            IsOpen = true;

            if (selection != null)
            {
                selection.Changed += HandleSelectionChanged;
                selection.ReportReady();
            }

            RefreshSlots();
        }

        private void Update()
        {
            if (!IsOpen || selection == null)
            {
                return;
            }

            if (countdownLabel != null)
            {
                countdownLabel.text = $"Автовыбор через {Mathf.CeilToInt(selection.SecondsLeft)} с";
            }

            // Сервер закрепил персонажа — сам он это сделал или по истечении
            // срока, экрану больше висеть незачем.
            if (selection.HasChosen)
            {
                Close(-1);
            }
        }

        /// <summary>Вызывается CharacterSlotButton при клике по доступному слоту.</summary>
        public void OnSlotChosen(int characterIndex)
        {
            if (!IsOpen)
            {
                return;
            }

            // В одиночном режиме подтверждать некому — закрываемся сразу.
            if (selection == null)
            {
                Close(characterIndex);
                return;
            }

            selection.Choose(characterIndex);

            // Экран не закрываем: ждём, пока сервер подтвердит. Слоты гасим,
            // чтобы не сыпать намерениями по второму разу.
            SetSlotsInteractable(false);
        }

        private void HandleSelectionChanged()
        {
            if (!IsOpen)
            {
                return;
            }

            if (selection != null && selection.HasChosen)
            {
                Close(-1);
                return;
            }

            RefreshSlots();
        }

        private void RefreshSlots()
        {
            var characters = roster.Characters;
            for (int i = 0; i < slots.Length; i++)
            {
                CharacterDefinition character = i < characters.Count ? characters[i] : null;
                bool taken = selection != null && i < characters.Count && selection.IsTaken(i);
                slots[i].Bind(i, character, taken, this);
            }
        }

        private void SetSlotsInteractable(bool interactable)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].SetInteractable(interactable);
            }
        }

        private void Close(int characterIndex)
        {
            IsOpen = false;

            if (selection != null)
            {
                selection.Changed -= HandleSelectionChanged;
            }

            if (panel != null)
            {
                panel.SetActive(false);
            }

            // Возвращаем ригу управление камерой и курсором.
            if (cameraRig != null)
            {
                cameraRig.enabled = true;
            }

            Action<int> callback = onChosen;
            onChosen = null;
            selection = null;
            callback?.Invoke(characterIndex);
        }
    }
}
