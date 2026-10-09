using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    [CreateAssetMenu(menuName = "Igruha/One Bullet Config")]
    public sealed class OneBulletConfig : ScriptableObject
    {
        [SerializeField, Min(1f)] private float duration = 300f;
        [SerializeField, Min(0f)] private float countdown = 3f;
        [SerializeField, Min(0f)] private float firstSpawnDelay = 10f;
        [SerializeField, Min(0.1f)] private float respawnDelay = 5f;
        [SerializeField, Min(0.1f)] private float pickupRadius = 0.65f;
        [SerializeField, Min(0.1f)] private float deathSeconds = 1.5f;
        [SerializeField, Min(1f)] private float shotRange = 65f;
        [SerializeField, Min(0f)] private float deathImpulse = 2f;
        [SerializeField] private LayerMask hitMask = ~0;
        [Header("Decoys")]
        [SerializeField, Range(1, 4)] private int cansPerRound = 2;
        [SerializeField, Min(.1f)] private float canCooldown = .6f;
        [SerializeField, Min(1f)] private float canSpeed = 9f;
        [SerializeField, Min(0f)] private float canLift = 2f;
        [SerializeField, Min(.01f)] private float canRadius = .09f;
        [SerializeField, Min(1f)] private float canLifetime = 8f;
        [SerializeField, Range(0, 1)] private float canBounce = .45f;
        [SerializeField, Range(0, 1)] private float canDrag = .24f;
        [SerializeField, Min(1f)] private float canSoundRange = 12f;
        [SerializeField] private LayerMask canCollisionMask = (1 << 6) | (1 << 8) | (1 << 9);
        [Header("Storm")]
        [SerializeField, Min(1f)] private float stormWarning = 20f;
        [SerializeField, Min(1f)] private float stormExposure = 8f;
        [SerializeField, Min(1f)] private float stormIdle = 60f;
        public int CansPerRound => cansPerRound;
        public float CanCooldown => canCooldown;
        public float CanSpeed => canSpeed;
        public float CanLift => canLift;
        public float CanRadius => canRadius;
        public float CanLifetime => canLifetime;
        public float CanBounce => canBounce;
        public float CanDrag => canDrag;
        public float CanSoundRange => canSoundRange;
        public LayerMask CanCollisionMask => canCollisionMask;
        public float StormWarning => stormWarning;
        public float StormExposure => stormExposure;
        public float StormIdle => stormIdle;
        public float Duration => duration;
        public float Countdown => countdown;
        public float FirstSpawnDelay => firstSpawnDelay;
        public float RespawnDelay => respawnDelay;
        public float PickupRadius => pickupRadius;
        public float DeathSeconds => deathSeconds;
        public float ShotRange => shotRange;
        public float DeathImpulse => deathImpulse;
        public LayerMask HitMask => hitMask;
    }
}
