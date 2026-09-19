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
        internal const float LampHeight = 3.6f;
        // Дальность лампы режет конус по сфере, а не по полу: при 5.1 м свет
        // не доставал до края стола и круг схлопывался в пятно метра на три.
        internal const float LampRange = 9.5f;

        /// <summary>Сила лунного заполнения. Выше — зал сереет и круг света перестаёт быть главным.</summary>
        private const float MoonlightIntensity = .17f;

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
            if (arena.transform.Find("_Furniture") != null)
                BelieveClubFurnitureBuilder.PositionShelfLights(arena.transform, config);
            if (before != ProtectedGeometry(arena.transform))
                throw new InvalidOperationException("Club art changed protected gameplay geometry.");
            Audit();
            EditorSceneManager.MarkSceneDirty(arena.scene);
            EditorSceneManager.SaveScene(arena.scene);
            AssetDatabase.SaveAssets();
        }

        internal static void Build(Transform arena, BelieveOrNotConfig config)
        {
            FitPhysicalHall(arena, config);
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
            // Flat rugs gather the side seating into rooms within the room, without blocking play.
            BelievePrivateClubAssets.Model(floor, "RoundRug", new Vector3(-6.45f, .013f, 1.1f), 0,
                new Vector3(.29f, 1, .43f));
            BelievePrivateClubAssets.Model(floor, "RoundRug", new Vector3(6.7f, .013f, 0), 0,
                new Vector3(.19f, 1, .56f));
            // Structural shell follows the room config; gameplay anchors are independent.
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
            // Окружение тремя цветами, а не одним: холодный верх даёт синеву
            // панелей референса, тёплый низ — отсвет ковра и дерева. Плоский
            // серый красил и то и другое одинаково, и зал читался стерильно.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.34f, .37f, .48f);
            RenderSettings.ambientEquatorColor = new Color(.33f, .29f, .28f);
            RenderSettings.ambientGroundColor = new Color(.27f, .20f, .16f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .06f;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            ConfigureMoonlight(arena);
            var lamp = arena.Find("TableLamp");
            if (lamp == null) { lamp = Group(arena, "TableLamp"); lamp.gameObject.AddComponent<Light>(); }
            lamp.localPosition = new Vector3(0, LampHeight, 0);
            lamp.localRotation = Quaternion.Euler(90, 0, 0);
            var spot = lamp.GetComponent<Light>();
            spot.enabled = true; spot.type = LightType.Spot;
            spot.spotAngle = 110; spot.innerSpotAngle = 58;
            spot.range = LampRange; spot.intensity = 28f;
            spot.color = Mathf.CorrelatedColorTemperatureToRGB(3000).gamma;
            spot.useColorTemperature = false;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = .90f; spot.shadowBias = .025f; spot.shadowNormalBias = .12f;
            spot.renderMode = LightRenderMode.ForcePixel;
            var hall = arena.Find("_Hall");
            var previous = hall.Find("DistantWarmPoints");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var accents = Group(hall, "DistantWarmPoints");
            float side = arena.Find("Wall_East").localPosition.x;
            Accent(accents, "EastShelfGlow", new Vector3(side - 1.29f, 2.7f, -1.65f),
                new Vector3(side - .69f, 1.7f, -1.65f), 3.2f, 5f, 105, 75);
            Accent(accents, "EastShelfGlow", new Vector3(side - 1.29f, 2.7f, 1.65f),
                new Vector3(side - .69f, 1.7f, 1.65f), 3.2f, 5f, 105, 75);
            Accent(accents, "WestSilhouette", new Vector3(-5.25f, 2.55f, 1f),
                new Vector3(-side + 1.2f, 1.1f, 1f), 5.5f, 5.8f, 120, 90);
            Accent(accents, "BarFrontBounce", new Vector3(5.3f, 2.65f, 0),
                new Vector3(side - 1.34f, 1.1f, 0), 4f, 5.2f, 125, 90);
            FaceFill(arena);
        }

        /// <summary>
        /// Холодный заполняющий свет над залом — «луна из окна» референса.
        ///
        /// Раньше все направленные источники просто гасились, и за кругом лампы
        /// зал проваливался в чёрное: пол под ногами бегущего не читался вовсе,
        /// а половина кадра зрителя была чёрной дырой. Источник намеренно очень
        /// слабый и без теней: он выравнивает зал, не спорит с лампой над столом
        /// и не даёт второго набора теней.
        /// </summary>
        private static void ConfigureMoonlight(Transform arena)
        {
            Light fill = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                if (fill == null && light.gameObject.scene == arena.gameObject.scene) { fill = light; continue; }
                light.enabled = false;
            }

            if (fill == null) return;
            fill.enabled = true;
            fill.intensity = MoonlightIntensity;
            fill.color = new Color(.62f, .70f, .95f);
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(58, 35, 0);
        }

        /// <summary>
        /// Мягкий тёплый подсвет лиц сидящих. Лампа бьёт строго сверху, и лицо
        /// целиком уходит в тень от собственного лба — а вся игра построена на
        /// том, что сорок секунд смотришь сопернику в лицо. Теней не даёт:
        /// вторая тень на сукне спорила бы с лампой.
        /// </summary>
        private static void FaceFill(Transform arena)
        {
            var previous = arena.Find("FaceFill");
            if (previous != null) return;
            var t = Group(arena, "FaceFill");
            t.localPosition = new Vector3(0, 1.62f, 0);
            var l = t.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, .77f, .54f);
            l.intensity = 2.6f; l.range = 3.4f;
            l.shadows = LightShadows.None;
        }

        private static void Accent(Transform parent, string name, Vector3 position, Vector3 target,
            float intensity, float range, float outer, float inner)
        {
            var t = Group(parent, name);
            t.localPosition = position;
            t.localRotation = Quaternion.LookRotation(target - position);
            var light = t.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, .80f, .58f);
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = outer;
            light.innerSpotAngle = inner;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        private static void FitPhysicalHall(Transform arena, BelieveOrNotConfig config)
        {
            float w = config.HallWidth, d = config.HallDepth, h = config.CeilingHeight;
            foreach (string name in new[] { "Floor", "Ceiling", "Wall_North", "Wall_South", "Wall_West", "Wall_East" })
            {
                var t = arena.Find(name);
                if (t == null) throw new InvalidOperationException("Missing structural shell: " + name);
                var p = t.localPosition;
                var size = t.localScale;
                if (name == "Floor" || name == "Ceiling") { size.x = w; size.z = d; }
                else if (name == "Wall_North" || name == "Wall_South")
                { size.x = w; size.y = h; p.z = (name == "Wall_North" ? 1 : -1) * d * .5f; }
                else { size.z = d; size.y = h; p.x = (name == "Wall_East" ? 1 : -1) * w * .5f; }
                t.localPosition = p;
                t.localScale = size;
            }
        }

        [MenuItem("Igruha/Верю не верю/Собственный клуб — компактная композиция")]
        public static void ApplyComposition()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open BelieveOrNot first.");
            var arena = GameObject.Find("_Arena").transform;
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            var before = ProtectedGeometry(arena);
            Build(arena, config);
            BelieveClubFurnitureBuilder.Build(arena, config);
            BelieveClubDetailsBuilder.Build(arena, config);
            if (before != ProtectedGeometry(arena)) throw new InvalidOperationException("Gameplay anchors changed.");
            Audit();
            BelieveClubFurnitureBuilder.Audit();
            BelieveClubDetailsBuilder.Audit();
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            EditorSceneManager.SaveScene(arena.gameObject.scene);
            AssetDatabase.SaveAssets();
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
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            if (config == null) throw new InvalidOperationException("Missing BelieveOrNotConfig.");
            if (nearest < config.SpectatorZoneRadius) throw new InvalidOperationException("Decor crosses spectator circle: " + nearest);
            int missing = hall.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            foreach (var r in hall.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterials.Any(m => m == null || m.shader == null)) missing++;
            foreach (var filter in hall.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh == null) missing++;
            if (missing != 0) throw new InvalidOperationException("Missing club references: " + missing);
            Debug.Log($"IGR-565: shell valid; decor colliders 0; all Default; nearest perimeter {nearest:F2} m; " +
                $"lamp cone cutoff {2 * LampHeight * Mathf.Tan(55f * Mathf.Deg2Rad):F2} m wide at floor; missing refs 0.");
        }
    }
}
