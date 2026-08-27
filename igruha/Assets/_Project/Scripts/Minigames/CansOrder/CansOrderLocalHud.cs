using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Экранный интерфейс этой игры: сколько секунд осталось в текущей стадии
    /// и что прямо сейчас сделает E.
    ///
    /// <b>Подсказка про E здесь, а не в Core.</b> Строки «Выбрать банку»,
    /// «Поменять местами», «Подтвердить расстановку» полка и кнопка отдают
    /// давно, но во всём проекте их читал ровно один класс — <c>HubController</c>,
    /// то есть внутри мини-игры они не показывались никому. На плейтесте 22.08
    /// это дало «нажимаю E и не понимаю, что происходит»: игрок жал вслепую.
    ///
    /// <b>Экранного счёта здесь нет и быть не должно.</b> Напряжение читается
    /// по высоте клеток и по табло над ямой — как в «Секундомере». А вот
    /// без остатка стадии игрок не понимает, сколько ещё можно двигать банки,
    /// и это не атмосфера, а просто непонятный интерфейс.
    ///
    /// Совпадений, счёта и чужих расстановок эта панель не знает вовсе:
    /// она читает только <see cref="MinigameStageState"/>, где ничего
    /// секретного нет.
    /// </summary>
    public sealed class CansOrderLocalHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [Tooltip("Полоса остатка стадии. Заполнение от 1 до 0")]
        [SerializeField] private Image bar;
        [SerializeField] private MinigameStageState stageState;
        [Tooltip("Что сейчас сделает E. Живёт только в окне выставления")]
        [SerializeField] private TMP_Text promptLabel;
        [Tooltip("Постоянная строка управления полкой. Живёт только в окне выставления")]
        [SerializeField] private TMP_Text controlsLabel;
        [Tooltip("Своя карточка результата. Живёт только в стадии показа")]
        [SerializeField] private TMP_Text revealLabel;
        [Tooltip("Что было в прошлом круге. Живёт только в окне выставления")]
        [SerializeField] private TMP_Text lastCircleLabel;

        [Header("Цвета полосы")]
        [Tooltip("Идёт окно выставления — можно двигать банки")]
        [SerializeField] private Color placementColor = new Color(0.35f, 0.78f, 0.42f);
        [Tooltip("Все остальные стадии: смотреть, а не действовать")]
        [SerializeField] private Color idleColor = new Color(0.45f, 0.48f, 0.55f);

        /// <summary>
        /// Подписи стадий по индексу байта. Порядок совпадает с константами
        /// <see cref="CansOrderMinigame"/>; нулевой элемент — «стадии нет».
        /// </summary>
        private static readonly string[] StageNames =
        {
            string.Empty,
            "РАУНД НАЧИНАЕТСЯ",
            "ВЫСТАВЛЯЙ РАССТАНОВКУ",
            "РЕЗУЛЬТАТЫ",
            "ДНО ОТКРЫВАЕТСЯ",
            string.Empty
        };

        /// <summary>Стадия выставления — единственная, в которой полоса зелёная.</summary>
        private const byte PlacementStage = 2;

        /// <summary>Стадия показа результатов: только в ней игрок видит свой счёт.</summary>
        private const byte RevealStage = 3;

        /// <summary>
        /// Управление полкой одной строкой. Висит всё окно выставления:
        /// обучалку показывают один раз в начале матча, а расставлять банки
        /// приходится каждые шестнадцать секунд.
        /// </summary>
        private const string ControlsHint =
            "Мышь или стрелки ведут подсветку     ЛКМ или E — выбрать банку и поменять местами     " +
            "Enter — подтвердить";

        private int lastSeconds = -1;
        private byte lastStage = MinigameStageState.NoStage;
        private string lastPrompt;
        private CanShelf shelf;
        private CansOrderMinigame rules;
        private byte lastRevealStage = MinigameStageState.NoStage;

        private void Awake()
        {
            if (stageState == null)
            {
                stageState = FindAnyObjectByType<MinigameStageState>();
            }

            if (controlsLabel != null)
            {
                controlsLabel.text = ControlsHint;
            }

            if (revealLabel != null)
            {
                revealLabel.enabled = false;
            }

            if (lastCircleLabel != null)
            {
                lastCircleLabel.enabled = false;
            }

            ShowShelfHints(false);
        }

        /// <summary>
        /// Чью полку показывать в подсказке. Зовёт <see cref="CansOrderMinigame"/>,
        /// когда разобрал, чей персонаж на этой машине: искать полку поиском
        /// по сцене нельзя, клетки раздаются уже после загрузки.
        ///
        /// Спрашиваем полку, а не <c>PlayerInteractor</c>: в окне выставления
        /// полка забирает E сама, минуя радиус взаимодействия, и у интерактора
        /// в этот момент честно нет цели.
        /// </summary>
        public void BindLocalShelf(CanShelf localShelf)
        {
            shelf = localShelf;
        }

        /// <summary>
        /// Правила игры — источник своей карточки результата. Спрашиваем их,
        /// а не считаем сами: совпадения физически не покидают контроллер
        /// до стадии показа, и это единственный способ не сломать честность.
        /// </summary>
        public void BindRules(CansOrderMinigame localRules)
        {
            rules = localRules;
        }

        private void Update()
        {
            if (stageState == null)
            {
                return;
            }

            byte stage = stageState.Stage;
            UpdateShelfHints(stage);
            UpdateReveal(stage);

            bool visible = stage != MinigameStageState.NoStage
                           && stage < StageNames.Length
                           && StageNames[stage].Length > 0;

            if (label != null)
            {
                label.enabled = visible;
            }

            if (bar != null)
            {
                bar.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            float remaining = stageState.StageRemaining;

            // Строка пересобирается только когда меняется целая секунда или
            // стадия: конкатенация каждый кадр — это мусор в Update,
            // а доли секунды здесь ничего не решают и только дёргают взгляд
            // от полки.
            int seconds = Mathf.CeilToInt(remaining);
            if (label != null && (seconds != lastSeconds || stage != lastStage))
            {
                lastSeconds = seconds;
                lastStage = stage;
                label.text = StageNames[stage] + "   " + seconds;
            }

            if (bar == null)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, stageState.StageDuration);
            bar.fillAmount = Mathf.Clamp01(remaining / duration);
            bar.color = stage == PlacementStage ? placementColor : idleColor;
        }

        /// <summary>
        /// Своя карточка результата: что отправил и сколько совпало.
        ///
        /// <b>Появляется в стадии показа, а не в момент подтверждения.</b>
        /// Своё число совпадений игрок обязан узнать вместе со всеми
        /// (спека 4): показанное раньше — это преимущество. Зато табло над
        /// ямой из клетки читается плохо, оно за решёткой и далеко, поэтому
        /// то же самое дублируется на своём экране крупно.
        ///
        /// Текст собираем один раз на входе в стадию, а не каждый кадр:
        /// в нём разметка цвета и склейка строк.
        /// </summary>
        private void UpdateReveal(byte stage)
        {
            if (revealLabel == null)
            {
                return;
            }

            if (stage == lastRevealStage)
            {
                return;
            }

            lastRevealStage = stage;

            string text = stage == RevealStage && rules != null ? rules.BuildLocalRevealText() : string.Empty;
            revealLabel.text = text;
            revealLabel.enabled = text.Length > 0;

            UpdateLastCircle(stage);
        }

        /// <summary>
        /// Прошлый круг — своя расстановка и сколько совпало. Ровно один круг
        /// назад: полный журнал решал бы игру логикой вместо памяти
        /// (решение геймдизайнера 22.08, разбор — в правилах игры).
        /// </summary>
        private void UpdateLastCircle(byte stage)
        {
            if (lastCircleLabel == null)
            {
                return;
            }

            string text = stage == PlacementStage && rules != null ? rules.BuildLastCircleText() : string.Empty;
            lastCircleLabel.text = text;
            lastCircleLabel.enabled = text.Length > 0;
        }

        /// <summary>
        /// Подсказки полки. Показываем только в окне выставления: в остальных
        /// стадиях полка не отзывается, и живая подсказка врала бы.
        /// </summary>
        private void UpdateShelfHints(byte stage)
        {
            bool placement = stage == PlacementStage;
            bool shelfLive = placement && shelf != null && shelf.Active;

            // Строка управления живёт, только пока полкой действительно можно
            // работать: после подтверждения она замирает, и подсказка про ЛКМ
            // врала бы.
            if (controlsLabel != null)
            {
                controlsLabel.enabled = shelfLive;
            }

            if (promptLabel != null)
            {
                promptLabel.enabled = placement;
            }

            if (!placement || promptLabel == null)
            {
                return;
            }

            string prompt = shelfLive
                ? "ЛКМ — " + shelf.InteractionPrompt
                : (rules != null ? rules.LocalWaitHint() : string.Empty);

            // Строку пересобираем только когда она меняется: подсказка живёт
            // в Update, а склейка каждый кадр — это мусор в куче.
            if (prompt == lastPrompt)
            {
                return;
            }

            lastPrompt = prompt;
            promptLabel.text = prompt;
        }

        private void ShowShelfHints(bool visible)
        {
            if (promptLabel != null)
            {
                promptLabel.enabled = visible;
            }

            if (controlsLabel != null)
            {
                controlsLabel.enabled = visible;
            }
        }
    }
}
