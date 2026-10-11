using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class OneBulletRouteTests
    {
        private const string Folder = "Assets/_Project/Scripts/Minigames/OneBullet/";
        [Serializable] private class Edge { public int a, b; }
        [Serializable] private class Source { public Edge[] edges; public int[] guns; }
        private static OneBulletStormLayout Layout => AssetDatabase.LoadAssetAtPath<OneBulletStormLayout>(
            "Assets/_Project/Settings/Gameplay/Minigames/OneBulletStorm.asset");

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void EveryStormStageKeepsLoopsAndShortDeadEnds(int stage)
        {
            var layout = Layout;
            var graph = Enumerable.Range(0, layout.NodeCount).Where(n => layout.Safe(n, stage))
                .ToDictionary(n => n, n => new List<int>());
            foreach (var e in layout.Edges)
                if (graph.ContainsKey(e.A) && graph.ContainsKey(e.B))
                { graph[e.A].Add(e.B); graph[e.B].Add(e.A); }
            var seen = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(graph.Keys.First());
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(); if (!seen.Add(n)) continue;
                foreach (int next in graph[n]) queue.Enqueue(next);
            }
            Assert.AreEqual(graph.Count, seen.Count, "All safe routes must connect.");
            Assert.GreaterOrEqual(graph.Values.Sum(v => v.Count) / 2 - graph.Count + 1, 3,
                "At least three loops must remain, including the finale.");
            foreach (var entry in graph.Where(p => p.Value.Count == 1))
            {
                int previous = entry.Key, current = entry.Value[0], steps = 1;
                while (graph[current].Count == 2)
                {
                    int next = graph[current].First(n => n != previous);
                    previous = current; current = next; steps++;
                }
                Assert.LessOrEqual(steps, stage == 0 ? 1 : 3, "Long blind alley at node " + entry.Key);
            }
            var source = JsonUtility.FromJson<Source>(File.ReadAllText(Folder + "OneBulletLayout.json"));
            var safeGuns = source.guns.Where(n => graph.ContainsKey(n)).ToArray();
            Assert.GreaterOrEqual(safeGuns.Length, 4, "Weapon choices must survive every shrink.");
            if (stage == layout.FinalStage)
                foreach (int gun in safeGuns) Assert.GreaterOrEqual(graph[gun].Count, 2, "No final weapon in a dead end.");
        }

        [Test]
        public void AuthoredPassagesMatchBakedStormGraph()
        {
            var source = JsonUtility.FromJson<Source>(File.ReadAllText(Folder + "OneBulletLayout.json"));
            var authored = source.edges.Select(e => Math.Min(e.a, e.b) * 100 + Math.Max(e.a, e.b)).ToArray();
            var baked = Layout.Edges.Select(e => Math.Min(e.A, e.B) * 100 + Math.Max(e.A, e.B)).ToArray();
            CollectionAssert.AllItemsAreUnique(authored);
            CollectionAssert.AreEquivalent(authored, baked);
        }
    }
}
