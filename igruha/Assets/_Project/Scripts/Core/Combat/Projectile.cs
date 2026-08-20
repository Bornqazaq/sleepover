using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Снаряд: летит по физике, попал в персонажа → импульс (кувырок делает
    /// порог нокдауна в PlayerController). Уничтожается по таймеру или при ударе.
    ///
    /// Попадание — исход, поэтому считает его только сервер: у клиентов снаряд
    /// лишь отображается, его ведёт NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Projectile : NetworkBehaviour
    {
        [Tooltip("Импульс, передаваемый персонажу при попадании")]
        [SerializeField] private float hitForce = 12f;
        [Tooltip("Время жизни, с")]
        [SerializeField] private float lifeTime = 4f;

        /// <summary>Попадание по персонажу — на это подписываются правила мини-игры. Только на сервере.</summary>
        public event Action<PlayerController> HitPlayer;

        private Rigidbody body;
        private float lifeTimer;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Полёт считает сервер; у остальных своя физика только испортила бы
            // картинку, споря с приехавшей позицией.
            if (!IsServer)
            {
                body.isKinematic = true;
            }
        }

        public void Launch(Vector3 velocity)
        {
            lifeTimer = lifeTime;

            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            body.linearVelocity = velocity;
        }

        private void Update()
        {
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                DestroySelf();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Засчитать попадание вправе только сервер: иначе каждый клиент
            // объявит своё, и толчок прилетит по разу на участника матча.
            if (!WorldAuthority.HasAuthority)
            {
                return;
            }

            PlayerController player = collision.collider.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                Vector3 direction = body.linearVelocity.sqrMagnitude > 0.01f
                    ? body.linearVelocity.normalized
                    : transform.forward;

                // Тот же серверный путь импульсов, что у ловушек: сервер решает,
                // применяет владелец цели.
                player.ApplyWorldImpulse(direction * hitForce);
                HitPlayer?.Invoke(player);
            }

            DestroySelf();
        }

        /// <summary>Снаряд, заспавненный сервером, снимается тоже сервером — иначе у клиентов он останется висеть.</summary>
        private void DestroySelf()
        {
            if (IsSpawned)
            {
                NetworkObject.Despawn();
                return;
            }

            Destroy(gameObject);
        }
    }
}
