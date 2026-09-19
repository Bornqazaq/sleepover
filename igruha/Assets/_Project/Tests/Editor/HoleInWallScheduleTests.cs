using Igruha.Minigames.HoleInWall;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class HoleInWallScheduleTests
    {
        private HoleInWallConfig config;

        [SetUp] public void SetUp() => config = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<HoleInWallConfig>("Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset"));
        [TearDown] public void TearDown() => Object.DestroyImmediate(config);

        [TestCase(.5f)] [TestCase(1f)] [TestCase(1.5f)] [TestCase(2f)]
        public void SpeedEditsStillEndAtTheMinuteWithoutTailOrOverlappingApproaches(float factor)
        {
            var data = new SerializedObject(config);
            var approaches = data.FindProperty("wallApproachSeconds");
            for (int i = 0; i < approaches.arraySize; i++) approaches.GetArrayElementAtIndex(i).floatValue *= factor;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(config.WallCount, Is.EqualTo(8));
            Assert.That(config.RoundLength, Is.EqualTo(60));
            float total = 0;
            for (int i = 0; i < config.WallCount; i++)
            {
                total += config.StageDuration(i);
                Assert.That(config.StartTime(i, true) + config.ApproachSeconds(i, true), Is.EqualTo(config.HitTime(i)).Within(.0001));
                if (i > 0)
                {
                    Assert.That(config.StartTime(i, false), Is.GreaterThanOrEqualTo(config.HitTime(i - 1) - .0001f));
                    Assert.That(config.WallSpeed(i, false), Is.GreaterThanOrEqualTo(config.WallSpeed(i - 1, false) - .0001f));
                }
            }
            // Расписание кончается раньше минуты на развязку (IGR-533): последняя
            // стена обязана ударить до итогов, иначе исход не успевает отыграть.
            // Поэтому сравниваем не с длиной раунда, а с длиной расписания.
            Assert.That(config.StartTime(0, false) + total, Is.EqualTo(60).Within(.0001));
            Assert.That(config.HitTime(7), Is.EqualTo(config.ScheduleLength).Within(.0001));
            Assert.That(config.ScheduleLength, Is.EqualTo(60 - config.FinalPayoffSeconds).Within(.0001));
            // Хвост развязки принадлежит последней стадии: она тянется от подъезда
            // восьмой стены до конца раунда, то есть длиннее своего подъезда ровно
            // на развязку.
            Assert.That(config.StageDuration(7),
                Is.EqualTo(config.ApproachSeconds(7) + config.FinalPayoffSeconds).Within(.0001));
        }

        [Test] public void CurrentApproachTimesAndAccelerationArePreserved()
        {
            float[] seconds = { 6, 6, 5, 5, 4, 4, 3.5f, 3 };
            for (int i = 0; i < seconds.Length; i++)
                Assert.That(config.ApproachSeconds(i), Is.EqualTo(seconds[i]));
        }

        [Test] public void DefinitionIsTheOnlyEditableDurationSource()
        {
            var definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Igruha.Core.Minigame.MinigameDefinition>(
                "Assets/_Project/Settings/Gameplay/Minigames/HoleInWall.asset"));
            try
            {
                var d = new SerializedObject(definition);
                d.FindProperty("roundDuration").floatValue = 75;
                d.ApplyModifiedPropertiesWithoutUndo();
                var c = new SerializedObject(config);
                c.FindProperty("definition").objectReferenceValue = definition;
                c.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(config.RoundLength, Is.EqualTo(definition.RoundDuration));
                Assert.That(config.HitTime(7), Is.EqualTo(75 - config.FinalPayoffSeconds).Within(.0001));
                float sum = 0;
                for (int i = 0; i < config.WallCount; i++) sum += config.StageDuration(i);
                Assert.That(config.StartTime(0, false) + sum, Is.EqualTo(75).Within(.0001));
            }
            finally { Object.DestroyImmediate(definition); }
        }
    }
}
