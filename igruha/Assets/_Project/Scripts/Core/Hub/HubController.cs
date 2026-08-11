using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Igruha.Core.Interaction;
using Igruha.Core.Minigame;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// UI-логика хаба: подсказка у якоря, окно подтверждения и запуск мини-игры.
    /// MVP-переход без кинематографичной камеры (ROADMAP 5.6 — позже).
    /// </summary>
    public sealed class HubController : MonoBehaviour
    {
        [SerializeField] private MinigameLoader loader;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private TMP_Text confirmText;

        private PlayerInteractor interactor;
        private MinigameAnchor[] anchors;
        private MinigameAnchor pendingAnchor;
        private bool confirmOpenedThisFrame;

        private void Awake()
        {
            anchors = FindObjectsByType<MinigameAnchor>(FindObjectsSortMode.None);
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i].Activated += OnAnchorActivated;
            }

            if (confirmPanel != null)
            {
                confirmPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (anchors == null)
            {
                return;
            }

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] != null)
                {
                    anchors[i].Activated -= OnAnchorActivated;
                }
            }
        }

        /// <summary>Связать с персонажем локального игрока (вызывает HubBootstrap после спавна).</summary>
        public void BindLocalPlayer(PlayerInteractor localInteractor)
        {
            interactor = localInteractor;
        }

        private void Update()
        {
            if (pendingAnchor != null)
            {
                UpdateConfirmation();
                return;
            }

            UpdatePrompt();
        }

        private void UpdatePrompt()
        {
            if (promptText == null)
            {
                return;
            }

            IInteractable target = interactor != null ? interactor.CurrentInteractable : null;
            string text = target != null ? target.InteractionPrompt : string.Empty;
            if (promptText.text != text)
            {
                promptText.text = text;
            }
        }

        private void OnAnchorActivated(MinigameAnchor anchor)
        {
            if (pendingAnchor != null)
            {
                return;
            }

            if (!anchor.IsPlayable)
            {
                if (promptText != null)
                {
                    promptText.text = $"{anchor.InteractionPrompt} (игра ещё не готова)";
                }

                return;
            }

            pendingAnchor = anchor;
            confirmOpenedThisFrame = true;

            if (confirmText != null)
            {
                confirmText.text = $"Запустить «{anchor.Definition.DisplayName}»?\n\nEnter — да     Esc — отмена";
            }

            if (confirmPanel != null)
            {
                confirmPanel.SetActive(true);
            }
        }

        private void UpdateConfirmation()
        {
            // Кадр открытия пропускаем: та же кнопка не должна сразу подтвердить.
            if (confirmOpenedThisFrame)
            {
                confirmOpenedThisFrame = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            bool confirmed = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool cancelled = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                confirmed |= gamepad.buttonSouth.wasPressedThisFrame;
                cancelled |= gamepad.buttonEast.wasPressedThisFrame;
            }

            if (confirmed)
            {
                MinigameAnchor anchor = pendingAnchor;
                CloseConfirmation();
                loader?.Load(anchor.Definition);
                return;
            }

            if (cancelled)
            {
                CloseConfirmation();
            }
        }

        private void CloseConfirmation()
        {
            pendingAnchor = null;
            if (confirmPanel != null)
            {
                confirmPanel.SetActive(false);
            }
        }
    }
}
