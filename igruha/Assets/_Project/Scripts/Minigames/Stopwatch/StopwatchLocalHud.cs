using TMPro;
using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Строка состояния своего отсчёта. Единственный элемент интерфейса,
    /// который у каждого игрока свой: всё остальное — общее табло.
    ///
    /// Нужна затем, что подсветку кнопки заслоняет спина персонажа, и момент
    /// «отсчёт пошёл» иначе приходится угадывать. Времени она не показывает
    /// и показывать не должна: игра ровно про то, чтобы отмерять без таймера.
    /// </summary>
    public sealed class StopwatchLocalHud : MonoBehaviour
    {
        [SerializeField] private StopwatchMinigame game;
        [SerializeField] private TMP_Text label;
        [Tooltip("Пока кнопку можно зажать")]
        [SerializeField] private string idleText = "ЗАЖМИ E";
        [Tooltip("Пока кнопку держат")]
        [SerializeField] private string runningText = "ОТСЧЁТ ИДЁТ";
        [Tooltip("После того как отпустили")]
        [SerializeField] private string stoppedText = "ГОТОВО — ждём остальных";
        [SerializeField] private Color idleColor = new Color(0.85f, 0.87f, 0.92f);
        [SerializeField] private Color runningColor = new Color(1f, 0.28f, 0.2f);
        [SerializeField] private Color stoppedColor = new Color(0.55f, 0.85f, 0.55f);
        [Tooltip("Частота мигания строки во время отсчёта, Гц")]
        [SerializeField] private float blinkSpeed = 2.4f;

        private void Reset()
        {
            label = GetComponent<TMP_Text>();
        }

        private void Update()
        {
            if (label == null || game == null)
            {
                return;
            }

            CageButton button = game.LocalButton;
            if (button == null || !button.WindowOpen)
            {
                if (label.enabled)
                {
                    label.enabled = false;
                }

                return;
            }

            label.enabled = true;

            switch (button.State)
            {
                case CageButton.ButtonState.Running:
                    label.text = runningText;
                    // Мигание, а не ровный свет: ровный теряется на сером фоне,
                    // а «идёт прямо сейчас» должно читаться боковым зрением.
                    float t = 0.55f + 0.45f * Mathf.Sin(Time.time * blinkSpeed * Mathf.PI * 2f);
                    label.color = runningColor * t;
                    break;
                case CageButton.ButtonState.Stopped:
                    label.text = stoppedText;
                    label.color = stoppedColor;
                    break;
                default:
                    label.text = idleText;
                    label.color = idleColor;
                    break;
            }
        }
    }
}
