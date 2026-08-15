using System;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Стрельба с «жидким» прицелом и отдачей, разворачивающей стрелка (GDD 4.2).
    ///
    /// Fire — намерение: по сети оно уходит серверу, тот проверяет кулдаун и
    /// спавнит снаряд. Попадание и толчок от него считает только сервер.
    /// Без сети всё исполняется на месте, как раньше.
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
        private PlayerController ownerController;
        private ICombatRelay relay;
        private float cooldownTimer;
        private float wobbleSeed;

        /// <summary>Кулдаун у авторитета. Отдельный от локального: тот клиент может обнулить у себя.</summary>
        private float serverCooldownTimer;

        public bool CanFire => cooldownTimer <= 0f && projectilePrefab != null && muzzle != null;

        private void Awake()
        {
            ownerBody = GetComponentInParent<Rigidbody>();
            ownerController = GetComponentInParent<PlayerController>();
            relay = GetComponentInParent<ICombatRelay>();
            wobbleSeed = UnityEngine.Random.value * 100f;
        }

        private void Update()
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);

            if (WorldAuthority.HasAuthority)
            {
                serverCooldownTimer = Mathf.Max(0f, serverCooldownTimer - Time.deltaTime);
            }
        }

        /// <summary>Направление с учётом «плавания» прицела.</summary>
        public Vector3 GetWobbledDirection(Vector3 aimDirection)
        {
            float t = Time.time * aimWobbleFrequency + wobbleSeed;
            float yaw = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * aimWobbleAmplitude;
            float pitch = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * aimWobbleAmplitude;
            return Quaternion.Euler(pitch, yaw, 0f) * aimDirection;
        }

        /// <summary>
        /// Намерение выстрелить в направлении (уже с wobble, если нужен).
        /// Возвращает, принято ли намерение: по сети настоящее решение выносит
        /// сервер, поэтому ответ здесь — «отправлено», а не «попал».
        /// </summary>
        public bool Fire(Vector3 direction)
        {
            if (!CanFire || direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            // Локальный кулдаун держит скорострел на самой машине; на сервере
            // он проверяется ещё раз и уже по-настоящему.
            cooldownTimer = fireCooldown;

            if (relay != null && relay.TryRelayFire(direction))
            {
                return true;
            }

            ServerFire(direction);
            return true;
        }

        /// <summary>
        /// Единственная точка исполнения выстрела. По сети её зовёт сервер,
        /// получив намерение владельца, — там же и настоящая проверка кулдауна:
        /// клиент мог прислать запрос сколь угодно часто.
        /// </summary>
        public void ServerFire(Vector3 direction)
        {
            if (projectilePrefab == null || muzzle == null)
            {
                return;
            }

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (WorldAuthority.IsNetworkSession)
            {
                if (serverCooldownTimer > 0f)
                {
                    return;
                }

                serverCooldownTimer = fireCooldown;
            }

            Vector3 aim = direction.normalized;
            Projectile projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(aim));
            projectile.HitPlayer += OnProjectileHit;

            // Снаряд — общий объект: у клиентов он должен появиться от сервера,
            // а не родиться локально, иначе у каждого будет свой полёт.
            var projectileObject = projectile.GetComponent<NetworkObject>();
            if (projectileObject != null && WorldAuthority.IsNetworkSession)
            {
                projectileObject.Spawn();
            }

            projectile.Launch(aim * projectileSpeed);

            ApplyRecoil(aim);
        }

        /// <summary>Отдача — импульс по стрелку, поэтому идёт тем же серверным путём, что и ловушки.</summary>
        private void ApplyRecoil(Vector3 aim)
        {
            if (ownerController != null)
            {
                ownerController.ApplyWorldImpulse(-aim * recoilImpulse);
            }
            else if (ownerBody != null)
            {
                ownerBody.AddForce(-aim * recoilImpulse, ForceMode.Impulse);
            }

            if (ownerBody != null)
            {
                float kick = UnityEngine.Random.Range(-recoilYawKick, recoilYawKick);
                ownerBody.MoveRotation(ownerBody.rotation * Quaternion.Euler(0f, kick, 0f));
            }
        }

        private void OnProjectileHit(PlayerController victim) => HitPlayer?.Invoke(victim);
    }
}
