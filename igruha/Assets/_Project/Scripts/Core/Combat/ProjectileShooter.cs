using System;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Стрельба с «жидким» прицелом и отдачей, разворачивающей стрелка (GDD 4.2).
    /// Fire — публичная точка входа: позже клиент будет просить выстрел ServerRpc,
    /// сервер — валидировать и применять.
    /// </summary>
    public sealed class ProjectileShooter : MonoBehaviour
    {
        [SerializeField] private Projectile projectilePrefab;
        [Tooltip("Точка вылета снаряда")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private float projectileSpeed = 24f;
        [SerializeField] private float fireCooldown = 0.7f;
        [Header("Отдача")]
        [Tooltip("Импульс отдачи, толкающий стрелка назад")]
        [SerializeField] private float recoilImpulse = 6f;
        [Tooltip("Случайный разворот стрелка от отдачи, ° (комичная «жидкая» стрельба)")]
        [SerializeField] private float recoilYawKick = 25f;
        [Header("Жидкий прицел")]
        [Tooltip("Амплитуда плавания прицела, °")]
        [SerializeField] private float aimWobbleAmplitude = 4f;
        [SerializeField] private float aimWobbleFrequency = 1.7f;

        /// <summary>Снаряд этого стрелка попал по персонажу (для подсчёта попаданий в правилах).</summary>
        public event Action<PlayerController> HitPlayer;

        private Rigidbody ownerBody;
        private float cooldownTimer;
        private float wobbleSeed;

        public bool CanFire => cooldownTimer <= 0f && projectilePrefab != null && muzzle != null;

        private void Awake()
        {
            ownerBody = GetComponentInParent<Rigidbody>();
            wobbleSeed = UnityEngine.Random.value * 100f;
        }

        private void Update()
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
        }

        /// <summary>Направление с учётом «плавания» прицела.</summary>
        public Vector3 GetWobbledDirection(Vector3 aimDirection)
        {
            float t = Time.time * aimWobbleFrequency + wobbleSeed;
            float yaw = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * aimWobbleAmplitude;
            float pitch = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * aimWobbleAmplitude;
            return Quaternion.Euler(pitch, yaw, 0f) * aimDirection;
        }

        /// <summary>Выстрел в направлении (уже с wobble, если нужен). Возвращает успех.</summary>
        public bool Fire(Vector3 direction)
        {
            if (!CanFire)
            {
                return false;
            }

            cooldownTimer = fireCooldown;

            Projectile projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(direction));
            projectile.HitPlayer += OnProjectileHit;
            projectile.Launch(direction.normalized * projectileSpeed);

            if (ownerBody != null)
            {
                ownerBody.AddForce(-direction.normalized * recoilImpulse, ForceMode.Impulse);
                float kick = UnityEngine.Random.Range(-recoilYawKick, recoilYawKick);
                ownerBody.MoveRotation(ownerBody.rotation * Quaternion.Euler(0f, kick, 0f));
            }

            return true;
        }

        private void OnProjectileHit(PlayerController victim) => HitPlayer?.Invoke(victim);
    }
}
