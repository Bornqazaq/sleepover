using UnityEditor;
using UnityEngine;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>Detailed contacts for defeated bodies, without new walkable shortcuts or camera obstacles.</summary>
    internal static class MemoryRunBodyCollisionBuilder
    {
        internal const string RootName = "_BodyContacts";
        private const string DetailLayer = "Eliminated";
        private const string BodyLayer = "Ignore Raycast";
        private const float PaneThickness = .04f;
        private const float BeltWidth = 1.54f, BeltThickness = .133f;
        private const int BeltPathStride = 4;

        internal static void Build(Transform arena)
        {
            var old = arena.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(arena, false);
            foreach (var filter in arena.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !renderer.enabled) continue;
                if (mesh.name == "MF_LightShaft" || mesh.name == "MF_BeltLink") continue;
                if (mesh.name == "Quad" && filter.name != "ObservationPane") continue;
                // These meshes already have a precise solid contact on this very transform.
                var existing = filter.GetComponent<Collider>();
                if (existing != null && existing.enabled && !existing.isTrigger) continue;
                var proxy = new GameObject("Contact_" + filter.name);
                proxy.transform.SetParent(root, false);
                proxy.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                proxy.transform.localScale = filter.transform.lossyScale;
                bool cargo = mesh.name == "MF_DynamiteBundle" || mesh.name == "MF_DynamiteHeavy";
                bool roller = mesh.name == "MF_ConveyorRoller";
                Collider shape;
                if (mesh.name == "Cube" || mesh.name == "Quad" || cargo)
                {
                    var box = proxy.AddComponent<BoxCollider>();
                    box.center = mesh.bounds.center;
                    var size = mesh.bounds.size;
                    if (mesh.name == "Quad") size.z = PaneThickness;
                    box.size = size;
                    shape = box;
                }
                else
                {
                    var geometry = proxy.AddComponent<MeshCollider>();
                    geometry.sharedMesh = mesh;
                    shape = geometry;
                }
                Configure(shape);
                if (cargo)
                {
                    var body = proxy.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    var follower = proxy.AddComponent<MemoryRunBodyContactFollower>();
                    var so = new SerializedObject(follower);
                    so.FindProperty("target").objectReferenceValue = filter.transform;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                // Rollers rotate around their symmetry axis: their collision hull stays static.
                if (roller) proxy.name = "Contact_StaticRoller";
            }
            foreach (var conveyor in arena.GetComponentsInChildren<MemoryFoundryConveyor>())
                BuildBelt(root, conveyor);
        }

        private static void Configure(Collider shape)
        {
            shape.gameObject.layer = LayerMask.NameToLayer(DetailLayer);
            int bodyMask = LayerMask.GetMask(BodyLayer);
            shape.includeLayers = bodyMask;
            shape.excludeLayers = ~bodyMask;
            shape.layerOverridePriority = 1;
        }

        private static void BuildBelt(Transform root, MemoryFoundryConveyor line)
        {
            var so = new SerializedObject(line);
            var path = so.FindProperty("topPath");
            float radius = so.FindProperty("radius").floatValue;
            // A continuous thin skin covers moving links, instead of hundreds of moving colliders.
            for (int i = 0; i < path.arraySize - 1; i += BeltPathStride)
            {
                int end = Mathf.Min(i + BeltPathStride, path.arraySize - 1);
                Vector3 from = path.GetArrayElementAtIndex(i).vector3Value;
                Vector3 to = path.GetArrayElementAtIndex(end).vector3Value;
                foreach (float offset in new[] { 0f, -2 * radius })
                {
                    var proxy = new GameObject("Contact_BeltSurface");
                    proxy.transform.SetParent(root, false);
                    proxy.transform.SetPositionAndRotation(line.transform.TransformPoint((from + to) * .5f + Vector3.up * offset),
                        line.transform.rotation * Quaternion.LookRotation(to - from));
                    var box = proxy.AddComponent<BoxCollider>();
                    box.size = new Vector3(BeltWidth, BeltThickness, Vector3.Distance(from, to) + BeltThickness);
                    Configure(box);
                }
            }
        }
    }
}
