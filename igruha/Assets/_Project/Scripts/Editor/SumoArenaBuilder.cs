using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Spawning;
using Igruha.Core.UI;
using Igruha.Minigames.SumoRing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class SumoArenaBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Minigames/SumoRing.unity";
        public const string Art = "Assets/_Project/Art/Minigames/SumoRing";
        public const string Settings = "Assets/_Project/Settings/Gameplay/Minigames/";
        [MenuItem("Igruha/Minigames/Build Sumo Ring blockout")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before rebuilding SumoRing.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MinigameTemplate.unity");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath, true);
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Art + "/Meshes"); Directory.CreateDirectory(Art + "/Materials"); AssetDatabase.Refresh();
            var config = Asset<SumoConfig>(Settings + "SumoConfig.asset");
            var definition = Asset<MinigameDefinition>(Settings + "SumoRing.asset");
            Set(definition, "displayName", "Ринг сумо"); Set(definition, "sceneName", "SumoRing");
            Set(definition, "minPlayers", 3); Set(definition, "maxPlayers", 8); Set(definition, "roundDuration", 0f);
            Set(definition, "objective", "Вытолкни соперников с ринга. Край осыпается — последний устоявший побеждает.");
            Strings(definition, "controlHints", new[] { "WASD — движение · мышь — камера", "ЛКМ — толчок · Space — прыжок", "Трещины предупреждают об обвале за 2 секунды.", "Упал с ринга — наблюдаешь. Одновременный вылет делит место." });
            Strings(definition, "tutorialSteps", new[] { "Подойди к сопернику и толкни его за край.", "Следи за трещинами: внешние кольца осыпаются.", "Займи центр и останься последним на ринге." });
            Strings(definition, "tutorialQuickHints", new[] { "ЛКМ — толчок", "Space — прыжок", "Край трещит — отойди к центру" });
            var arena = GameObject.Find("_Arena").transform; Clear(arena);
            Clear(GameObject.Find("_Traps").transform); Clear(GameObject.Find("_Pickups").transform); Clear(GameObject.Find("_Bounds").transform);
            var grey = Material("Clay", new Color(.46f, .46f, .44f));
            var sand = Material("Sand", new Color(.66f, .66f, .62f));
            var rope = Material("Rope", new Color(.95f, .93f, .83f));
            var crack = Material("Crack", new Color(.28f, .09f, .025f));
            Box(arena, "SafeSand", new Vector3(0, -.2f, 0), new Vector3(40, .4f, 40), sand);
            var centre = GameObject.CreatePrimitive(PrimitiveType.Cylinder); centre.name = "PermanentCentre";
            centre.transform.SetParent(arena, false); centre.transform.position = Vector3.up * config.Height * .5f;
            centre.transform.localScale = new Vector3(config.CentreRadius * 2, config.Height * .5f, config.CentreRadius * 2);
            centre.layer = LayerMask.NameToLayer("Ground"); centre.GetComponent<Renderer>().sharedMaterial = sand;
            Object.DestroyImmediate(centre.GetComponent<Collider>());
            centre.AddComponent<MeshCollider>().sharedMesh = centre.GetComponent<MeshFilter>().sharedMesh;
            var segmentList = new List<SumoRingSegment>(); var boundaryList = new List<GameObject>();
            for (int ring = 0; ring < config.RingCount; ring++)
            {
                Mesh mesh = SectorMesh(config.OuterRadius(ring) - config.RingWidth, config.OuterRadius(ring), config.Height, 360f / config.Sectors);
                string meshPath = Art + "/Meshes/Ring_" + ring + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (saved == null) { AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
                else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
                for (int sector = 0; sector < config.Sectors; sector++)
                {
                    var root = new GameObject($"Ring_{ring}_Sector_{sector}"); root.transform.SetParent(arena, false);
                    // Positive mathematical angle is negative Unity yaw.
                    root.transform.localRotation = Quaternion.Euler(0, -sector * 360f / config.Sectors, 0);
                    root.layer = LayerMask.NameToLayer("Ground");
                    var collider = root.AddComponent<MeshCollider>(); collider.sharedMesh = saved;
                    var visual = new GameObject("ClayVisual"); visual.transform.SetParent(root.transform, false);
                    visual.AddComponent<MeshFilter>().sharedMesh = saved;
                    visual.AddComponent<MeshRenderer>().sharedMaterials = new[] { sand, grey };
                    var cracks = new GameObject("WarningCracks"); cracks.transform.SetParent(visual.transform, false);
                    float start = config.OuterRadius(ring) - config.RingWidth;
                    for (int line = 0; line < 2; line++)
                    {
                        var points = new Vector3[7];
                        for (int p = 0; p < points.Length; p++)
                        {
                            float r = start + config.RingWidth * p / (points.Length - 1);
                            float a = (line == 0 ? 3f : 11f) * Mathf.Deg2Rad + Mathf.Sin(p * 2.8f + sector) * .02f;
                            points[p] = new Vector3(Mathf.Cos(a) * r, config.Height + .015f, Mathf.Sin(a) * r);
                        }
                        Line(cracks.transform, "Fracture", points, .045f, crack);
                    }
                    cracks.SetActive(false);
                    var component = root.AddComponent<SumoRingSegment>();
                    Set(component, "ring", ring); Set(component, "sector", sector); Set(component, "support", collider);
                    Set(component, "visual", visual.transform); Set(component, "cracks", cracks);
                    segmentList.Add(component);
                }
            }
            for (int ring = 0; ring <= config.RingCount; ring++)
            {
                float radius = ring < config.RingCount ? config.OuterRadius(ring) : config.CentreRadius;
                var points = new Vector3[193];
                for (int i = 0; i < points.Length; i++)
                { float a = i * Mathf.PI * 2 / (points.Length - 1); points[i] = new Vector3(Mathf.Cos(a) * (radius - .035f), config.Height + .035f, Mathf.Sin(a) * (radius - .035f)); }
                var edge = Line(arena, "Boundary_" + ring, points, .11f, rope);
                edge.gameObject.SetActive(ring == 0); boundaryList.Add(edge.gameObject);
            }
            var spawnRoot = GameObject.Find("_Spawns");
            foreach (var point in spawnRoot.GetComponentsInChildren<SpawnPoint>()) Object.DestroyImmediate(point.gameObject);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * .25f; var spawn = new GameObject("Spawn_" + i);
                spawn.transform.SetParent(spawnRoot.transform, false);
                spawn.transform.position = new Vector3(Mathf.Cos(a) * 5.4f, config.Height + .06f, Mathf.Sin(a) * 5.4f);
                spawn.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), 0, -Mathf.Sin(a)));
                spawn.AddComponent<SpawnPoint>();
            }
            Set(spawnRoot.GetComponent<PlayerSpawner>(), "debugPlayerCount", 4);
            var recovery = new GameObject("RecoveryCentre").transform; recovery.SetParent(arena, false); recovery.position = Vector3.up * (config.Height + .08f);
            var manager = GameObject.Find("MinigameManager"); Object.DestroyImmediate(manager.GetComponent<TemplateMinigame>());
            var game = manager.AddComponent<SumoMinigame>(); var ringArena = arena.gameObject.AddComponent<SumoArena>();
            manager.AddComponent<SumoNetwork>();
            Set(game, "config", config); Set(game, "definition", definition); Set(game, "recovery", recovery); Set(game, "arena", ringArena);
            Set(game, "roundTimer", manager.GetComponent<RoundTimer>());
            var ui = GameObject.Find("_UI"); var hud = ui.GetComponentInChildren<RoundHud>();
            Set(game, "hud", hud); Set(game, "tutorialScreen", ui.GetComponentInChildren<TutorialScreen>());
            Set(game, "timerPlate", new SerializedObject(hud).FindProperty("timerPlate").objectReferenceValue);
            Set(manager.GetComponent<MinigameBootstrap>(), "minigame", game);
            var cameraRoot = GameObject.Find("_Camera");
            Set(game, "gameCamera", cameraRoot.GetComponentInChildren<Camera>());
            var spectator = manager.AddComponent<SpectatorCamera>();
            Set(spectator, "cameraController", cameraRoot.GetComponentInChildren<MinigameCameraController>());
            Set(spectator, "cameraRig", cameraRoot.GetComponentInChildren<ThirdPersonCameraRig>()); Set(spectator, "hud", hud); Set(game, "spectator", spectator);
            Set(ringArena, "game", game); References(ringArena, "segments", segmentList.ToArray()); References(ringArena, "boundaries", boundaryList.ToArray());
            Register(definition); Save(); Debug.Log("SUMO: blockout built, 144 sectors + permanent centre, 8 spawns.");
        }
        private static Mesh SectorMesh(float inner, float outer, float height, float degrees)
        {
            var vertices = new List<Vector3>(); var top = new List<int>(); var sides = new List<int>();
            Vector3 P(float r, float a, float y) => new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> indices)
            { int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d); indices.AddRange(new[] { n, n+1, n+2, n, n+2, n+3 }); }
            for (int i = 0; i < 4; i++)
            {
                float a = degrees * Mathf.Deg2Rad * i / 4, b = degrees * Mathf.Deg2Rad * (i+1) / 4;
                Q(P(inner,a,height), P(inner,b,height), P(outer,b,height), P(outer,a,height), top);
                Q(P(outer,a,0), P(outer,a,height), P(outer,b,height), P(outer,b,0), sides);
                Q(P(inner,b,0), P(inner,b,height), P(inner,a,height), P(inner,a,0), sides);
                Q(P(inner,a,0), P(outer,a,0), P(outer,b,0), P(inner,b,0), sides);
            }
            float end = degrees * Mathf.Deg2Rad;
            Q(P(inner,0,0), P(inner,0,height), P(outer,0,height), P(outer,0,0), sides);
            Q(P(outer,end,0), P(outer,end,height), P(inner,end,height), P(inner,end,0), sides);
            var mesh = new Mesh { name = "SumoClayWedge", subMeshCount = 2 }; mesh.SetVertices(vertices);
            mesh.SetTriangles(top, 0); mesh.SetTriangles(sides, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static void RequireScene()
        { if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open SumoRing in edit mode first."); }
        public static void Save() { EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); AssetDatabase.SaveAssets(); }
        public static void Clear(Transform root) { while (root.childCount > 0) Object.DestroyImmediate(root.GetChild(0).gameObject); }
        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
            if (collide) go.layer = LayerMask.NameToLayer("Ground"); else Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        public static LineRenderer Line(Transform parent, string name, Vector3[] points, float width, Material material)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(parent, false); line.useWorldSpace = false;
            line.positionCount = points.Length; line.SetPositions(points); line.widthMultiplier = width; line.numCornerVertices = 3; line.numCapVertices = 3;
            line.sharedMaterial = material; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return line;
        }
        public static Material Material(string name, Color color)
        {
            string path = Art + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .18f); EditorUtility.SetDirty(material); return material;
        }
        public static T Asset<T>(string path) where T : ScriptableObject
        { var a = AssetDatabase.LoadAssetAtPath<T>(path); if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); } return a; }
        public static void Set(Object target, string name, object value)
        {
            var so = new SerializedObject(target); var p = so.FindProperty(name);
            if (p == null) throw new ArgumentException(target.name + " has no property " + name);
            if (value is Object obj) p.objectReferenceValue = obj;
            else if (value is int n) p.intValue = n;
            else if (value is float f) p.floatValue = f;
            else if (value is bool b) p.boolValue = b;
            else if (value is string s) p.stringValue = s;
            else if (value is Vector3 v) p.vector3Value = v;
            else if (value == null) p.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void References<T>(Object target, string name, T[] values) where T : Object
        { var so = new SerializedObject(target); var p = so.FindProperty(name); p.arraySize = values.Length; for (int i=0;i<values.Length;i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Strings(Object target, string name, string[] values)
        { var so = new SerializedObject(target); var p = so.FindProperty(name); p.arraySize = values.Length; for (int i=0;i<values.Length;i++) p.GetArrayElementAtIndex(i).stringValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Register(MinigameDefinition definition)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            var catalog = AssetDatabase.LoadAssetAtPath<MinigameCatalog>(Settings + "MinigameCatalog.asset"); var so = new SerializedObject(catalog); var games = so.FindProperty("games");
            for (int i = 0; i < games.arraySize; i++) if (games.GetArrayElementAtIndex(i).objectReferenceValue == definition) return;
            games.arraySize++; games.GetArrayElementAtIndex(games.arraySize - 1).objectReferenceValue = definition; so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
