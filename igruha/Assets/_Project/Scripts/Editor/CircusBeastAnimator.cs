using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Compatibility entry point for the circus menu; never retargets player clips.</summary>
    internal static class CircusBeastAnimator
    {
        internal const string ControllerPath = CircusNightAssets.Controller;

        [MenuItem("Igruha/Цирк/Пересобрать аниматор зверя")]
        internal static void Rebuild()
        {
            LoadOrBuild();
            Debug.Log("Bruno: rebuilt the five original Blender actions.");
        }

        internal static AnimatorController LoadOrBuild()
        {
            CircusNightAssets.BuildAnimator();
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        }
    }
}
