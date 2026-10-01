using Igruha.Minigames.CarryItem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Targeted migration: preserves the arena and the user's glass materials.</summary>
    public static class CarryCartHandlingSetup
    {
        private const string VoicePath = "Assets/_Project/Audio/Minigames/CarryItem/TemporaryVoice/";

        [MenuItem("Tools/Minigames/CarryItem/Apply cart handling")]
        public static void Apply()
        {
            const string path = "Assets/_Project/Prefabs/Minigames/CarryItem/WaterCart.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ApplyToCart(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var definition = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Settings/Gameplay/Minigames/CarryItem.asset");
            var definitionSo = new SerializedObject(definition);
            definitionSo.FindProperty("minPlayers").intValue = 2;
            var steps = definitionSo.FindProperty("tutorialSteps");
            string[] instructions = { "E — взяться. WASD — катить и плавно поворачивать.",
                "Перед поворотом отпустите движение. Назад — тормозить; резкий манёвр поднимает волну.",
                "Вода теряется, когда волна пересекает борт. Подкатите к насосу для откачки." };
            steps.arraySize = instructions.Length;
            for (int i = 0; i < instructions.Length; i++) steps.GetArrayElementAtIndex(i).stringValue = instructions[i];
            var hints = definitionSo.FindProperty("tutorialQuickHints"); hints.arraySize = 3;
            hints.GetArrayElementAtIndex(0).stringValue = "WASD — катить · назад — тормозить";
            hints.GetArrayElementAtIndex(1).stringValue = "E — взяться / отпустить";
            hints.GetArrayElementAtIndex(2).stringValue = "ЛКМ — толкнуть тележку / удар";
            definitionSo.ApplyModifiedPropertiesWithoutUndo();
            var config = AssetDatabase.LoadAssetAtPath<CarryItemConfig>("Assets/_Project/Settings/Gameplay/Minigames/CarryItemConfig.asset");
            var configSo = new SerializedObject(config);
            configSo.FindProperty("carrierStandoff").floatValue = 0.45f;
            configSo.FindProperty("rollingTetherGain").floatValue = 4f;
            configSo.FindProperty("rollingLateralGain").floatValue = 8f;
            configSo.FindProperty("brakeWaveGain").floatValue = 0.18f;
            configSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config); // Save newly added tuning fields.

            var droplets = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Materials/Minigames/CarryItem/CI_PourWater.mat");
            var softParticle = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Materials/Minigames/CarryItem/Original/CS_Particle.mat");
            droplets.SetTexture("_BaseMap", softParticle.GetTexture("_BaseMap"));
            droplets.SetColor("_BaseColor", new Color(0.78f, 0.88f, 0.9f, 0.72f));
            EditorUtility.SetDirty(droplets);

            var game = Object.FindFirstObjectByType<CarryItemMinigame>();
            if (game == null) throw new System.InvalidOperationException("Open CarryItem before applying the handling setup.");
            ApplyVoice(game.gameObject);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        internal static void ApplyToCart(GameObject root)
        {
            var network = root.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (network != null && !(network is Igruha.Networking.ObservedNetworkTransform))
            {
                var fields = typeof(Unity.Netcode.Components.NetworkTransform).GetFields(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++) values[i] = fields[i].GetValue(network);
                Object.DestroyImmediate(network);
                var observed = root.AddComponent<Igruha.Networking.ObservedNetworkTransform>();
                for (int i = 0; i < fields.Length; i++) if (!fields[i].IsInitOnly) fields[i].SetValue(observed, values[i]);
            }
            if (!root.TryGetComponent<WaterCartStability>(out _)) root.AddComponent<WaterCartStability>();
            if (!root.TryGetComponent<CartOverflowVisual>(out _)) root.AddComponent<CartOverflowVisual>();
            var water = root.transform.Find("Body/WaterPivot");
            if (water != null && !water.TryGetComponent<HorizontalCartWater>(out _)) water.gameObject.AddComponent<HorizontalCartWater>();
            if (water != null)
            {
                var renderer = water.GetComponentInChildren<Renderer>();
                var mat = renderer.sharedMaterial;
                mat.SetColor("_Tint", new Color(0.07f, 0.24f, 0.28f));
                mat.SetFloat("_Opacity", 0.55f); mat.SetFloat("_RippleStrength", 0.06f);
                EditorUtility.SetDirty(mat);
            }
            var leak = root.transform.Find("Body/LeakJet")?.GetComponent<ParticleSystem>();
            if (leak != null)
            {
                var main = leak.main;
                main.startColor = new Color(0.69f, 0.85f, 0.86f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
                main.startLifetime = 0.55f; main.startSpeed = 0.4f; main.gravityModifier = 1f;
                var shape = leak.shape; shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.3f, 0.02f, 0.01f);
            }
            var supports = new Transform[8];
            var handles = root.transform.Find("Body/Handles");
            if (handles != null)
            {
                var steel = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/_Project/Materials/Minigames/CarryItem/Original/CW_BrushedSteel.mat");
                int handleIndex = 0;
                foreach (Transform handle in handles)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        string postName = "ChassisPost" + side;
                        var old = handle.Find(postName);
                        if (old != null) Object.DestroyImmediate(old.gameObject);
                        string name = "HandleSupport" + handleIndex + "_" + side;
                        var existing = root.transform.Find(name);
                        var post = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        post.name = name;
                        if (post.TryGetComponent<Collider>(out var collider)) Object.DestroyImmediate(collider);
                        post.transform.SetParent(root.transform, false);
                        post.GetComponent<Renderer>().sharedMaterial = steel;
                        supports[handleIndex * 2 + side] = post.transform;
                    }
                    handleIndex++;
                }
            }
            var dust = root.transform.Find("WheelDust");
            if (dust == null)
            {
                dust = new GameObject("WheelDust").transform;
                dust.SetParent(root.transform, false);
                dust.localPosition = new Vector3(0f, 0.06f, -0.4f);
                var particles = dust.gameObject.AddComponent<ParticleSystem>();
                var main = particles.main; main.playOnAwake = false; main.startLifetime = 0.55f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
                main.startColor = new Color(0.55f, 0.46f, 0.34f, 0.18f);
                main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = particles.emission; emission.rateOverTime = 18f;
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.95f, 0.01f, 0.15f);
                var size = particles.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));
                var color = particles.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0.6f, 0), new GradientAlphaKey(0, 1) });
                color.color = gradient;
                particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/_Project/Materials/Minigames/CarryItem/Original/CS_Particle.mat");
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            var strain = dust.GetComponent<AudioSource>();
            if (strain == null) strain = dust.gameObject.AddComponent<AudioSource>();
            strain.clip = Clip("wheel_strain"); strain.loop = true; strain.playOnAwake = false;
            strain.spatialBlend = 1f; strain.minDistance = 2f; strain.maxDistance = 18f; strain.volume = 0.35f;
            var presentation = new SerializedObject(root.GetComponent<WaterCartPresentation>());
            var supportsProperty = presentation.FindProperty("handleSupports"); supportsProperty.arraySize = supports.Length;
            for (int i = 0; i < supports.Length; i++) supportsProperty.GetArrayElementAtIndex(i).objectReferenceValue = supports[i];
            presentation.FindProperty("wheelDust").objectReferenceValue = dust.GetComponent<ParticleSystem>();
            presentation.FindProperty("strainLoop").objectReferenceValue = strain;
            presentation.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void ApplyVoice(GameObject root)
        {
            var voice = root.GetComponent<CarryCartVoice>();
            if (voice == null) voice = root.AddComponent<CarryCartVoice>();
            var so = new SerializedObject(voice);
            foreach (var name in new[] { "disagreement", "turn", "release", "impact", "brake", "boss", "aza" })
                so.FindProperty(name).objectReferenceValue = Clip(name);
            var names = so.FindProperty("numberedPlayers"); names.arraySize = 8;
            for (int i = 0; i < 8; i++) names.GetArrayElementAtIndex(i).objectReferenceValue = Clip("player" + (i + 1));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(VoicePath + name + ".wav");
    }
}
