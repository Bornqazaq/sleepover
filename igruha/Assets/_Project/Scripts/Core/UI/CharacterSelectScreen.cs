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
        [SerializeField] private CharacterSelectionView presentation;
        [SerializeField, Min(1)] private float localChoiceSeconds = 30f;

        private Action<int> onChosen;
        private CharacterRoster roster;
        private ICharacterSelection selection;
        private float localDeadline;
        private float countdownDuration;
        private int pendingIndex = -1;

        /// <summary>Экран открыт и ждёт решения.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Показать экран.
        /// </summary>
        /// <param name="characterRoster">Кого показывать.</param>
        /// <param name="networkSelection">
        /// Сетевой выбор: гасит занятые и ведёт отсчёт. Пусто — одиночный
        /// режим с локальным случайным выбором по истечении срока.
        /// </param>
        /// <param name="chosenCallback">Индекс выбранного персонажа в ростере.</param>
        public void Show(CharacterRoster characterRoster, ICharacterSelection networkSelection, Action<int> chosenCallback)
        {
            if (selection != null) selection.Changed -= HandleSelectionChanged;
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
            pendingIndex = -1;
            localDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, localChoiceSeconds);
            presentation?.Initialize(this, roster, slots);

            if (selection != null)
            {
                selection.Changed += HandleSelectionChanged;
                selection.ReportReady();
            }
            if (!IsOpen) return;

            RefreshSlots();
            if (UnityEngine.EventSystems.EventSystem.current != null)
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].IsAvailable)
                    {
                        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(slots[i].gameObject);
                        break;
                    }
            countdownDuration = selection != null ? Mathf.Max(1f, selection.SecondsLeft) : Mathf.Max(1f, localChoiceSeconds);
            presentation?.SetPending(false);
            UpdateCountdown();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            UpdateCountdown();

            // Сервер закрепил персонажа — сам он это сделал или по истечении
            // срока, экрану больше висеть незачем.
            if (selection != null && selection.HasChosen)
            {
                Close(-1);
            }
            else if (selection == null && Time.realtimeSinceStartup >= localDeadline)
            {
                // Local session only. Network expiry remains entirely server-authoritative.
                int count = 0;
                int chosen = -1;
                for (int i = 0; i < roster.Characters.Count; i++)
                    if (roster.Characters[i].IsAvailable && UnityEngine.Random.Range(0, ++count) == 0) chosen = i;
                Close(chosen);
            }
        }

        private void UpdateCountdown()
        {
            float remaining = selection != null ? selection.SecondsLeft : Mathf.Max(0, localDeadline - Time.realtimeSinceStartup);
            if (countdownLabel != null) countdownLabel.text = $"Автовыбор через {Mathf.CeilToInt(remaining)} с";
            presentation?.SetCountdown(remaining, countdownDuration);
        }

        public void PreviewCharacter(int index) { if (IsOpen && pendingIndex < 0) presentation?.Focus(index); }

        /// <summary>Вызывается CharacterSlotButton при клике по доступному слоту.</summary>
        public void OnSlotChosen(int characterIndex)
        {
            if (!IsOpen || pendingIndex >= 0 || characterIndex < 0 || characterIndex >= roster.Characters.Count
                || !roster.Characters[characterIndex].IsAvailable || (selection != null && selection.IsTaken(characterIndex)))
            {
                return;
            }

            // В одиночном режиме подтверждать некому — закрываемся сразу.
            if (selection == null)
            {
                Close(characterIndex);
                return;
            }

            // Экран не закрываем: ждём, пока сервер подтвердит. Слоты гасим,
            // чтобы не сыпать намерениями по второму разу.
            // Do this before Choose: a host can confirm synchronously in the call.
            pendingIndex = characterIndex;
            SetSlotsInteractable(false);
            presentation?.SetPending(true);
            selection.Choose(characterIndex);
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

            if (pendingIndex >= 0 && selection != null && selection.IsTaken(pendingIndex))
            {
                pendingIndex = -1;
                presentation?.SetPending(false);
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
            presentation?.RefreshAvailability();
            if (pendingIndex >= 0) SetSlotsInteractable(false);
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
            pendingIndex = -1;
            callback?.Invoke(characterIndex);
        }

        private void OnDestroy()
        {
            if (selection != null) selection.Changed -= HandleSelectionChanged;
        }
    }
}
