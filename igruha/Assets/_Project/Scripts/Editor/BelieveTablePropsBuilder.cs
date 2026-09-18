using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Art-only overlay. Never rebuilds lighting, player anchors or physical blockout.</summary>
    public static class BelieveTablePropsBuilder
    {
        private const float TableDiameter = 2.88f;
        private const float TableHeight = .72f;
        private const float CasketHalfWidth = .216f;
        private const float CasketHingeHeight = .137f;

        [MenuItem("Igruha/Believe Or Not/Apply Original Table Props")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before applying prop art.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Minigames/BelieveOrNot.unity")
                throw new InvalidOperationException("Open BelieveOrNot first.");
            var table = Object.FindFirstObjectByType<BelieveTable>();
            var configs = AssetDatabase.FindAssets("t:BelieveOrNotConfig");
            if (table == null || configs.Length != 1) throw new InvalidOperationException("Missing table or unique config.");
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(AssetDatabase.GUIDToAssetPath(configs[0]));
            BelieveTablePropsAssets.Prepare();
            DressTable(table.transform.Find("TableTop").gameObject, config);
            for (int i = 0; i < BelieveTable.SeatCount; i++)
            {
                var seat = table.GetSeatAnchor(i);
                var direction = seat.position - table.transform.position;
                direction.y = 0;
                DressChair(table.transform.Find("Chair_" + i).gameObject, direction.normalized, direction.magnitude);
                var box = table.GetBox(i);
                DressBox(box, box.transform.Find("Body").gameObject, box.transform.Find("LidHinge"),
                    box.transform.Find("LidHinge/Lid").gameObject, config.BoxSize);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("IGR-565: original round table, identical mahogany caskets and barrel chairs applied. Lighting and blockout preserved.");
        }

        internal static void DressTable(GameObject top, BelieveOrNotConfig config)
        {
            var holder = Holder(top.transform);
            holder.position = top.transform.parent.position;
            var model = Child(holder, "RoundTable", Vector3.zero, Quaternion.identity);
            model.localScale = new Vector3(config.TableDiameter / TableDiameter, config.TableHeight / TableHeight,
                config.TableDiameter / TableDiameter);
        }

        internal static void DressChair(GameObject blockout, Vector3 direction, float distance)
        {
            var holder = Holder(blockout.transform);
            var model = Child(holder, "BarrelChair", Vector3.zero, Quaternion.identity);
            model.position = blockout.transform.parent.TransformPoint(direction * distance);
            model.rotation = blockout.transform.parent.rotation * Quaternion.LookRotation(-direction, Vector3.up);
            blockout.layer = 0;
        }

        internal static void DressBox(BelieveBox box, GameObject body, Transform hinge, GameObject lidPlate,
            float boxSize)
        {
            // Both visible models and both closed hinges share the same world yaw.
            // Preserve the gameplay roots, slot positions, card pivots and all serialized references.
            var rotation = box.transform.parent.rotation;
            var bottom = box.transform.position - box.transform.parent.up * (boxSize * .5f);
            var bodyHolder = Holder(body.transform);
            bodyHolder.position = bottom;
            // Rotate below the scale-cancelling holder: rotating the holder itself would shear
            // a casket inherited from the non-uniformly scaled body blockout.
            var bodyModel = Child(bodyHolder, "CasketBody", Vector3.zero, Quaternion.identity);
            bodyModel.rotation = rotation;
            hinge.position = bottom + rotation * new Vector3(CasketHalfWidth, CasketHingeHeight, 0);
            hinge.rotation = rotation;
            var lidHolder = Holder(lidPlate.transform);
            lidHolder.position = hinge.position;
            lidHolder.rotation = rotation;
            BelieveTablePropsAssets.Model(lidHolder, "CasketLid");
            body.layer = lidPlate.layer = hinge.gameObject.layer = 0;
        }

        private static Transform Child(Transform parent, string model, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(model);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            BelieveTablePropsAssets.Model(go.transform, model);
            return go.transform;
        }

        private static Transform Holder(Transform host)
        {
            // Idempotent: remove only the previous visual layer, keep every gameplay object.
            var old = host.Find("Dress");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var renderer = host.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            var go = new GameObject("Dress");
            go.transform.SetParent(host, false);
            var scale = host.localScale;
            go.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            return go.transform;
        }
    }
}
