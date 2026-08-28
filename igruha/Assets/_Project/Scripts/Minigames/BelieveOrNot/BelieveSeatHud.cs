using TMPro;
using UnityEngine;

namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>
    /// Две строки на экране, которые отвечают за вопрос «что вообще
    /// происходит»: кто ты в этом коне и что сейчас сказали за столом.
    ///
    /// <b>Зачем экран, если есть пузыри.</b> Пузырь висит в мире над головой,
    /// и своего собственного сидящий не видит никогда — он у него за спиной,
    /// позади камеры. Реплику оппонента видно только пока кадр стоит ровно;
    /// стоит камере уехать или зрителю встать между — и половина разговора
    /// пропадает. Разговор — это вся игра, и он обязан быть виден гарантированно,
    /// а не при удачном ракурсе. Пузыри при этом остаются: они для зала.
    ///
    /// <b>Зачем строка роли.</b> Карточка показывается три секунды и гаснет,
    /// а уговоры идут сорок. До плейтеста 28.08 Знающий эти сорок секунд
    /// держал в голове, что ему выпало, а Решающий вообще не видел на экране
    /// ни слова о том, что от него хотят.
    /// </summary>
    public sealed class BelieveSeatHud : MonoBehaviour
    {
        [Tooltip("Постоянная строка: кто ты в этом коне и что тебе делать")]
        [SerializeField] private TMP_Text roleText;

        [Tooltip("Всплывающая строка: реплика за столом и исход кона")]
        [SerializeField] private TMP_Text talkText;

        [SerializeField] private Color knowerColor = new Color(0.98f, 0.85f, 0.45f);
        [SerializeField] private Color deciderColor = new Color(0.6f, 0.85f, 1f);
        [SerializeField] private Color neutralColor = new Color(0.93f, 0.91f, 0.85f);

        private float talkHideAt;

        /// <summary>Показать, кто ты в этом коне. Держится до конца кона.</summary>
        public void ShowRole(string line, bool knower)
        {
            if (roleText == null)
            {
                return;
            }

            roleText.color = knower ? knowerColor : deciderColor;
            roleText.text = line;
            roleText.enabled = true;
        }

        /// <summary>Убрать строку роли: кон кончился или ты в нём зритель.</summary>
        public void HideRole()
        {
            if (roleText != null)
            {
                roleText.enabled = false;
            }
        }

        /// <summary>Реплика за столом. Видна всем, а не только сидящим: зал тоже слушает.</summary>
        public void ShowTalk(string speaker, string phrase, float seconds)
        {
            Show($"{speaker}: «{phrase}»", neutralColor, seconds);
        }

        /// <summary>Исход кона той же строкой — она уже там, где игрок смотрит.</summary>
        public void ShowResult(string line, bool good, float seconds)
        {
            Show(line, good ? knowerColor : deciderColor, seconds);
        }

        /// <summary>Убрать обе строки: кон кончился, матч закрылся, игрок уехал в хаб.</summary>
        public void HideAll()
        {
            HideRole();

            if (talkText != null)
            {
                talkText.enabled = false;
            }

            talkHideAt = 0f;
        }

        private void Show(string line, Color color, float seconds)
        {
            if (talkText == null)
            {
                return;
            }

            talkText.color = color;
            talkText.text = line;
            talkText.enabled = true;
            talkHideAt = Time.time + Mathf.Max(0.1f, seconds);
        }

        private void Update()
        {
            if (talkHideAt <= 0f || Time.time < talkHideAt)
            {
                return;
            }

            talkHideAt = 0f;

            if (talkText != null)
            {
                talkText.enabled = false;
            }
        }
    }
}
