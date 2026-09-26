using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Minigames.SumoRing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class SumoRingTests
    {
        [TestCase(2)] [TestCase(4)] [TestCase(8)]
        public void EveryStarterGetsOnePlaceAndTheLastStandingWins(int count)
        {
            var round = new SumoRound(); round.Reset(Enumerable.Range(0, count).ToArray(), 3);
            for (int i = 0; i < count - 1; i++) round.Eliminate(new[] { i }, 5 + i);
            round.Finish(20); var result = new MinigameResults(); round.Collect(result);
            Assert.That(result.Entries.Count, Is.EqualTo(count));
            foreach (var p in result.Entries) Assert.That(p.Place, Is.EqualTo(count - p.PlayerId));
        }
        [Test]
        public void FinalSimultaneousFallsShareFirstPlaceAndIgnoreIterationOrder()
        {
            var round = new SumoRound(); round.Reset(new[] { 0, 1, 2, 3 }, 3);
            round.Eliminate(new[] { 2, 3 }, 10); round.Eliminate(new[] { 1, 0 }, 20); round.Finish(20);
            var result = new MinigameResults(); round.Collect(result);
            Assert.That(result.Entries.Single(p => p.PlayerId == 0).Place, Is.EqualTo(1));
            Assert.That(result.Entries.Single(p => p.PlayerId == 1).Place, Is.EqualTo(1));
            Assert.That(result.Entries.Single(p => p.PlayerId == 2).Place, Is.EqualTo(3));
            Assert.That(result.Entries.Single(p => p.PlayerId == 3).Place, Is.EqualTo(3));
        }
        [Test]
        public void DisconnectAndFallInSameStepDoNotRankAPlayerTwice()
        {
            var r = new SumoRound(); r.Reset(new[] { 8, 13, 21 }, 3);
            r.Eliminate(new[] { 13, 13, 999 }, 2); r.Eliminate(new[] { 13 }, 10); r.Eliminate(new[] { 8 }, 11); r.Finish(11);
            var result = new MinigameResults(); r.Collect(result);
            Assert.That(result.Entries.Count, Is.EqualTo(3)); Assert.That(r.Find(13).Life, Is.Zero);
            Assert.That(result.Entries.Single(p => p.PlayerId == 13).Place, Is.EqualTo(3));
            Assert.That(r.Eliminate(new[] { 21 }, 12), Is.False);
        }
        [Test]
        public void SegmentSweepRetainsTheFarSideAndPermanentCentre()
        {
            var c = ScriptableObject.CreateInstance<SumoConfig>();
            try
            {
                Assert.That(c.Radius, Is.EqualTo(8.64f).Within(.001f));
                Assert.That(c.SupportRadius(Vector3.right, 10.1), Is.EqualTo(7.56f).Within(.001f));
                Assert.That(c.SupportRadius(Vector3.back, 10.1), Is.EqualTo(8.64f).Within(.001f));
                Assert.That(c.SupportRadius(Vector3.right, 10000), Is.EqualTo(2.16f).Within(.001f));
                Assert.That(c.NextRing(74.99), Is.EqualTo(5)); Assert.That(c.NextRing(75), Is.EqualTo(6));
            }
            finally { Object.DestroyImmediate(c); }
        }
        [Test]
        public void JumpOverMissingRingIsLegalUntilFeetFallBelowPlatform()
        {
            var c = ScriptableObject.CreateInstance<SumoConfig>();
            try
            {
                Assert.That(c.HasFallen(new Vector3(8, c.Height + 1, 0), 76), Is.False);
                Assert.That(c.HasFallen(new Vector3(8, c.Height - .1f, 0), 76), Is.False);
                Assert.That(c.HasFallen(new Vector3(8, 0, 0), 76), Is.True);
            }
            finally { Object.DestroyImmediate(c); }
        }
        [Test]
        public void RoundDefinitionExcludesTwoPlayersAndHasNoCountdownTimer()
        {
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>("Assets/_Project/Settings/Gameplay/Minigames/SumoRing.asset");
            Assert.That(definition, Is.Not.Null); Assert.That(definition.MinPlayers, Is.EqualTo(3));
            Assert.That(definition.MaxPlayers, Is.EqualTo(8)); Assert.That(definition.RoundDuration, Is.Zero);
            var catalog = AssetDatabase.LoadAssetAtPath<MinigameCatalog>("Assets/_Project/Settings/Gameplay/Minigames/MinigameCatalog.asset");
            Assert.That(PartySeries.BuildQueue(catalog, 2, new System.Random(1), _ => true).Any(g => g == definition), Is.False);
            Assert.That(PartySeries.BuildQueue(catalog, 3, new System.Random(1), _ => true).Any(g => g == definition), Is.True);
        }
        [Test]
        public void ShippedSceneKeepsTheExactSupportHeightAndFullSizeDecorativeRoof()
        {
            const string path = "Assets/_Project/Scenes/Minigames/SumoRing.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                Assert.That(transforms.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)), Is.Zero);
                var centre = transforms.Single(t => t.name == "PermanentCentre").GetComponent<MeshCollider>();
                Assert.That(centre.bounds.max.y, Is.EqualTo(2.16f).Within(.001f));
                var art = transforms.Single(t => t.name == "_SumoArt");
                Assert.That(art.GetComponentsInChildren<Collider>().Length, Is.Zero, "Decor must not shorten the camera boom or block a falling player.");
                var roof = art.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "SM_Canopy");
                Assert.That(roof.bounds.size.x, Is.InRange(21f, 23f));
                Assert.That(roof.bounds.min.y, Is.GreaterThan(8f));
                Assert.That(transforms.Count(t => t.GetComponent<SumoRingSegment>() != null), Is.EqualTo(144));
            }
            finally { if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }
        [Test]
        public void EveryAudioCueAndTheConsoleCoverHaveShippedAssets()
        {
            var audio = AssetDatabase.LoadAssetAtPath<Igruha.Core.Audio.MinigameSfxLibrary>("Assets/_Project/Audio/SumoRing/SfxLibrary.asset");
            Assert.That(audio.Entries.Count, Is.EqualTo(6));
            foreach (var entry in audio.Entries) Assert.That(entry.Clip != null || (entry.Variants != null && entry.Variants.Any(c => c != null)), Is.True, entry.Id);
            var library = AssetDatabase.LoadAssetAtPath<Igruha.Core.Hub.ConsoleArtworkLibrary>("Assets/_Project/Art/Hub/Console/ConsoleArtwork.asset");
            var game = AssetDatabase.LoadAssetAtPath<MinigameDefinition>("Assets/_Project/Settings/Gameplay/Minigames/SumoRing.asset");
            Assert.That(library.Find(game)?.Cover, Is.Not.Null);
        }
    }
}
