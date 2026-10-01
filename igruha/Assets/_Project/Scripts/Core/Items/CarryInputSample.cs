using System;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Items
{
    /// <summary>Four bytes: movement in the world and on the owner's controls. Presentation only.</summary>
    public struct CarryInputSample : INetworkSerializable, IEquatable<CarryInputSample>
    {
        private const float Precision = 127f;
        private sbyte worldX, worldZ, moveX, moveY;

        public Vector3 World => new Vector3(worldX / Precision, 0, worldZ / Precision);
        public Vector2 Move => new Vector2(moveX / Precision, moveY / Precision);

        public CarryInputSample(Vector2 world, Vector2 move)
        {
            world = Sanitize(world); move = Sanitize(move);
            worldX = (sbyte)Mathf.RoundToInt(world.x * Precision);
            worldZ = (sbyte)Mathf.RoundToInt(world.y * Precision);
            moveX = (sbyte)Mathf.RoundToInt(move.x * Precision);
            moveY = (sbyte)Mathf.RoundToInt(move.y * Precision);
        }

        public static Vector2 Sanitize(Vector2 value) =>
            float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y)
                ? Vector2.zero : Vector2.ClampMagnitude(value, 1f);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref worldX); serializer.SerializeValue(ref worldZ);
            serializer.SerializeValue(ref moveX); serializer.SerializeValue(ref moveY);
        }

        public bool Equals(CarryInputSample other) => worldX == other.worldX && worldZ == other.worldZ &&
            moveX == other.moveX && moveY == other.moveY;
    }
}
