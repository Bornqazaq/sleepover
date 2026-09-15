using System.Collections.Generic;
using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class PartySeriesTests
    {
        private readonly List<Object> assets = new List<Object>();
        private MinigameCatalog Catalog(params string[] scenes)
        {
            var catalog = ScriptableObject.CreateInstance<MinigameCatalog>(); assets.Add(catalog);
            var data = new SerializedObject(catalog); var list = data.FindProperty("games"); list.arraySize = scenes.Length;
            for (int i = 0; i < scenes.Length; i++)
            {
                var game = ScriptableObject.CreateInstance<MinigameDefinition>(); assets.Add(game);
                var g = new SerializedObject(game); g.FindProperty("sceneName").stringValue = scenes[i];
                g.FindProperty("minPlayers").intValue = i == 2 ? 4 : 2; g.ApplyModifiedPropertiesWithoutUndo();
                list.GetArrayElementAtIndex(i).objectReferenceValue = game;
            }
            data.ApplyModifiedPropertiesWithoutUndo(); return catalog;
        }
        [TearDown] public void Clean() { foreach(var a in assets) Object.DestroyImmediate(a); assets.Clear(); PartySeries.Reset(); }
        [Test] public void QueueExcludesUnavailableAndUnsuitableGamesAndDuplicateScenes()
        {
            var c = Catalog("A", "a", "NeedsFour", "Missing", "B", "");
            var q = PartySeries.BuildQueue(c, 2, new System.Random(17), s => s != "Missing");
            Assert.That(q.Select(g=>g.SceneName), Is.EquivalentTo(new[]{"A","B"}));
        }
        [Test] public void ShuffleHasNoRepeatsForManySeedsAndChangesOrder()
        {
            var c = Catalog("A","B","C","D","E","F","G","H"); var orders = new HashSet<string>();
            for(int seed=0;seed<60;seed++)
            {
                var q=PartySeries.BuildQueue(c,4,new System.Random(seed),_=>true);
                Assert.That(q.Count,Is.EqualTo(8)); Assert.That(q.Select(g=>g.SceneName).Distinct().Count(),Is.EqualTo(8));
                orders.Add(string.Join(",",q.Select(g=>g.SceneName)));
            }
            Assert.That(orders.Count,Is.GreaterThan(50));
        }
        [TestCase("  Друг  ", true, "Друг")]
        [TestCase("", false, "")]
        [TestCase("<b>Имя</b>", false, "")]
        [TestCase("Имя\nдруга", false, "")]
        [TestCase("Абвгдежзийклмн", true, "Абвгдежзийклмн")]
        [TestCase("Абвгдежзийклмно", false, "")]
        public void NameFitsNetworkPayloadAndCannotInjectMarkup(string input,bool valid,string expected)
        { Assert.That(PartyDisplayName.TryNormalize(input,out var result),Is.EqualTo(valid)); if(valid) Assert.That(result,Is.EqualTo(expected)); }
        [Test] public void InvalidUnicodeIsRejectedWithoutThrowing()
        { Assert.That(PartyDisplayName.TryNormalize("\ud800", out _), Is.False); }
        [Test] public void LocalProfileKeepsNameCharacterAndScoreAcrossAvatars()
        {
            var player=new SessionPlayer(701,"Друг") { CharacterIndex=6, Score=17 }; LocalPartyProfile.Save(player);
            var next=new SessionPlayer(701,"Default");LocalPartyProfile.Restore(next);
            Assert.That(next.DisplayName,Is.EqualTo("Друг"));Assert.That(next.CharacterIndex,Is.EqualTo(6));Assert.That(next.Score,Is.EqualTo(17));
        }
    }
}
