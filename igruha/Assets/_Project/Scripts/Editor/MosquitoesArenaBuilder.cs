using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Cinemachine;
using Unity.Netcode;
using TMPro;
using Igruha.Core.Minigame;
using Igruha.Core.CameraSystems;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Core.Audio;
using Igruha.Minigames.Mosquitoes;

namespace Igruha.EditorTools
{
    public static class MosquitoesArenaBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Minigames/Mosquitoes.unity";
        public const string Art = "Assets/_Project/Art/Mosquitoes";
        private const string Settings = "Assets/_Project/Settings/Gameplay/Minigames";
        private static Transform arena;
        [MenuItem("Igruha/Minigames/Собрать арену «Комары»")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
            if (EditorSceneManager.GetActiveScene().isDirty && EditorSceneManager.GetActiveScene().path != ScenePath)
                throw new System.InvalidOperationException("Save the current scene before building Mosquitoes.");
            MosquitoesCoreSetup.Build();
            MosquitoesCoreSetup.EnsureFolder(Art + "/Materials");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                AssetDatabase.CopyAsset("Assets/_Project/Scenes/MinigameTemplate.unity", ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            arena = GameObject.Find("_Arena").transform; Clear(arena);
            foreach (string root in new[] { "_Bounds", "_Traps", "_Pickups" }) { var go = GameObject.Find(root); if (go != null) Clear(go.transform); }
            var wood = Material("Walnut", "#4A3626"); var wall = Material("Midnight", "#2A3550");
            var ivory = Material("Ivory", "#D8CFC0"); var teal = Material("Teal", "#285861");
            Block("Floor", new Vector3(0, -.15f, 0), new Vector3(14.4f, .3f, 11.52f), wood, "Ground");
            Set(GameObject.Find("Floor").AddComponent<SurfaceAudio>(), "kind", (int)SurfaceKind.Wood);
            Block("CarpetCollider", new Vector3(3.6f, .005f, -3.63f), new Vector3(4.9f, .01f, 4f), teal, "Ground");
            Set(GameObject.Find("CarpetCollider").AddComponent<SurfaceAudio>(), "kind", (int)SurfaceKind.Carpet);
            Block("Ceiling", new Vector3(0, 3.75f, 0), new Vector3(14.4f, .3f, 11.52f), wall, "Ground");
            Block("WestWall", new Vector3(-7.35f, 1.8f, 0), new Vector3(.3f, 3.6f, 11.52f), wall, "Ground");
            Block("EastWall", new Vector3(7.35f, 1.8f, 0), new Vector3(.3f, 3.6f, 11.52f), wall, "Ground");
            Block("NorthWall", new Vector3(0, 1.8f, 5.91f), new Vector3(14.4f, 3.6f, .3f), wall, "Ground");
            Block("SouthWall", new Vector3(0, 1.8f, -5.91f), new Vector3(14.4f, 3.6f, .3f), wall, "Ground");
            Block("BedCollider", new Vector3(4.65f, .225f, -4.15f), new Vector3(1.8f, .45f, 2.88f), ivory, "Cover");
            Block("NightstandCollider", new Vector3(3.43f, .34f, -5.18f), new Vector3(.58f, .68f, .58f), wood, "Cover");
            Block("WardrobeCollider", new Vector3(-5.8f, 1.15f, 4.3f), new Vector3(2.88f, 2.3f, 1.08f), wood, "Cover");
            Block("DeskCollider", new Vector3(3.65f, .70f, 4.95f), new Vector3(2.16f, .1f, 1.08f), wood, "Cover");
            Block("DeskLegL", new Vector3(2.75f, .35f, 4.95f), new Vector3(.12f, .7f, .85f), wood, "Cover");
            Block("DeskLegR", new Vector3(4.55f, .35f, 4.95f), new Vector3(.12f, .7f, .85f), wood, "Cover");
            Block("ChairCollider", new Vector3(3.65f, .5f, 3.83f), new Vector3(.65f, 1f, .6f), wood, "Cover");
            for (int i = 0; i < 2; i++) Block("ShelfCollider" + i, new Vector3(3.5f, 1.4f + i * .6f, 5.42f), new Vector3(2.8f, .08f, .45f), wood, "Cover");
            var bed = Point("BedPose", new Vector3(4.65f, .46f, -4.85f));
            var exit = Point("BedExit", new Vector3(3.0f, .05f, -4.0f));
            var lampPoint = Point("LampCenter", new Vector3(3.43f, 1.05f, -5.18f));
            var config = Asset<MosquitoesConfig>(Settings + "/MosquitoesConfig.asset");
            var definition = Asset<MinigameDefinition>(Settings + "/Mosquitoes.asset");
            Set(definition, "displayName", "Комары"); Set(definition, "sceneName", "Mosquitoes");
            Set(definition, "category", (int)MinigameCategory.Asymmetric); Set(definition, "cameraMode", (int)CameraMode.ThirdPerson);
            Set(definition, "roundDuration", 120f); Set(definition, "minPlayers", 3); Set(definition, "maxPlayers", 8);
            Set(definition, "objective", "Великан: накопи 60 секунд сна. Комары: не дай ему выспаться за 120 секунд.");
            Set(definition, "controlHints", new[] { "Великан: E у кровати — уснуть / встать, ЛКМ — шлепок", "Комар: WASD, Space / Ctrl — полёт, Shift — тихо", "Зажми ЛКМ у груди на 0,5 с. Два укуса за 4 с будят великана" });
            Set(definition, "tutorialSteps", new[] { "Проверь свою роль и управление на арене", "Сон копится только в темноте. Свет выдаёт комаров", "Комары: атакуйте вместе, затем отходите на 5 секунд" });
            Set(definition, "tutorialQuickHints", new[] { "E — кровать", "ЛКМ — шлепок / укус", "F1 — правила, F2 — готов" });
            Transform spawns = GameObject.Find("_Spawns").transform;
            foreach (SpawnPoint p in spawns.GetComponentsInChildren<SpawnPoint>(true)) Object.DestroyImmediate(p.gameObject);
            for (int i = 0; i < 7; i++) { var p = new GameObject("MosquitoSpawn" + i).AddComponent<SpawnPoint>(); p.transform.SetParent(spawns); p.transform.position = new Vector3(-5.5f + i * 1.6f, 1.5f, 2.8f); p.transform.rotation = Quaternion.Euler(0, 180, 0); }
            var special = new GameObject("GiantSpawn").AddComponent<SpawnPoint>(); special.transform.SetParent(spawns); special.transform.position = bed.position; Set(special, "role", (int)SpawnRole.Special);
            var spawner = Object.FindFirstObjectByType<PlayerSpawner>(); Set(spawner, "debugPlayerCount", 3);
            var manager = GameObject.Find("MinigameManager");
            var template = manager.GetComponent<TemplateMinigame>(); if (template != null) Object.DestroyImmediate(template);
            var game = manager.GetComponent<MosquitoesMinigame>() ?? manager.AddComponent<MosquitoesMinigame>();
            if (manager.GetComponent<NetworkObject>() == null) manager.AddComponent<NetworkObject>();
            if (manager.GetComponent<Igruha.Networking.NetworkMinigameBridge>() == null) manager.AddComponent<Igruha.Networking.NetworkMinigameBridge>();
            if (manager.GetComponent<MosquitoesNetwork>() == null) manager.AddComponent<MosquitoesNetwork>();
            if (manager.GetComponent<MosquitoesDiagnostics>() == null) manager.AddComponent<MosquitoesDiagnostics>();
            var presentation = manager.GetComponent<MosquitoesPresentation>() ?? manager.AddComponent<MosquitoesPresentation>();
            MosquitoesCoreSetup.EnsureFolder("Assets/_Project/Audio/Mosquitoes");
            SfxLibraryBuilder.Build(System.IO.Path.GetFullPath("../docs/art/mosquitoes-sfx.json"));
            var audio = manager.GetComponent<MosquitoesAudio>(); if (audio == null) audio = manager.AddComponent<MosquitoesAudio>();
            Set(audio, "library", AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/Mosquitoes/SfxLibrary.asset"));
            Set(presentation, "audioPlayer", audio); Set(game, "audioPlayer", audio);
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var prefab = SetupPrefab(input, teal);
            var camera = Object.FindFirstObjectByType<MinigameCameraController>();
            var third = Object.FindFirstObjectByType<ThirdPersonCameraRig>(FindObjectsInactive.Include);
            var canvas = GameObject.Find("_UI").transform.Find("Canvas");
            Transform old = canvas.Find("MosquitoesOverlay"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var ui = new GameObject("MosquitoesOverlay", typeof(RectTransform)); ui.transform.SetParent(canvas, false); Stretch(ui.GetComponent<RectTransform>());
            var vg = new GameObject("SleepVignette", typeof(RectTransform), typeof(Image), typeof(ScreenVignette)); vg.transform.SetParent(ui.transform, false); Stretch(vg.GetComponent<RectTransform>());
            ui.transform.SetAsFirstSibling();
            var status = Label(ui.transform, "Status", new Vector2(.5f, .87f), new Vector2(650, 90), 26);
            var hints = Label(ui.transform, "RoleControls", new Vector2(.5f, .10f), new Vector2(1120, 95), 22);
            hints.text = ""; status.text = "";
            var lighting = GameObject.Find("_Lighting"); if (lighting != null) Clear(lighting.transform);
            var lightRoot = lighting != null ? lighting.transform : arena;
            var lamp = Light("BedsideLamp", lampPoint.position, LightType.Point, 4f, new Color(1, .64f, .28f), lightRoot); lamp.range = config.LightRadius;
            var window = Light("MoonWindow", new Vector3(6.8f, 2.1f, 3.9f), LightType.Spot, .15f, new Color(.37f, .55f, 1), lightRoot);
            window.transform.rotation = Quaternion.Euler(25, -90, 0); window.range = 13; window.spotAngle = 100;
            var fill = Light("MoonFill", new Vector3(0, 3, 0), LightType.Directional, .2f, new Color(.4f, .5f, .8f), lightRoot); fill.transform.rotation = Quaternion.Euler(45, -40, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.2f, .25f, .38f);
            Set(presentation, "config", config); Set(presentation, "bedsideLight", lamp); Set(presentation, "windowLight", window); Set(presentation, "fillLight", fill);
            Set(presentation, "vignette", vg.GetComponent<ScreenVignette>()); Set(presentation, "status", status); Set(presentation, "instructions", hints);
            Set(presentation, "biteFlash", Burst("BiteFlash", new Color(1f, .5f, .25f), .04f, 5));
            Set(presentation, "deathPuff", Burst("DeathPuff", new Color(.7f, .75f, .8f), .08f, 12));
            Set(game, "definition", definition); Set(game, "roundTimer", manager.GetComponent<RoundTimer>());
            Set(game, "tutorialScreen", canvas.GetComponent<TutorialScreen>()); Set(game, "hud", canvas.GetComponent<RoundHud>());
            Set(game, "config", config); Set(game, "mosquitoPrefab", prefab.GetComponent<MosquitoBody>());
            Set(game, "bed", bed); Set(game, "bedExit", exit); Set(game, "lampCenter", lampPoint); Set(game, "cameraController", camera);
            Set(game, "orbit", third != null ? third.GetComponent<CinemachineOrbitalFollow>() : null); Set(game, "presentation", presentation); Set(game, "inputTemplate", input);
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "BedInteract" : "LampInteract"); go.transform.SetParent(arena); go.transform.position = i == 0 ? exit.position + Vector3.up * .6f : lampPoint.position;
                go.AddComponent<NetworkObject>(); var trigger = go.AddComponent<SphereCollider>(); trigger.radius = .16f; trigger.isTrigger = true;
                var interactable = go.AddComponent<BedsideLamp>(); Set(interactable, "game", game); Set(interactable, "lampOnly", i == 1);
            }
            Set(game, "giantTutorial", RoleTutorial(true)); Set(game, "mosquitoTutorial", RoleTutorial(false));
            Set(manager.GetComponent<MinigameBootstrap>(), "minigame", game);
            ApplyArt(); WindowDust(); Register(definition);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Mosquitoes: room 14.4 × 11.52 × 3.6, 7 + 1 spawns, scene and catalog registered.");
        }
        private static MinigameDefinition RoleTutorial(bool giant)
        {
            var d = Asset<MinigameDefinition>(Settings + (giant ? "/MosquitoesGiantTutorial.asset" : "/MosquitoesFlightTutorial.asset"));
            Set(d, "displayName", giant ? "Комары · Великан" : "Комары · Комар"); Set(d, "category", (int)MinigameCategory.Asymmetric);
            Set(d, "objective", giant ? "Накопи 60 секунд сна. Сон растёт только лёжа в темноте. Два укуса за 4 секунды разбудят тебя. Уничтожь всех комаров, чтобы победить раньше." : "Не дай великану выспаться за 120 секунд. Два укуса за 4 секунды разбудят его. После укуса перерыв 5 секунд — нападайте вместе. После шлепка ты наблюдатель до конца раунда.");
            Set(d, "tutorialSteps", giant ? new[] { "E у кровати — лечь / встать", "E у тумбочки — лампа", "ЛКМ — шлепок. Слушай жужжание" } : new[] { "Лети к груди спящего великана", "Зажми ЛКМ на 0,5 секунды", "Улетай в темноту на 5 секунд" });
            Set(d, "tutorialQuickHints", giant ? new[] { "WASD — бег", "E — кровать / лампа", "ЛКМ — шлепок" } : new[] { "WASD + Space / Ctrl — полёт", "Shift — тихо", "ЛКМ — держать укус" });
            Set(d, "controlHints", giant ? new[] { "WASD — бег, Space — прыжок", "E — ближайшая кровать / лампа", "ЛКМ — удар, Ctrl — присесть", "В темноте комаров слышно, но не видно" } : new[] { "WASD — полёт, Space / Ctrl — вверх / вниз", "Shift — тихий полёт", "ЛКМ — держать у груди 0,5 секунды", "Высота полёта ограничена 2,16 м" });
            return d;
        }
        private static GameObject SetupPrefab(InputActionAsset input, Material teal)
        {
            var root = PrefabUtility.LoadPrefabContents(MosquitoesCoreSetup.PrefabPath);
            var body = root.GetComponent<MosquitoBody>() ?? root.AddComponent<MosquitoBody>();
            Set(body, "inputTemplate", input);
            var flightTransform = root.GetComponent<Igruha.Networking.ClientNetworkTransform>();
            flightTransform.UseUnreliableDeltas = true;
            flightTransform.PositionThreshold = .005f; flightTransform.RotAngleThreshold = .5f;
            flightTransform.SyncRotAngleX = false; flightTransform.SyncRotAngleZ = false;
            flightTransform.SyncScaleX = flightTransform.SyncScaleY = flightTransform.SyncScaleZ = false;
            var anchor = root.transform.Find("FlightCameraTarget");
            if (anchor == null)
            {
                var go = new GameObject("FlightCameraTarget"); go.transform.SetParent(root.transform, false); anchor = go.transform;
                // The unchanged shared rig reads this disabled probe's dimensions to frame a tiny body.
                var probe = go.AddComponent<CapsuleCollider>(); probe.radius = .03f; probe.height = .18f; probe.enabled = false;
            }
            Set(body, "cameraTarget", anchor);
            anchor.localPosition = Vector3.down * .25f;
            var old = root.transform.Find("Visual"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/Mosquito.fbx");
            GameObject visual;
            if (model != null) { visual = (GameObject)PrefabUtility.InstantiatePrefab(model); visual.transform.SetParent(root.transform, false); }
            else { visual = GameObject.CreatePrimitive(PrimitiveType.Sphere); visual.transform.SetParent(root.transform, false); visual.transform.localScale = Vector3.one * .18f; Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.GetComponent<Renderer>().sharedMaterial = teal; }
            visual.name = "Visual"; Set(body, "visual", visual.transform);
            RemapMaterials(visual);
            var buzz = root.GetComponent<AudioSource>(); if (buzz == null) buzz = root.AddComponent<AudioSource>(); buzz.playOnAwake = false; buzz.loop = true; buzz.spatialBlend = 1; buzz.minDistance = .4f; buzz.maxDistance = 12; buzz.rolloffMode = AudioRolloffMode.Linear;
            Set(body, "buzz", buzz);
            foreach (Transform t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = 12;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, MosquitoesCoreSetup.PrefabPath); PrefabUtility.UnloadPrefabContents(root); return saved;
        }
        public static void ApplyArt()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/Bedroom.fbx"); if (model == null) return;
            if (arena == null) arena = GameObject.Find("_Arena").transform;
            var old = arena.Find("BedroomArt"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model); instance.name = "BedroomArt"; instance.transform.SetParent(arena, false);
            RemapMaterials(instance);
            var glow = new List<Renderer>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                if (renderer.name == "LampShade" || renderer.name == "WarmBulb")
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var fabric = Material("LampFabric", "#D8CFC0"); fabric.EnableKeyword("_EMISSION");
                    // URP validates _EMISSION from this flag. These renderers are not static; no lightmaps are baked.
                    fabric.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    fabric.SetColor("_EmissionColor", new Color(.5f, .24f, .06f)); renderer.sharedMaterial = fabric;
                    glow.Add(renderer);
                }
            var presentation = Object.FindFirstObjectByType<MosquitoesPresentation>();
            var lightingData = new SerializedObject(presentation); var glows = lightingData.FindProperty("lampGlow"); glows.arraySize = glow.Count;
            for (int i = 0; i < glow.Count; i++) glows.GetArrayElementAtIndex(i).objectReferenceValue = glow[i]; lightingData.ApplyModifiedPropertiesWithoutUndo();
            var motion = instance.AddComponent<MosquitoRoomMotion>();
            var curtains = new List<Transform>(); foreach (Transform child in instance.GetComponentsInChildren<Transform>()) if (child.name.StartsWith("Curtain") && !child.name.StartsWith("CurtainRail")) curtains.Add(child);
            var data = new SerializedObject(motion); var array = data.FindProperty("curtains"); array.arraySize = curtains.Count;
            for (int i = 0; i < curtains.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = curtains[i]; data.ApplyModifiedPropertiesWithoutUndo();
            foreach (Transform child in arena) if (child != instance.transform) foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        }
        private static ParticleSystem Burst(string name, Color color, float size, short count)
        {
            var go = new GameObject(name); go.transform.SetParent(arena); var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = .6f; main.startLifetime = .45f;
            main.startSpeed = .22f; main.startSize = size; main.startColor = color; main.maxParticles = 24; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .045f;
            var overLife = ps.colorOverLifetime; overLife.enabled = true; var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) }, new[] { new GradientAlphaKey(.7f, 0), new GradientAlphaKey(0, 1) }); overLife.color = gradient;
            string path = Art + "/Materials/SoftParticles.mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0); material.renderQueue = 3000; AssetDatabase.CreateAsset(material, path); }
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); return ps;
        }
        private static void WindowDust()
        {
            var ps = Burst("WindowDust", new Color(.48f, .62f, .8f, .18f), .009f, 0);
            ps.transform.position = new Vector3(5.65f, 1.8f, 3.9f);
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.duration = 8;
            main.startLifetime = 7; main.startSpeed = .018f; main.maxParticles = 32;
            var emission = ps.emission; emission.SetBursts(new ParticleSystem.Burst[0]); emission.rateOverTime = 4;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.8f, 1.8f, 1.3f);
            var drift = ps.velocityOverLifetime; drift.enabled = true; drift.y = -.025f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        private static void RemapMaterials(GameObject root)
        {
            string[] names = { "Walnut", "Wall", "Ivory", "Amber", "Teal", "Blue", "Coral", "Gold", "Paper", "Black", "Skin", "Hair", "Wing", "Glass", "Leaf", "Cardboard" };
            string[] colors = { "4A3626", "2A3550", "D8CFC0", "F0B860", "285861", "53687F", "BB7159", "A68A49", "C9BDA2", "202634", "D59B71", "694639", "92ABB9", "6B8799", "426A53", "876D53" };
            var palette = new Dictionary<string, Material>();
            for (int i = 0; i < names.Length; i++)
            {
                var material = Material(names[i], "#" + colors[i]); material.SetFloat("_Cull", 0);
                if (names[i] == "Glass") { material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(.07f, .13f, .23f)); }
                palette.Add("MSQ_" + names[i], material);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) if (materials[i] != null && palette.TryGetValue(materials[i].name.Split('.')[0], out Material replacement)) materials[i] = replacement;
                renderer.sharedMaterials = materials;
            }
        }
        private static void Register(MinigameDefinition definition)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath); scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            var catalog = new SerializedObject(AssetDatabase.LoadAssetAtPath<MinigameCatalog>(Settings + "/MinigameCatalog.asset"));
            var games = catalog.FindProperty("games");
            for (int i = 0; i < games.arraySize; i++) if (games.GetArrayElementAtIndex(i).objectReferenceValue == definition) return;
            games.InsertArrayElementAtIndex(games.arraySize); games.GetArrayElementAtIndex(games.arraySize - 1).objectReferenceValue = definition; catalog.ApplyModifiedPropertiesWithoutUndo();
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        { var a = AssetDatabase.LoadAssetAtPath<T>(path); if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); } return a; }
        public static void Set(Object target, string field, object value)
        {
            var s = new SerializedObject(target); var p = s.FindProperty(field);
            if (p == null) throw new System.ArgumentException(target.name + ": " + field);
            if (value is string[] list) { p.arraySize = list.Length; for (int i = 0; i < list.Length; i++) p.GetArrayElementAtIndex(i).stringValue = list[i]; }
            else if (value is string text) p.stringValue = text;
            else if (value is int number) p.intValue = number;
            else if (value is float numberFloat) p.floatValue = numberFloat;
            else if (value is bool flag) p.boolValue = flag;
            else p.objectReferenceValue = value as Object;
            s.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
        }
        private static void Clear(Transform parent) { for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject); }
        private static Transform Point(string name, Vector3 p) { var go = new GameObject(name); go.transform.SetParent(arena); go.transform.position = p; return go.transform; }
        private static void Block(string name, Vector3 p, Vector3 size, Material material, string layer)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(arena); go.transform.position = p; go.transform.localScale = size; go.layer = LayerMask.NameToLayer(layer); go.GetComponent<Renderer>().sharedMaterial = material; }
        private static Material Material(string name, string hex)
        {
            string path = Art + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            ColorUtility.TryParseHtmlString(hex, out Color color); material.color = color; material.SetFloat("_Smoothness", .12f); EditorUtility.SetDirty(material); return material;
        }
        private static Light Light(string name, Vector3 p, LightType type, float intensity, Color color, Transform root)
        { var go = new GameObject(name); go.transform.SetParent(root); go.transform.position = p; var light = go.AddComponent<Light>(); light.type = type; light.color = color; light.intensity = intensity; light.shadows = LightShadows.Soft; return light; }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static TextMeshProUGUI Label(Transform parent, string name, Vector2 anchor, Vector2 size, float font)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.sizeDelta = size;
            var label = go.AddComponent<TextMeshProUGUI>(); label.fontSize = font; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.color = new Color(.92f, .88f, .78f); return label;
        }
    }
}
