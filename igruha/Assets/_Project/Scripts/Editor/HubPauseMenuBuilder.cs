using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.EditorTools
{
    public static class HubPauseMenuBuilder
    {
        [MenuItem("Igruha/Хаб/Оформить меню паузы")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new System.InvalidOperationException("Open Hub in Edit Mode first.");
            UnifiedPauseMenuBuilder.BuildPrefab();
            UnifiedPauseMenuBuilder.Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
