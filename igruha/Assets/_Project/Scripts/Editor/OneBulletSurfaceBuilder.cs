using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Owned, seamless limestone grain: subtle colour and a restrained pore normal.</summary>
    public static class OneBulletSurfaceBuilder
    {
        private const string Root = "Assets/_Project/Art/Minigames/OneBullet/Textures/";
        private const int Resolution = 1024;
        private static bool generated;
        public static void Apply(Material[] materials)
        {
            Directory.CreateDirectory(Root);
            string colourPath = Root + "LimestoneGrain.png", normalPath = Root + "LimestoneNormal.png";
            if (!generated || !File.Exists(colourPath) || !File.Exists(normalPath))
            { Generate(colourPath, normalPath); generated = true; }
            var colour = AssetDatabase.LoadAssetAtPath<Texture2D>(colourPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            foreach (var material in materials)
            {
                material.SetTexture("_BaseMap", colour); material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", .08f); material.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(material);
            }
        }
        private static float Noise(float u, float v, float frequency)
        {
            float a = Mathf.PerlinNoise(31 + u * frequency, 71 + v * frequency);
            float b = Mathf.PerlinNoise(31 + (u - 1) * frequency, 71 + v * frequency);
            float c = Mathf.PerlinNoise(31 + u * frequency, 71 + (v - 1) * frequency);
            float d = Mathf.PerlinNoise(31 + (u - 1) * frequency, 71 + (v - 1) * frequency);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
        private static void Generate(string colourPath, string normalPath)
        {
            var heights = new float[Resolution * Resolution];
            var colours = new Color32[heights.Length]; var normals = new Color32[heights.Length];
            for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution; x++)
            {
                float u = (float)x / Resolution, v = (float)y / Resolution;
                float broad = Noise(u, v, 8), grain = Noise(u, v, 95), fine = Noise(u, v, 230);
                heights[y * Resolution + x] = grain * .7f + fine * .3f;
                float tone = .89f + (broad - .5f) * .09f + (grain - .5f) * .05f + (fine - .5f) * .02f;
                colours[y * Resolution + x] = new Color(tone, tone, tone, 1);
            }
            for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution; x++)
            {
                float dx = heights[y * Resolution + (x + 1) % Resolution] - heights[y * Resolution + (x + Resolution - 1) % Resolution];
                float dy = heights[((y + 1) % Resolution) * Resolution + x] - heights[((y + Resolution - 1) % Resolution) * Resolution + x];
                var n = new Vector3(-dx * 5, -dy * 5, 1).normalized;
                normals[y * Resolution + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }
            Save(colourPath, colours, false); Save(normalPath, normals, true);
        }
        private static void Save(string path, Color32[] pixels, bool normal)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = Resolution; importer.anisoLevel = 8; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
