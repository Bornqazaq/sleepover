using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender hero props and their explicit URP materials.</summary>
    internal static class BelieveTablePropsAssets
    {
        internal const string Root = "Assets/_Project/Art/BelieveTableProps";

        internal static void Prepare()
        {
            System.IO.Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Lit("Walnut", Color.white, .64f, "BTP_Walnut", "BTP_WoodNormal", .20f);
            Lit("Felt", Color.white, .04f, "BTP_Felt", "BTP_FeltNormal", .22f);
            Lit("Leather", Color.white, .32f, "BTP_Leather", "BTP_LeatherNormal", .25f);
            Lit("Seam", new Color32(69, 32, 15, 255), .24f);
            var casket = Lit("Casket", Color.white, 1, "BTP_Casket");
            casket.SetTexture("_MetallicGlossMap", Texture("BTP_CasketMetalSmooth", false));
            casket.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(casket);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D Texture(string name, bool srgb = true, bool normal = false)
        {
            var path = Root + "/Textures/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing prop texture " + path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture != srgb || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Lit(string name, Color color, float smooth, string albedo = null,
            string normal = null, float bump = 1)
        {
            var path = Root + "/Materials/BTP_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Metallic", 0);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetTexture("_BaseMap", albedo == null ? null : Texture(albedo));
            mat.SetTexture("_BumpMap", normal == null ? null : Texture(normal, false, true));
            mat.SetFloat("_BumpScale", bump);
            if (normal == null) mat.DisableKeyword("_NORMALMAP"); else mat.EnableKeyword("_NORMALMAP");
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static GameObject Model(Transform parent, string name)
        {
            var path = Root + "/Models/BTP_" + name + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing original prop " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Keep imported FBX axis conversion and unit scale intact.
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            if (go.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Hero prop must have no colliders: " + path);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var materialName = mats[i].name;
                    mats[i] = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + materialName + ".mat");
                    if (mats[i] == null) throw new InvalidOperationException("Missing prop material " + materialName);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            return go;
        }
    }
}
