using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Minigames.HoleInWall;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class HoleInWallCameraBuilder
    {
        internal static void Audit()
        {
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>();
            var driver = Object.FindFirstObjectByType<HoleInWallCamera>(FindObjectsInactive.Include);
            var controller = Object.FindFirstObjectByType<MinigameCameraController>();
            if (game.Definition.CameraMode != CameraMode.Fixed || driver == null || controller == null)
                throw new System.InvalidOperationException("HoleInWall requires its scene-owned Fixed camera.");
            var data = new SerializedObject(driver);
            var camera = driver.GetComponent<CinemachineCamera>();
            if (camera == null || data.FindProperty("game").objectReferenceValue != game ||
                data.FindProperty("view").objectReferenceValue != camera ||
                data.FindProperty("controller").objectReferenceValue != controller ||
                new SerializedObject(controller).FindProperty("fixedRig").objectReferenceValue != camera)
                throw new System.InvalidOperationException("Unwired HoleInWall camera.");
            var results = Object.FindFirstObjectByType<HoleInWallResultsPanel>(FindObjectsInactive.Include);
            var portraits = new SerializedObject(results).FindProperty("portraits");
            if (portraits.arraySize != 8) throw new System.InvalidOperationException("Results require all eight roster portraits.");
            for (int i = 0; i < portraits.arraySize; i++)
                if (portraits.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    throw new System.InvalidOperationException("Missing results portrait " + i);
            foreach (var track in Object.FindObjectsByType<HoleInWallTrack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var cutout in track.GetComponentsInChildren<WallCutout>(true))
                    if (new SerializedObject(cutout).FindProperty("laneIndex").intValue != track.Index)
                        throw new System.InvalidOperationException("Outline hue belongs to the wrong lane.");
        }

        internal static void Build()
        {
            var controller = Object.FindFirstObjectByType<MinigameCameraController>();
            var previous = controller.transform.Find("HoleInWall view");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>();
            var go = new GameObject("HoleInWall view");
            go.transform.SetParent(controller.transform, false);
            var camera = go.AddComponent<CinemachineCamera>();
            var fields = new SerializedObject(controller);
            var original = fields.FindProperty("thirdPersonRig").objectReferenceValue as CinemachineCamera;
            camera.Lens = original.Lens;
            camera.Priority = 20;
            var driver = go.AddComponent<HoleInWallCamera>();
            var data = new SerializedObject(driver);
            data.FindProperty("game").objectReferenceValue = game;
            data.FindProperty("view").objectReferenceValue = camera;
            data.FindProperty("controller").objectReferenceValue = controller;
            data.ApplyModifiedPropertiesWithoutUndo();
            fields.FindProperty("fixedRig").objectReferenceValue = camera;
            fields.ApplyModifiedPropertiesWithoutUndo();
            var definition = new SerializedObject(game.Definition);
            definition.FindProperty("cameraMode").enumValueIndex = (int)CameraMode.Fixed;
            definition.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(game.Definition);
        }
    }
}
