using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Igruha.Core.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Панель Решающего: «Оставить» или «Поменять». Единственное действие,
    /// которым игрок влияет на исход.
    ///
    /// Решение <b>окончательное и мгновенное</b>, подтверждения нет — в этом
    /// весь драматизм сорока секунд перед ним. Панель закрывается сразу после
    /// нажатия, поэтому второй раз нажать нельзя даже случайно.
    ///
    /// <b>Клавиши читаются здесь напрямую.</b> Все привязки заморожены
    /// (`igruha/CLAUDE.md`, раздел 0), новых действий в общий input-ассет
    /// не добавляем.
    /// </summary>
    public sealed class BelieveDecisionPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button keepButton;
        [SerializeField] private Button swapButton;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private TMP_Text countdownText;

        /// <summary>Решающий решил. Дальше слово за игрой, а в фазе 3 — за сервером.</summary>
        public event Action<Decision> DecisionPicked;

        public bool IsOpen { get; private set; }

        private readonly PanelCursor cursor = new PanelCursor();

        private void Awake()
        {
            SetRootActive(false);

            keepButton?.onClick.AddListener(() => Pick(Decision.Keep));
            swapButton?.onClick.AddListener(() => Pick(Decision.Swap));
        }

        /// <summary>
        /// Открыть панель. Курсор разблокируется: камера у сидящего
        /// фиксированная, мышь ей не нужна, а по кнопкам кликать надо.
        /// </summary>
        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            if (hintText != null)
            {
                hintText.text = "← оставить    •    поменять →";
            }

            cursor.Release();

            SetRootActive(true);
            IsOpen = true;
        }

        /// <summary>Сколько осталось до конца уговоров.</summary>
        public void SetCountdown(float secondsLeft)
        {
            if (countdownText != null)
            {
                countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)).ToString();
            }
        }

        /// <summary>
        /// Закрыть и вернуть курсор как было. Звать и по концу стадии,
        /// и в <c>OnRoundEnded</c>.
        /// </summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            SetRootActive(false);

            cursor.Restore();
        }

        private void Update()
        {
            if (!IsOpen || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                Pick(Decision.Keep);
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                Pick(Decision.Swap);
            }
        }

        private void Pick(Decision decision)
        {
            if (!IsOpen)
            {
                return;
            }

            // Закрываемся до события: обработчик может открыть раскрытие тем же
            // кадром, и панель не должна успеть принять второе нажатие.
            Close();
            DecisionPicked?.Invoke(decision);
        }

        private void SetRootActive(bool value)
        {
            if (root != null)
            {
                root.SetActive(value);
            }
        }
    }
}
