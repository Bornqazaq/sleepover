using System;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Снаряд: летит по физике, попал в персонажа → импульс (кувырок делает
    /// порог нокдауна в PlayerController). Уничтожается по таймеру или при ударе.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Projectile : MonoBehaviour
    {
        [Tooltip("Импульс, передаваемый персонажу при попадании")]
        [SerializeField] private float hitForce = 12f;
        [Tooltip("Время жизни, с")]
        [SerializeField] private float lifeTime = 4f;

        /// <summary>Попадание по персонажу — на это подписываются правила мини-игры.</summary>
        public event Action<PlayerController> HitPlayer;

        private Rigidbody rb;
        private float lifeTimer;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public void Launch(Vector3 velocity)
        {
            lifeTimer = lifeTime;
            rb.linearVelocity = velocity;
        }

        private void Update()
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            PlayerController player = collision.collider.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                Vector3 direction = rb.linearVelocity.sqrMagnitude > 0.01f
                    ? rb.linearVelocity.normalized
                    : transform.forward;
                player.ApplyPush(direction, hitForce);
                HitPlayer?.Invoke(player);
            }

            Destroy(gameObject);
        }
    }
}
