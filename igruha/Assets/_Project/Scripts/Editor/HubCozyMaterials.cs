using System;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Materials owned by the hub; never edits the shared Synty palette.</summary>
    internal static class HubCozyMaterials
    {
        internal const string Folder = "Assets/_Project/Materials/Hub/Cozy";
        private const string Shops = "Assets/Synty/PolygonShops/Textures/";
        private const string TextureFolder = "Assets/_Project/Art/Hub/Cozy/Textures";

        internal static void PrepareTextures()
        {
            LocalTexture("PolygonShops_Building_Carpet_03");
        }

        private static Texture LocalTexture(string name)
        {
            EnsureFolder(TextureFolder);
            string path = TextureFolder + "/" + name + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture != null) return texture;
            if (!AssetDatabase.CopyAsset(Shops + name + ".png", path))
                throw new InvalidOperationException("Missing texture: " + name);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture>(path)
                ?? throw new InvalidOperationException("Texture import failed: " + name);
        }

        internal static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString("#" + value, out Color color))
                throw new ArgumentException(value);
            return color;
        }

        internal static Material Surface(string name, string hex, float smoothness = .08f,
            string texture = null, Vector2? tiling = null, float glow = 0)
        {
            EnsureFolder(Folder);
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit is missing.");
                material = new Material(shader) { name = name, enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
            Color color = Hex(hex);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_Cull", 0);
            Texture map = null;
            if (texture != null)
            {
                map = LocalTexture(texture);
            }
            material.SetTexture("_BaseMap", map);
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            material.SetColor("_EmissionColor", color * glow);
            if (glow > 0) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = glow > 0
                ? MaterialGlobalIlluminationFlags.BakedEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static void Assign(GameObject target, Material material)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        internal static void EnsureFolder(string path)
        {
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
