using System.Collections.Generic;
using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Tests
{
    public sealed class UnifiedMinigameUiTests
    {
        private const string ResultsPrefab = "Assets/_Project/Prefabs/UI/UnifiedRoundResults.prefab";
        private const string PausePrefab = "Assets/_Project/Prefabs/UI/UnifiedPauseMenu.prefab";

        [TestCase(2, false)] [TestCase(4, true)] [TestCase(8, true)]
        public void LongNamesCannotOverlapScoreAndTiesKeepTheirPlaces(int count, bool series)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ResultsPrefab));
            try
            {
                root.SetActive(true);
                var view = root.GetComponent<RoundResultsView>();
                var results = new MinigameResults { CountsTowardSession = series };
                var players = new List<SessionPlayer>();
                for (int i = 0; i < count; i++)
                {
                    players.Add(new SessionPlayer(i, new string('Ж', 20)) { CharacterIndex = i });
                    results.Add(i, i < 2 ? 1 : i + 1, 8 - i, 100 + i);
                }
                view.Show(results, players);
                Canvas.ForceUpdateCanvases();
                var rows = root.GetComponentsInChildren<ResultRow>(true);
                Assert.That(rows.Count(row => row.gameObject.activeSelf), Is.EqualTo(count));
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    var name = row.transform.Find("PlayerName").GetComponent<TMP_Text>();
                    var value = row.transform.Find("Value").GetComponent<TMP_Text>();
                    Assert.That(name.richText, Is.False, "Names must not be interpreted as TMP markup");
                    Assert.That(name.text.Length, Is.EqualTo(20));
                    Assert.That(name.rectTransform.anchoredPosition.x + name.rectTransform.rect.width,
                        Is.LessThan(value.rectTransform.anchoredPosition.x));
                    Assert.That(row.transform.Find("Place").GetComponent<TMP_Text>().text, Is.EqualTo((i < 2 ? 1 : i + 1).ToString("00")));
                    Assert.That(row.transform.Find("Total").GetComponent<TMP_Text>().text, Is.EqualTo(series ? (100 + i).ToString() : ""));
                    Assert.That(row.transform.Find("Portrait").GetComponent<Image>().sprite, Is.Not.Null);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AwardAndCopyKeepAuthoritativeGameDetails()
        {
            var results = new MinigameResults { MetricTitle = "ВОДА", AreTeams = true };
            results.Add(7, 1);
            results.SetDetail(0, new RoundResultDetail("120", "КОМАНДА А", Color.blue));
            results.SetAward(0, 8, 24);
            var copy = new MinigameResults(); copy.CopyFrom(results);
            Assert.That(copy.MetricTitle, Is.EqualTo("ВОДА"));
            Assert.That(copy.AreTeams, Is.True);
            Assert.That(copy.Entries[0].Detail.Value, Is.EqualTo("120"));
            Assert.That(copy.Entries[0].Detail.Note, Is.EqualTo("КОМАНДА А"));
            Assert.That(copy.Entries[0].Points, Is.EqualTo(8));
            Assert.That(copy.Entries[0].Total, Is.EqualTo(24));
        }

        [Test]
        public void HostCanChooseRoundExitAndGameExitWithoutConfusingTheirConsequences()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PausePrefab));
            try
            {
                var view = root.GetComponentInChildren<PauseMenuView>(true);
                view.gameObject.SetActive(true);
                view.Show(true, true, true);
                view.ConfirmExit();
                Assert.That(view.WantsQuit, Is.False);
                Assert.That(view.GetComponentInChildren<TMP_Text>().text, Is.Not.Empty);
                view.CancelExit();
                view.ConfirmExit(true);
                Assert.That(view.WantsQuit, Is.True);
                var message = view.transform.Find("PauseCard/Message").GetComponent<TMP_Text>();
                Assert.That(message.text, Does.Contain("потеряют соединение"));
                view.CancelExit();
                Assert.That(view.IsConfirmingExit, Is.False);
                Assert.That(view.transform.Find("PauseCard/SoundSettings").gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EveryMinigameUsesSharedResultsAndExactlyOnePause()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/Minigames" }))
                {
                    var scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid));
                    var roots = scene.GetRootGameObjects();
                    Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<PauseScreen>(true)).Count(), Is.EqualTo(1), scene.name);
                    Assert.That(roots.Count(r => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r) == PausePrefab), Is.EqualTo(1), scene.name);
                    var hud = roots.SelectMany(r => r.GetComponentsInChildren<RoundHud>(true)).Single();
                    var data = new SerializedObject(hud);
                    foreach (string field in new[] { "timerPlate", "statusPlate", "resultsView", "restartButton" })
                        Assert.That(data.FindProperty(field).objectReferenceValue, Is.Not.Null, scene.name + ": " + field);
                    var panel = (GameObject)data.FindProperty("resultsPanel").objectReferenceValue;
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel), Is.EqualTo(ResultsPrefab), scene.name);
                }
            }
            finally
            {
                if (setup.Any(item => item.isLoaded && item.isActive && !string.IsNullOrEmpty(item.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            }
        }
    }
}
