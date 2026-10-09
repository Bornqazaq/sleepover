using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.OneBullet
{
    [CreateAssetMenu(menuName = "Igruha/One Bullet Storm Layout")]
    public sealed class OneBulletStormLayout : ScriptableObject
    {
        [Serializable] public struct Edge { public int A, B; }
        [SerializeField] private Vector3[] positions = Array.Empty<Vector3>();
        [SerializeField] private int[] lastSafeStage = Array.Empty<int>();
        [SerializeField] private Edge[] edges = Array.Empty<Edge>();
        [SerializeField] private int[] rooms = Array.Empty<int>();
        [SerializeField] private int[] starts = Array.Empty<int>();
        [SerializeField] private int stageCount = 7;
        [SerializeField] private float pitch = 4.8f, roomHalfWidth = 2.88f;
        private int[,] nextHop;
        public int NodeCount => positions.Length;
        public int FinalStage => stageCount - 1;
        public float Pitch => pitch;
        public IReadOnlyList<Edge> Edges => edges;
        public Vector3 Position(int node) => positions[node];
        public bool Safe(int node, int stage) => node >= 0 && node < lastSafeStage.Length && lastSafeStage[node] >= stage;
        public bool Safe(Vector3 point, int stage) => Safe(NodeAt(point), stage);
        public Vector3 StandingPoint(int node)
        {
            Vector3 point = positions[node] + Vector3.up * .08f;
            // The column and well occupy their room centres; spawn/recovery never intersects them.
            if (Array.IndexOf(rooms, node) >= 0) point.x += 1.5f;
            return point;
        }
        public int StartNode(int stage, int slot) => starts[Mathf.Clamp(stage, 0, FinalStage) * 8 + slot % 8];
        public int NodeAt(Vector3 point)
        {
            foreach (int room in rooms)
            {
                Vector3 d = point - positions[room];
                if (Mathf.Abs(d.x) < roomHalfWidth && Mathf.Abs(d.z) < roomHalfWidth) return room;
            }
            int closest = -1; float distance = float.PositiveInfinity;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 d = point - positions[i]; float sq = d.x * d.x + d.z * d.z;
                if (sq < distance) { distance = sq; closest = i; }
            }
            return closest;
        }
        public int NearestSafe(Vector3 point, int stage)
        {
            int closest = -1; float distance = float.PositiveInfinity;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!Safe(i, stage)) continue;
                float d = (StandingPoint(i) - point).sqrMagnitude;
                if (d < distance) { closest = i; distance = d; }
            }
            return closest;
        }
        public int NextEscapeNode(int node, int stage)
        {
            if (node < 0 || node >= NodeCount) return -1;
            if (nextHop == null) BuildRoutes();
            return nextHop[Mathf.Clamp(stage, 0, FinalStage), node];
        }
        private void OnEnable() => nextHop = null;
        private void BuildRoutes()
        {
            nextHop = new int[stageCount, NodeCount];
            var queue = new Queue<int>(NodeCount);
            for (int stage = 0; stage < stageCount; stage++)
            {
                for (int i = 0; i < NodeCount; i++)
                {
                    nextHop[stage, i] = Safe(i, stage) ? i : -1;
                    if (Safe(i, stage)) queue.Enqueue(i);
                }
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    foreach (var edge in edges)
                    {
                        int other = edge.A == node ? edge.B : edge.B == node ? edge.A : -1;
                        if (other < 0 || nextHop[stage, other] >= 0) continue;
                        nextHop[stage, other] = node; queue.Enqueue(other);
                    }
                }
            }
        }
        // Editor construction keeps the authored graph/stages reviewable in one asset.
        public void Configure(Vector3[] nodes, int[] stages, Edge[] links, int[] roomNodes, int[] spawnNodes)
        { positions = nodes; lastSafeStage = stages; edges = links; rooms = roomNodes; starts = spawnNodes; nextHop = null; }
    }
}
