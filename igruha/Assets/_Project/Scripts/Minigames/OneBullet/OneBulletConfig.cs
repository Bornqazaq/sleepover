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
