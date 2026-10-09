using System;
using Unity.Netcode;

namespace Igruha.Minigames.OneBullet
{
    public struct OneBulletNetCan : INetworkSerializable, IEquatable<OneBulletNetCan>
    {
        public OneBulletCanState Value;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Value.Active); s.SerializeValue(ref Value.Position); s.SerializeValue(ref Value.Velocity);
            s.SerializeValue(ref Value.ImpactPoint); s.SerializeValue(ref Value.LaunchedAt); s.SerializeValue(ref Value.UpdatedAt);
            s.SerializeValue(ref Value.ImpactAt); s.SerializeValue(ref Value.Bounces); s.SerializeValue(ref Value.ImpactSpeed);
        }
        public bool Equals(OneBulletNetCan other)
        {
            var b = other.Value;
            return Value.Active == b.Active && Value.Position == b.Position && Value.Velocity == b.Velocity &&
                Value.LaunchedAt == b.LaunchedAt && Value.UpdatedAt == b.UpdatedAt && Value.Bounces == b.Bounces &&
                Value.ImpactPoint == b.ImpactPoint && Value.ImpactAt == b.ImpactAt && Value.ImpactSpeed == b.ImpactSpeed;
        }
    }
}
