using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Болванка соло-прогона: жмёт кнопку в своей клетке за игрока, которым
    /// никто не управляет. Нужна затем же, зачем манекены в шаблоне мини-игры —
    /// проверить правила на 2, 4 и 8 участниках, не собирая четверых людей.
    ///
    /// Отмеряет цель с промахом из заданного разброса, поэтому рейтинг худших
    /// каждый подраунд получается разный, а не вырожденный. Часть болванок
    /// можно сделать молчунами — проверка правила «не завершивший получает
    /// ошибку всегда».
    /// </summary>
    public sealed class StopwatchDebugBot : MonoBehaviour
    {
        [Tooltip("Максимальный промах болванки мимо цели, с")]
        [SerializeField] private float maxError = 1.2f;
        [Tooltip("С какой вероятностью болванка вообще не нажмёт кнопку")]
        [SerializeField] private float silentChance;

        private CageButton button;
        private PlayerController owner;
        private System.Random random;
        private bool armed;
        private bool silentThisSubround;
        private double pressAt;
        private double stopAt;

        public void Bind(CageButton cageButton, PlayerController player, int seed)
        {
            button = cageButton;
            owner = player;
            random = new System.Random(seed);
        }

        public void Arm(float target)
        {
            if (button == null)
            {
                return;
            }

            armed = true;
            silentThisSubround = random.NextDouble() < silentChance;
            // Небольшая задержка перед стартом: иначе все болванки жмут
            // в один кадр, и досрочный конец стадии проверить нечем.
            pressAt = NetworkClock.Now + 0.2 + random.NextDouble() * 0.8;
            float error = (float)(random.NextDouble() * 2.0 - 1.0) * maxError;
            stopAt = pressAt + Mathf.Max(0.2f, target + error);
        }

        public void Disarm() => armed = false;

        private void Update()
        {
            if (!armed || button == null || owner == null || !button.WindowOpen || silentThisSubround)
            {
                return;
            }

            double now = NetworkClock.Now;
            if (button.State == CageButton.ButtonState.Idle && now >= pressAt)
            {
                button.Interact(owner);
                return;
            }

            if (button.State == CageButton.ButtonState.Running && now >= stopAt)
            {
                button.Interact(owner);
                armed = false;
            }
        }
    }
}
