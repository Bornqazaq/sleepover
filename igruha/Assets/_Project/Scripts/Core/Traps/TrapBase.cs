using System;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// База ловушки: Activate — публичная точка входа, кулдаун общий.
    /// Конкретные ловушки реализуют OnActivated.
    ///
    /// Срабатывание — исход раунда, поэтому решает его только сервер.
    /// Намерение игрока доезжает сюда через серверное взаимодействие
    /// (PlayerInteractor → сервер → TrapActivationButton.Interact).
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

        /// <summary>
        /// На клиенте всегда true: кулдаун тикает только у авторитета, и клиент
        /// про него не знает. Это подсказка для UI, а не решение — отказ по
        /// кулдауну выносит сервер в <see cref="Activate"/>.
        /// </summary>
        public bool IsReady => cooldownTimer <= 0f;

        /// <summary>Сколько осталось до готовности, с. Ноль — готова.</summary>
        public float CooldownRemaining => cooldownTimer;

        protected virtual void Update()
        {
            // Обе проверки обязательны. Авторитет — потому что кулдаун тикает
            // только у сервера. Нулевой таймер — потому что иначе строка ниже
            // каждый кадр объявляет ловушку снова готовой, и кнопка мигает
            // событием ReadyChanged бесконечно.
            if (!WorldAuthority.HasAuthority || cooldownTimer <= 0f)
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
            // Клиент сюда попасть может — например, своим локальным нажатием
            // до того, как намерение уйдёт на сервер. Молча выходим: сработает
            // серверная копия, и её результат приедет всем.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

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
