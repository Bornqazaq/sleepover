using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    // The room floor is floor 10, at world Y=0; the courtyard is 27 metres below.
    public static class MosquitoWindowEntry
    {
        public const float Deadline = 10f;
        public const float Plane = 3.35f;
        public static readonly Vector3 Approach = new Vector3(3.05f, 1.85f, .94f);
        public static readonly Vector3 Inside = new Vector3(2.85f, 1.85f, .94f);
        // Spread players through the open air near the facade. Every start is
        // reachable in the entry time, but flight is not confined to this area.
        public static Vector3 Spawn(int slot) => new Vector3(8f + slot % 3 * 2.2f,
            1.85f + Mathf.Sin(slot * 2.4f) * 3.5f, .94f + Mathf.Cos(slot * 1.75f) * 5.5f);
        public static Vector3 Recovery(int slot) => new Vector3(-3.15f, 1.85f + slot / 4 * .25f, -2.5f + slot % 4 * 1.5f);
        public static bool Crossed(Vector3 previous, Vector3 current)
        {
            if (previous.x <= Plane || current.x > Plane) return false;
            float t = (previous.x - Plane) / (previous.x - current.x);
            Vector3 crossing = Vector3.Lerp(previous, current, t);
            return crossing.y >= 1.24f && crossing.y <= 2.78f && crossing.z >= .12f && crossing.z <= 1.76f;
        }
        // Classification for spawn diagnostics only; never confines exterior flight.
        public static bool ExteriorBounds(Vector3 p) => p.x >= Plane;
        public static Vector3 ClampInside(Vector3 p) => new Vector3(Mathf.Min(p.x, Plane), p.y, p.z);
    }
}
