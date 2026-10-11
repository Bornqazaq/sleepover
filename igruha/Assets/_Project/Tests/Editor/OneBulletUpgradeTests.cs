using System.Collections.Generic;
using System.Linq;
using Igruha.Core.Items;
using Igruha.Minigames.OneBullet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class OneBulletUpgradeTests
    {
        [Test] public void CansHaveQuotaCooldownAndRoundLifetime()
        {
            var r = new OneBulletRound(); r.Reset(new[] { 0, 1 }, 3, 300, 10);
            Assert.False(r.ThrowCan(0, 2, .6)); Assert.False(r.ThrowCan(9, 4, .6));
            Assert.True(r.ThrowCan(0, 4, .6)); Assert.False(r.ThrowCan(0, 4.5, .6));
            Assert.True(r.ThrowCan(0, 4.61, .6)); Assert.False(r.ThrowCan(0, 20, .6));
            Assert.AreEqual(2, r.Find(1).Cans);
            r.Leave(1, 21, 5); Assert.False(r.ThrowCan(1, 22, .6));
            r.Reset(new[] { 0, 1 }, 30, 300, 10); Assert.AreEqual(2, r.Find(0).Cans);
            r.Finish(32); Assert.False(r.ThrowCan(0, 33, .6));
        }
        [Test] public void DangerResetsOnEscapeAndDeathDoesNotAwardKills()
        {
            var r = new OneBulletRound(); r.Reset(new[] { 0, 1 }, 3, 300, 10);
            Assert.True(r.SetDanger(0, true, 5)); Assert.False(r.SetDanger(0, true, 6)); Assert.AreEqual(5, r.Find(0).DangerSince);
            Assert.True(r.SetDanger(0, false, 12)); Assert.AreEqual(-1, r.Find(0).DangerSince);
            r.SetDanger(0, true, 20); Assert.AreEqual(20, r.Find(0).DangerSince);
            r.Spawn(1, 20); r.Take(0, 20); r.Leave(0, 28, 5);
            Assert.AreEqual(-1, r.Holder); Assert.AreEqual(33, r.SpawnAt); Assert.AreEqual(0, r.Find(1).Kills);
        }
        [Test] public void RelocationPreservesExactlyOneWeaponAndDoesNotStealHeldGun()
        {
            var r = new OneBulletRound(); r.Reset(new[] { 0, 1 }, 0, 300, 0);
            Assert.False(r.RelocatePickup(2)); r.Spawn(0, 1);
            Assert.True(r.RelocatePickup(2)); Assert.AreEqual(2, r.Pickup); Assert.AreEqual(2, r.PreviousPickup);
            Assert.False(r.Spawn(3, 2)); r.Take(0, 3);
            Assert.False(r.RelocatePickup(4)); Assert.AreEqual(0, r.Holder);
        }
        [TestCase(2)] [TestCase(4)] [TestCase(8)]
        public void StartsWithoutStormForEveryRoster(int players)
        {
            var s = new OneBulletStormState(); s.Reset(players, 6, 3, 60);
            Assert.AreEqual(0, s.Stage); Assert.AreEqual(0, s.SafeStage);
            Assert.False(s.Warning); Assert.AreEqual(63, s.IdleAt);
            Assert.False(s.Tick(62.99, 6, 12, 60));
        }
        [TestCase(3)] [TestCase(4)] [TestCase(6)] [TestCase(8)]
        public void EachEliminationQueuesOnlyOneClosureRegardlessOfRoster(int players)
        {
            var s = new OneBulletStormState(); s.Reset(players, 6, 3, 60);
            for (int i = 0; i < players - 2; i++) s.Elimination(10, 6, 12, 60);
            Assert.AreEqual(players-2, s.Target); Assert.AreEqual(0, s.Stage);
            for (int stage = 1; stage <= players-2; stage++)
            { s.Tick(10 + stage * 12, 6, 12, 60); Assert.AreEqual(stage, s.Stage); }
            Assert.False(s.Warning);
        }
        [Test] public void SimultaneousEliminationsQueueFullWarnings()
        {
            var s = new OneBulletStormState(); s.Reset(8, 6, 3, 60);
            s.Elimination(10, 6, 12, 60); s.Elimination(10, 6, 12, 60); s.Elimination(11, 6, 12, 60);
            Assert.AreEqual(3, s.Target); Assert.AreEqual(22, s.ClosesAt);
            Assert.False(s.Tick(21.99, 6, 12, 60)); Assert.True(s.Tick(22, 6, 12, 60));
            Assert.AreEqual(1, s.Stage); Assert.AreEqual(34, s.ClosesAt);
            s.Tick(34, 6, 12, 60); s.Tick(46, 6, 12, 60);
            Assert.AreEqual(3, s.Stage); Assert.False(s.Warning);
        }
        [Test] public void IdleAdvancesButNeverShrinksPastFinale()
        {
            var s = new OneBulletStormState(); s.Reset(4, 6, 3, 60);
            Assert.False(s.Tick(62.99, 6, 12, 60)); Assert.True(s.Tick(63, 6, 12, 60));
            Assert.AreEqual(75, s.ClosesAt); s.Tick(75, 6, 12, 60);
            Assert.AreEqual(1, s.Stage); Assert.AreEqual(135, s.IdleAt);
            for (int stage = 2; stage <= 6; stage++)
            { double at = s.IdleAt; s.Tick(at, 6, 12, 60); s.Tick(at + 12, 6, 12, 60); }
            Assert.AreEqual(6, s.Stage); Assert.False(s.Tick(1000, 6, 12, 60));
            s.Reset(8, 6, 1000, 60); Assert.AreEqual(0, s.Stage); Assert.AreEqual(0, s.ClosesAt);
        }
        [Test] public void CanPrefabKeepsFbxUnitsAndReadablePhysicalSize()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Minigames/OneBullet/DecoyCan.prefab");
            var can = Object.Instantiate(asset);
            try
            {
                var renderers = can.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.EqualTo(.18f).Within(.002f));
                Assert.That(bounds.center.magnitude, Is.LessThan(.002f));
                Assert.IsEmpty(can.GetComponentsInChildren<Collider>());
            }
            finally { Object.DestroyImmediate(can); }
        }
        [Test] public void EveryTerritoryIsConnectedNestedAndHasTwoWeaponPoints()
        {
            var l = AssetDatabase.LoadAssetAtPath<OneBulletStormLayout>("Assets/_Project/Settings/Gameplay/Minigames/OneBulletStorm.asset");
            Assert.NotNull(l);
            int[] sizes = { 81, 69, 58, 47, 36, 25, 13 }, guns = { 8,13,26,41,38,52,67,72,75,61,0,48 };
            for (int stage = 0; stage <= l.FinalStage; stage++)
            {
                var safe = Enumerable.Range(0, l.NodeCount).Where(n => l.Safe(n, stage)).ToHashSet();
                Assert.AreEqual(sizes[stage], safe.Count); Assert.GreaterOrEqual(guns.Count(n => safe.Contains(n)), 2);
                var seen = new HashSet<int> { safe.First() }; var q = new Queue<int>(seen);
                while (q.Count > 0)
                {
                    int n = q.Dequeue(); foreach (var e in l.Edges)
                    {
                        int o = e.A == n ? e.B : e.B == n ? e.A : -1;
                        if (safe.Contains(o) && seen.Add(o)) q.Enqueue(o);
                    }
                }
                Assert.AreEqual(safe.Count, seen.Count, "Disconnected stage " + stage);
                for (int n = 0; n < l.NodeCount; n++)
                {
                    if (stage > 0 && l.Safe(n, stage)) Assert.True(l.Safe(n, stage - 1));
                    int current = n, steps = 0;
                    while (!l.Safe(current, stage) && steps++ < l.NodeCount)
                    {
                        int next = l.NextEscapeNode(current, stage);
                        Assert.True(l.Edges.Any(e => (e.A == current && e.B == next) || (e.B == current && e.A == next)));
                        current = next;
                    }
                    Assert.Less(steps, l.NodeCount, "No escape path");
                    if (stage > 0 && l.Safe(n, stage - 1)) Assert.LessOrEqual(steps * l.Pitch, 30f, "Warning escape is too long");
                }
                var starts = new HashSet<int>();
                for (int i = 0; i < 8 - stage; i++) { int n = l.StartNode(stage, i); Assert.True(l.Safe(n, stage)); Assert.True(starts.Add(n)); }
            }
        }
        [Test] public void FastPropBouncesOffThinWallInsteadOfTunnelling()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = 6;
            try
            {
                wall.transform.position = new Vector3(100, 2, 103); wall.transform.localScale = new Vector3(5, 5, .1f);
                Physics.SyncTransforms();
                var p = new Vector3(100, 2, 100); var v = Vector3.forward * 100;
                Assert.True(BouncingBallistics.Step(ref p, ref v, .05f, .09f, .45f, .24f, 1 << 6, out _, out float speed));
                Assert.Less(p.z, 103); Assert.Less(v.z, 0); Assert.Greater(speed, 90);
            }
            finally { Object.DestroyImmediate(wall); }
        }
    }
}
