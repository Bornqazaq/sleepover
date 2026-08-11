using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Падающий предмет: при активации тяжёлый Rigidbody-ящик срывается с якоря.
    /// Сбивание с ног делает обычная физика столкновения (порог нокдауна в моторе).
    /// Возвращается на место автоматически к моменту готовности кулдауна.
    /// </summary>
    public sealed class FallingCrateTrap : TrapBase
    {
        [Tooltip("Rigidbody падающего предмета (ящик, ведро, наковальня)")]
        [SerializeField] private Rigidbody crate;
        [Tooltip("Через сколько секунд после активации предмет вернётся на якорь")]
        [SerializeField] private float resetDelay = 2.5f;

        private Vector3 initialLocalPosition;
        private Quaternion initialLocalRotation;
        private float resetTimer;
        private bool dropped;

        private void Awake()
        {
            if (crate != null)
            {
                initialLocalPosition = crate.transform.localPosition;
                initialLocalRotation = crate.transform.localRotation;
                crate.isKinematic = true;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!dropped)
            {
                return;
            }

            resetTimer -= Time.deltaTime;
            if (resetTimer <= 0f)
            {
                ResetCrate();
            }
        }

        protected override void OnActivated()
        {
            if (crate == null)
            {
                return;
            }

            crate.isKinematic = false;
            crate.WakeUp();
            dropped = true;
            resetTimer = resetDelay;
        }

        private void ResetCrate()
        {
            dropped = false;
            crate.isKinematic = true;
            crate.linearVelocity = Vector3.zero;
            crate.angularVelocity = Vector3.zero;
            crate.transform.localPosition = initialLocalPosition;
            crate.transform.localRotation = initialLocalRotation;
        }
    }
}
