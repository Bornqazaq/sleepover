using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Original Blender kit. A dressed solid has exactly one gameplay collider.</summary>
    internal static class CarryItemDress
    {
        internal enum Kind { None, Rubble, Plank, Stash, Beam, Floor, WallFacade, WallCrown, PitWall, ChasmLip, Brick }
        private const string Models = CarrySkyscraperAssets.Art + "/Models/CS_";
        internal const string PalletPath = Models + "Pallet.fbx";
        internal const string WheelbarrowPath = Models + "Wheelbarrow.fbx";
        internal const string StandpipePath = Models + "Standpipe.fbx";
        internal const string SpoutPath = Models + "Spout.fbx";
        internal const string LadderPath = Models + "Ladder.fbx";
        internal const string OutletPath = Models + "Spout.fbx";
        private static readonly List<string> missing = new List<string>();
        internal static IReadOnlyList<string> Missing => missing;
        internal static void Begin() { missing.Clear(); }
        internal static string Report() => "CarryItem: original Blender construction kit; missing=" + missing.Count;

        internal static GameObject Apply(GameObject box, Kind kind, System.Random rng)
        {
            string model;
            switch (kind)
            {
                case Kind.Floor: model = "Slab"; break;
                case Kind.Plank: model = "Bridge"; break;
                case Kind.Rubble: model = "CoreBlock"; break;
                case Kind.Stash: model = "BrickStack"; break;
                case Kind.Beam: model = "Beam"; break;
                case Kind.Brick: model = "Brick"; break;
                default: return null;
            }
            var old = box.transform.Find("Dress");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Dress"); root.transform.SetParent(box.transform, false);
            // Parent block is scaled; model placement is in world metres.
            root.transform.localScale = new Vector3(1 / box.transform.lossyScale.x, 1 / box.transform.lossyScale.y, 1 / box.transform.lossyScale.z);
            var cage = box.GetComponent<Renderer>().bounds;
            if (kind == Kind.Floor)
            {
                int nx = Mathf.CeilToInt(cage.size.x / 4), nz = Mathf.CeilToInt(cage.size.z / 4);
                float sx = cage.size.x / nx, sz = cage.size.z / nz;
                for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                {
                    var go = Load(root.transform, model);
                    Fit(go, new Bounds(new Vector3(cage.min.x + sx * (x + .5f), cage.center.y, cage.min.z + sz * (z + .5f)), new Vector3(sx, cage.size.y, sz)));
                }
            }
            else
            {
                var go = Load(root.transform, model);
                if (kind == Kind.Beam) go.transform.localRotation = Quaternion.Euler(0, 90, 0) * go.transform.localRotation;
                Fit(go, cage);
            }
            box.GetComponent<Renderer>().enabled = false;
            return root;
        }
        private static GameObject Load(Transform parent, string model)
        {
            string path = Models + model + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing.Add(path); throw new InvalidOperationException("Missing original model: " + path); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return go;
        }
        internal static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }
        private static void Fit(GameObject go, Bounds cage)
        {
            var b = BoundsOf(go);
            // Apply axis-aligned scaling on a wrapper so quarter-turned beams fit correctly.
            var wrapper = new GameObject("Fit").transform;
            wrapper.SetParent(go.transform.parent, false); wrapper.position = b.center;
            go.transform.SetParent(wrapper, true);
            wrapper.localScale = new Vector3(cage.size.x / b.size.x, cage.size.y / b.size.y, cage.size.z / b.size.z);
            wrapper.position += cage.center - BoundsOf(go).center;
        }
        internal static GameObject Prop(Transform parent, string propName, string path, Vector3 position,
            float yaw, float targetHeight = 0, bool castShadows = true, bool byPivot = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing.Add(path); throw new InvalidOperationException(path); }
            // Preserve FBX's axis/unit correction on the imported child.
            var go = new GameObject(propName); go.transform.SetParent(parent, false);
            PrefabUtility.InstantiatePrefab(prefab, go.transform);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = BoundsOf(go);
            if (targetHeight > 0) { go.transform.localScale *= targetHeight / b.size.y; b = BoundsOf(go); }
            go.transform.position += position - new Vector3(byPivot ? go.transform.position.x : b.center.x, b.min.y, byPivot ? go.transform.position.z : b.center.z);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
