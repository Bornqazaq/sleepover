using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>One bounded, depth-aware cloud volume below the city, with an open view down the shafts.</summary>
    internal static class CarryCloudSea
    {
        private const int NoiseSize = 64;
        internal static void Apply(Transform arena)
        {
            var horizon = arena.Find("Environment/Horizon");
            for (int i = horizon.childCount - 1; i >= 0; i--)
                if (horizon.GetChild(i).name == "CS_HazeSheet") Object.DestroyImmediate(horizon.GetChild(i).gameObject);
            var old = arena.Find("Cloud sea");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            string texturePath = CarrySiteFinish.Art + "/Textures/CloudDensity.asset";
            var noise = AssetDatabase.LoadAssetAtPath<Texture3D>(texturePath);
            if (noise == null)
            {
                noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.R8, false)
                    { name = "Periodic cloud density", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                var values = new byte[NoiseSize * NoiseSize * NoiseSize];
                for (int z = 0; z < NoiseSize; z++)
                for (int y = 0; y < NoiseSize; y++)
                for (int x = 0; x < NoiseSize; x++)
                {
                    // Periodic Fourier noise: seamless in all three axes, generated once in the editor.
                    float a = x * Mathf.PI * 2 / NoiseSize, b = y * Mathf.PI * 2 / NoiseSize, c = z * Mathf.PI * 2 / NoiseSize;
                    float n = .5f + .14f * Mathf.Sin(a + b + .7f) * Mathf.Cos(c - b)
                        + .13f * Mathf.Cos(a * 2 - c + 1.2f) * Mathf.Sin(b + c)
                        + .09f * Mathf.Sin(a * 3 + b * 2 + c + 2.1f)
                        + .065f * Mathf.Cos(a * 5 - b * 3 + c * 2)
                        + .035f * Mathf.Sin(a * 9 + b * 7 - c * 5);
                    values[x + NoiseSize * (y + NoiseSize * z)] = (byte)(Mathf.Clamp01(n) * 255);
                }
                noise.SetPixelData(values, 0); noise.Apply(false, true); AssetDatabase.CreateAsset(noise, texturePath);
            }
            string path = CarrySiteFinish.Art + "/Materials/CloudSea.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Igruha/CarryItem/CloudSea");
            if (shader == null) throw new System.InvalidOperationException("CloudSea shader is missing");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetTexture("_Density", noise);
            material.SetColor("_SunColor", new Color(.96f, .985f, 1f));
            material.SetColor("_ShadeColor", new Color(.33f, .52f, .73f));
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            var cloud = GameObject.CreatePrimitive(PrimitiveType.Cube); cloud.name = "Cloud sea";
            cloud.transform.SetParent(arena, false); cloud.transform.position = new Vector3(0, -48, 0);
            cloud.transform.localScale = new Vector3(680, 48, 680);
            Object.DestroyImmediate(cloud.GetComponent<Collider>());
            var renderer = cloud.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
