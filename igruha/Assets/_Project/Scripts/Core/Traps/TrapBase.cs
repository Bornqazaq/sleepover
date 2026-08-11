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

        private float cooldownTimer;

        public bool IsReady => cooldownTimer <= 0f;

        protected virtual void Update()
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
        }

        public void Activate()
        {
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
