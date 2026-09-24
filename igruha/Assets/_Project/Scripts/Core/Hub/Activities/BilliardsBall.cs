using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Шар бильярда в хабе. Объект сцены с физикой на сервере и репликой
    /// у остальных — тот же приём, что у шара боулинга: без сетевого префаба,
    /// чтобы не разъезжался список в NetworkConfig.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BilliardsBall : NetworkBehaviour
    {
        [SerializeField] private bool isCueBall;

        private Rigidbody body;
        private Renderer[] views;
        private Collider shape;
        private Vector3 homePosition;
        private bool pocketed;

        public bool IsCueBall => isCueBall;
        public bool IsPocketed => pocketed;

        /// <summary>Шар докатился и стоит, либо уже в лузе.</summary>
        public bool IsResting
        {
            get
            {
                if (pocketed || body == null)
                {
                    return true;
                }

                return body.IsSleeping() || body.linearVelocity.sqrMagnitude < 0.01f;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            views = GetComponentsInChildren<Renderer>(true);
            shape = GetComponent<Collider>();
            homePosition = transform.position;
            ApplyOwnership();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyOwnership();
        }

        /// <summary>Запомнить дом после расстановки билдером.</summary>
        public void CaptureHome()
        {
            homePosition = transform.position;
        }

        private void ApplyOwnership()
        {
            if (body != null)
            {
                body.isKinematic = !WorldAuthority.HasAuthority;
            }
        }

        /// <summary>
        /// Удар кием: скорость задаётся прямо, как у шара боулинга — сила
        /// читается в м/с и не зависит от массы, которую подберут потом.
        /// </summary>
        public void Strike(Vector3 direction, float speed)
        {
            if (body == null || pocketed)
            {
                return;
            }

            body.linearVelocity = direction.normalized * speed;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        /// <summary>Убрать в лузу: гасим вид и тело, пока станция не вернёт.</summary>
        public void Pocket()
        {
            if (pocketed)
            {
                return;
            }

            pocketed = true;
            SetVisible(false);

            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            if (shape != null)
            {
                shape.enabled = false;
            }

            // Под стол: иначе NetworkTransform ещё кадр покажет шар в лузе.
            transform.position = homePosition + Vector3.down * 2f;
        }

        /// <summary>Вернуть на дом и успокоить. Только на авторитете.</summary>
        public void ResetBall()
        {
            pocketed = false;
            SetVisible(true);

            if (shape != null)
            {
                shape.enabled = true;
            }

            transform.SetPositionAndRotation(homePosition, Quaternion.identity);

            if (body == null)
            {
                return;
            }

            body.isKinematic = !WorldAuthority.HasAuthority;

            if (body.isKinematic)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }

        private void SetVisible(bool visible)
        {
            if (views == null)
            {
                return;
            }

            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null)
                {
                    views[i].enabled = visible;
                }
            }
        }
    }
}
