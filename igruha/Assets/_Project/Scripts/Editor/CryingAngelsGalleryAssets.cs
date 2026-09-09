using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Imports the Blender kit and creates reusable URP prefabs with shared stone textures.</summary>
    internal static class CryingAngelsGalleryAssets
    {
        internal const string Art = "Assets/_Project/Art/CryingAngels";
        internal const string Prefabs = "Assets/_Project/Prefabs/Minigames/CryingAngels/Gallery";
        internal const string Materials = "Assets/_Project/Materials/Minigames/CryingAngels/Gallery";
        private static readonly string[] Names = { "CA_PaleLimestone", "CA_BlueSlate", "CA_CarvedStone", "CA_Marble", "CA_AgedBrass", "CA_Crevices", "CA_MoonGlass", "CA_OxidizedIron", "CA_Cobweb" };
        private static readonly Color[] Colors = {
            new Color(.66f,.70f,.73f),new Color(.15f,.22f,.29f),new Color(.40f,.48f,.54f),
            new Color(.39f,.46f,.51f),new Color(.34f,.23f,.105f),new Color(.045f,.062f,.071f),
            new Color(.45f,.64f,1f),new Color(.052f,.075f,.09f),new Color(.33f,.43f,.47f)
        };

        internal static void Import()
        {
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Materials);
            ConfigureTexture("CA_WeatheredStone", false);
            ConfigureTexture("CA_StoneNormal", true);
            var stone = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/CA_WeatheredStone.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/CA_StoneNormal.png");
            var map = new Dictionary<string, Material>();
            for (int i = 0; i < Names.Length; i++)
            {
                var mat = EnsureMaterial(Names[i], "Universal Render Pipeline/Lit");
                mat.SetColor("_BaseColor", Colors[i]);
                bool textured = i != 5 && i != 6 && i != 8;
                mat.SetTexture("_BaseMap", textured ? stone : null);
                mat.SetTexture("_BumpMap", textured ? normal : null);
                mat.SetFloat("_BumpScale", .09f);
                if (textured) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
                mat.SetFloat("_Metallic", i == 4 ? .72f : i == 7 ? .65f : 0f);
                mat.SetFloat("_Smoothness", i == 3 ? .40f : i == 4 ? .47f : .22f);
                if (i == 6)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Colors[i] * .16f);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                map.Add(Names[i], mat);
            }
            foreach (string file in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                string path = file.Replace('\\', '/');
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (!importer.bakeAxisConversion || importer.importAnimation || importer.isReadable)
                {
                    importer.bakeAxisConversion = true;
                    importer.importAnimation = false;
                    importer.isReadable = false;
                    importer.addCollider = false;
                    importer.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Missing Blender module: " + path);
                var wrapper = new GameObject(Path.GetFileNameWithoutExtension(file));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
                try
                {
                    instance.name = Path.GetFileNameWithoutExtension(file);
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var shared = renderer.sharedMaterials;
                        for (int i = 0; i < shared.Length; i++)
                        {
                            Material replacement;
                            if (shared[i] != null && map.TryGetValue(shared[i].name, out replacement)) shared[i] = replacement;
                            else throw new InvalidOperationException("Unmapped Blender material on " + instance.name);
                        }
                        renderer.sharedMaterials = shared;
                    }
                    PrefabUtility.SaveAsPrefabAsset(wrapper, Prefabs + "/" + wrapper.name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(wrapper); }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Near-black cold cubemap: the null skybox otherwise reflects URP's bright default probe off the marble.</summary>
        internal static Cubemap EnsureNightReflection()
        {
            string path = Materials + "/CA_NightReflection.asset";
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            bool created = cube == null;
            const int size = 16;
            if (created) cube = new Cubemap(size, TextureFormat.RGBAHalf, false) { name = "CA_NightReflection" };
            var pixels = new Color[size * size];
            foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
            {
                if (face == CubemapFace.Unknown) continue;
                Color tint = face == CubemapFace.PositiveY ? new Color(.010f, .018f, .034f) : face == CubemapFace.NegativeY ? new Color(.003f, .004f, .008f) : new Color(.006f, .010f, .020f);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = tint;
                cube.SetPixels(pixels, face);
            }
            cube.Apply();
            if (!created) { EditorUtility.SetDirty(cube); return cube; }
            AssetDatabase.CreateAsset(cube, path);
            return cube;
        }

        /// <summary>Procedural torch cookie: hot centre, faint reflector ring, uneven rim, black beyond the cone.</summary>
        internal static Texture2D EnsureTorchCookie()
        {
            string path = Art + "/Textures/CA_TorchCookie.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float angle = Mathf.Atan2(v, u);
                float core = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.08f, .62f, r)) * .75f + .25f;
                float ring = Mathf.Exp(-Mathf.Pow((r - .70f) / .06f, 2f)) * .22f;
                float unevenness = 1f - .10f * (Mathf.Sin(angle * 3f + 1.1f) * .5f + .5f) - .08f * (Mathf.Sin(angle * 7f + r * 9f) * .5f + .5f);
                float rim = 1f - Mathf.SmoothStep(.80f, .99f, r);
                float value = Mathf.Clamp01((core + ring) * unevenness * rim);
                pixels[y * size + x] = new Color(value, value * .985f, value * .96f);
            }
            texture.SetPixels(pixels); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true; importer.alphaSource = TextureImporterAlphaSource.None; importer.maxTextureSize = 256;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static Material EnsureMaterial(string name, string shader)
        {
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var found = Shader.Find(shader);
            if (found == null) throw new InvalidOperationException("Missing shader: " + shader);
            material = new Material(found) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static GameObject Place(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + name + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing gallery prefab: " + name);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            return go;
        }

        private static void ConfigureTexture(string name, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Art + "/Textures/" + name + ".png");
            if (importer == null) throw new InvalidOperationException("Stone texture has not imported: " + name);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }
}
