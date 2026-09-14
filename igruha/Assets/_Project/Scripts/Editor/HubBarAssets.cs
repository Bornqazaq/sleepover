using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace Igruha.EditorTools
{
    /// <summary>Copies only static prop meshes and their albedo into the hub. No prefab scripts or physics.</summary>
    internal static class HubBarAssets
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Bar";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/";
        private static readonly Dictionary<UnityEngine.Object, UnityEngine.Object> Copies = new();

        internal static void Begin() => Copies.Clear();

        internal static TMP_FontAsset LetteringFont()
        {
            const string source = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Oswald Bold SDF.asset";
            const string target = Folder + "/Fonts/HubBarOswald.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target);
            if (font != null) return font;
            HubCozyMaterials.EnsureFolder(Folder + "/Fonts");
            if (!AssetDatabase.CopyAsset(source, target)) throw new InvalidOperationException("Bar lettering font is missing");
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target)
                ?? throw new InvalidOperationException("Could not import the bar lettering font");
        }

        internal static GameObject Prop(string relativePath, Transform parent, string name)
        {
            string bakedPath = Folder + "/Props/" + Path.GetFileName(relativePath);
            var baked = AssetDatabase.LoadAssetAtPath<GameObject>(bakedPath);
            if (baked != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(baked, parent);
                instance.name = name;
                return instance;
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Shops + relativePath);
            if (source == null) throw new InvalidOperationException("Hub prop missing: " + relativePath);
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                var child = new GameObject(filter.name);
                child.transform.SetParent(result.transform, false);
                child.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                child.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                Vector3 scale = filter.transform.lossyScale, rootScale = source.transform.lossyScale;
                child.transform.localScale = new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z);
                child.AddComponent<MeshFilter>().sharedMesh = CopyMesh(filter.sharedMesh);
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = CopyMaterial(materials[i]);
                child.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            if (result.GetComponentsInChildren<MeshRenderer>().Length == 0)
                throw new InvalidOperationException("Prop contains no static meshes: " + relativePath);
            HubCozyMaterials.EnsureFolder(Folder + "/Props");
            PrefabUtility.SaveAsPrefabAsset(result, bakedPath);
            return result;
        }

        internal static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No prop bounds: " + go.name);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        internal static void Place(GameObject prop, Vector3 bottomCenter, float scale, float yaw = 90)
        {
            prop.transform.rotation = Quaternion.Euler(0, yaw, 0);
            prop.transform.localScale = Vector3.one * scale;
            Bounds bounds = BoundsOf(prop);
            prop.transform.position += bottomCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        internal static Mesh SaveMesh(string name, Mesh source)
        {
            source.name = name;
            HubCozyMaterials.EnsureFolder(Folder + "/Meshes");
            string path = Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { source.name = name; AssetDatabase.CreateAsset(source, path); return source; }
            EditorUtility.CopySerialized(source, mesh);
            UnityEngine.Object.DestroyImmediate(source);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh CopyMesh(Mesh source)
        {
            if (Copies.TryGetValue(source, out var cached)) return (Mesh)cached;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id);
            string name = source.name + "_" + guid.Substring(0, 8) + "_" + id;
            string path = Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) mesh = SaveMesh(name, UnityEngine.Object.Instantiate(source));
            Copies[source] = mesh;
            return mesh;
        }

        private static Material CopyMaterial(Material source)
        {
            if (source == null) throw new InvalidOperationException("Missing prop material");
            if (Copies.TryGetValue(source, out var cached)) return (Material)cached;
            Texture texture = source.HasProperty("_Albedo_Map") ? source.GetTexture("_Albedo_Map") : source.mainTexture;
            var material = HubCozyMaterials.Surface("HB_" + source.name, "FFFFFF", .14f);
            if (texture != null)
            {
                string sourcePath = AssetDatabase.GetAssetPath(texture);
                HubCozyMaterials.EnsureFolder(Folder + "/Textures");
                string path = Folder + "/Textures/" + Path.GetFileName(sourcePath);
                var local = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (local == null)
                {
                    if (!AssetDatabase.CopyAsset(sourcePath, path)) throw new InvalidOperationException("Cannot copy " + sourcePath);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    local = AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
                if (local == null) throw new InvalidOperationException("Prop texture import failed: " + path);
                material.SetTexture("_BaseMap", local);
            }
            EditorUtility.SetDirty(material);
            Copies[source] = material;
            return material;
        }
    }
}
