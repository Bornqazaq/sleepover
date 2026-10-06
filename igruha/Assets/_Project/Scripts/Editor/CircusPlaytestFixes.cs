using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Core.Arena;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Narrow, repeatable update of cage floors and circus tuning; preserves arena layout.</summary>
    public static class CircusPlaytestFixes
    {
        [MenuItem("Igruha/Цирк/Исправления плейтеста — люки и погоня")]
        public static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit play mode first.");
            string opened = EditorSceneManager.GetActiveScene().path;
            string settings = "Assets/_Project/Settings/Gameplay/Minigames/";
            SetNumbers(AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings + "CircusBearConfig.asset"),
                new[] { "chaseSpeed", "firstAttackDelay" }, new[] { 8.2f, 1.25f });
            SetNumbers(AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings + "StopwatchConfig.asset"),
                new[] { "bearSpeed", "bearFirstAttackDelay", "hatchOpenSeconds" }, new[] { 8.2f, 1.25f, .9f });
            SetNumbers(AssetDatabase.LoadAssetAtPath<ScriptableObject>(settings + "CansOrderConfig.asset"),
                new[] { "hatchOpenSeconds" }, new[] { .9f });
            var wood = Material("CN_CagePlywood", new Color(.57f, .31f, .115f));
            var edge = Material("CN_CagePlyEdge", new Color(.79f, .51f, .23f));
            var iron = CircusNightAssets.Material("CN_BlackIron");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Project/Art/Animations/Karlan(Fbx without color)@Flying Back Death.fbx");
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    foreach (var cage in Object.FindObjectsByType<CageStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var data = new SerializedObject(cage);
                        data.FindProperty("fallingClip").objectReferenceValue = clip;
                        data.ApplyModifiedPropertiesWithoutUndo();
                        SetNumbers(cage.GetComponent<HingedFloorHatch>(), new[] { "anticipationSeconds" }, new[] { .18f });
                        foreach (string side in new[] { "DoorLeft", "DoorRight" })
                        {
                            var door = cage.transform.Find("Floor/" + side);
                            var old = door.Find("Plywood"); if (old != null) Object.DestroyImmediate(old.gameObject);
                            foreach (var renderer in door.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                            var parent = new GameObject("Plywood").transform; parent.SetParent(door, false);
                            var collider = door.Find("Collider");
                            Vector3 center = collider.localPosition, size = collider.localScale;
                            float width = size.x - .018f, depth = size.z - .018f;
                            // Continuous skin, exposed laminated edge and underside battens.
                            Box(parent,"Sheet",center,new Vector3(width,size.y,depth),wood);
                            for (int layer = 0; layer < 3; layer++)
                                Box(parent,"PlyEdge_"+layer,center+Vector3.up*(-.035f+layer*.025f),
                                    new Vector3(width+.003f,.008f,depth+.003f),edge);
                            for (int i = -1; i <= 1; i += 2)
                            {
                                Box(parent,"Underbrace_"+i,center+new Vector3(0,-.08f,i*depth*.28f),new Vector3(width,.075f,.12f),edge);
                                Box(parent,"Hinge_"+i,new Vector3(0,.008f,i*depth*.32f),new Vector3(.25f,.025f,.18f),iron);
                                for (int corner = -1; corner <= 1; corner += 2)
                                    Box(parent,"Fastener_"+i+"_"+corner,center+new Vector3(corner*(width*.5f-.1f),size.y*.5f+.003f,i*(depth*.5f-.12f)),
                                        new Vector3(.045f,.008f,.045f),iron);
                            }
                        }
                    }
                    foreach (var bear in Object.FindObjectsByType<PitBear>(FindObjectsSortMode.None))
                        SetNumbers(bear,new[] { "turnSpeed", "acceleration" },new[] { 220f,12f });
                    var cans = Object.FindFirstObjectByType<CansOrderMinigame>();
                    if (cans != null) SetNumbers(cans,new[] { "shelfCameraDistance", "shelfCameraHeight" },new[] { 3.1f,1.3f });
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { if (!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened); }
        }

        private static void SetNumbers(Object obj, string[] names, float[] values)
        {
            if (obj == null) throw new InvalidOperationException("Missing circus config");
            var so = new SerializedObject(obj);
            for (int i = 0; i < names.Length; i++) so.FindProperty(names[i]).floatValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(obj);
        }
        private static Material Material(string name, Color color)
        {
            string path = CircusNightAssets.Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
            material.color = color; material.SetFloat("_Smoothness",.23f); EditorUtility.SetDirty(material); return material;
        }
        private static void Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent,false); go.transform.localPosition = position; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
