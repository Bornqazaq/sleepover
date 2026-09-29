using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    public static class MosquitoSurfaceBuilder
    {
        public static void Apply()
        {
            string folder = MosquitoesArenaBuilder.Art + "/Textures";
            MosquitoesCoreSetup.EnsureFolder(folder);
            for (int kind = 0; kind < 2; kind++)
            {
                string path = folder + (kind == 0 ? "/WoodGrain.png" : "/WovenFabric.png");
                const int size = 256;
                var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float noise = Mathf.PerlinNoise(x * .08f, y * .08f);
                    float value;
                    if (kind == 0)
                    {
                        float grain = Mathf.Sin(x * .48f + Mathf.PerlinNoise(x * .025f, y * .008f) * 12);
                        value = .945f + grain * .012f + noise * .035f;
                    }
                    else value = .87f + .05f * Mathf.Sin(x * Mathf.PI * .5f) * Mathf.Sin(y * Mathf.PI * .5f) + noise * .08f;
                    texture.SetPixel(x, y, new Color(value, value, value));
                }
                texture.Apply(); System.IO.File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 4; importer.mipmapEnabled = true; importer.SaveAndReimport();
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                foreach (string name in kind == 0 ? new[] { "Walnut", "Oak" } : new[] { "Blue", "Coral", "Teal" })
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(MosquitoesArenaBuilder.Art + "/Materials/" + name + ".mat");
                    material.SetTexture("_BaseMap", asset); material.SetTextureScale("_BaseMap", kind == 0 ? new Vector2(3, 1) : Vector2.one * 5);
                    EditorUtility.SetDirty(material);
                }
            }
            // Carton is matte paper, not the wood grain used by the furniture.
            var carton = AssetDatabase.LoadAssetAtPath<Material>(MosquitoesArenaBuilder.Art + "/Materials/Cardboard.mat");
            if (carton != null) { carton.SetTexture("_BaseMap", null); carton.SetFloat("_Smoothness", .05f); EditorUtility.SetDirty(carton); }
            ApplyClinker(folder);
            ApplyRoomFinishes(folder);
        }

        private static void ApplyRoomFinishes(string folder)
        {
            // Fine twill weave: low colour contrast, relief on the normal map.
            const int size = 512;
            var albedo = new Texture2D(size, size, TextureFormat.RGB24, false);
            var normal = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float a = x * Mathf.PI / 4, b = y * Mathf.PI / 4;
                float weave = Mathf.Sin(a) * Mathf.Cos(b);
                float value = .955f + .016f * weave + .018f * Mathf.PerlinNoise(x * .13f, y * .13f);
                albedo.SetPixel(x, y, new Color(value, value, value));
                var n = new Vector3(-.20f * Mathf.Cos(a) * Mathf.Cos(b), .20f * Mathf.Sin(a) * Mathf.Sin(b), 1).normalized;
                normal.SetPixel(x, y, new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f));
            }
            albedo.Apply(); normal.Apply();
            string colorPath = folder + "/RoomFabric.png", normalPath = folder + "/RoomFabricNormal.png";
            System.IO.File.WriteAllBytes(colorPath, albedo.EncodeToPNG());
            System.IO.File.WriteAllBytes(normalPath, normal.EncodeToPNG());
            Object.DestroyImmediate(albedo); Object.DestroyImmediate(normal);
            AssetDatabase.ImportAsset(colorPath); AssetDatabase.ImportAsset(normalPath);
            foreach (string path in new[] { colorPath, normalPath })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 8; importer.mipmapEnabled = true;
                if (path == normalPath) importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            var bump = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            foreach (string name in new[] { "GarmentTeal", "GarmentSand", "GarmentCharcoal", "Denim", "Canvas", "Linen", "Upholstery" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MosquitoesArenaBuilder.Art + "/Materials/" + name + ".mat");
                if (mat == null) continue;
                mat.SetTexture("_BaseMap", color); mat.SetTextureScale("_BaseMap", Vector2.one * 8);
                mat.SetTexture("_BumpMap", bump); mat.SetTextureScale("_BumpMap", Vector2.one * 8);
                mat.SetFloat("_BumpScale", .24f); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_Smoothness", .06f);
                EditorUtility.SetDirty(mat);
            }
            var oak = AssetDatabase.LoadAssetAtPath<Material>(MosquitoesArenaBuilder.Art + "/Materials/NaturalOak.mat");
            if (oak != null)
            {
                oak.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/WoodGrain.png"));
                oak.SetTextureScale("_BaseMap", new Vector2(3, 1)); EditorUtility.SetDirty(oak);
            }
        }

        private static void ApplyClinker(string folder)
        {
            // One repeat covers 2 x 1 metres, with staggered brick courses.
            // FacadeMetres UVs in the Blender generator preserve physical scale.
            const int width = 512, height = 256;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int row = y / 32;
                int staggeredX = (x + (row % 2) * 32) % width;
                bool mortar = y % 32 < 2 || staggeredX % 64 < 2;
                float grain = Mathf.PerlinNoise(x * .17f, y * .17f);
                float brickShade = Mathf.PerlinNoise((staggeredX / 64) * 2.71f, row * 3.17f);
                float value = mortar ? .58f : .86f + brickShade * .12f + grain * .05f;
                texture.SetPixel(x, y, new Color(value, value, value));
            }
            texture.Apply();
            string path = folder + "/ClinkerCourses.png";
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 4;
            importer.mipmapEnabled = true; importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MosquitoesArenaBuilder.Art + "/Materials/Clinker.mat");
            if (material != null)
            {
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                material.SetTextureScale("_BaseMap", Vector2.one);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
