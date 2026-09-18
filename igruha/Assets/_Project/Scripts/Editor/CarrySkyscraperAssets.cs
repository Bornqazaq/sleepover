using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Импорт собственного набора «Переноски»: материалы из palette.json и все FBX
    /// папки Original. Палитру пишут два генератора — carry_skyscraper.py (база)
    /// и carry_skyscraper_city.py (город, улица, ярусы, бак, движение), поэтому
    /// у записи есть необязательные поля: карта свечения, тайлинг, unlit,
    /// вырез, прозрачность и двусторонность. Отсутствующее поле — значение по умолчанию.
    /// </summary>
    internal static class CarrySkyscraperAssets
    {
        internal const string Art = "Assets/_Project/Art/CarryItem/Original";
        internal const string Materials = "Assets/_Project/Materials/Minigames/CarryItem/Original";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        [Serializable] private sealed class Palette { public Entry[] materials; }
        [Serializable] private sealed class Entry
        {
            public string name; public float[] color; public float roughness;
            public float metallic; public string texture; public float emission;
            public float tiling; public string emissionTexture;
            public bool unlit; public bool cutout; public bool transparent; public bool doubleSided;
        }

        [MenuItem("Igruha/Переноска предмета/Импортировать собственную стройку")]
        internal static void Import()
        {
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Art + "/palette.json"));
            var map = new Dictionary<string, Material>();
            foreach (var e in palette.materials)
            {
                string path = Materials + "/" + e.name + ".mat";
                string shaderName = e.unlit ? UnlitShader : LitShader;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find(shaderName));
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader == null || material.shader.name != shaderName)
                {
                    material.shader = Shader.Find(shaderName);
                }

                float alpha = e.color.Length > 3 ? e.color[3] : 1f;
                var color = new Color(e.color[0], e.color[1], e.color[2], alpha);
                material.SetColor("_BaseColor", color);
                material.enableInstancing = true;
                if (!e.unlit)
                {
                    material.SetFloat("_Metallic", e.metallic);
                    material.SetFloat("_Smoothness", 1 - e.roughness);
                }

                ConfigureSurface(material, e);

                material.SetTexture("_BaseMap", string.IsNullOrEmpty(e.texture) ? null : Texture(e.texture));
                float tiling = e.tiling > 0 ? e.tiling : 1f;
                material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));

                bool glows = e.emission > 0 && !e.unlit;
                material.globalIlluminationFlags = glows
                    ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (glows) material.EnableKeyword("_EMISSION");
                else material.DisableKeyword("_EMISSION");
                if (!e.unlit)
                {
                    var emissionMap = string.IsNullOrEmpty(e.emissionTexture) ? null : Texture(e.emissionTexture);
                    material.SetTexture("_EmissionMap", emissionMap);
                    // С картой свечения цвет — множитель карты, без неё — сам цвет поверхности.
                    material.SetColor("_EmissionColor", glows
                        ? (emissionMap != null ? Color.white : color).linear * e.emission
                        : Color.black);
                }
                EditorUtility.SetDirty(material); map[e.name] = material;
            }
            foreach (string path in Directory.GetFiles(Art + "/Models", "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion = true;
                importer.addCollider = false;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false;
                foreach (var e in map)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), e.Key), e.Value);
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Режим поверхности URP: непрозрачная, вырез по альфе или прозрачная; двусторонность.</summary>
        private static void ConfigureSurface(Material material, Entry e)
        {
            material.SetFloat("_Cull", e.doubleSided ? 0 : 2);
            if (e.transparent)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0); material.SetFloat("_AlphaClip", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return;
            }

            material.SetFloat("_Surface", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (e.cutout)
            {
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .4f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else
            {
                material.SetFloat("_AlphaClip", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = -1;
            }
        }

        private static Texture2D Texture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/CS_" + name + ".png");

        internal static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(Materials + "/CS_" + name + ".mat");

        internal static Transform Place(Transform parent, string model, Vector3 position, float yaw = 0)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/CS_" + model + ".fbx");
            if (asset == null) throw new InvalidOperationException("Missing original skyscraper model: " + model);
            var go = new GameObject("CS_" + model);
            go.transform.SetParent(parent, false);
            PrefabUtility.InstantiatePrefab(asset, go.transform);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return go.transform;
        }
    }
}
