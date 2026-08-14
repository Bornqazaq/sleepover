using System;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Таймер раунда с автозавершением (обязателен каждой мини-игре, 4.3).
    /// Позже время будет реплицироваться NetworkVariable — потребители
    /// уже сейчас читают только Remaining/событие.
    /// </summary>
    public sealed class RoundTimer : MonoBehaviour
    {
        public event Action Finished;

        public bool IsRunning { get; private set; }
        public float Remaining { get; private set; }
        public float Duration { get; private set; }

        /// <summary>
        /// Время приходит от сервера: локальный тик выключен, а Finished не
        /// стреляет. Иначе у каждого клиента раунд кончался бы в своё время.
        /// </summary>
        public bool DrivenExternally { get; set; }

        public void StartTimer(float duration)
        {
            Duration = Mathf.Max(0f, duration);
            Remaining = Duration;
            IsRunning = Duration > 0f;
        }

        public void StopTimer()
        {
            IsRunning = false;
        }

        /// <summary>Показать время, посчитанное сервером.</summary>
        public void SyncFromNetwork(float remaining, float duration)
        {
            Duration = Mathf.Max(0f, duration);
            Remaining = Mathf.Clamp(remaining, 0f, Duration);
            IsRunning = Remaining > 0f;
        }

        private void Update()
        {
            if (!IsRunning || DrivenExternally)
            {
                return;
            }

            Remaining -= Time.deltaTime;
            if (Remaining <= 0f)
            {
                Remaining = 0f;
                IsRunning = false;
                Finished?.Invoke();
            }
        }
    }
}
