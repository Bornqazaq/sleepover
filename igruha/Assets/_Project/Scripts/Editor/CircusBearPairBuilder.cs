using System;
using System.Linq;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Two persistent scene bears, with independent NGO identities and one shared local camera.</summary>
    public static class CircusBearPairBuilder
    {
        private const string SecondName = "PitBear_Second";
        private const float StartRadius = 4.4f;

        [MenuItem("Igruha/Цирк/Два медведя и студийный свет — обе сцены")]
        public static void ApplyBoth()
        {
            var opened = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || opened.isDirty)
                throw new InvalidOperationException("Save the scene and leave Play Mode first.");
            string original = opened.path;
            try
            {
                foreach (string name in new[] { "CansOrder", "Stopwatch" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    ApplyActive();
                    CircusNightBuilder.ApplyNaturalLighting();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original); }
        }

        internal static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "CansOrder" && scene.name != "Stopwatch")
                throw new InvalidOperationException("Open a circus scene.");
            foreach (var cage in Object.FindObjectsByType<CageStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                CircusPlaytestFixes.ApplyShelfSupport(cage.transform);
            var game = Object.FindFirstObjectByType<MinigameControllerBase>();
            var data = new SerializedObject(game);
            var first = data.FindProperty("bear").objectReferenceValue as PitBear;
            if (first == null) throw new InvalidOperationException("The first circus bear is missing.");
            var second = first.transform.parent.GetComponentsInChildren<PitBear>(true).FirstOrDefault(b => b.name == SecondName);
            if (second == null)
            {
                second = Object.Instantiate(first.gameObject, first.transform.parent).GetComponent<PitBear>();
                second.name = SecondName;
            }
            Configure(first, new Vector3(-StartRadius, 0, 0), 0);
            Configure(second, new Vector3(StartRadius, 0, 0), 180);
            data.FindProperty("secondBear").objectReferenceValue = second;
            data.ApplyModifiedPropertiesWithoutUndo();
            var presentation = game.GetComponent<CircusAttackPresentation>();
            if (presentation != null)
            {
                var view = new SerializedObject(presentation);
                view.FindProperty("bear").objectReferenceValue = first;
                view.FindProperty("secondBear").objectReferenceValue = second;
                view.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void Configure(PitBear bear, Vector3 position, float yaw)
        {
            bear.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var capsule = bear.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                // Protect the torso; leave the swiping forepaw outside the body
                // collider so it can reach a victim before the torso pushes them away.
                capsule.direction = 2;
                capsule.radius = 1f;
                capsule.height = 3.55f;
                capsule.center = new Vector3(0, 1.05f, -.2f);
                EditorUtility.SetDirty(capsule);
            }
            var animator = bear.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var fields = new SerializedObject(bear);
            fields.FindProperty("wallMargin").floatValue = PitBear.BodyWallClearance;
            fields.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bear);
        }
    }
}
