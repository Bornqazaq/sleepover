using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Точные прокси отделки для камеры, без физических контактов с игроками.</summary>
    public static class HubCameraOcclusion
    {
        private const string RootName = "_HubCameraOcclusion";
        private const string LayerName = "CameraOnly";
        private const int FirstUserLayer = 8;

        [MenuItem("Igruha/Hub/Rebuild Camera Occlusion")]
        public static void Rebuild()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode first.");

            var art = GameObject.Find("_HubRoomCozy");
            if (art == null)
                throw new InvalidOperationException("Hub room finish is missing.");

            int layer = EnsureLayer();
            var previous = GameObject.Find(RootName);
            if (previous != null)
                Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Rebuild Hub camera occlusion");

            foreach (var filter in art.GetComponentsInChildren<MeshFilter>())
            {
                // Только выступающая архитектура. Мебель и мелкий декор не
                // должны постоянно подталкивать камеру при обходе комнаты.
                if (!filter.name.Contains("_HR_OakBeam")
                    && !filter.name.Contains("_HR_OakEdge")
                    && !filter.name.Contains("_HR_BeamShadow"))
                    continue;
                if (filter.sharedMesh == null)
                    throw new InvalidOperationException($"Missing mesh: {filter.name}");

                var proxy = new GameObject(filter.name + "_CameraOnly");
                proxy.transform.SetParent(root.transform, false);
                proxy.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                proxy.transform.localScale = filter.transform.lossyScale;
                proxy.layer = layer;
                var collider = proxy.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.excludeLayers = ~0;
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static int EnsureLayer()
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
                return layer;

            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/TagManager.asset")[0]);
            var layers = settings.FindProperty("layers");
            for (int i = FirstUserLayer; i < layers.arraySize; i++)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    continue;
                layers.GetArrayElementAtIndex(i).stringValue = LayerName;
                settings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
            throw new InvalidOperationException("No free layer for camera-only geometry.");
        }
    }
}
