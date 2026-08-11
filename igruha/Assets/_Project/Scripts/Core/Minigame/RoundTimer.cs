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

        private void Update()
        {
            if (!IsRunning)
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
