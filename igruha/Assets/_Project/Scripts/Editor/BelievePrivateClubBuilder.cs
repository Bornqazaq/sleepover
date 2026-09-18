using System;
using System.Linq;
using Igruha.Minigames.BelieveOrNot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Art-only, repeatable shell pass. Never rebuilds the table or gameplay anchors.</summary>
    public static class BelievePrivateClubBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/BelieveOrNot.unity";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/BelieveOrNotConfig.asset";
        private const float ModuleWidth = 2f;
        private const float ModuleHeight = 4.12f;
        private const float ClearRadius = 7.2f;
        internal const float LampHeight = 3.6f;
        internal const float LampRange = 5.1f;

        [MenuItem("Igruha/Верю не верю/Собственный клуб — оболочка и свет")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before applying club art.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open BelieveOrNot first; this pass edits only that scene.");
            var arena = GameObject.Find("_Arena");
            var before = ProtectedGeometry(arena.transform);
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            Build(arena.transform, config);
            if (before != ProtectedGeometry(arena.transform))
                throw new InvalidOperationException("Club art changed protected gameplay geometry.");
            Audit();
            EditorSceneManager.MarkSceneDirty(arena.scene);
            EditorSceneManager.SaveScene(arena.scene);
            AssetDatabase.SaveAssets();
        }

        internal static void Build(Transform arena, BelieveOrNotConfig config)
        {
            BelievePrivateClubAssets.Prepare();
            foreach (string name in new[] { "_Hall", "Carpet", "LampShade", "BeamDust" })
            {
                var old = arena.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var hall = new GameObject("_Hall").transform;
            hall.SetParent(arena, false);
            var perimeter = Group(hall, "Perimeter");
            // Skin follows the existing physical hall. Configuration uses character widths.
            float width = config.HallWidth - .4f;
            float depth = config.HallDepth - .4f;
            float height = config.CeilingHeight - .2f;
            BuildWall(perimeter, "North", new Vector3(0, 0, depth * .5f), 180, width, height);
            BuildWall(perimeter, "South", new Vector3(0, 0, -depth * .5f), 0, width, height);
            BuildWall(perimeter, "West", new Vector3(-width * .5f, 0, 0), 90, depth, height);
            BuildWall(perimeter, "East", new Vector3(width * .5f, 0, 0), 270, depth, height);
            var ceiling = Group(hall, "CeilingModules");
            var floor = Group(hall, "WovenFloor");
            int nx = Mathf.CeilToInt(width / ModuleWidth);
            int nz = Mathf.CeilToInt(depth / ModuleWidth);
            float sx = width / nx, sz = depth / nz;
            for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
            {
                var point = new Vector3(-width * .5f + (x + .5f) * sx, 0, -depth * .5f + (z + .5f) * sz);
                BelievePrivateClubAssets.Model(floor, "CarpetTile_2m", point + Vector3.up * .004f, 0,
                    new Vector3(sx / ModuleWidth, 1, sz / ModuleWidth));
                BelievePrivateClubAssets.Model(ceiling, "CeilingCoffer_2m", point + Vector3.up * height, 0,
                    new Vector3(sx / ModuleWidth, 1, sz / ModuleWidth));
            }
            BelievePrivateClubAssets.Model(hall, "RoundRug", new Vector3(0, .008f, 0));
            // Structural colliders retain their exact transforms and layers; only renderers are covered.
            foreach (string name in new[] { "Floor", "Ceiling", "Wall_North", "Wall_South", "Wall_West", "Wall_East" })
            {
                var t = arena.Find(name);
                if (t != null && t.TryGetComponent<Renderer>(out var r)) r.enabled = false;
            }
            var pendant = Group(hall, "PendantAndAir");
            BelievePrivateClubAssets.Model(pendant, "Pendant", new Vector3(0, LampHeight + .055f, 0));
            float chainStart = LampHeight + .48f;
            int links = Mathf.CeilToInt((height - chainStart) / .069f);
            for (int i = 0; i < links; i++)
                BelievePrivateClubAssets.Model(pendant, "ChainLink", new Vector3(0, chainStart + i * .069f, 0), i % 2 * 90);
            BuildAir(pendant);
            ConfigureLighting(arena);
        }

        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }

        private static void BuildWall(Transform parent, string name, Vector3 position, float yaw, float width, float height)
        {
            var wall = Group(parent, name);
            wall.localPosition = position; wall.localRotation = Quaternion.Euler(0, yaw, 0);
            int count = Mathf.CeilToInt(width / ModuleWidth);
            float span = width / count;
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(-width * .5f + (i + .5f) * span, 0, 0);
                var scale = new Vector3(span / ModuleWidth, height / ModuleHeight, 1);
                BelievePrivateClubAssets.Model(wall, "WallPanel_2m", p, 0, scale);
                BelievePrivateClubAssets.Model(wall, "Skirting_2m", p, 0, new Vector3(scale.x, 1, 1));
                BelievePrivateClubAssets.Model(wall, "Cornice_2m", p + Vector3.up * (height - .2f), 0, new Vector3(scale.x, 1, 1));
                // Two pairs per end wall, one per side wall: blue timber remains dominant.
                bool curtain = name == "North" || name == "South" ? i == 2 || i == count - 3 : i == count - 2;
                if (curtain) BelievePrivateClubAssets.Model(wall, "Curtain_2m", p + Vector3.forward * .12f, 0, scale);
            }
        }

        internal static void ConfigureLighting(Transform arena)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.045f, .055f, .075f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .02f;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light.type == LightType.Directional) light.enabled = false;
            var lamp = arena.Find("TableLamp");
            if (lamp == null) { lamp = Group(arena, "TableLamp"); lamp.gameObject.AddComponent<Light>(); }
            lamp.localPosition = new Vector3(0, LampHeight, 0);
            lamp.localRotation = Quaternion.Euler(90, 0, 0);
            var spot = lamp.GetComponent<Light>();
            spot.enabled = true; spot.type = LightType.Spot;
            spot.spotAngle = 110; spot.innerSpotAngle = 100;
            spot.range = LampRange; spot.intensity = 22f;
            spot.color = Mathf.CorrelatedColorTemperatureToRGB(3000).gamma;
            spot.useColorTemperature = false;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = .90f; spot.shadowBias = .025f; spot.shadowNormalBias = .12f;
            spot.renderMode = LightRenderMode.ForcePixel;
            var hall = arena.Find("_Hall");
            var previous = hall.Find("DistantWarmPoints");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var accents = Group(hall, "DistantWarmPoints");
            float side = arena.Find("Wall_East").position.x - .65f;
            Point(accents, "EastShelfGlow", new Vector3(side, 2.7f, -3.0f), .65f, 2.3f);
            Point(accents, "EastShelfGlow", new Vector3(side, 2.7f, 3.0f), .65f, 2.3f);
            Point(accents, "WestSilhouette", new Vector3(-side, 2.1f, 1.0f), .5f, 2.4f);
        }

        private static void Point(Transform parent, string name, Vector3 position, float intensity, float range)
        {
            var t = Group(parent, name); t.localPosition = position;
            var l = t.gameObject.AddComponent<Light>(); l.type = LightType.Point;
            l.color = new Color(1, .65f, .32f); l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        private static void BuildAir(Transform parent)
        {
            var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "AmberConeVolume"; volume.transform.SetParent(parent, false);
            volume.transform.localPosition = new Vector3(0, LampHeight * .5f, 0);
            volume.transform.localScale = new Vector3(10.3f, LampHeight, 10.3f);
            Object.DestroyImmediate(volume.GetComponent<Collider>());
            var r = volume.GetComponent<Renderer>(); r.sharedMaterial = BelievePrivateClubAssets.Get("Volume");
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var dust = Group(parent, "SlowDust"); dust.localPosition = new Vector3(0, 1.55f, 0);
            var ps = dust.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.duration = 10;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7, 11);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.007f, .023f);
            main.startSize = new ParticleSystem.MinMaxCurve(.008f, .021f);
            main.startColor = new Color(1, .77f, .43f, .45f); main.maxParticles = 110;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            ps.useAutoRandomSeed = false; ps.randomSeed = 565;
            var emission = ps.emission; emission.rateOverTime = 9;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.6f, 1.3f, 1.6f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .035f; noise.frequency = .25f; noise.scrollSpeed = .025f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(1, .75f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = BelievePrivateClubAssets.Get("Dust");
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static string ProtectedGeometry(Transform arena)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var root in new[] { arena.Find("Table"), arena.Find("TableTopBlocker_Invisible"), GameObject.Find("_Spawns").transform })
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    sb.Append(t.GetInstanceID()).Append(EditorJsonUtility.ToJson(t));
                    foreach (var c in t.GetComponents<Collider>()) sb.Append(EditorJsonUtility.ToJson(c));
                }
            return sb.ToString();
        }

        [MenuItem("Igruha/Верю не верю/Проверить оболочку клуба")]
        public static void Audit()
        {
            var hall = GameObject.Find("_Arena/_Hall");
            if (hall == null) throw new InvalidOperationException("Club shell missing.");
            if (hall.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Collider in club decor.");
            if (hall.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.layer != 0)) throw new InvalidOperationException("Club decor must use Default.");
            float nearest = float.MaxValue;
            foreach (var r in hall.transform.Find("Perimeter").GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                float x = Mathf.Max(0, Mathf.Abs(b.center.x) - b.extents.x);
                float z = Mathf.Max(0, Mathf.Abs(b.center.z) - b.extents.z);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(x * x + z * z));
            }
            if (nearest < ClearRadius) throw new InvalidOperationException("Decor crosses spectator circle: " + nearest);
            int missing = hall.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            foreach (var r in hall.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterials.Any(m => m == null || m.shader == null)) missing++;
            foreach (var filter in hall.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh == null) missing++;
            if (missing != 0) throw new InvalidOperationException("Missing club references: " + missing);
            Debug.Log($"IGR-565: shell valid; decor colliders 0; all Default; nearest perimeter {nearest:F2} m; " +
                $"floor light diameter {2 * Mathf.Sqrt(LampRange * LampRange - LampHeight * LampHeight):F2} m; missing refs 0.");
        }
    }
}
