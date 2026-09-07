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

        /// <summary>Кегль описания, каким он задан в сцене. Ноль — ещё не считан.</summary>
        private float objectiveFontMax;

        public void Show(MinigameDefinition definition, Action closedCallback)
        {
            onClosed = closedCallback;

            if (panel == null || definition == null)
            {
                Close();
                return;
            }

            EnforceWrapping();

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
            EnforceObjectiveFits();
        }

        /// <summary>
        /// Заставить строки переноситься по словам.
        ///
        /// ⚠️ В шаблоне сцены у всех трёх полей стоит <c>NoWrap</c>, и описание
        /// длиннее одной строки уезжало за оба края экрана: у «Верю / не верю»
        /// из четырнадцати слов читались шесть средних, начало и конец были
        /// срезаны рамкой кадра. Правится кодом, а не в инспекторе, по той же
        /// причине, что и формат камеры (`igruha/CLAUDE.md`, 2a): шаблон
        /// копируется в каждую мини-игру, YAML не переживает слияние веток,
        /// и одна правка руками чинит одну сцену из девяти.
        /// </summary>
        private void EnforceWrapping()
        {
            Wrap(titleText);
            Wrap(objectiveText);
            Wrap(controlsText);
        }

        private static void Wrap(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>Отступ описания от заголовка и от списка клавиш, пикселей канваса.</summary>
        private const float ObjectivePadding = 24f;

        /// <summary>Ниже этого кегля описание уже не читается — лучше упереться, чем ужать в пыль.</summary>
        private const float ObjectiveMinFont = 22f;

        /// <summary>
        /// Уложить описание в свободную полосу между заголовком и списком
        /// клавиш.
        ///
        /// ⚠️ <b>Та же болезнь, что и <see cref="EnforceWrapping"/>, и лечится
        /// там же.</b> В шаблоне сцены коробка описания — 1200×260 при кегле 38
        /// и режиме <c>Overflow</c>, то есть держит пять строк, а дальше молча
        /// лезет за свои края: вверх на заголовок, вниз на список клавиш. Всё
        /// это копируется в каждую мини-игру, и правка руками чинит одну сцену
        /// из девяти.
        ///
        /// Полоса считается по фактическим углам заголовка и списка клавиш,
        /// а не по числам из шаблона: сцены разъезжаются, а углы честные.
        /// Автокегль включается здесь же — без него длинное описание всё равно
        /// вылезло бы, только уже из новой коробки.
        /// </summary>
        private void EnforceObjectiveFits()
        {
            if (objectiveText == null || panel == null)
            {
                return;
            }

            var panelRect = panel.transform as RectTransform;
            var objectiveRect = objectiveText.rectTransform;
            if (panelRect == null || objectiveRect.parent != panelRect)
            {
                return;
            }

            float top = LocalEdge(panelRect, titleText, false, panelRect.rect.yMax);
            float bottom = LocalEdge(panelRect, controlsText, true, panelRect.rect.yMin);

            float height = top - bottom - ObjectivePadding * 2f;
            if (height <= 0f)
            {
                return;
            }

            // ⚠️ Ширину и центр снимаем ДО смены якорей, и только фактические.
            // У поля они в разных сценах разные: в «Секундомере» якоря сведены
            // в точку, и sizeDelta.x — это готовая ширина, а в «Порядке банок»
            // растянуты, и та же sizeDelta.x означает отступ от краёв, обычно
            // ноль. Перевод растянутого поля в точечное «как есть» даёт ширину
            // ноль — описание вставало столбиком по букве в строке.
            float width = objectiveRect.rect.width;
            float centreX = LocalCentreX(panelRect, objectiveText);

            objectiveRect.anchorMin = new Vector2(0.5f, 0.5f);
            objectiveRect.anchorMax = new Vector2(0.5f, 0.5f);
            objectiveRect.pivot = new Vector2(0.5f, 0.5f);
            objectiveRect.sizeDelta = new Vector2(width, height);
            objectiveRect.anchoredPosition = new Vector2(centreX, (top + bottom) * 0.5f);

            // Кегль-максимум берётся из сцены ровно один раз. Со второго
            // показа fontSize у поля с автокеглем — это уже ужатый размер,
            // и если брать его снова, описание усыхает от показа к показу.
            if (objectiveFontMax <= 0f)
            {
                objectiveFontMax = Mathf.Max(ObjectiveMinFont, objectiveText.fontSize);
            }

            // Порядок важен: присваивание fontSize после включения автокегля
            // сбрасывает режим, и текст снова растёт (та же грабля, что
            // у граней табло в CircusArenaBuilder).
            objectiveText.fontSizeMax = objectiveFontMax;
            objectiveText.fontSizeMin = ObjectiveMinFont;
            objectiveText.enableAutoSizing = true;
        }

        /// <summary>Центр поля по горизонтали в системе координат панели.</summary>
        private static float LocalCentreX(RectTransform panelRect, TMP_Text field)
        {
            var corners = new Vector3[4];
            field.rectTransform.GetWorldCorners(corners);
            float min = panelRect.InverseTransformPoint(corners[0]).x;
            float max = min;
            for (int i = 1; i < 4; i++)
            {
                float x = panelRect.InverseTransformPoint(corners[i]).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }

            return (min + max) * 0.5f;
        }

        /// <summary>
        /// Край соседнего поля в системе координат панели. <paramref name="upper"/> —
        /// брать верхний край (для нижнего соседа) или нижний (для верхнего).
        /// Поля может не быть вовсе — тогда возвращается край самой панели.
        /// </summary>
        private static float LocalEdge(RectTransform panelRect, TMP_Text neighbour, bool upper, float fallback)
        {
            if (neighbour == null || !neighbour.gameObject.activeInHierarchy)
            {
                return fallback;
            }

            var corners = new Vector3[4];
            neighbour.rectTransform.GetWorldCorners(corners);
            float edge = panelRect.InverseTransformPoint(corners[0]).y;
            for (int i = 1; i < 4; i++)
            {
                float y = panelRect.InverseTransformPoint(corners[i]).y;
                edge = upper ? Mathf.Max(edge, y) : Mathf.Min(edge, y);
            }

            return edge;
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
