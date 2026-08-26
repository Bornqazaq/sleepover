using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Панель показа: Знающий видит свою карточку крупным планом. Больше
    /// её не видит никто.
    ///
    /// <b>Почему панель, а не открытая крышка.</b> Вокруг стола стоят зрители,
    /// и открытую крышку увидит всякий, кто смотрит на стол сбоку или сверху.
    /// В общем 3D-мире «открыть только одному» не бывает — бывает «не открывать
    /// вовсе и показать одному панелью» (спека 5.5).
    ///
    /// Карточка приходит в <see cref="Show"/> единственным путём. В фазе 3 этот
    /// метод позовёт адресный <c>Rpc</c>, доставленный ровно одному клиенту,
    /// и переписывать здесь будет нечего.
    /// </summary>
    public sealed class BelievePeekView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Image cardImage;
        [SerializeField] private TMP_Text cardLabel;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private TMP_Text countdownText;

        [Header("Цвета карточек")]
        [SerializeField] private Color winColor = new Color(0.35f, 0.72f, 0.4f);
        [SerializeField] private Color loseColor = new Color(0.75f, 0.32f, 0.3f);

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            // Прячемся только если панель не открывают прямо сейчас.
            // У панели, выключенной в сцене, Awake впервые срабатывает ровно
            // в момент её включения из Show() — и без этой проверки она
            // гасила сама себя: флаг «открыта» стоял, а объекта на экране нет.
            if (!IsOpen)
            {
                SetRootActive(false);
            }
        }

        /// <summary>Показать карточку. Зовётся только у того, кто Знающий.</summary>
        public void Show(BelieveCard card)
        {
            if (card == BelieveCard.Unknown)
            {
                return;
            }

            bool win = card == BelieveCard.Win;

            if (cardImage != null)
            {
                cardImage.color = win ? winColor : loseColor;
            }

            if (cardLabel != null)
            {
                // Словами, а не значками: атлас шрифта статический, и знака,
                // которого в наборе нет, не будет видно вовсе (STATE 3.12).
                cardLabel.text = win ? "ГАЛОЧКА" : "КРЕСТ";
            }

            if (hintText != null)
            {
                hintText.text = win
                    ? "У тебя галочка. Убеди его НЕ меняться."
                    : "У тебя крест. Убеди его ПОМЕНЯТЬСЯ.";
            }

            IsOpen = true;
            SetRootActive(true);
        }

        /// <summary>Сколько осталось до конца показа.</summary>
        public void SetCountdown(float secondsLeft)
        {
            if (countdownText != null)
            {
                countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)).ToString();
            }
        }

        /// <summary>
        /// Закрыть. Звать и по концу стадии, и в <c>OnRoundEnded</c>: незакрытая
        /// панель уедет в хаб вместе с персонажем.
        /// </summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            SetRootActive(false);
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
