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

        [Header("Отсчёт")]
        [Tooltip("Цвет отсчёта, пока время есть")]
        [SerializeField] private Color countdownColor = new Color(0.93f, 0.91f, 0.85f);

        [Tooltip("Цвет отсчёта на последних секундах. Над кадром висит ещё и матчевый " +
                 "таймер, и без этого важный отсчёт терялся рядом с ним")]
        [SerializeField] private Color countdownUrgentColor = new Color(0.98f, 0.78f, 0.35f);

        [Tooltip("С какой секунды отсчёт становится тревожным")]
        [SerializeField] private int urgentSeconds = 5;

        /// <summary>Решающий решил. Дальше слово за игрой, а в фазе 3 — за сервером.</summary>
        public event Action<Decision> DecisionPicked;

        public bool IsOpen { get; private set; }

        private readonly PanelCursor cursor = new PanelCursor();

        private void Awake()
        {
            // Прячемся только если панель не открывают прямо сейчас.
            // У панели, выключенной в сцене, Awake впервые срабатывает ровно
            // в момент её включения из Open() — и без этой проверки она
            // гасила сама себя: флаг «открыта» стоял, а объекта на экране нет.
            if (!IsOpen)
            {
                SetRootActive(false);
            }

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
                // Кнопки подписаны сами, и повторять подписи под ними незачем.
                // Сказать надо то, чего по кнопкам не видно: второго нажатия
                // не будет.
                hintText.text = "Решение окончательное";
            }

            cursor.Release();

            IsOpen = true;
            SetRootActive(true);
        }

        /// <summary>
        /// Сколько осталось до конца уговоров. Последние секунды идут другим
        /// цветом: решение принимается именно по этому числу, а не по матчевому
        /// таймеру над кадром, и на общем кремовом они не отличались ничем.
        /// </summary>
        public void SetCountdown(float secondsLeft)
        {
            if (countdownText == null)
            {
                return;
            }

            int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
            countdownText.text = whole.ToString();
            countdownText.color = whole <= urgentSeconds ? countdownUrgentColor : countdownColor;
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
