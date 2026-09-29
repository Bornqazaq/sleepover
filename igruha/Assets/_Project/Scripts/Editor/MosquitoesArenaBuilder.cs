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
            Block("Floor", new Vector3(0, -.15f, 0), new Vector3(7.2f, .3f, 6.4f), wood, "Ground");
            Set(GameObject.Find("Floor").AddComponent<SurfaceAudio>(), "kind", (int)SurfaceKind.Wood);
            Block("CarpetCollider", new Vector3(-.4f, .018f, -1.12f), new Vector3(5.65f, .035f, 3.65f), teal, "Ground");
            Set(GameObject.Find("CarpetCollider").AddComponent<SurfaceAudio>(), "kind", (int)SurfaceKind.Carpet);
            Block("Ceiling", new Vector3(0, 3.55f, 0), new Vector3(7.2f, .3f, 6.4f), wall, "Ground");
            Block("WestWall", new Vector3(-3.75f, 1.7f, 0), new Vector3(.3f, 3.4f, 6.4f), wall, "Ground");
            // Four collision pieces around the open aperture, matching the visible wall.
            Block("EastWallLower", new Vector3(3.75f, .51f, 0), new Vector3(.3f, 1.02f, 6.4f), wall, "Ground");
            Block("EastWallUpper", new Vector3(3.75f, 3.22f, 0), new Vector3(.3f, .36f, 6.4f), wall, "Ground");
            Block("EastWallSouth", new Vector3(3.75f, 2.03f, -1.64f), new Vector3(.3f, 2.02f, 3.12f), wall, "Ground");
            Block("EastWallNorth", new Vector3(3.75f, 2.03f, 2.58f), new Vector3(.3f, 2.02f, 1.24f), wall, "Ground");
            Block("NorthWall", new Vector3(0, 1.7f, 3.35f), new Vector3(7.2f, 3.4f, .3f), wall, "Ground");
            Block("SouthWall", new Vector3(0, 1.7f, -3.35f), new Vector3(7.2f, 3.4f, .3f), wall, "Ground");
            Block("BedCollider", new Vector3(-1.6f, .3325f, -1.1f), new Vector3(3.12f, .665f, 1.95f), ivory, "Cover");
            Block("HeadboardCollider", new Vector3(-3.17f, .87f, -1.1f), new Vector3(.16f, 1.48f, 2.05f), wood, "Cover");
            Block("NightstandCollider", new Vector3(-2.85f, .45f, -2.63f), new Vector3(1.02f, .9f, .93f), wood, "Cover");
            Block("WardrobeCollider", new Vector3(-1.93f, 1.35f, 2.61f), new Vector3(2.06f, 2.62f, 1.07f), wood, "Cover");
            Block("WardrobeDoorCollider", new Vector3(-.79f, 1.36f, 1.73f), new Vector3(.91f, 2.43f, .11f), wood, "Cover");
            GameObject.Find("WardrobeDoorCollider").transform.rotation = Quaternion.Euler(0, 62, 0);
            Block("DeskCollider", new Vector3(1.52f, .84f, 2.62f), new Vector3(2.45f, .12f, 1f), wood, "Cover");
            Block("DeskLegL", new Vector3(.43f, .41f, 2.62f), new Vector3(.13f, .82f, .84f), wood, "Cover");
            Block("DeskCabinetCollider", new Vector3(2.18f, .46f, 2.65f), new Vector3(.73f, .75f, .81f), wood, "Cover");
            Block("ChairCollider", new Vector3(1.41f, .76f, 1.57f), new Vector3(.82f, 1.5f, .8f), wood, "Cover");
            for (int i = 0; i < 2; i++) Block("ShelfCollider" + i, new Vector3(1.45f, 1.85f + i * .6f, 2.95f), new Vector3(2.5f, .075f, .43f), wood, "Cover");
            Block("BoxStackCollider", new Vector3(2.90f, .67f, -1.05f), new Vector3(.70f, 1.33f, .70f), wood, "Cover");
            Block("SmallBoxCollider", new Vector3(2.94f, .28f, -1.74f), new Vector3(.56f, .56f, .56f), wood, "Cover");
            Block("BackpackCollider", new Vector3(.57f, .45f, -2.12f), new Vector3(.60f, .90f, .50f), teal, "Cover");
            var bed = Point("BedPose", new Vector3(-2.40f, .68f, -1.1f));
            bed.rotation = Quaternion.Euler(0, 90, 0);
            var exit = Point("BedExit", new Vector3(-1.70f, .05f, .45f));
            exit.rotation = Quaternion.Euler(0, 180, 0);
            var lampPoint = Point("LampCenter", new Vector3(-3.02f, 1.48f, -2.47f));
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
            for (int i = 0; i < 7; i++) { var p = new GameObject("MosquitoSpawn" + i).AddComponent<SpawnPoint>(); p.transform.SetParent(spawns); p.transform.position = MosquitoesMinigame.RoomSpawn(i); p.transform.rotation = Quaternion.Euler(0, 180, 0); }
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
            var lamp = Light("BedsideLamp", lampPoint.position, LightType.Point, 5f, new Color(1, .64f, .28f), lightRoot); lamp.range = 5.5f;
            var window = Light("MoonWindow", new Vector3(3.2f, 2.5f, 1.0f), LightType.Spot, 4f, new Color(.37f, .55f, 1), lightRoot);
            window.transform.position = new Vector3(2.95f, 2.55f, 1f); window.transform.rotation = Quaternion.Euler(22, -105, 0); window.range = 8; window.spotAngle = 110;
            var fill = Light("MoonFill", new Vector3(0, 3, 0), LightType.Directional, .12f, new Color(.4f, .5f, .8f), lightRoot); fill.transform.rotation = Quaternion.Euler(40, -30, 0); fill.shadows = LightShadows.None;
            var warmPool = Light("LampWallPool", lampPoint.position + new Vector3(.08f, .12f, .1f), LightType.Spot, 24f, new Color(1, .58f, .22f), lightRoot);
            warmPool.transform.LookAt(new Vector3(-1.6f, 1.5f, 2.6f)); warmPool.range = 7f; warmPool.spotAngle = 42; warmPool.innerSpotAngle = 25;
            Set(presentation, "lampWallPool", warmPool);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.10f, .13f, .20f);
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
            ApplyArt(); ApplyExterior(game); MosquitoSurfaceBuilder.Apply(); WindowDust(); Register(definition);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            Debug.Log("Mosquitoes: room 7.2 × 6.4 × 3.4, 7 + 1 spawns, scene and catalog registered.");
        }
        private static MinigameDefinition RoleTutorial(bool giant)
        {
            var d = Asset<MinigameDefinition>(Settings + (giant ? "/MosquitoesGiantTutorial.asset" : "/MosquitoesFlightTutorial.asset"));
            Set(d, "displayName", giant ? "Комары · Великан" : "Комары · Комар"); Set(d, "category", (int)MinigameCategory.Asymmetric);
            Set(d, "objective", giant ? "Накопи 60 секунд сна. Сон растёт только лёжа в темноте. Два укуса за 4 секунды разбудят тебя. Уничтожь всех комаров, чтобы победить раньше." : "Не дай великану выспаться за 120 секунд. Два укуса за 4 секунды разбудят его. После укуса перерыв 5 секунд — нападайте вместе. После шлепка ты наблюдатель до конца раунда.");
            Set(d, "tutorialSteps", giant ? new[] { "E у кровати — лечь / встать", "E у тумбочки — лампа", "ЛКМ — шлепок. Слушай жужжание" } : new[] { $"За {MosquitoWindowEntry.Deadline:0} секунд влети в окно — иначе стрекоза съест тебя", "Зажми ЛКМ на 0,5 секунды", "Улетай в темноту на 5 секунд" });
            Set(d, "tutorialQuickHints", giant ? new[] { "WASD — бег", "E — кровать / лампа", "ЛКМ — шлепок" } : new[] { "WASD + Space / Ctrl — полёт", "Shift — тихо", "ЛКМ — держать укус" });
            Set(d, "controlHints", giant ? new[] { "WASD — бег, Space — прыжок", "E — ближайшая кровать / лампа", "ЛКМ — удар, Ctrl — присесть", "В темноте комаров слышно, но не видно" } : new[] { "WASD — полёт, Space / Ctrl — вверх / вниз", "Shift — тихий полёт", "ЛКМ — держать у груди 0,5 секунды", "В окно можно влететь только один раз. Назад пути нет" });
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
            Set(body, "visual", null);
            var variants = MosquitoCharacterBuilder.Build();
            var serialized = new SerializedObject(body); var characters = serialized.FindProperty("characterVisuals");
            characters.arraySize = variants.Length;
            for (int i = 0; i < variants.Length; i++) characters.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
        // Reimports presentation only; preserves the running rules, colliders and scene references.
        public static void RefreshPresentation()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before updating art");
            // Auto Refresh can be disabled while an external Blender export is running.
            // Read the new mesh/material slots before instantiating the model.
            AssetDatabase.ImportAsset(Art + "/Models/Bedroom.fbx", ImportAssetOptions.ForceUpdate);
            arena = GameObject.Find("_Arena").transform;
            ApplyArt(); ApplyExterior(Object.FindFirstObjectByType<MosquitoesMinigame>());
            MosquitoSurfaceBuilder.Apply();
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        [System.Serializable] private class ExteriorLightList { public ExteriorLightEntry[] lights; }
        [System.Serializable] private class ExteriorLightEntry { public float[] position; public float intensity; public float range; public float[] color; }
        private static void ApplyExterior(MosquitoesMinigame game)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/ResidentialExterior.fbx");
            if (asset == null) throw new System.InvalidOperationException("Residential exterior is missing");
            var previous=arena.Find("ResidentialExterior_Floor10of22");
            if(previous!=null) Object.DestroyImmediate(previous.gameObject);
            var exterior = (GameObject)PrefabUtility.InstantiatePrefab(asset); exterior.name = "ResidentialExterior_Floor10of22";
            exterior.transform.SetParent(arena, false); RemapMaterials(exterior);
            foreach (var r in exterior.GetComponentsInChildren<Renderer>()) { r.gameObject.layer = 29; r.renderingLayerMask = 1; }
            var moon = Light("CourtyardMoon", new Vector3(20,30,0), LightType.Directional, .50f, new Color(.40f,.54f,.85f), exterior.transform);
            moon.transform.rotation = Quaternion.Euler(35,-60,0); moon.cullingMask = 1 << 29; moon.renderingLayerMask = 1; moon.shadows = LightShadows.None;
            var layout=JsonUtility.FromJson<ExteriorLightList>(System.IO.File.ReadAllText(Art+"/Models/ExteriorLights.json"));
            int lampIndex=0;
            foreach(var item in layout.lights)
            {
                var pool=Light("PracticalLamp_"+lampIndex++,new Vector3(item.position[0],item.position[1],item.position[2]),LightType.Point,item.intensity,new Color(item.color[0],item.color[1],item.color[2]),exterior.transform);
                pool.range=item.range;pool.cullingMask=1<<29;pool.renderingLayerMask=1;pool.shadows=LightShadows.None;
            }
            var dragonfly=AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/Dragonfly.fbx");
            var temp=(GameObject)PrefabUtility.InstantiatePrefab(dragonfly); RemapMaterials(temp);
            string path="Assets/_Project/Prefabs/Minigames/Mosquitoes/Dragonfly.prefab";
            var prefab=PrefabUtility.SaveAsPrefabAsset(temp,path); Object.DestroyImmediate(temp); Set(game,"dragonflyVisual",prefab);
            string skyPath=Art+"/Materials/NightSky.mat";
            var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if(sky==null){sky=new Material(Shader.Find("Igruha/Mosquitoes/NightSky"));AssetDatabase.CreateAsset(sky,skyPath);}
            RenderSettings.skybox=sky;
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared; RenderSettings.fogDensity=.006f; RenderSettings.fogColor=new Color(.025f,.037f,.070f);
            var main=Camera.main;if(main!=null){main.farClipPlane=Mathf.Max(main.farClipPlane,250);main.backgroundColor=new Color(.014f,.022f,.052f);}
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
            ps.transform.position = new Vector3(2.85f, 1.8f, 1f);
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.duration = 8;
            main.startLifetime = 7; main.startSpeed = .018f; main.maxParticles = 32;
            var emission = ps.emission; emission.SetBursts(new ParticleSystem.Burst[0]); emission.rateOverTime = 4;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.8f, 1.8f, 1.3f);
            var drift = ps.velocityOverLifetime; drift.enabled = true; drift.y = -.025f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public static void RemapMaterials(GameObject root)
        {
            string[] names = { "Walnut", "Wall", "Ivory", "Amber", "Teal", "Blue", "Coral", "Gold", "Paper", "Black", "Skin", "Hair", "Wing", "Glass", "Leaf", "Cardboard", "Concrete", "Asphalt", "Soil", "WindowWarm", "WindowCool", "WindowDark", "ClearGlass", "WaterTint", "Facade", "Stone", "Paving", "PavingJoint", "Brick", "Sage", "Oak", "Metal", "Bark", "Foliage", "FoliageLight", "FoliageDark", "Grass", "Lantern", "CarBlue", "CarRed", "CarCream", "CarGreen", "CarGlass", "Chrome", "Rubber", "Headlamp", "TailLamp", "RoadPaint" };
            string[] colors = { "765943", "414B68", "D8CFC0", "F0B860", "285861", "53687F", "BB7159", "A68A49", "C9BDA2", "202634", "D59B71", "694639", "92ABB9", "6B8799", "426A53", "876D53", "626E89", "202A3D", "293B3E", "FFD99C", "86BCD9", "182739", "C5E1ED", "6495AA", "647185", "9C9C96", "6D6B69", "52555A", "856757", "627675", "94704B", "253039", "645343", "46684D", "66835C", "2D5143", "3B5444", "FFE1A7", "526F89", "92534B", "BEB6A4", "546F66", "243B4C", "9DA9AF", "1D252A", "B9CDCF", "A5342E", "AAAFAC" };
            var palette = new Dictionary<string, Material>();
            for (int i = 0; i < names.Length; i++)
            {
                var material = Material(names[i], "#" + colors[i]); material.SetFloat("_Cull", 0);
                if (names[i] == "WindowWarm" || names[i] == "WindowCool")
                { material.EnableKeyword("_EMISSION"); material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive; material.SetColor("_EmissionColor",material.color * (names[i]=="WindowWarm" ? .65f : .35f)); }
                if(names[i]=="Lantern") { material.EnableKeyword("_EMISSION"); material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive; material.SetColor("_EmissionColor", new Color(3.8f,2.2f,.85f)); }
                if(names[i].StartsWith("Car") || names[i]=="Chrome") { material.SetFloat("_Smoothness",names[i]=="CarGlass"?.8f:.55f); material.SetFloat("_Metallic",names[i]=="Chrome"?.8f:.25f); }
                if (names[i] == "Glass") { material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(.18f, .30f, .50f)); }
                if (names[i] == "ClearGlass" || names[i] == "WaterTint")
                {
                    material.SetFloat("_Surface",1); material.SetFloat("_SrcBlend",5); material.SetFloat("_DstBlend",10); material.SetFloat("_ZWrite",0);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue=3000;
                    var tint=material.color;tint.a=names[i]=="ClearGlass"?.18f:.34f;material.color=tint;material.SetFloat("_Smoothness",.92f);
                }
                if (names[i] == "Wing")
                {
                    material.shader = Shader.Find("Universal Render Pipeline/Unlit"); material.SetFloat("_Cull", 0);
                    material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10);
                    material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = 3000; material.color = new Color(.65f, .78f, .86f, .25f);
                    material.SetFloat("_Smoothness", .25f); material.SetFloat("_SpecularHighlights", 0); material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }
                palette.Add("MSQ_" + names[i], material);
            }
            string[] architectureNames = { "FacadeCream", "FacadeSand", "FacadeGraphite", "Clinker", "Roofing", "WindowReflection", "PlinthBasalt" };
            string[] architectureColors = { "C6C0AE", "AC9780", "4C5159", "92705B", "49555B", "557483", "5A6268" };
            for (int i = 0; i < architectureNames.Length; i++)
            {
                var material = Material(architectureNames[i], "#" + architectureColors[i]);
                material.SetFloat("_Cull", 2);
                material.SetFloat("_Smoothness", architectureNames[i] == "WindowReflection" ? .55f : .16f);
                palette.Add("MSQ_" + architectureNames[i], material);
            }
            string[] roomNames = { "GarmentTeal", "GarmentSand", "GarmentCharcoal", "Denim", "Canvas", "ShoeLeather", "Sole", "Stitch", "BrushedMetal", "Ceramic", "Upholstery", "NaturalOak", "Linen" };
            string[] roomColors = { "48756F", "C6B79D", "424A51", "596F8B", "B78262", "CAD0C6", "D3CDBC", "B5AEA1", "909A9A", "D0B498", "5A6972", "987957", "D2C9B8" };
            for (int i = 0; i < roomNames.Length; i++)
            {
                var material = Material(roomNames[i], "#" + roomColors[i]);
                material.SetFloat("_Cull", 0);
                material.SetFloat("_Smoothness", roomNames[i] == "BrushedMetal" ? .48f : roomNames[i] == "Ceramic" ? .45f : .12f);
                material.SetFloat("_Metallic", roomNames[i] == "BrushedMetal" ? .7f : 0);
                palette.Add("MSQ_" + roomNames[i], material);
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
