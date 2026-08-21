using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Единственный элемент экранного интерфейса этой игры: сколько секунд
    /// осталось в текущей стадии.
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

        private int lastSeconds = -1;
        private byte lastStage = MinigameStageState.NoStage;

        private void Awake()
        {
            if (stageState == null)
            {
                stageState = FindAnyObjectByType<MinigameStageState>();
            }
        }

        private void Update()
        {
            if (stageState == null)
            {
                return;
            }

            byte stage = stageState.Stage;
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
    }
}
