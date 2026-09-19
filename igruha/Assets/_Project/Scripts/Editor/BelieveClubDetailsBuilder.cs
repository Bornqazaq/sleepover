using System;
using System.Linq;
using Igruha.Minigames.BelieveOrNot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Final side-wall detail layer. Rebuilds only _Props; accepted art is untouched.</summary>
    public static class BelieveClubDetailsBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/BelieveOrNot.unity";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/BelieveOrNotConfig.asset";
        private const string RootName = "_Props";
        private static readonly string[] SolidNames = { "East_Roulette", "West_Bureau" };
        private static readonly string[] ItemNames = {
            "East_Roulette", "East_BankerLamp", "West_Bureau", "West_BureauChair",
            "West_BankerLamp", "West_DeskStationery", "West_Globe", "West_FloorLamp",
            "West_Painting_Portrait", "West_Painting_Landscape", "East_Painting_StillLife",
            "West_Painting_Hunt", "East_BarService", "West_LoungeService"
        };

        [MenuItem("Igruha/Верю не верю/Собственный клуб — реквизит и детали")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before applying details.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open BelieveOrNot before applying details.");
            var arena = GameObject.Find("_Arena");
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            if (arena == null || config == null) throw new InvalidOperationException("Arena or config missing.");
            Build(arena.transform, config);
            BelievePrivateClubBuilder.Audit();
            BelieveClubFurnitureBuilder.Audit();
            Audit();
            EditorSceneManager.MarkSceneDirty(arena.scene);
            EditorSceneManager.SaveScene(arena.scene);
            AssetDatabase.SaveAssets();
        }

        internal static void Build(Transform arena, BelieveOrNotConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            BelieveClubDetailsAssets.Prepare();
            var old = arena.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(arena, false);
            float side = config.HallWidth * .5f;
            // Side recesses remain outside the shoulder-camera compositions.
            Place(root, "East_Roulette", "RouletteTable", new Vector3(side - 1.59f, 0, -5.73f), 0);
            Lamp(Place(root, "East_BankerLamp", "BankerLamp", new Vector3(side - 1.79f, .92f, -6.10f), 270));
            Place(root, "West_Bureau", "Bureau", new Vector3(-side + .99f, 0, 4.97f), 90);
            Place(root, "West_BureauChair", "BureauChair", new Vector3(-side + 1.89f, 0, 4.97f), 270);
            Lamp(Place(root, "West_BankerLamp", "BankerLamp", new Vector3(-side + .94f, .805f, 5.42f), 90));
            Place(root, "West_DeskStationery", "DeskStationery", new Vector3(-side + 1.11f, .806f, 4.92f), 90);
            Place(root, "West_Globe", "Globe", new Vector3(-side + 1.14f, 0, 6.10f), 90);
            var floorLamp = Place(root, "West_FloorLamp", "FloorLamp", new Vector3(-side + 2.04f, 0, 6.10f), 0);
            var floorLight = new GameObject("FloorLampWarmPoint").AddComponent<Light>();
            floorLight.transform.SetParent(floorLamp, false);
            floorLight.transform.localPosition = new Vector3(0, 1.53f, 0);
            floorLight.type = LightType.Point;
            floorLight.color = new Color(1f, .82f, .60f);
            floorLight.intensity = 2f;
            floorLight.range = 4.5f;
            floorLight.shadows = LightShadows.None;
            Place(root, "West_Painting_Portrait", "Painting_Portrait", new Vector3(-side + .395f, 2.48f, -.65f), 90);
            Place(root, "West_Painting_Landscape", "Painting_Landscape", new Vector3(-side + .395f, 2.48f, 2.8f), 90);
            Place(root, "East_Painting_StillLife", "Painting_StillLife", new Vector3(side - .395f, 2.45f, -5.73f), 270);
            Place(root, "West_Painting_Hunt", "Painting_Hunt", new Vector3(-side + .395f, 2.48f, 4.97f), 90);
            Place(root, "East_BarService", "BarService", new Vector3(side - 1.68f, 1.131f, -.7f), 270);
            Place(root, "West_LoungeService", "LoungeService", new Vector3(-side + 2.23f, .502f, 1.1f), 90);
        }

        private static Transform Place(Transform root, string name, string model, Vector3 position, float yaw)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.Euler(0, yaw, 0);
            var go = BelieveClubDetailsAssets.Model(holder, model);
            if (!SolidNames.Contains(name)) return holder;
            var bounds = new Bounds();
            bool first = true;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = holder.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            var collider = holder.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            return holder;
        }

        private static void Lamp(Transform holder)
        {
            var go = new GameObject("BankerWarmPoint");
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(0, .325f, .035f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .82f, .55f);
            light.intensity = 1.5f;
            light.range = 2.7f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        [MenuItem("Igruha/Верю не верю/Проверить реквизит клуба")]
        public static void Audit()
        {
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            var arena = GameObject.Find("_Arena");
            var root = arena == null ? null : arena.transform.Find(RootName);
            if (root == null || config == null) throw new InvalidOperationException("Details or config missing.");
            if (root.parent != arena.transform || root.childCount != ItemNames.Length ||
                ItemNames.Any(n => root.Find(n) == null))
                throw new InvalidOperationException("Details must be a complete, separate _Props group.");
            if (root.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.layer != 0))
                throw new InvalidOperationException("All details must use Default.");
            float nearest = float.MaxValue;
            foreach (Transform item in root)
            {
                bool westWall = item.name == "West_Painting_Portrait" || item.name == "West_Painting_Landscape";
                bool surface = item.name == "East_BarService" || item.name == "West_LoungeService";
                foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    var c = b.center - arena.transform.position;
                    float x = Mathf.Max(0, Mathf.Abs(c.x) - b.extents.x);
                    float z = Mathf.Max(0, Mathf.Abs(c.z) - b.extents.z);
                    nearest = Mathf.Min(nearest, Mathf.Sqrt(x * x + z * z));
                    if (Mathf.Abs(c.x) <= Mathf.Abs(c.z) || Mathf.Abs(c.z) > 6.5f)
                        throw new InvalidOperationException("Details leave the East / West wedge: " + item.name);
                    if (!westWall && !surface && (c.x > 0
                        ? c.z - b.extents.z < -6.5f || c.z + b.extents.z > -5f
                        : c.z - b.extents.z < 4.2f || c.z + b.extents.z > 6.5f))
                        throw new InvalidOperationException("Details leave the camera-safe recess: " + item.name);
                    if (westWall && (c.x > -config.HallWidth * .5f + .6f || b.min.y < 1.8f))
                        throw new InvalidOperationException("Central paintings must stay on the wall above furniture.");
                    if (r.sharedMaterials.Any(m => m == null || m.shader == null))
                        throw new InvalidOperationException("Missing detail material: " + r.name);
                }
                var colliders = item.GetComponentsInChildren<Collider>(true);
                bool solid = SolidNames.Contains(item.name);
                if (colliders.Length != (solid ? 1 : 0) || (solid &&
                    (!(colliders[0] is BoxCollider) || colliders[0].transform != item ||
                     colliders[0].isTrigger || !colliders[0].enabled)))
                    throw new InvalidOperationException("Detail collider policy violated: " + item.name);
                if (item.name.Contains("Painting"))
                    foreach (var curtain in arena.transform.Find("_Hall").GetComponentsInChildren<Renderer>(true)
                        .Where(r => r.name.Contains("Curtain")))
                        if (item.GetComponentInChildren<Renderer>().bounds.Intersects(curtain.bounds))
                            throw new InvalidOperationException("Painting intersects a curtain: " + item.name);
            }
            if (nearest < config.SpectatorZoneRadius)
                throw new InvalidOperationException("Details cross spectator circle: " + nearest);
            var lights = root.GetComponentsInChildren<Light>(true);
            if (lights.Length != 3 || lights.Count(l => l.transform.parent.name.EndsWith("BankerLamp")) != 2 ||
                lights.Count(l => l.transform.parent.name == "West_FloorLamp") != 1 ||
                lights.Any(l => l.type != LightType.Point || l.shadows != LightShadows.None || !l.enabled))
                throw new InvalidOperationException("Two banker lamps and one warm floor lamp are required.");
            foreach (var light in lights)
            {
                bool banker = light.transform.parent.name.EndsWith("BankerLamp");
                if (light.intensity != (banker ? 1.5f : 2f) || light.range != (banker ? 2.7f : 4.5f))
                    throw new InvalidOperationException("Unexpected practical lamp settings: " + light.name);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new InvalidOperationException("Missing detail script: " + t.name);
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh == null) throw new InvalidOperationException("Missing detail mesh: " + f.name);
            Debug.Log($"IGR-565: details valid; 14 items; 2 BoxColliders; 2 banker lights + floor lamp; all Default; nearest renderer {nearest:F2} m >= spectator radius {config.SpectatorZoneRadius:F2} m; missing refs 0.");
        }
    }
}
