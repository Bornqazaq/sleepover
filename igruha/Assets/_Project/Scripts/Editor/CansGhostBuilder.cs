using System;
using Igruha.Minigames.CansOrder;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    public static class CansGhostBuilder
    {
        [MenuItem("Igruha/Цирк/Призрак победителя — Порядок банок")]
        public static void ApplyActive()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "CansOrder")
                throw new InvalidOperationException("Open CansOrder outside Play Mode.");
            var game = UnityEngine.Object.FindFirstObjectByType<CansOrderMinigame>();
            if (game == null) throw new InvalidOperationException("CansOrder rules missing.");
            var ghost = game.GetComponent<CansGhostController>() ?? game.gameObject.AddComponent<CansGhostController>();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/CansGhost.shader");
            if (shader == null) throw new InvalidOperationException("Ghost shader missing.");
            Material body = Material("CansGhostBody", shader);
            body.SetFloat("_Body", 1); body.SetFloat("_Wave", .008f);
            body.SetColor("_BaseColor", new Color(.65f, 1, .93f, .64f));
            Material ribbon = Material("CansGhostGlow", shader);
            ribbon.SetFloat("_Body", 0); ribbon.SetFloat("_Wave", 0);
            ribbon.SetColor("_BaseColor", new Color(.65f, 1, .93f, .6f));
            var so = new SerializedObject(ghost);
            so.FindProperty("ghostMaterial").objectReferenceValue = body;
            so.FindProperty("ribbonMaterial").objectReferenceValue = ribbon;
            so.FindProperty("puffMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Art/CircusNight/Mascot/CN_Steam.mat");
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/_Project/Art/UI/Fonts/Circus_Russo SDF.asset");
            so.FindProperty("trickSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/_Project/Audio/BelieveOrNot/SFX_BEL_Puff.wav");
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(ribbon);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Cans ghost configured: two tricks, private shelf revisions, bounded flight.");
        }
        private static Material Material(string name, Shader shader)
        {
            string path = "Assets/_Project/Art/UI/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
    }
}
