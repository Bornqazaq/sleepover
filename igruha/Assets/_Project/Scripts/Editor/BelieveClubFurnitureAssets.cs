using System;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Explicit URP equivalents for the original Blender furniture kit.</summary>
    internal static class BelieveClubFurnitureAssets
    {
        internal const string Root = "Assets/_Project/Art/BelieveClubFurniture";

        internal static void Prepare()
        {
            System.IO.Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Lit("Walnut", Color.white, .38f, 0, "BCF_Walnut");
            Lit("Leather", Color.white, .30f, 0, "BCF_Leather", "BCF_LeatherNormal");
            Lit("Brass", new Color(.57f, .36f, .16f), .68f, .65f);
            Lit("Seam", new Color(.16f, .068f, .032f), .22f);
            Lit("Bottle", new Color(.045f, .105f, .07f), .75f, .15f);
            Lit("Glass", new Color(.29f, .33f, .32f), .88f, .3f);
            Lit("Ivory", new Color(.55f, .45f, .30f), .25f);
            Lit("Fur", new Color(.25f, .135f, .072f), .08f);
            Lit("Antler", new Color(.43f, .34f, .22f), .18f);
            Lit("Black", new Color(.028f, .020f, .016f), .42f);
            Lit("Glazing", new Color(.22f, .29f, .27f, .08f), .94f);
            var glazing = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BCF_Glazing.mat");
            glazing.SetFloat("_Surface", 1);
            glazing.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glazing.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glazing.SetFloat("_ZWrite", 0);
            glazing.SetOverrideTag("RenderType", "Transparent");
            glazing.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glazing.SetShaderPassEnabled("ShadowCaster", false);
            glazing.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(glazing);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D Texture(string name, bool normal = false)
        {
            string path = Root + "/Textures/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing furniture texture: " + path);
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
            string path = Root + "/Materials/BCF_" + name + ".mat";
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
            string path = Root + "/Models/BCF_" + name + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing furniture model: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Keep the FBX root's unit / axis conversion; layout lives on its parent.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            if (go.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Furniture FBX must not generate colliders: " + name);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i].name;
                    materials[i] = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + materialName + ".mat");
                    if (materials[i] == null) throw new InvalidOperationException("Missing furniture material: " + materialName);
                }
                r.sharedMaterials = materials;
            }
            return go;
        }
    }
}
