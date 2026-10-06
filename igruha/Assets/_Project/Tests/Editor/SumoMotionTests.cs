using System;
using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoMotionTests
    {
        private const string Path = "Assets/_Project/Art/Animations/Sumo/SumoMotions.asset";
        [Test]
        public void ReleasingHeavyChargeKeepsTheSameFullBodyPose()
        {
            var library = AssetDatabase.LoadAssetAtPath<SumoMotionLibrary>(Path);
            var charge = library.Get(SumoMotion.Charge); var heavy = library.Get(SumoMotion.Heavy);
            Assert.That(charge.Tracks.Length, Is.EqualTo(heavy.Tracks.Length));
            for (int i = 0; i < charge.Tracks.Length; i++)
            {
                Assert.That(heavy.Tracks[i].Muscle, Is.EqualTo(charge.Tracks[i].Muscle));
                Assert.That(heavy.Tracks[i].Curve.Evaluate(0), Is.EqualTo(charge.Tracks[i].Curve.Evaluate(1)).Within(.001f), HumanTrait.MuscleName[charge.Tracks[i].Muscle]);
            }
        }
        [Test]
        public void EveryShippedMotionIsValidAndBalanceLoopClosesWithoutASnap()
        {
            var library = AssetDatabase.LoadAssetAtPath<SumoMotionLibrary>(Path);
            foreach (SumoMotion motion in Enum.GetValues(typeof(SumoMotion)))
            {
                var data = library.Get(motion);
                Assert.That(data, Is.Not.Null, motion.ToString());
                Assert.That(data.EditableClip, Is.Not.Null);
                Assert.That(data.Tracks.Length, Is.GreaterThan(30));
                foreach (var track in data.Tracks)
                {
                    Assert.That(track.Muscle, Is.InRange(0, HumanTrait.MuscleCount - 1));
                    for (int i = 0; i <= 60; i++) Assert.That(track.Curve.Evaluate(i / 60f), Is.InRange(-1f, 1f), motion.ToString());
                    if (motion == SumoMotion.Balance)
                        Assert.That(track.Curve.Evaluate(0), Is.EqualTo(track.Curve.Evaluate(1)).Within(.001f));
                }
            }
        }
    }
}
