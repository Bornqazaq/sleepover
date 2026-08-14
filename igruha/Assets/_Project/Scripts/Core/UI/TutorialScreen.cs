using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Igruha.Core.Minigame;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Обучающая заставка перед стартом мини-игры (как в Pummel Party, 4.5).
    /// Данные приходят из MinigameDefinition — для новой игры нужен только
    /// новый ScriptableObject. Закрывается по таймеру или любой кнопкой.
    /// </summary>
    public sealed class TutorialScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text objectiveText;
        [SerializeField] private TMP_Text controlsText;

        private Action onClosed;
        private float hideTimer;
        private bool visible;

        public void Show(MinigameDefinition definition, Action closedCallback)
        {
            onClosed = closedCallback;

            if (panel == null || definition == null)
            {
                Close();
                return;
            }

            if (titleText != null)
            {
                titleText.text = definition.DisplayName;
            }

            if (objectiveText != null)
            {
                objectiveText.text = definition.Objective;
            }

            if (controlsText != null)
            {
                var sb = new StringBuilder();
                string[] hints = definition.ControlHints;
                for (int i = 0; i < hints.Length; i++)
                {
                    sb.AppendLine(hints[i]);
                }

                controlsText.text = sb.ToString();
            }

            hideTimer = definition.TutorialDuration;
            visible = true;
            panel.SetActive(true);
        }

        /// <summary>
        /// Убрать заставку без обратного вызова: раунд уже начал сервер,
        /// и просить старт второй раз не нужно.
        /// </summary>
        public void Hide()
        {
            onClosed = null;
            visible = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void Update()
        {
            if (!visible)
            {
                return;
            }

            hideTimer -= Time.deltaTime;

            bool skipPressed =
                (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

            if (hideTimer <= 0f || skipPressed)
            {
                Close();
            }
        }

        private void Close()
        {
            visible = false;
            if (panel != null)
            {
                panel.SetActive(false);
            }

            Action callback = onClosed;
            onClosed = null;
            callback?.Invoke();
        }
    }
}
