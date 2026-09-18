using System;
using System.IO;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Shared metre-space plan used by the collision builder and Blender surface generator.</summary>
    internal static class InfectionCourtyardLayout
    {
        internal const string Path = InfectionQuarantineAssets.Art + "/layout.json";

        [Serializable] internal sealed class Point
        {
            public float x, z;
            public Vector3 Position => new Vector3(x, 0, z);
        }

        [Serializable] internal sealed class Prop
        {
            public string name;
            public float x, z, yaw, scale = 1;
        }

        [Serializable] internal sealed class Plan
        {
            public Point[] boundary;
            public Point[] spawns;
            public Prop[] props;
        }

        internal static Plan Load() => JsonUtility.FromJson<Plan>(File.ReadAllText(Path));

        internal static void ApplyProps(Transform arena)
        {
            foreach (var placement in Load().props)
            {
                var prop = arena.Find(placement.name);
                if (prop == null) throw new InvalidOperationException("Missing gameplay prop: " + placement.name);
                prop.localPosition = new Vector3(placement.x, 0, placement.z);
                prop.localRotation = Quaternion.Euler(0, placement.yaw, 0);
                prop.localScale = new Vector3(placement.scale, 1, placement.scale);
            }
        }
    }
}
