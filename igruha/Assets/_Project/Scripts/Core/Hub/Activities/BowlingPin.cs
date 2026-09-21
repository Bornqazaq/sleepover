using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Кегля на дорожке боулинга в хабе. Объект сцены, а не префаб: сетевые
    /// объекты сцены не требуют места в списке сетевых префабов, а именно
    /// расхождение того списка уже стоило проекту часов на <c>NetworkConfig
    /// mismatch</c>.
    ///
    /// Физику считает только сервер, остальные видят её через
    /// <c>NetworkTransform</c>. У клиента тело кинематическое: своя физика
    /// поверх реплики даёт дрожь и расхождение — тот же приём, что у
    /// переносимого предмета.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BowlingPin : NetworkBehaviour
    {
        [Tooltip("Наклон, с которого кегля считается сбитой, градусы")]
        [SerializeField] private float knockedAngle = 30f;

        [Tooltip("Смещение, с которого кегля считается сбитой, м")]
        [SerializeField] private float knockedOffset = 0.15f;

        private Rigidbody body;
        private Vector3 homePosition;
        private Quaternion homeRotation;

        /// <summary>Сбита ли: наклонилась или уехала с места.</summary>
        public bool IsKnocked
        {
            get
            {
                float tilt = Vector3.Angle(transform.up, Vector3.up);
                if (tilt > knockedAngle)
                {
                    return true;
                }

                Vector3 drift = transform.position - homePosition;
                drift.y = 0f;

                return drift.magnitude > knockedOffset;
            }
        }

        /// <summary>Тело стоит и больше не поедет само.</summary>
        public bool IsResting => body == null || body.IsSleeping() || body.linearVelocity.sqrMagnitude < 0.01f;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();

            homePosition = transform.position;
            homeRotation = transform.rotation;

            ApplyOwnership();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnership();
        }

        /// <summary>Физику ведёт только авторитет, остальные — зрители.</summary>
        private void ApplyOwnership()
        {
            if (body == null)
            {
                return;
            }

            body.isKinematic = !WorldAuthority.HasAuthority;
        }

        /// <summary>Вернуть на место и поставить ровно. Только на авторитете.</summary>
        public void ResetPin()
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);

            if (body == null || body.isKinematic)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }

    }
}
