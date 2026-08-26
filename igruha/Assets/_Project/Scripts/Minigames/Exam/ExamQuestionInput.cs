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
        private string lastHint;

        private const string HintNeedQuestion = "Впиши вопрос. Tab — следующее поле, «Взять готовый» — подставит заготовку.";
        private const string HintNeedOptions = "Нужны оба варианта: без них вопрос не состоится и ход сгорит.";
        private const string HintNeedCorrect = "Отметь верный вариант — иначе его выберет случай. Никто не увидит.";
        private const string HintReady = "Вопрос готов. Жми «Готово» или дождись таймера.";

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
            // Открыть уже открытую панель нельзя, и это не придирка к стилю.
            // У клиента фаза печати приходит дважды — сменой Ведущего в
            // состоянии матча и сменой стадии, — и второй Open запоминал бы
            // «ввод был выключен» вместо «ввод был включён»: он сам его и
            // выключил секундой раньше. После закрытия панели человек
            // оставался без управления до конца матча.
            if (IsOpen)
            {
                return;
            }

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

            lastHint = null;

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

            if (countdownText != null)
            {
                countdownText.text = string.Empty;
            }

            SetRootActive(false);
            RestorePlayerControls();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            UpdateHint();

            // Tab ходит по трём полям. Бинд колеса эмоций при этом не тронут —
            // само колесо на время печати выключено, поэтому конфликта нет
            // (замороженное из igruha/CLAUDE.md остаётся замороженным).
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                FocusNextField();
            }
        }

        /// <summary>
        /// Подсказать, чего вопросу не хватает прямо сейчас.
        ///
        /// Незаполненное поле стоит Ведущему всего хода: вопрос без варианта Б
        /// не состоится вовсе (спека 5.7), и человек узнаёт об этом уже после
        /// таймера. На ручном прогоне 26.08 так сгорели оба хода подряд.
        ///
        /// Текст — готовые константы, а не сборка на лету: это Update.
        /// </summary>
        private void UpdateHint()
        {
            if (hintText == null)
            {
                return;
            }

            string hint;
            if (string.IsNullOrWhiteSpace(Question))
            {
                hint = HintNeedQuestion;
            }
            else if (string.IsNullOrWhiteSpace(OptionA) || string.IsNullOrWhiteSpace(OptionB))
            {
                hint = HintNeedOptions;
            }
            else if (CorrectSide == ExamSide.None)
            {
                hint = HintNeedCorrect;
            }
            else
            {
                hint = HintReady;
            }

            if (ReferenceEquals(hint, lastHint))
            {
                return;
            }

            lastHint = hint;
            hintText.text = hint;
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
        /// <summary>
        /// Заполнить панель заготовкой и отметить вариант — за болванку
        /// автопрогона. Печатать ей нечем, а Ведущим на стенде из восьми
        /// процессов побывает каждый: без этого семь вопросов из восьми
        /// не состоялись бы и проверять было бы нечего.
        ///
        /// Дальше болванка идёт тем же путём, что человек: «Готово», ServerRpc,
        /// серверные проверки. Короткого пути в обход них нет намеренно.
        /// </summary>
        public void FillWithPreset(ExamSide correct)
        {
            if (!IsOpen)
            {
                return;
            }

            TakeNextPreset();

            CorrectSide = correct;
            if (correctAToggle != null) correctAToggle.SetIsOnWithoutNotify(correct == ExamSide.A);
            if (correctBToggle != null) correctBToggle.SetIsOnWithoutNotify(correct == ExamSide.B);
        }

        /// <summary>Нажать «Готово» за болванку — то же событие, что даёт кнопка.</summary>
        public void RequestDone() => DoneRequested?.Invoke();

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
