using System.Linq;
using Igruha.Core.Hub;
using Igruha.Core.Minigame;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Tests
{
    public sealed class ConsolePresentationTests
    {
        [TestCase(4f / 3f)]
        [TestCase(16f / 10f)]
        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        public void EntireTelevisionFitsWithMargin(float aspect)
        {
            var go = new GameObject("TV framing regression");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.aspect = aspect;
                camera.fieldOfView = ConsoleCameraFraming.FitFieldOfView(new Vector2(3.12f, 1.755f), aspect, 2f, .88f);
                foreach (float x in new[] { -1.56f, 1.56f })
                foreach (float y in new[] { -.8775f, .8775f })
                {
                    Vector3 corner = camera.WorldToViewportPoint(new Vector3(x, y, 2f));
                    Assert.That(corner.x, Is.InRange(.059f, .941f), "TV crops horizontally");
                    Assert.That(corner.y, Is.InRange(.059f, .941f), "TV crops vertically");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SavedHubRetainsCameraAndLibraryBindings()
        {
            const string path = "Assets/_Project/Scenes/Hub.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var menu = roots.SelectMany(root => root.GetComponentsInChildren<ConsoleMenu>(true)).Single();
                var data = new SerializedObject(menu);
                var focus = data.FindProperty("tvCameraFocus").objectReferenceValue as Transform;
                Assert.That(focus, Is.Not.Null, "Compact room rebuild must preserve TV focus");
                Assert.That(data.FindProperty("cameraController").objectReferenceValue, Is.Not.Null);
                Assert.That(data.FindProperty("libraryView").objectReferenceValue, Is.Not.Null);
                var framing = roots.SelectMany(root => root.GetComponentsInChildren<ConsoleCameraFraming>(true)).Single();
                Assert.That(framing.GetComponent<CinemachineCamera>().Follow, Is.EqualTo(focus));
                var frameData = new SerializedObject(framing);
                Assert.That(frameData.FindProperty("outputCamera").objectReferenceValue, Is.Not.Null);
                Assert.That(frameData.FindProperty("screen").objectReferenceValue, Is.Not.Null);
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void EveryCatalogGameHasDistinctCoverAndShortDescription()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MinigameCatalog>("Assets/_Project/Settings/Gameplay/Minigames/MinigameCatalog.asset");
            var artwork = AssetDatabase.LoadAssetAtPath<ConsoleArtworkLibrary>("Assets/_Project/Art/Hub/Console/ConsoleArtwork.asset");
            var covers = new System.Collections.Generic.HashSet<Sprite>();
            foreach (var game in catalog.Games)
            {
                var entry = artwork.Find(game);
                Assert.That(entry, Is.Not.Null, game.DisplayName);
                Assert.That(entry.Cover, Is.Not.Null, game.DisplayName);
                Assert.That(covers.Add(entry.Cover), Is.True, "Each game needs its own cover");
                Assert.That(entry.Summary, Is.Not.Empty, game.DisplayName);
                Assert.That(entry.Genre, Is.Not.Empty, game.DisplayName);
            }
        }
    }
}
