using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Шар боулинга в хабе. Как и кегли — объект сцены с физикой на сервере
    /// и репликой у остальных.
    ///
    /// Шар намеренно остаётся обычным телом, а не триггером: сбить его чужим
    /// боком или поймать телом можно, и запрещать это не надо — из этого и
    /// получается смешное.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BowlingBall : NetworkBehaviour
    {
        private Rigidbody body;

        /// <summary>Шар докатился и стоит.</summary>
        public bool IsResting => body == null || body.IsSleeping() || body.linearVelocity.sqrMagnitude < 0.04f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ApplyOwnership();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            if (body != null)
            {
                body.isKinematic = !WorldAuthority.HasAuthority;
            }
        }

        /// <summary>
        /// Поставить шар на метку и пустить. Скорость задаётся прямо, а не
        /// импульсом: так сила броска читается в метрах в секунду и не зависит
        /// от массы, которую подберут потом.
        /// </summary>
        public void Roll(Vector3 from, Vector3 direction, float speed)
        {
            if (body == null)
            {
                return;
            }

            transform.SetPositionAndRotation(from, Quaternion.identity);

            body.linearVelocity = direction.normalized * speed;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        /// <summary>Вернуть на подставку и успокоить.</summary>
        public void ResetTo(Vector3 home)
        {
            if (body == null)
            {
                transform.position = home;
                return;
            }

            transform.SetPositionAndRotation(home, Quaternion.identity);

            if (body.isKinematic)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }
    }
}
