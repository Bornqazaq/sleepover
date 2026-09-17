using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    internal static class MemoryFoundryBuilder
    {
        private const float CorridorHalfWidth = 6.2f;
        private static readonly Color Warm = new Color(1, .68f, .35f);
        private static readonly Color Cool = new Color(.66f, .79f, 1);

        internal static void ConfigureHud(GameObject manager)
        {
            var local = manager.GetComponent<MemoryRunLocalHud>();
            var localSo = new SerializedObject(local);
            var hud = localSo.FindProperty("hud").objectReferenceValue;
            var text = new SerializedObject(hud).FindProperty("statusText").objectReferenceValue as TMP_Text;
            if (text == null) throw new System.InvalidOperationException("MemoryRun status text is missing.");
            var panel = (RectTransform)text.transform.parent;
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1); panel.anchoredPosition = new Vector2(28, -28);
            panel.sizeDelta = new Vector2(480, 150);
            text.fontSize = 27; text.enableAutoSizing = true; text.fontSizeMin = 21; text.fontSizeMax = 27;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.Normal;
            localSo.FindProperty("statusPanel").objectReferenceValue = panel.gameObject;
            localSo.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void Build(Transform arena, MemoryRunConfig config)
        {
            var root = new GameObject("_Environment").transform;
            root.SetParent(arena, false);
            float half = config.HallWidth * .5f;
            MemoryFoundryRuins.BuildShell(root, config);
            // Environmental damage is authored independently of the secret route.
            for (int i = -4; i < config.Steps + 2; i++)
            {
                float z = config.StepZ(0) + i * config.StepPitch;
                var bay = new GameObject("Bay_" + i).transform;
                bay.SetParent(root, false);
                bay.localPosition = new Vector3(0, 0, z);
                foreach (int sign in new[] { -1, 1 })
                {
                    LightAt(bay, "GalleryLamp", new Vector3(sign * (half - 3.0f), 3.9f, 0), Warm, 4.5f, 10);
                    LightAt(bay, "WindowBounce", new Vector3(sign * (half - 3.5f), 10, 0), Cool, 1.6f, 13);
                    LightAt(bay, "DeepWorkLamp", new Vector3(sign * (half - 2.2f), -13, 0), Warm, 2, 7);
                    foreach (float height in new[] { 3.9f, -13f, -31f })
                    {
                        float x = sign * (half - 1.35f);
                        Box(bay, "LampBracket", new Vector3(x, height, 0), new Vector3(.95f, .14f, .22f), "Iron");
                        Box(bay, "CagedWorkLamp", new Vector3(x - sign * .45f, height - .3f, 0), new Vector3(.24f, .65f, .25f), "Opal");
                        foreach (float y in new[] { height - .66f, height + .05f })
                            Box(bay, "LampCap", new Vector3(x - sign * .45f, y, 0), new Vector3(.34f, .10f, .34f), "Iron");
                    }
                }
            }

            EndWall(root, config, false);
            EndWall(root, config, true);
            ShaftEnds(root, config);
            DressPlatforms(root, config);
            MemoryFoundryProduction.BuildLines(root, config);
            BuildLighting(root);
            MemoryFoundryAtmosphere.Build(root, config);
        }

        private static void EndWall(Transform parent, MemoryRunConfig c, bool exit)
        {
            float z = (exit ? 1 : -1) * (c.HallDepth * .5f - .24f);
            float yaw = exit ? 0 : 180;
            var end = new GameObject(exit ? "ExitFacade" : "StartFacade").transform;
            end.SetParent(parent, false); end.localPosition = new Vector3(0, 0, z);
            end.localRotation = Quaternion.Euler(0, yaw, 0);
            float half = c.HallWidth / 2;
            foreach (int sign in new[] { -1, 1 })
            {
                for (int i = 0; i < 3; i++)
                {
                    if (exit && sign > 0 && i > 0) continue;
                    float x = sign * (5 + i * 4.2f);
                    Box(end, "MasonryPier", new Vector3(x, 9, -.18f), new Vector3(3.9f, 18, .35f), "Brick");
                    Box(end, "TallWindow", new Vector3(x, 12.1f, -.40f), new Vector3(2.7f, 6.8f, .06f), "Window");
                    for (int j = 0; j < 5; j++)
                        Box(end, "WindowMullion", new Vector3(x, 9.2f + j * 1.45f, -.47f), new Vector3(2.9f, .09f, .10f), "Iron");
                    for (int j = -1; j <= 1; j++)
                        Box(end, "WindowMullion", new Vector3(x + j * .95f, 12.1f, -.47f), new Vector3(.08f, 7, .1f), "Iron");
                }
                Box(end, "MainColumn", new Vector3(sign * 3.3f, 9.3f, -.4f), new Vector3(.45f, 18.6f, .6f), "Iron");
                var vessel = MemoryFoundryAssets.Place(end, "PressureVessel", new Vector3(sign * (half - 3), exit ? 0 : c.StartZoneLift, -2.6f));
                vessel.localScale = Vector3.one * (exit ? 1.15f : .90f);
                Solid(vessel, new Vector3(0, 2.5f, 0), new Vector3(2.35f, 5.1f, 2.5f));
                LightAt(end, "PortalSconce", new Vector3(sign * 3.8f, 3.3f, -1.1f), Warm, 5, 9);
            }
            Box(end, "Cornice", new Vector3(exit ? -5 : 0, 7, -.5f), new Vector3(exit ? c.HallWidth - 10 : c.HallWidth, .5f, .8f), "Iron");
            Box(end, "DoorSurround", new Vector3(0, 3.2f, -.18f), new Vector3(6.2f, 6.4f, .2f), "Concrete");
            Box(end, "UpperPortalMasonry", new Vector3(0, 12.4f, -.18f), new Vector3(6.2f, 11.9f, .35f), "Brick");
            if (exit)
            {
                Text(end, "ВЫХОД", new Vector3(0, 4.50f, -.80f), new Vector2(2.5f, .55f), .42f, Color.white);
                Text(end, "ЦЕХ ПАМЯТИ", new Vector3(0, 6.1f, -.81f), new Vector2(8, 1), .65f, new Color(.91f, .83f, .65f));
                LightAt(end, "ExitBeacon", new Vector3(0, 4.65f, -1.0f), new Color(.24f, 1, .43f), 6, 6);
            }
            else
            {
                Text(end, "ДИНАМИТНЫЙ ЗАВОД", new Vector3(0, 5.6f, -.81f), new Vector2(11, 1.4f), .8f, new Color(.91f, .83f, .65f));
                MemoryFoundryAssets.Place(end, "Switchboard", new Vector3(0, c.StartZoneLift, -1.6f));
            }
        }

        private static void ShaftEnds(Transform parent, MemoryRunConfig c)
        {
            foreach (float z in new[] { c.GateZ - .2f, c.ExitPadZ + .2f })
            {
                Box(parent, "ShaftAbutment", new Vector3(0, -c.PitDepth / 2, z),
                    new Vector3(c.HallWidth, c.PitDepth, .45f), "Iron");
                for (float y = -5; y > -c.PitDepth; y -= 6)
                {
                    Box(parent, "AbutmentCourse", new Vector3(0, y, z), new Vector3(c.HallWidth, .25f, .8f), "Concrete");
                    foreach (float x in new[] { -12f, -6, 0, 6, 12 })
                        Box(parent, "AbutmentRecess", new Vector3(x, y - 2.4f, z + (z > 0 ? -.3f : .3f)),
                            new Vector3(4.5f, 3.9f, .08f), "Recess");
                }
            }
        }

        private static void DressPlatforms(Transform root, MemoryRunConfig c)
        {
            float half = c.HallWidth * .5f;
            foreach (bool exit in new[] { false, true })
            {
                float y = exit ? 0 : c.StartZoneLift;
                float z = exit ? c.ExitPadZ + .28f : c.GateZ - .28f;
                int count = Mathf.CeilToInt(c.HallWidth / 3);
                float width = c.HallWidth / count;
                for (int i = 0; i < count; i++)
                {
                    float x = -half + (i + .5f) * width;
                    var stripe = MemoryFoundryAssets.Place(root, "HazardEdge", new Vector3(x, y + .015f, z));
                    stripe.localScale = new Vector3(width / 3, 1, 1);
                    if (Mathf.Abs(x) > CorridorHalfWidth + 1.5f)
                    {
                        var railing = MemoryFoundryAssets.Place(root, "Railing", new Vector3(x, y, z));
                        railing.localScale = new Vector3(width / 3, 1, 1);
                        Solid(railing, new Vector3(0, .55f, 0), new Vector3(3, 1.1f, .12f));
                    }
                }
            }
            float start = c.GateZ - 5.7f;
            foreach (int sign in new[] { -1, 1 })
            {
                float x = sign * (half - 2.6f);
                var crate = MemoryFoundryAssets.Place(root, "DynamiteCrate", new Vector3(x, c.StartZoneLift, start), sign * 12);
                Solid(crate, new Vector3(0, .56f, 0), new Vector3(1.5f, 1.15f, 1.1f));
                crate = MemoryFoundryAssets.Place(root, "DynamiteCrate", new Vector3(x + sign * .3f, c.StartZoneLift, start + 1.9f), sign * -9);
                crate.localScale = Vector3.one * .78f;
                Solid(crate, new Vector3(0, .56f, 0), new Vector3(1.5f, 1.15f, 1.1f));
                for (int j = 0; j < 3; j++)
                {
                    var drum = MemoryFoundryAssets.Place(root, "Drum", new Vector3(x + sign * (j % 2) * 1.1f, c.StartZoneLift, start - 2 - j * .8f));
                    Solid(drum, new Vector3(0, .72f, 0), new Vector3(1, 1.44f, 1));
                }
                var reel = MemoryFoundryAssets.Place(root, "CableReel", new Vector3(sign * (half - 7.4f), c.StartZoneLift, start - 2), sign * 25);
                Solid(reel, new Vector3(0, .8f, 0), new Vector3(1.6f, 1.6f, 1.1f));
                var cabinet = MemoryFoundryAssets.Place(root, "Switchboard", new Vector3(sign * (half - 1.6f), c.StartZoneLift, start - 3), sign * 90);
                Solid(cabinet, new Vector3(0, 1.05f, 0), new Vector3(2.2f, 2.1f, .6f));
                Poster(root, new Vector3(sign * 10, 5.1f, c.HallDepth / 2 - .9f),
                    sign < 0 ? "БОЛЬШЕ\nДИНАМИТА\nЯРЧЕ ЗАВТРА" : "ТРУД\nВЗРЫВНОЙ\nРЕЗУЛЬТАТ", 1.3f);
            }
            var gantry = MemoryFoundryAssets.Place(root, "Gantry", new Vector3(-12.5f, c.StartZoneLift, c.GateZ - 10));
            foreach (float x in new[] { -3.2f, 3.2f })
            {
                var leg = new GameObject("GantryLegCollision").transform; leg.SetParent(gantry, false);
                Solid(leg, new Vector3(x, 3.4f, 0), new Vector3(.8f, 6.8f, .8f));
            }
            // The start sign is beside the observation corridor, never across it.
            Poster(root, new Vector3(9.7f, 3.9f, c.GateZ - 1.5f), "СМОТРИ\nЗАПОМИНАЙ\nВЫЖИВАЙ", .67f);
            var startSign = MemoryFoundryAssets.Place(root, "PosterFrame", new Vector3(-9.5f, 4, c.GateZ - 1.3f));
            startSign.localScale = new Vector3(1.1f, .45f, 1.1f);
            Text(root, "СТАРТ", new Vector3(-9.5f, 4, c.GateZ - 1.48f), new Vector2(4.2f, 1.2f), .7f, new Color(.96f, .78f, .36f));
            foreach (float x in new[] { -11.3f, -7.7f, 8.7f, 10.7f })
            {
                float height = 4.1f - c.StartZoneLift;
                var post = Box(root, "SignSupport", new Vector3(x, c.StartZoneLift + height / 2, c.GateZ - 1.2f),
                    new Vector3(.12f, height, .12f), "Iron");
                Solid(post.transform, Vector3.zero, Vector3.one);
                Box(root, "SignFoot", new Vector3(x, c.StartZoneLift + .04f, c.GateZ - 1.2f),
                    new Vector3(.36f, .08f, .36f), "SafetyOchre");
            }
        }

        private static void Poster(Transform parent, Vector3 position, string text, float scale)
        {
            var poster = MemoryFoundryAssets.Place(parent, "PosterFrame", position);
            poster.localScale = Vector3.one * scale;
            Text(poster, text, new Vector3(0, 0, -.16f), new Vector2(4.1f, 2.55f), .54f, new Color(.94f, .85f, .65f));
        }

        private static void Text(Transform parent, string words, Vector3 pos, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject("FoundrySign", typeof(TextMeshPro));
            go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var text = go.GetComponent<TextMeshPro>();
            text.font = MemoryFoundryAssets.Font();
            text.text = words; text.fontSize = fontSize * 10; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = size; text.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        internal static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, string material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MemoryFoundryAssets.Material(material); return go;
        }

        private static void Solid(Transform prop, Vector3 center, Vector3 size)
        {
            prop.gameObject.layer = LayerMask.NameToLayer("Ground");
            var collider = prop.gameObject.AddComponent<BoxCollider>(); collider.center = center; collider.size = size;
        }

        private static Light LightAt(Transform parent, string name, Vector3 pos, Color color, float intensity, float range, bool shadows = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var light = go.AddComponent<Light>(); light.type = LightType.Point;
            light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowBias = .04f; light.shadowNormalBias = .2f; return light;
        }

        private static void BuildLighting(Transform parent)
        {
            // This scene owns its standalone render camera; the shared orbit rig,
            // projection, controls and all character settings remain untouched.
            if (Camera.main != null)
            {
                var cameraData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null) cameraData = Camera.main.gameObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
                Camera.main.clearFlags = CameraClearFlags.Skybox;
            }
            MemoryFoundryRuins.ConfigureSky();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (!light.transform.IsChildOf(parent)) light.enabled = false;
            var sun = new GameObject("FoundrySkylight").AddComponent<Light>();
            sun.transform.SetParent(parent, false); sun.type = LightType.Directional;
            sun.transform.localRotation = Quaternion.Euler(40, -110, 0);
            sun.color = new Color(.72f, .80f, .89f); sun.intensity = .8f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .75f;
            sun.shadowBias = .045f; sun.shadowNormalBias = .3f;
            RenderSettings.sun = sun;
            var fill = new GameObject("WarmRoofBounce").AddComponent<Light>();
            fill.transform.SetParent(parent, false); fill.type = LightType.Directional;
            fill.transform.localRotation = Quaternion.Euler(35, 25, 0);
            fill.color = new Color(.62f, .72f, .79f); fill.intensity = .24f;
            foreach (int side in new[] { -1, 1 })
            {
                var spot = LightAt(parent, "LoadingBaySpot", new Vector3(side * 10, 8, -25), Warm, 18, 17, true);
                spot.type = LightType.Spot; spot.spotAngle = 78; spot.innerSpotAngle = 48;
                spot.transform.rotation = Quaternion.LookRotation(new Vector3(side * 12, .72f, -24) - spot.transform.position);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.53f, .57f, .60f);
            RenderSettings.ambientEquatorColor = new Color(.39f, .37f, .34f);
            RenderSettings.ambientGroundColor = new Color(.10f, .14f, .16f);
            DynamicGI.UpdateEnvironment();
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(.12f, .15f, .17f));
            RenderSettings.ambientProbe = ambient;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.13f, .17f, .19f); RenderSettings.fogDensity = .010f;
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) volume.enabled = false;
            string path = MemoryFoundryAssets.Materials + "/MF_FoundryVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            if (!profile.TryGet<Bloom>(out var bloom)) { bloom = profile.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bloom, profile); }
            bloom.threshold.Override(1.15f); bloom.intensity.Override(.34f); bloom.scatter.Override(.65f);
            if (!profile.TryGet<ColorAdjustments>(out var colors)) { colors = profile.Add<ColorAdjustments>(); AssetDatabase.AddObjectToAsset(colors, profile); }
            colors.contrast.Override(9); colors.saturation.Override(-9); colors.postExposure.Override(.15f);
            if (!profile.TryGet<Tonemapping>(out var tone)) { tone = profile.Add<Tonemapping>(); AssetDatabase.AddObjectToAsset(tone, profile); }
            tone.mode.Override(TonemappingMode.ACES);
            var go = new GameObject("FoundryVolume"); go.transform.SetParent(parent, false);
            var local = go.AddComponent<Volume>(); local.isGlobal = true; local.priority = 20; local.sharedProfile = profile;
            EditorUtility.SetDirty(profile);
        }
    }
}
