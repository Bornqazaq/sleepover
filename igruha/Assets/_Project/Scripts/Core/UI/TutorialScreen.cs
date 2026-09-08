using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Igruha.Core.Minigame;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Экран правил перед стартом мини-игры. Данные приходят из
    /// <see cref="MinigameDefinition"/> — для новой игры нужен только новый
    /// ScriptableObject. Закрывается по таймеру или любой кнопкой.
    ///
    /// Карточку собирает <c>HudSkin</c> из сборщика интерфейса, и это важно
    /// для понимания класса: раскладка держится группами компоновки, а не
    /// подгонкой чисел в рантайме. Раньше здесь жили два метода, которые
    /// вручную мерили свободную полосу между заголовком и списком клавиш
    /// и ужимали кегль описания, — они существовали потому, что канвас был
    /// собран руками в шаблоне сцены и разъезжался в каждой из девяти копий.
    /// Собранная кодом карточка не разъезжается, и подгонка не нужна.
    ///
    /// Подсказки управления показываются не абзацем, а строками «клавиша →
    /// действие» (<see cref="ControlHintParser"/>): именно сплошной текст
    /// на весь экран и был жалобой на старый экран правил.
    /// </summary>
    public sealed class TutorialScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text objectiveText;

        [Header("Карточка")]
        [Tooltip("Плашка категории: «Все против всех», «Команды», «Один против всех»")]
        [SerializeField] private TMP_Text categoryText;
        [Tooltip("Готовые строки управления. Лишние прячутся, нехватка молча отбрасывает хвост подсказок")]
        [SerializeField] private TutorialHintRow[] hintRows = Array.Empty<TutorialHintRow>();
        [Tooltip("Полоса, показывающая, сколько осталось до автостарта")]
        [SerializeField] private Image timerFill;
        [Tooltip("Появление карточки")]
        [SerializeField] private UiPop cardPop;

        [Tooltip("Сама карточка: по ней пересчитывается компоновка перед подгонкой кегля")]
        [SerializeField] private RectTransform card;

        private readonly List<ControlHint> hints = new List<ControlHint>(8);

        private Action onClosed;
        private float hideTimer;
        private float hideDuration;
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

            if (categoryText != null)
            {
                categoryText.text = CategoryName(definition.Category);
            }

            if (objectiveText != null)
            {
                objectiveText.text = definition.Objective;
            }

            hideDuration = Mathf.Max(0.01f, definition.TutorialDuration);
            hideTimer = hideDuration;
            visible = true;

            // Панель включается до наполнения строк и подгонки кегля:
            // компоновка выключенного объекта не пересчитывается, и подгонка
            // мерила бы описание по его заготовочной коробке 200×50 — то есть
            // ужимала бы его до нижнего кегля всегда.
            panel.SetActive(true);

            FillHints(definition.ControlHints);
            FitObjective();

            if (timerFill != null)
            {
                timerFill.fillAmount = 1f;
            }

            cardPop?.Play();
        }

        /// <summary>
        /// Разложить подсказки по строкам. Строк в карточке фиксированное
        /// число: она собрана заранее, чтобы показ раунда не создавал объекты.
        /// </summary>
        private void FillHints(IReadOnlyList<string> controlHints)
        {
            ControlHintParser.Parse(controlHints, hints);

            for (int i = 0; i < hintRows.Length; i++)
            {
                if (hintRows[i] == null)
                {
                    continue;
                }

                if (i < hints.Count)
                {
                    hintRows[i].Set(hints[i]);
                }
                else
                {
                    hintRows[i].Clear();
                }
            }
        }

        /// <summary>Наибольший кегль описания.</summary>
        private const float ObjectiveFontMax = 34f;

        /// <summary>Ниже этого кегля описание уже не читается — лучше упереться, чем ужать в пыль.</summary>
        private const float ObjectiveFontMin = 18f;

        /// <summary>Выше этого карточка не растёт: дальше край экрана.</summary>
        private const float CardMaxHeight = 1000f;

        /// <summary>Ниже этого коробку описания не ужимаем даже ради потолка карточки.</summary>
        private const float ObjectiveMinHeight = 110f;

        /// <summary>
        /// Уложить описание: сначала карточка растёт под текст, и только
        /// упершись в потолок, текст начинает ужиматься кеглем.
        ///
        /// Порядок именно такой, потому что обе крайности уже были видны
        /// на карточках. Постоянная высота карточки оставляла у короткой
        /// цели («Плачущие ангелы» — три строки) полкарточки пустоты. А цель
        /// в семь строк («Порядок банок») при фиксированной коробке ложилась
        /// поверх списка клавиш.
        ///
        /// Кегль подбирается замером, а не автокеглем TMP: автокегль считает
        /// себя раньше, чем компоновка выдаст полю настоящую ширину.
        /// </summary>
        private void FitObjective()
        {
            if (objectiveText == null || card == null)
            {
                return;
            }

            var box = objectiveText.GetComponent<LayoutElement>();
            if (box == null)
            {
                return;
            }

            objectiveText.enableAutoSizing = false;
            objectiveText.fontSize = ObjectiveFontMax;

            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            float width = objectiveText.rectTransform.rect.width;
            if (width <= 1f)
            {
                return;
            }

            float needed = objectiveText.GetPreferredValues(objectiveText.text, width, 0f).y;
            box.preferredHeight = Mathf.Max(ObjectiveMinHeight, needed);
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);

            float excess = card.rect.height - CardMaxHeight;
            if (excess > 0f)
            {
                box.preferredHeight = Mathf.Max(ObjectiveMinHeight, box.preferredHeight - excess);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            }

            // Замер повторяется по итоговой коробке: между первым замером
            // и этим местом коробка успела измениться дважды, и старое число
            // ужало бы текст сильнее, чем нужно.
            float available = objectiveText.rectTransform.rect.height;
            float finalNeeded = objectiveText.GetPreferredValues(
                objectiveText.text, objectiveText.rectTransform.rect.width, 0f).y;
            if (finalNeeded <= available || available <= 1f)
            {
                return;
            }

            objectiveText.fontSize = Mathf.Max(ObjectiveFontMin, ObjectiveFontMax * available / finalNeeded);

            // Ужатый текст занимает меньше, чем заказанная под него коробка,
            // и остаток превратился бы в дыру между целью и списком клавиш.
            float shrunk = objectiveText.GetPreferredValues(
                objectiveText.text, objectiveText.rectTransform.rect.width, 0f).y;
            if (shrunk < box.preferredHeight)
            {
                box.preferredHeight = Mathf.Max(ObjectiveMinHeight, shrunk);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            }
        }

        private static string CategoryName(MinigameCategory category)
        {
            switch (category)
            {
                case MinigameCategory.Team: return "Команда на команду";
                case MinigameCategory.Asymmetric: return "Разные роли";
                default: return "Все против всех";
            }
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

            if (timerFill != null)
            {
                timerFill.fillAmount = Mathf.Clamp01(hideTimer / hideDuration);
            }

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
