using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Igruha.Networking;

namespace Igruha.EditorTools
{
    public static class MosquitoesCoreSetup
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Minigames/Mosquitoes/Mosquito.prefab";
        [MenuItem("Igruha/Minigames/Комары/Core setup")]
        public static void Build()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layer = tags.FindProperty("layers").GetArrayElementAtIndex(12);
            if (layer.stringValue != "" && layer.stringValue != "Mosquito") throw new System.InvalidOperationException("Layer 12 is occupied.");
            layer.stringValue = "Mosquito"; tags.ApplyModifiedPropertiesWithoutUndo();
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(12, i, i != LayerMask.NameToLayer("Ground") && i != LayerMask.NameToLayer("Cover"));
            EnsureFolder("Assets/_Project/Prefabs/Minigames/Mosquitoes");
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing == null)
            {
                var go = new GameObject("Mosquito"); go.layer = 12;
                go.AddComponent<NetworkObject>(); go.AddComponent<ClientNetworkTransform>();
                var collider = go.AddComponent<SphereCollider>(); collider.radius = .12f;
                var rb = go.AddComponent<Rigidbody>(); rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                existing = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath); Object.DestroyImmediate(go);
            }
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            bool found = false;
            foreach (var p in list.PrefabList) if (p.Prefab == existing) found = true;
            if (!found) { list.Add(new NetworkPrefab { Prefab = existing }); EditorUtility.SetDirty(list); }
            AssetDatabase.SaveAssets();
        }
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
