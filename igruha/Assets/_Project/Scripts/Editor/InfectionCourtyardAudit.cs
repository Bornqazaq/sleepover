using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.EditorTools
{
    // Conservative standing capsule with the game's 0.42 m automatic step clearance.
    // Connectivity is sampled on the actual rebuilt colliders, not inferred from model positions.
    internal static class InfectionCourtyardAudit
    {
        private static int mask;
        private const float Radius = .36f;
        private static bool Clear(Vector3 p) => !Physics.CheckCapsule(
            p + Vector3.up * (.43f + Radius), p + Vector3.up * (.43f + 1.65f - Radius),
            Radius, mask, QueryTriggerInteraction.Ignore);

        internal static void Check()
        {
            Physics.SyncTransforms();
            mask = LayerMask.GetMask("Ground", "Cover", "PlayerBarrier");
            var plan = InfectionCourtyardLayout.Load();
            var cells = new HashSet<Vector2Int>();
            for (int x = -42; x <= 40; x++) for (int z = -36; z <= 36; z++)
            {
                var p = new Vector3(x * .5f, 0, z * .5f);
                bool inside = true;
                for (int i = 0; i < plan.boundary.Length; i++)
                {
                    var a = plan.boundary[i].Position;
                    var b = plan.boundary[(i + 1) % plan.boundary.Length].Position;
                    var d = b - a;
                    if (d.x * (p.z - a.z) - d.z * (p.x - a.x) < .45f * d.magnitude) inside = false;
                }
                if (inside && Clear(p)) cells.Add(new Vector2Int(x, z));
            }
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            var first = new Vector2Int(Mathf.RoundToInt(plan.spawns[0].x * 2), Mathf.RoundToInt(plan.spawns[0].z * 2));
            if (!cells.Contains(first)) throw new InvalidOperationException("First spawn blocked");
            queue.Enqueue(first); visited.Add(first);
            var directions = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in directions)
                    if (cells.Contains(c + d) && visited.Add(c + d)) queue.Enqueue(c + d);
            }
            foreach (var spawn in plan.spawns)
            {
                var cell = new Vector2Int(Mathf.RoundToInt(spawn.x * 2), Mathf.RoundToInt(spawn.z * 2));
                if (!Clear(spawn.Position) || !visited.Contains(cell))
                    throw new InvalidOperationException("Spawn disconnected/blocked: " + spawn.Position);
            }
            var arena = GameObject.Find("_Arena").transform;
            foreach (var name in new[] { "Tubes/Tube_West", "Tubes/Tube_East", "Climber" })
            {
                var prop = arena.Find(name);
                for (int i = -16; i <= 16; i++)
                {
                    if (!Clear(prop.TransformPoint(new Vector3(0, 0, i * .25f))))
                        throw new InvalidOperationException("Blocked north/south passage: " + name);
                    if (name == "Climber" && !Clear(prop.TransformPoint(new Vector3(i * .25f, 0, 0))))
                        throw new InvalidOperationException("Blocked east/west climber passage");
                }
            }
            Debug.Log("Courtyard functional audit: 8 connected spawns; both tube passages and both climber axes clear; reachable ground cells=" + visited.Count + "/" + cells.Count);
        }
    }
}
