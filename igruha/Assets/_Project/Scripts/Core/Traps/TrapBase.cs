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

        private float cooldownTimer;

        /// <summary>
        /// На клиенте всегда true: кулдаун тикает только у авторитета, и клиент
        /// про него не знает. Это подсказка для UI, а не решение — отказ по
        /// кулдауну выносит сервер в <see cref="Activate"/>.
        /// </summary>
        public bool IsReady => cooldownTimer <= 0f;

        protected virtual void Update()
        {
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
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
            OnActivated();
        }

        protected abstract void OnActivated();
    }
}
