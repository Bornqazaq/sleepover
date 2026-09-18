using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender kit and explicit URP equivalents; no store dependencies.</summary>
    internal static class BelievePrivateClubAssets
    {
        internal const string Root = "Assets/_Project/Art/BelievePrivateClub";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        internal static void Prepare()
        {
            Materials.Clear();
            System.IO.Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            Lit("Navy", new Color32(24, 34, 53, 255), .24f, 0, "BPC_TimberGrain");
            Lit("Wood", new Color32(39, 27, 23, 255), .32f, 0, "BPC_TimberGrain");
            Lit("Brass", new Color32(104, 73, 35, 255), .42f, .65f);
            Lit("Velvet", new Color32(76, 13, 24, 255), .02f, 0);
            Lit("Fringe", new Color32(119, 89, 45, 255), .15f, .15f);
            Lit("Ceiling", new Color32(17, 22, 31, 255), .08f, 0);
            Lit("Carpet", Color.white, .01f, 0, "BPC_WovenCarpet");
            Lit("Lining", new Color32(193, 154, 93, 255), .28f, .15f);
            var brass = Lit("LampBrass", new Color32(104, 73, 35, 255), .40f, .45f);
            brass.EnableKeyword("_EMISSION");
            brass.SetColor("_EmissionColor", new Color(.012f, .007f, .0025f));
            brass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            var lining = Get("Lining");
            lining.EnableKeyword("_EMISSION");
            lining.SetColor("_EmissionColor", new Color(.16f, .09f, .035f));
            lining.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            var bulb = Lit("Bulb", new Color(1f, .72f, .35f), .15f, 0);
            bulb.EnableKeyword("_EMISSION");
            bulb.SetColor("_EmissionColor", new Color(2.5f, 1.4f, .55f));
            bulb.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            var volume = NewMaterial("Volume", "Igruha/BelieveOrNot/ClubVolume");
            volume.SetColor("_BaseColor", new Color(.75f, .65f, .50f, .002f));
            volume.SetFloat("_LampHeight", BelievePrivateClubBuilder.LampHeight);
            volume.SetFloat("_LampRange", BelievePrivateClubBuilder.LampRange);
            volume.SetFloat("_ConeRadius", BelievePrivateClubBuilder.LampHeight * Mathf.Tan(55 * Mathf.Deg2Rad));
            var dust = NewMaterial("Dust", "Universal Render Pipeline/Particles/Unlit");
            dust.SetTexture("_BaseMap", Texture("BPC_Smoke"));
            dust.SetColor("_BaseColor", new Color(1f, .73f, .38f, .38f));
            dust.SetFloat("_Surface", 1);
            dust.SetFloat("_Blend", 0);
            dust.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            dust.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            dust.SetFloat("_ZWrite", 0);
            dust.SetOverrideTag("RenderType", "Transparent");
            dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dust.renderQueue = 3000;
            foreach (var material in Materials.Values) EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        internal static Material Get(string name) => Materials.TryGetValue(name, out var m)
            ? m : AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BPC_" + name + ".mat");

        private static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name + ".png");

        private static Material NewMaterial(string name, string shader)
        {
            var found = Shader.Find(shader);
            if (found == null) throw new InvalidOperationException("Missing club shader: " + shader);
            var path = Root + "/Materials/BPC_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(found); AssetDatabase.CreateAsset(m, path); }
            m.shader = found;
            Materials[name] = m;
            return m;
        }

        private static Material Lit(string name, Color color, float smooth, float metal, string texture = null)
        {
            var m = NewMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", texture == null ? null : Texture(texture));
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.enableInstancing = true;
            m.SetFloat("_Cull", name == "Velvet" || name == "Lining" ? 0 : 2);
            return m;
        }

        internal static GameObject Model(Transform parent, string model, Vector3 position, float yaw = 0, Vector3? scale = null)
        {
            var path = Root + "/Models/BPC_" + model + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing original club model: " + path);
            var holder = new GameObject(model);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = position;
            holder.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            holder.transform.localScale = scale ?? Vector3.one;
            var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            foreach (var t in child.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            foreach (var c in child.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in child.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var key = mats[i].name.Replace("BPC_", "");
                    if (model == "Pendant" && key == "Brass") key = "LampBrass";
                    mats[i] = Get(key) ?? throw new InvalidOperationException("Missing club material: " + key);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return holder;
        }
    }
}
