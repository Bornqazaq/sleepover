using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Igruha.Core.Player;

namespace Igruha.Minigames.Exam
{
    /// <summary>
    /// Панель Ведущего: три поля, тайная отметка верного варианта и кнопка
    /// «Готово». Видна только тому, кто сейчас за кафедрой, и только в фазе
    /// печати.
    ///
    /// Единственная механика игры, которой нет в Core вовсе.
    ///
    /// <b>Отметка верного варианта тайная.</b> Она не уходит ни на доску,
    /// ни в HUD, ни в сетевое состояние — живёт в контроллере на сервере
    /// до раскрытия створок.
    /// </summary>
    public sealed class ExamQuestionInput : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_InputField questionField;
        [SerializeField] private TMP_InputField optionAField;
        [SerializeField] private TMP_InputField optionBField;
        [SerializeField] private Toggle correctAToggle;
        [SerializeField] private Toggle correctBToggle;
        [SerializeField] private Button doneButton;
        [SerializeField] private Button presetButton;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text hintText;

        /// <summary>Ведущий нажал «Готово»: фаза печати закрывается досрочно.</summary>
        public event Action DoneRequested;

        public bool IsOpen { get; private set; }

        private ExamConfig config;
        private ExamQuestionPresets presets;
        private PlayerInputReader inputReader;
        private PlayerEmoteAbility emoteAbility;
        private bool emoteWasEnabled;
        private bool inputWasEnabled;
        private int presetCursor;

        private void Awake()
        {
            SetRootActive(false);

            if (doneButton != null)
            {
                doneButton.onClick.AddListener(() => DoneRequested?.Invoke());
            }

            if (presetButton != null)
            {
                presetButton.onClick.AddListener(TakeNextPreset);
            }

            if (correctAToggle != null)
            {
                correctAToggle.onValueChanged.AddListener(on => { if (on) CorrectSide = ExamSide.A; });
            }

            if (correctBToggle != null)
            {
                correctBToggle.onValueChanged.AddListener(on => { if (on) CorrectSide = ExamSide.B; });
            }
        }

        /// <summary>
        /// Что Ведущий отметил верным. <see cref="ExamSide.None"/> — не отметил
        /// вовсе: тогда сервер выберет случайно.
        /// </summary>
        public ExamSide CorrectSide { get; private set; } = ExamSide.None;

        public string Question => questionField != null ? questionField.text.Trim() : string.Empty;
        public string OptionA => optionAField != null ? optionAField.text.Trim() : string.Empty;
        public string OptionB => optionBField != null ? optionBField.text.Trim() : string.Empty;

        /// <summary>Вопрос годен к показу: есть и сам вопрос, и оба варианта.</summary>
        public bool HasQuestion =>
            !string.IsNullOrWhiteSpace(Question) &&
            !string.IsNullOrWhiteSpace(OptionA) &&
            !string.IsNullOrWhiteSpace(OptionB);

        /// <summary>
        /// Открыть панель своему игроку. Ввод игры и колесо эмоций на это
        /// время гасятся: иначе WASD будет одновременно печататься и водить
        /// персонажа, а Tab — открывать колесо вместо перехода между полями.
        /// </summary>
        public void Open(ExamConfig examConfig, ExamQuestionPresets questionPresets, PlayerController host)
        {
            config = examConfig;
            presets = questionPresets;

            if (questionField != null)
            {
                questionField.characterLimit = config != null ? config.QuestionMaxLength : 80;
                questionField.text = string.Empty;
            }

            int optionLimit = config != null ? config.OptionMaxLength : 30;
            if (optionAField != null)
            {
                optionAField.characterLimit = optionLimit;
                optionAField.text = string.Empty;
            }

            if (optionBField != null)
            {
                optionBField.characterLimit = optionLimit;
                optionBField.text = string.Empty;
            }

            CorrectSide = ExamSide.None;
            if (correctAToggle != null) correctAToggle.SetIsOnWithoutNotify(false);
            if (correctBToggle != null) correctBToggle.SetIsOnWithoutNotify(false);

            if (hintText != null)
            {
                hintText.text = "Напечатай вопрос и два варианта. Отметь верный — его никто не увидит.";
            }

            SuppressPlayerControls(host);
            SetRootActive(true);
            IsOpen = true;
            questionField?.ActivateInputField();
        }

        /// <summary>
        /// Закрыть панель и вернуть игроку управление. Зовётся и в конце фазы,
        /// и в <c>OnRoundEnded</c>: незакрытая панель уедет в хаб вместе
        /// с персонажем, и там он будет печатать вместо ходьбы.
        /// </summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            SetRootActive(false);
            RestorePlayerControls();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (countdownText != null)
            {
                countdownText.text = string.Empty;
            }

            // Tab ходит по трём полям. Бинд колеса эмоций при этом не тронут —
            // само колесо на время печати выключено, поэтому конфликта нет
            // (замороженное из igruha/CLAUDE.md остаётся замороженным).
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                FocusNextField();
            }
        }

        /// <summary>Показать, сколько осталось до конца фазы печати.</summary>
        public void SetCountdown(float secondsLeft)
        {
            if (countdownText != null)
            {
                countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)).ToString();
            }
        }

        private void FocusNextField()
        {
            if (questionField == null || optionAField == null || optionBField == null)
            {
                return;
            }

            if (questionField.isFocused)
            {
                optionAField.ActivateInputField();
            }
            else if (optionAField.isFocused)
            {
                optionBField.ActivateInputField();
            }
            else
            {
                questionField.ActivateInputField();
            }
        }

        /// <summary>
        /// Подставить следующую заготовку. Ровно то, чем пользуется геймпад:
        /// восемьдесят символов за тридцать секунд стиком не набрать.
        /// Верный вариант заготовка не приносит — его всё равно отмечать самому.
        /// </summary>
        private void TakeNextPreset()
        {
            if (presets == null || presets.Count == 0)
            {
                return;
            }

            ExamQuestionPresets.Preset preset = presets.Get(presetCursor);
            presetCursor = (presetCursor + 1) % presets.Count;

            if (questionField != null) questionField.text = preset.Question;
            if (optionAField != null) optionAField.text = preset.OptionA;
            if (optionBField != null) optionBField.text = preset.OptionB;
        }

        private void SuppressPlayerControls(PlayerController host)
        {
            if (host == null)
            {
                return;
            }

            if (host.TryGetComponent(out inputReader) && inputReader.LocallyControlled)
            {
                inputWasEnabled = inputReader.enabled;
                inputReader.enabled = false;
            }

            if (host.TryGetComponent(out emoteAbility))
            {
                emoteWasEnabled = emoteAbility.enabled;
                emoteAbility.enabled = false;
            }
        }

        private void RestorePlayerControls()
        {
            if (inputReader != null)
            {
                inputReader.enabled = inputWasEnabled;
                inputReader = null;
            }

            if (emoteAbility != null)
            {
                emoteAbility.enabled = emoteWasEnabled;
                emoteAbility = null;
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
