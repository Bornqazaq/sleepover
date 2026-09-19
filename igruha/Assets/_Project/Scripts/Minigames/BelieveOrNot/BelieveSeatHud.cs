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

        [Tooltip("Цвет пояснения после названия роли («· что в коробках, не знаешь…»). " +
                 "Прозрачный — вся строка цветом роли, как было")]
        [SerializeField] private Color roleDetailColor = Color.clear;

        [Header("Исход кона")]
        [SerializeField] private Color goodColor = new Color(0.98f, 0.85f, 0.45f);
        [SerializeField] private Color badColor = new Color(0.6f, 0.85f, 1f);

        [Header("Подложки")]
        [Tooltip("Плашка под строкой роли. Прячется вместе с текстом; пусто — строка без плашки")]
        [SerializeField] private GameObject rolePlate;

        [Tooltip("Плашка под строкой реплики")]
        [SerializeField] private GameObject talkPlate;

        [Tooltip("Через сколько секунд из строки роли уходит последний хвост — подсказка " +
                 "клавиш. Ноль оставляет строку целиком, как было")]
        [SerializeField] private float keyHintSeconds = 9f;

        /// <summary>Разделитель названия роли и пояснения в строке, которую собирает игра.</summary>
        private const string RoleSeparator = " · ";

        private float talkHideAt;
        private float roleTrimAt;
        private string roleFull;
        private bool roleKnower;

        /// <summary>Показать, кто ты в этом коне. Держится до конца кона.</summary>
        public void ShowRole(string line, bool knower)
        {
            if (roleText == null)
            {
                return;
            }

            roleFull = line;
            roleKnower = knower;
            roleTrimAt = keyHintSeconds > 0f && TrimKeyHint(line) != line ? Time.time + keyHintSeconds : 0f;
            PaintRole(line);
        }

        /// <summary>
        /// Подсказка клавиш нужна первые секунды кона, а висит все сорок и
        /// повторяет то, что уже написано на кнопках решения и в панели реплик.
        /// Роль и задача при этом обязаны остаться до конца кона.
        /// </summary>
        private static string TrimKeyHint(string line)
        {
            int last = line.LastIndexOf(RoleSeparator, System.StringComparison.Ordinal);
            return last <= 0 ? line : line.Substring(0, last);
        }

        private void PaintRole(string line)
        {
            roleText.color = roleKnower ? knowerColor : deciderColor;
            roleText.text = roleDetailColor.a > 0f ? WithQuietDetail(line) : line;
            SetVisible(roleText, rolePlate, true);
        }

        /// <summary>
        /// Название роли — цветом роли, пояснение после первого разделителя —
        /// спокойным. Вся строка одним ярким цветом читалась криком.
        /// Зовётся раз за кон, не в кадре.
        /// </summary>
        private string WithQuietDetail(string line)
        {
            int split = line.IndexOf(RoleSeparator, System.StringComparison.Ordinal);
            if (split < 0)
            {
                return line;
            }

            string hex = ColorUtility.ToHtmlStringRGBA(roleDetailColor);
            return line.Substring(0, split) + "<color=#" + hex + ">" + line.Substring(split) + "</color>";
        }

        private static void SetVisible(TMP_Text text, GameObject plate, bool visible)
        {
            if (text != null)
            {
                text.enabled = visible;
            }

            if (plate != null && plate.activeSelf != visible)
            {
                plate.SetActive(visible);
            }
        }

        /// <summary>Убрать строку роли: кон кончился или ты в нём зритель.</summary>
        public void HideRole()
        {
            roleTrimAt = 0f;
            SetVisible(roleText, rolePlate, false);
        }

        /// <summary>
        /// Обе строки спрятаны до первого слова. Плашки выключаются и сборкой
        /// интерфейса, но сцена переживает правки руками, а пустая рамка
        /// посреди кадра выглядит недоделкой.
        /// </summary>
        private void Awake()
        {
            SetVisible(roleText, rolePlate, false);
            SetVisible(talkText, talkPlate, false);
        }

        /// <summary>Реплика за столом. Видна всем, а не только сидящим: зал тоже слушает.</summary>
        public void ShowTalk(string speaker, string phrase, float seconds)
        {
            Show($"{speaker}: «{phrase}»", neutralColor, seconds);
        }

        /// <summary>Исход кона той же строкой — она уже там, где игрок смотрит.</summary>
        public void ShowResult(string line, bool good, float seconds)
        {
            Show(line, good ? goodColor : badColor, seconds);
        }

        /// <summary>Убрать обе строки: кон кончился, матч закрылся, игрок уехал в хаб.</summary>
        public void HideAll()
        {
            HideRole();
            roleTrimAt = 0f;
            SetVisible(talkText, talkPlate, false);
            talkHideAt = 0f;
        }

        private void Show(string line, Color color, float seconds)
        {
            if (talkText == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                talkHideAt = 0f;
                SetVisible(talkText, talkPlate, false);
                return;
            }

            talkText.color = color;
            talkText.text = line;
            SetVisible(talkText, talkPlate, true);
            talkHideAt = Time.time + Mathf.Max(0.1f, seconds);
        }

        private void Update()
        {
            if (roleTrimAt > 0f && Time.time >= roleTrimAt)
            {
                roleTrimAt = 0f;
                if (roleText != null && roleText.enabled)
                {
                    PaintRole(TrimKeyHint(roleFull));
                }
            }

            if (talkHideAt <= 0f || Time.time < talkHideAt)
            {
                return;
            }

            talkHideAt = 0f;
            SetVisible(talkText, talkPlate, false);
        }
    }
}
