using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Igruha.Core.Player;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Панель готовых реплик: показывает набор владельцу, принимает выбор
    /// мышью или цифрами и сообщает игре, какую реплику выбрали. Сама реплику
    /// не произносит — что с ней делать, решает мини-игра, а в сетевой фазе
    /// решает сервер.
    ///
    /// <b>Клавиши читаются здесь напрямую, а не через общий input-ассет.</b>
    /// Все привязки клавиш заморожены (`igruha/CLAUDE.md`, раздел 0), новых
    /// действий туда не добавляем. Тот же приём уже применён в
    /// <c>ExamQuestionInput</c>, где Tab по полям ловится через
    /// <see cref="Keyboard"/>.
    /// </summary>
    public sealed class QuickPhrasePanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        [Tooltip("Контейнер строк. Сами строки панель собирает сама при первом открытии")]
        [SerializeField] private RectTransform container;

        [Tooltip("Подсказка над списком. Пусто — подсказки просто не будет")]
        [SerializeField] private TMP_Text hintText;

        [Header("Вид строки")]
        [SerializeField] private float rowHeight = 34f;
        [SerializeField] private float rowSpacing = 4f;
        [SerializeField] private float fontSize = 18f;
        [SerializeField] private Color rowColor = new Color(0.08f, 0.08f, 0.10f, 0.85f);
        [SerializeField] private Color rowReadyColor = new Color(0.16f, 0.16f, 0.20f, 0.92f);
        [SerializeField] private Color textColor = new Color(0.92f, 0.90f, 0.84f);

        /// <summary>Выбрана реплика с таким индексом в наборе.</summary>
        public event Action<int> PhrasePicked;

        private readonly List<Button> rows = new List<Button>(QuickPhraseSet.MaxPhrases);
        private readonly List<TMP_Text> rowLabels = new List<TMP_Text>(QuickPhraseSet.MaxPhrases);
        private readonly List<Image> rowImages = new List<Image>(QuickPhraseSet.MaxPhrases);

        private QuickPhraseSet activeSet;
        private PlayerInputReader suppressedInput;
        private PlayerEmoteAbility suppressedEmotes;
        private bool inputWasEnabled;
        private bool emotesWereEnabled;
        private float cooldown;
        private float nextAllowedAt;
        private bool cooldownShownReady = true;

        public bool IsOpen { get; private set; }

        /// <summary>Кулдаун истёк, следующую реплику можно отправлять.</summary>
        public bool CanSend => Time.time >= nextAllowedAt;

        private void Awake()
        {
            SetRootActive(false);
        }

        /// <summary>
        /// Открыть панель владельцу.
        ///
        /// <paramref name="suppressMovement"/> гасит ввод и колесо эмоций
        /// на время панели. По умолчанию выключено намеренно: в «Верю / не верю»
        /// колесо эмоций у сидящих обязано остаться живым — рожи там половина
        /// комедии. Гасить нужно там, где панель забирает те же клавиши,
        /// что и движение.
        /// </summary>
        public void Open(QuickPhraseSet set, float phraseCooldown, PlayerController owner = null,
            bool suppressMovement = false, string hint = null)
        {
            if (set == null || set.Count == 0)
            {
                Debug.LogWarning($"{name}: набор реплик пуст — панель не открываю", this);
                return;
            }

            activeSet = set;
            cooldown = Mathf.Max(0f, phraseCooldown);
            nextAllowedAt = 0f;

            EnsureRows(set.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < set.Count;
                rows[i].gameObject.SetActive(used);
                if (used)
                {
                    rowLabels[i].text = $"{i + 1}.  {set.Get(i)}";
                }
            }

            if (hintText != null)
            {
                hintText.text = string.IsNullOrEmpty(hint) ? "Цифры 1–9 или мышью" : hint;
            }

            if (suppressMovement)
            {
                Suppress(owner);
            }

            ApplyCooldownTint(true);
            SetRootActive(true);
            IsOpen = true;
        }

        /// <summary>
        /// Закрыть панель и вернуть всё, что было погашено.
        ///
        /// Звать и в конце фазы, и в <c>OnRoundEnded</c>: незакрытая панель
        /// уедет в хаб вместе с персонажем — ровно тот класс багов, про который
        /// предупреждает <c>MinigameTemplate_HOWTO</c>.
        /// </summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            activeSet = null;
            SetRootActive(false);
            Restore();
        }

        private void Update()
        {
            if (!IsOpen || activeSet == null)
            {
                return;
            }

            bool ready = CanSend;
            if (ready != cooldownShownReady)
            {
                ApplyCooldownTint(ready);
            }

            if (!ready || Keyboard.current == null)
            {
                return;
            }

            // Цифровой ряд читается напрямую: общий input-ассет заморожен.
            for (int i = 0; i < activeSet.Count; i++)
            {
                if (Keyboard.current[FirstDigitKey + i].wasPressedThisFrame)
                {
                    Pick(i);
                    return;
                }
            }
        }

        private const Key FirstDigitKey = Key.Digit1;

        private void Pick(int index)
        {
            if (!IsOpen || activeSet == null || !activeSet.IsValidIndex(index) || !CanSend)
            {
                return;
            }

            nextAllowedAt = Time.time + cooldown;
            ApplyCooldownTint(false);
            PhrasePicked?.Invoke(index);
        }

        /// <summary>
        /// Собрать недостающие строки. Строится один раз за жизнь панели:
        /// дальше строки переиспользуются, и в игровом цикле аллокаций нет.
        /// </summary>
        private void EnsureRows(int count)
        {
            if (container == null)
            {
                Debug.LogError($"{name}: панели реплик не назначен контейнер — строки некуда класть", this);
                return;
            }

            for (int i = rows.Count; i < count; i++)
            {
                rows.Add(BuildRow(i));
            }
        }

        private Button BuildRow(int index)
        {
            var go = new GameObject($"Phrase_{index + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(container, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, rowHeight);
            rect.anchoredPosition = new Vector2(0f, -index * (rowHeight + rowSpacing));

            var image = go.GetComponent<Image>();
            image.color = rowColor;
            rowImages.Add(image);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);

            var label = textGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            rowLabels.Add(label);

            var button = go.GetComponent<Button>();
            int captured = index;
            button.onClick.AddListener(() => Pick(captured));
            return button;
        }

        /// <summary>Строки гаснут на время кулдауна: видно, что жать пока нечего.</summary>
        private void ApplyCooldownTint(bool ready)
        {
            cooldownShownReady = ready;
            Color color = ready ? rowReadyColor : rowColor;
            for (int i = 0; i < rowImages.Count; i++)
            {
                rowImages[i].color = color;
            }
        }

        private void Suppress(PlayerController owner)
        {
            if (owner == null)
            {
                return;
            }

            if (owner.TryGetComponent(out suppressedInput) && suppressedInput.LocallyControlled)
            {
                inputWasEnabled = suppressedInput.enabled;
                suppressedInput.enabled = false;
            }
            else
            {
                suppressedInput = null;
            }

            if (owner.TryGetComponent(out suppressedEmotes))
            {
                emotesWereEnabled = suppressedEmotes.enabled;
                suppressedEmotes.enabled = false;
            }
        }

        private void Restore()
        {
            if (suppressedInput != null)
            {
                suppressedInput.enabled = inputWasEnabled;
                suppressedInput = null;
            }

            if (suppressedEmotes != null)
            {
                suppressedEmotes.enabled = emotesWereEnabled;
                suppressedEmotes = null;
            }
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
