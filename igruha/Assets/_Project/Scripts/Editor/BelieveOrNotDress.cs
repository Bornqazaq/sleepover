using System.Collections.Generic;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender art over the phase 2–3 blockout. No store models or random variants.</summary>
    internal static class BelieveOrNotDress
    {
        private static readonly string[] NoMissingAssets = new string[0];
        internal static IReadOnlyList<string> Missing => NoMissingAssets;

        internal static void Begin()
        {
            BelieveOrNotPaletteAssets.Measure();
            BelieveTablePropsAssets.Prepare();
        }

        internal static void DressTable(GameObject tableTop, BelieveOrNotConfig config)
        {
            BelieveTablePropsBuilder.DressTable(tableTop, config);
        }

        internal static void DressChair(GameObject chair, Vector3 direction, float distance)
        {
            BelieveTablePropsBuilder.DressChair(chair, direction, distance);
        }

        internal static void DressBox(BelieveBox box, GameObject body, Transform hinge, GameObject lid, float size)
        {
            BelieveTablePropsBuilder.DressBox(box, body, hinge, lid, size);
        }

        internal static string Report(GameObject arena)
        {
            int colliders = 0;
            int triangles = 0;
            foreach (var t in arena.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Dress") continue;
                colliders += t.GetComponentsInChildren<Collider>(true).Length;
                foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) triangles += filter.sharedMesh.triangles.Length / 3;
            }
            return "BelieveOrNot original Blender props: " + triangles + " triangles, " + colliders + " decorative colliders (expected 0).";
        }
    }
}
