using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    internal static class ExamHallLighting
    {
        internal static void Apply(Transform root)
        {
            var previous = root.Find("AfternoonLight");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    light.intensity = .20f;
                    light.color = new Color(.64f, .76f, 1f);
                    light.shadows = LightShadows.None;
                }
                else if (light.name == "OpalLight") { light.intensity = 2.1f; light.range = 6.5f; }
                else if (light.name == "SconceLight") { light.intensity = .85f; light.range = 3.8f; }
                else if (light.name == "WindowFill") { light.intensity = 1.0f; light.range = 8; }
                else if (light.name == "PlatformKey") { light.intensity = 3; light.shadows = LightShadows.None; }
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.24f, .30f, .37f);
            RenderSettings.ambientEquatorColor = new Color(.14f, .15f, .17f);
            RenderSettings.ambientGroundColor = new Color(.07f, .055f, .045f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = false;
            var group = new GameObject("AfternoonLight").transform;
            group.SetParent(root, false);
            for (int i = 0; i < 3; i++)
            {
                float z = -5.8f + i * 5.8f;
                var sun = Spot(group, "WindowSun_" + i, new Vector3(-8.9f, 5.12f, z),
                    new Vector3(4.0f, .1f, z + 4.5f), new Color(1f, .79f, .48f), 85, 63, 27);
                sun.cookie = WindowCookie();
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 1;
                sun.shadowBias = .035f;
                sun.shadowNormalBias = .10f;
                var data=sun.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                var settings=new SerializedObject(data);settings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue=2;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            Spot(group, "BoardWash", new Vector3(0, 5.6f, 6.9f), new Vector3(0, 3.7f, 9.6f),
                new Color(1f, .88f, .67f), 5.5f, 115, 8);
        }

        private static Light Spot(Transform parent, string name, Vector3 position, Vector3 target,
            Color color, float intensity, float angle, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.rotation = Quaternion.LookRotation(target - position);
            var light = go.AddComponent<Light>(); light.type = LightType.Spot;
            light.color = color; light.intensity = intensity; light.range = range;
            light.spotAngle = angle; light.innerSpotAngle = angle * .82f;
            return light;
        }

        // Own arched-window gobo. The light starts inside the masonry, and the
        // cookie supplies the mullion silhouette while real props cast shadows.
        private static Texture2D WindowCookie()
        {
            string path = ExamHallAssets.Art + "/Textures/EH_WindowCookie.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) return texture;
            const int size = 256;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
                { name = "EH_WindowCookie", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                bool aperture = Mathf.Abs(u) < .81f && v > -.87f &&
                    (v < .36f || (u * u + (v - .36f) * (v - .36f)) < .81f * .81f);
                bool bar = Mathf.Abs(u) < .023f || Mathf.Abs(Mathf.Abs(u) - .41f) < .018f ||
                    Mathf.Abs(v + .29f) < .024f || Mathf.Abs(v - .32f) < .024f;
                float level = aperture && !bar ? .95f : 0;
                pixels[y * size + x] = new Color(level, level, level, level);
            }
            texture.SetPixels(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, path);
            return texture;
        }
    }
}
