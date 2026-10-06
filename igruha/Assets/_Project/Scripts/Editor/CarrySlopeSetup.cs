using Igruha.Core.Player;
using Igruha.Minigames.CarryItem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    public static class CarrySlopeSetup
    {
        private const string CartPath = "Assets/_Project/Prefabs/Minigames/CarryItem/WaterCart.prefab";

        [MenuItem("Tools/Minigames/CarryItem/Repair wheel and ramp contacts")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "CarryItem")
                throw new System.InvalidOperationException("Open CarryItem in Edit mode.");
            var root = PrefabUtility.LoadPrefabContents(CartPath);
            try
            {
                ConfigureCart(root);
                PrefabUtility.SaveAsPrefabAsset(root, CartPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var ramp = GameObject.Find("_Arena/HeistRoutes/UpperRoute/Continuous flyover collision");
            // Resolve by the authored collision name, irrespective of the art group's name.
            foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                if (collider.name == "Continuous flyover collision") ramp = collider.gameObject;
            if (ramp == null) throw new System.InvalidOperationException("Flyover collision missing.");
            if (!ramp.TryGetComponent<WalkableRamp>(out _)) ramp.AddComponent<WalkableRamp>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        internal static void ConfigureCart(GameObject root)
        {
            // Rounded tyre contacts climb continuous slope transitions. The tank's
            // rectangular envelope stops walls but no longer scrapes along the road.
            var box = root.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, .65f, 0f);
            box.size = new Vector3(.9f, .7f, 1.2f);
            foreach (var old in root.GetComponents<SphereCollider>()) Object.DestroyImmediate(old);
            var material = CarryWaterArt.RollingMaterial();
            for (int i = 0; i < 4; i++)
            {
                var wheel = root.AddComponent<SphereCollider>();
                wheel.center = new Vector3(i % 2 == 0 ? -CartWheelSupport.HalfTrack : CartWheelSupport.HalfTrack,
                    CartWheelSupport.Radius, i < 2 ? CartWheelSupport.HalfBase : -CartWheelSupport.HalfBase);
                wheel.radius = CartWheelSupport.Radius;
                wheel.sharedMaterial = material;
            }
            if (!root.TryGetComponent<CartWheelSupport>(out _)) root.AddComponent<CartWheelSupport>();
        }
    }
}
