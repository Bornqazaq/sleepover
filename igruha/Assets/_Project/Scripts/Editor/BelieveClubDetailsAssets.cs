using System;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Explicit URP equivalents for the original Blender finishing props.</summary>
    internal static class BelieveClubDetailsAssets
    {
        internal const string Root = "Assets/_Project/Art/BelieveClubDetails";

        internal static void Prepare()
        {
            System.IO.Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Lit("Walnut", Color.white, .38f, 0, "BCD_Walnut");
            Lit("Brass", new Color(.57f,.36f,.16f), .65f, .6f);
            Lit("Leather", new Color(.30f,.145f,.070f), .30f);
            Lit("Felt", new Color(.065f,.17f,.09f), .06f);
            Lit("Ivory", new Color(.64f,.55f,.37f), .25f);
            Lit("Red", new Color(.36f,.055f,.042f), .28f);
            Lit("Black", new Color(.028f,.022f,.018f), .38f);
            Lit("Glass", new Color(.19f,.26f,.24f), .86f, .20f);
            Lit("GreenGlass", new Color(.035f,.32f,.11f), .75f, .08f);
            Lit("Cream", new Color(.52f,.41f,.27f), .12f);
            Lit("LitLinen", new Color(.64f,.51f,.34f), .12f);
            var linen = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BCD_LitLinen.mat");
            linen.SetColor("_EmissionColor", new Color(.16f, .105f, .045f));
            linen.EnableKeyword("_EMISSION");
            linen.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(linen);
            Lit("Paper", new Color(.57f,.47f,.31f), .04f);
            Lit("Book", new Color(.19f,.060f,.045f), .25f);
            Lit("Blue", new Color(.10f,.19f,.22f), .30f);
            Lit("MapLand", new Color(.34f,.26f,.13f), .15f);
            Lit("MapSea", new Color(.11f,.17f,.17f), .22f);
            Lit("Ink", new Color(.018f,.018f,.019f), .80f);
            foreach (string name in new[] { "Portrait", "Landscape", "StillLife", "Hunt" })
                Lit(name, Color.white, .08f, 0, "BCD_" + name);
            var shade = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BCD_GreenGlass.mat");
            shade.SetColor("_EmissionColor", new Color(.02f, .42f, .07f));
            shade.EnableKeyword("_EMISSION");
            shade.SetTexture("_EmissionMap", ShadeGlow());
            shade.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(shade);
            AssetDatabase.SaveAssets();
        }

        // Cylindrical hood UVs: dark edges, luminous crown, no flat neon rectangle.
        private static Texture2D ShadeGlow()
        {
            string path = Root + "/Textures/BCD_ShadeGlow.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
                tex.name = "BCD_ShadeGlow";
                AssetDatabase.CreateAsset(tex, path);
            }
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float arch = Mathf.Sin(y / 63f * Mathf.PI);
                    float ends = Mathf.SmoothStep(.55f, 1f, Mathf.Min(x, 63 - x) / 8f);
                    float glow = (.12f + .88f * arch * arch) * ends;
                    pixels[y * 64 + x] = new Color(glow, glow, glow, 1f);
                }
            tex.SetPixels(pixels);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            EditorUtility.SetDirty(tex);
            return tex;
        }

        private static Texture2D Texture(string name, bool normal = false)
        {
            string path = Root + "/Textures/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing detail texture: " + path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture == normal || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Lit(string name, Color color, float smooth, float metal = 0, string albedo = null, string normal = null)
        {
            string path = Root + "/Materials/BCD_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetTexture("_BaseMap", albedo == null ? null : Texture(albedo));
            mat.SetTexture("_BumpMap", normal == null ? null : Texture(normal, true));
            mat.SetFloat("_BumpScale", .25f);
            if (normal == null) mat.DisableKeyword("_NORMALMAP"); else mat.EnableKeyword("_NORMALMAP");
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        internal static GameObject Model(Transform parent, string name)
        {
            string path = Root + "/Models/BCD_" + name + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing detail model: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Keep the FBX root's unit / axis conversion; layout lives on its parent.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            if (go.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Detail FBX must not generate colliders: " + name);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i].name;
                    if (name == "FloorLamp" && materialName == "BCD_Cream") materialName = "BCD_LitLinen";
                    materials[i] = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + materialName + ".mat");
                    if (materials[i] == null) throw new InvalidOperationException("Missing detail material: " + materialName);
                }
                r.sharedMaterials = materials;
            }
            return go;
        }
    }
}
