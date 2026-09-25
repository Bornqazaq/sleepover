using System;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Minigames.OneBullet;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Repeatable migration of only OneBullet; never rebuilds the arena or shared rigs.</summary>
    public static class OneBulletFirstPersonBuilder
    {
        private const string DefinitionPath = "Assets/_Project/Settings/Gameplay/Minigames/OneBullet.asset";
        private const string MaterialPath = "Assets/_Project/Art/Minigames/OneBullet/PickupRing.mat";
        private const float StartSeparation = 1.65f;
        public static Vector3 WeaponPosition(OneBulletArenaBuilder.Layout layout, int node)
        {
            Vector3 point = OneBulletArenaBuilder.Position(layout, node);
            if (Array.IndexOf(layout.spawns, node) >= 0)
                foreach (var edge in layout.edges)
                {
                    int neighbour = edge.a == node ? edge.b : edge.b == node ? edge.a : -1;
                    if (neighbour < 0) continue;
                    point = Vector3.MoveTowards(point, OneBulletArenaBuilder.Position(layout, neighbour), StartSeparation);
                    break;
                }
            return point + Vector3.up * .18f;
        }
        [MenuItem("Igruha/Minigames/One Bullet first person")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play mode.");
            if (EditorSceneManager.GetActiveScene().path != OneBulletArenaBuilder.ScenePath)
                EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            Configure();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        public static void Configure()
        {
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(DefinitionPath);
            ConfigureDefinition(definition);
            var game = Object.FindFirstObjectByType<OneBulletMinigame>();
            var view = Object.FindFirstObjectByType<OneBulletPresentation>();
            var cameraRoot = GameObject.Find("_Camera");
            var rig = cameraRoot.GetComponentInChildren<FirstPersonCameraRig>(true);
            OneBulletArenaBuilder.Set(game, "firstPersonRig", rig);
            var camera = cameraRoot.GetComponentInChildren<Camera>(true);
            var component = view.GetComponent<OneBulletFirstPerson>();
            if (component == null) component = view.gameObject.AddComponent<OneBulletFirstPerson>();
            var serialized = new SerializedObject(view);
            var gun = (Transform)serialized.FindProperty("gun").objectReferenceValue;
            OneBulletArenaBuilder.Set(component, "game", game);
            OneBulletArenaBuilder.Set(component, "rig", rig);
            OneBulletArenaBuilder.Set(component, "output", camera);
            OneBulletArenaBuilder.Set(component, "gunSource", gun);
            OneBulletViewArmsBuilder.Configure(component);
            OneBulletArenaBuilder.Set(rig, "fieldOfView", 72f);
            OneBulletArenaBuilder.Set(rig, "eyeDropFromTop", OneBulletMinigame.EyeDrop);
            OneBulletArenaBuilder.Set(rig, "maxTurnSpeed", 0f);
            OneBulletArenaBuilder.Set(rig, "minPitch", -80f);
            OneBulletArenaBuilder.Set(rig, "maxPitch", 80f);
            var group = (CanvasGroup)serialized.FindProperty("ammunition").objectReferenceValue;
            var cross = group.transform.Find("Reticle");
            if (cross != null)
            {
                var fade = cross.GetComponent<CanvasGroup>();
                if (fade == null) fade = cross.gameObject.AddComponent<CanvasGroup>();
                OneBulletArenaBuilder.Set(component, "reticle", fade);
            }
            var ammo = group.transform.Find("Ammo")?.GetComponent<TMP_Text>();
            if (ammo != null)
            {
                ammo.text = "<size=36>1</size>  /  1\n<size=15>ЛКМ ВЫСТРЕЛ · ПКМ ПРИЦЕЛ</size>";
                ammo.rectTransform.anchorMin = ammo.rectTransform.anchorMax = new Vector2(1, 0);
                ammo.rectTransform.pivot = new Vector2(1, 0);
                ammo.rectTransform.anchoredPosition = new Vector2(-44, 39);
                ammo.rectTransform.sizeDelta = new Vector2(288, 66);
                ammo.alignment = TextAlignmentOptions.Right;
                OneBulletWeaponHudBuilder.ConfigureAmmo(group.transform);
            }
            OneBulletWeaponHudBuilder.Configure(view, game, group.transform.parent);
            var pose = new SerializedObject(component);
            pose.FindProperty("hipPosition").vector3Value = new Vector3(.17f, -.16f, .46f);
            pose.FindProperty("aimPosition").vector3Value = new Vector3(0, -.094f, .46f);
            pose.ApplyModifiedPropertiesWithoutUndo();
            var layout = OneBulletArenaBuilder.ReadLayout();
            var points = new SerializedObject(game).FindProperty("weaponSpawns");
            for (int i = 0; i < points.arraySize; i++)
                ((Transform)points.GetArrayElementAtIndex(i).objectReferenceValue).position = WeaponPosition(layout, layout.guns[i]);
            var oldRing = view.transform.Find("PickupRing");
            if (oldRing != null) Object.DestroyImmediate(oldRing.gameObject);
            var ring = new GameObject("PickupRing").AddComponent<LineRenderer>();
            ring.transform.SetParent(view.transform, false);
            ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 64;
            ring.startWidth = ring.endWidth = .018f;
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.rotation = Quaternion.Euler(90, 0, 0);
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .30f);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetColor("_BaseColor", new Color(1.8f, 1.05f, .22f, 1));
            EditorUtility.SetDirty(material);
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false; ring.enabled = false;
            OneBulletArenaBuilder.Set(view, "pickupRing", ring);
        }
        public static void ConfigureDefinition(MinigameDefinition definition)
        {
            var so = new SerializedObject(definition);
            so.FindProperty("cameraMode").enumValueIndex = (int)CameraMode.FirstPerson;
            var hints = so.FindProperty("controlHints"); hints.arraySize = 4;
            hints.GetArrayElementAtIndex(0).stringValue = "WASD — движение · Space — прыжок · Ctrl — присед";
            hints.GetArrayElementAtIndex(1).stringValue = "ЛКМ — выстрел · Удерживай ПКМ — прицел · Shift — толчок";
            hints.GetArrayElementAtIndex(2).stringValue = "Револьвер появляется через 10 с после старта. Ищи золотое свечение, подбирай касанием.";
            hints.GetArrayElementAtIndex(3).stringValue = "После любого выстрела новое оружие появится через 5 с. Без оружия ЛКМ / ПКМ — толчок.";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }
}
