using System;
using UnityEngine;
using Igruha.Core.Combat;
using Igruha.Core.Player;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Боевая часть охотника: стрельба по открытой грани башни.
    /// Движение — обычный PlayerController по внешней лестнице, отдельного
    /// кода перемещения не требуется. Прицел «плавает», отдача разворачивает
    /// самого охотника (обе механики — из ProjectileShooter в Core).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class HunterCombat : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private ProjectileShooter shooter;

        /// <summary>Сколько уток охотник успел сбить — идёт в расчёт его места.</summary>
        public int Hits { get; private set; }

        public event Action HitRegistered;

        private PlayerController motor;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        public void Configure(PlayerInputReader reader, ProjectileShooter projectileShooter)
        {
            if (shooter != null)
            {
                shooter.HitPlayer -= OnShotHitPlayer;
            }

            inputReader = reader;
            shooter = projectileShooter;

            if (shooter != null)
            {
                shooter.HitPlayer += OnShotHitPlayer;
            }
        }

        private void OnDestroy()
        {
            if (shooter != null)
            {
                shooter.HitPlayer -= OnShotHitPlayer;
            }
        }

        private void OnShotHitPlayer(PlayerController victim)
        {
            if (victim == null || victim == motor)
            {
                return;
            }

            RegisterHit();

            DuckProgress duck = victim.GetComponent<DuckProgress>();
            if (duck != null)
            {
                duck.RegisterHit();
            }
        }

        public void RegisterHit()
        {
            Hits++;
            HitRegistered?.Invoke();
        }

        public void ResetHits() => Hits = 0;

        private void Update()
        {
            if (inputReader == null || shooter == null || !inputReader.PushPressed)
            {
                return;
            }

            inputReader.ConsumePush();

            if (motor.IsKnockedDown)
            {
                return;
            }

            shooter.Fire(shooter.GetWobbledDirection(transform.forward));
        }
    }
}
