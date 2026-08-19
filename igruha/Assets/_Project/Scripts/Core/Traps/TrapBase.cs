using System;
using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// База ловушки: Activate — публичная точка входа (позже server-authoritative),
    /// кулдаун общий. Конкретные ловушки реализуют OnActivated.
    /// </summary>
    public abstract class TrapBase : MonoBehaviour
    {
        [Tooltip("Кулдаун повторной активации, с")]
        [SerializeField] private float cooldown = 3f;

        /// <summary>
        /// Ловушка снова готова (true) или ушла на кулдаун (false).
        /// Кнопке это нужно, чтобы показывать игроку готовность: без индикации
        /// нажатие превращается в лотерею — жмущий не знает, сработает ли.
        /// </summary>
        public event Action<bool> ReadyChanged;

        private float cooldownTimer;

        public bool IsReady => cooldownTimer <= 0f;

        /// <summary>Сколько осталось до готовности, с. Ноль — готова.</summary>
        public float CooldownRemaining => cooldownTimer;

        protected virtual void Update()
        {
            if (cooldownTimer <= 0f)
            {
                return;
            }

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
            if (cooldownTimer <= 0f)
            {
                ReadyChanged?.Invoke(true);
            }
        }

        public void Activate()
        {
            if (!IsReady)
            {
                return;
            }

            cooldownTimer = cooldown;
            ReadyChanged?.Invoke(false);
            OnActivated();
        }

        /// <summary>Снять кулдаун и вернуть ловушку в исходное состояние. Для старта раунда.</summary>
        public virtual void ResetTrap()
        {
            bool wasBusy = cooldownTimer > 0f;
            cooldownTimer = 0f;

            if (wasBusy)
            {
                ReadyChanged?.Invoke(true);
            }
        }

        protected abstract void OnActivated();
    }
}
