using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.Minigame;
using Igruha.Minigames.OneBullet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Additive upgrade: never rebuilds or replaces the polished arena.</summary>
    public static class OneBulletUpgradeBuilder
    {
        public const string LayoutPath = "Assets/_Project/Settings/Gameplay/Minigames/OneBulletStorm.asset";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Minigames/OneBullet";
        [Serializable] private class Stages { public int[] lastSafeStage; }
        [MenuItem("Igruha/Minigames/Upgrade One Bullet rules")]
        public static void BuildRules()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play mode first.");
            EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            var game = Object.FindFirstObjectByType<OneBulletMinigame>();
            var source = OneBulletArenaBuilder.ReadLayout();
            var stages = JsonUtility.FromJson<Stages>(File.ReadAllText("Assets/_Project/Scripts/Minigames/OneBullet/OneBulletStormStages.json")).lastSafeStage;
            var positions = new Vector3[source.grid * source.grid];
            for (int i = 0; i < positions.Length; i++) positions[i] = OneBulletArenaBuilder.Position(source, i);
            var edges = new OneBulletStormLayout.Edge[source.edges.Length];
            for (int i = 0; i < edges.Length; i++) edges[i] = new OneBulletStormLayout.Edge { A = source.edges[i].a, B = source.edges[i].b };
            var starts = BuildStarts(stages, edges, source.spawns, source.rooms, source.guns);
            var layout = AssetDatabase.LoadAssetAtPath<OneBulletStormLayout>(LayoutPath);
            if (layout == null) { layout = ScriptableObject.CreateInstance<OneBulletStormLayout>(); AssetDatabase.CreateAsset(layout, LayoutPath); }
            layout.Configure(positions, stages, edges, source.rooms, starts); EditorUtility.SetDirty(layout);
            var old = GameObject.Find("_OneBulletStorm"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("_OneBulletStorm");
            var points = new Transform[positions.Length];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new GameObject("SafeNode_" + i).transform;
                points[i].SetParent(root.transform, false); points[i].position = layout.StandingPoint(i);
            }
            var storm = game.GetComponent<OneBulletStorm>(); if (storm == null) storm = game.gameObject.AddComponent<OneBulletStorm>();
            OneBulletArenaBuilder.Set(storm, "layout", layout);
            var so = new SerializedObject(storm); var refs = so.FindProperty("recoveryPoints"); refs.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++) refs.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var decoys = game.GetComponent<OneBulletDecoys>(); if (decoys == null) decoys = game.gameObject.AddComponent<OneBulletDecoys>();
            OneBulletArenaBuilder.Set(decoys, "canModel", BuildCan());
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>("Assets/_Project/Settings/Gameplay/Minigames/OneBullet.asset");
            var ds = new SerializedObject(definition);
            SetStrings(ds, "controlHints", new[] { "WASD — бег · Space — прыжок · Ctrl — присед", "ЛКМ — выстрел · ПКМ — прицел · Shift — толчок", "Q — бросить пустую банку. Две на раунд: обмани слух.", "Буря сужает дворы. Уходи из пыли по стрелкам." });
            SetStrings(ds, "tutorialSteps", new[] { "Ищи револьвер и слушай шаги. Оружие подбирается касанием.", "Q бросает шумную банку. Две попытки отвлечь соперника.", "После выбываний буря сужает карту. 12 секунд, чтобы уйти.", "В густой буре можно прожить 8 секунд. Доберись до чистого двора.", "Один патрон. Промахнулся — ищи оружие снова." });
            ds.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene); EditorSceneManager.SaveScene(game.gameObject.scene);
            AssetDatabase.SaveAssets(); Debug.Log("OneBullet rules upgraded: seven connected territories, safe starts, decoy model.");
        }
        private static void SetStrings(SerializedObject so, string field, string[] values)
        {
            var p = so.FindProperty(field); p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).stringValue = values[i];
        }
        private static GameObject BuildCan()
        {
            Directory.CreateDirectory(PrefabFolder);
            var root = new GameObject("DecoyCan");
            foreach (string name in new[] { "CN_TinBody", "CN_TinTrim" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CircusNight/Models/" + name + ".fbx");
                var part = (GameObject)PrefabUtility.InstantiatePrefab(asset); part.transform.SetParent(root.transform, false);
            }
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Vector3 centre = bounds.center;
            float scale = .18f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            foreach (Transform child in root.transform) { child.localPosition = -centre * scale; child.localScale = Vector3.one * scale; }
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/DecoyCan.prefab"); Object.DestroyImmediate(root); return prefab;
        }
        private static int[] BuildStarts(int[] stages, OneBulletStormLayout.Edge[] edges, int[] original, int[] rooms, int[] guns)
        {
            var starts = new int[7 * 8];
            var distances = new int[stages.Length, stages.Length];
            for (int source = 0; source < stages.Length; source++)
            {
                for (int i = 0; i < stages.Length; i++) distances[source, i] = int.MaxValue;
                distances[source, source] = 0; var q = new Queue<int>(); q.Enqueue(source);
                while (q.Count > 0)
                {
                    int n = q.Dequeue();
                    foreach (var e in edges)
                    {
                        int other = e.A == n ? e.B : e.B == n ? e.A : -1;
                        if (other < 0 || distances[source, other] != int.MaxValue) continue;
                        distances[source, other] = distances[source, n] + 1; q.Enqueue(other);
                    }
                }
            }
            for (int stage = 0; stage < 7; stage++)
                for (int slot = 0; slot < 8; slot++)
                {
                    if (stage == 0) { starts[slot] = original[slot]; continue; }
                    int best = -1, bestDistance = -1;
                    for (int n = 0; n < stages.Length; n++)
                    {
                        if (stages[n] < stage || Array.IndexOf(rooms, n) >= 0 || Array.IndexOf(guns, n) >= 0) continue;
                        int closest = slot == 0 ? distances[60, n] : int.MaxValue;
                        for (int j = 0; j < slot; j++) closest = Math.Min(closest, distances[starts[stage * 8 + j], n]);
                        if (closest > bestDistance) { bestDistance = closest; best = n; }
                    }
                    starts[stage * 8 + slot] = best;
                }
            return starts;
        }
    }
}
