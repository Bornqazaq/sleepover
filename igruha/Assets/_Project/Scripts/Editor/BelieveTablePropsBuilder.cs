using System;
using System.Text;
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

        /// <summary>Верх подушки кресла без подъёма, м. Пара SEAT в tools/blender/believe_table_props.py.</summary>
        private const float ChairSeatHeight = .38f;

        /// <summary>Низ корпуса кресла и верх ножек без подъёма, м. Пара LEG_TOP в том же скрипте.</summary>
        private const float ChairLegHeight = .23f;

        /// <summary>Насколько подушка проминается под сидящим, м: без этого между ними светится щель.</summary>
        private const float ChairCushionSink = .008f;

        /// <summary>
        /// Площадка подушки в осях сидящего (X вбок, Z вперёд), м. Передний край в 0.09 м
        /// за точкой посадки: перед ним висят голени, и в обмер сиденья они попадать не должны.
        /// </summary>
        private static readonly Rect ChairCushionFootprint = new Rect(-.40f, -.40f, .80f, .31f);

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
            table.ConfigureLayout(config.BoxOffset, config.TableHeight + config.BoxSize * .5f, config.BoxSideOffset);
            DressTable(table.transform.Find("TableTop").gameObject, config);
            var chairs = new BelieveChairFit[BelieveTable.SeatCount];
            for (int i = 0; i < BelieveTable.SeatCount; i++)
            {
                var seat = table.GetSeatAnchor(i);
                var direction = seat.position - table.transform.position;
                direction.y = 0;
                chairs[i] = DressChair(table.transform.Find("Chair_" + i).gameObject, direction.normalized, direction.magnitude);
                var box = table.GetBox(i);
                box.transform.position = table.GetBoxPosition(i);
                DressBox(box, box.transform.Find("Body").gameObject, box.transform.Find("LidHinge"),
                    box.transform.Find("LidHinge/Lid").gameObject, config.BoxSize);
            }
            AssignChairs(table, chairs);
            MeasureChairLifts(config);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("IGR-565: original round table, identical mahogany caskets and barrel chairs applied. Lighting and blockout preserved.");
        }

        /// <summary>Подключить кресла к столу: по ним игра подгоняет места под севших.</summary>
        internal static void AssignChairs(BelieveTable table, BelieveChairFit[] chairs)
        {
            var so = new SerializedObject(table);
            var array = so.FindProperty("chairs");
            array.arraySize = chairs.Length;
            for (int i = 0; i < chairs.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = chairs[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Обмерить сидячие позы всего ростера и записать в конфиг подъём кресла под
        /// каждого: подушка встаёт под самую низкую точку таза и бёдер и проминается
        /// на <see cref="ChairCushionSink"/>. Разброс по ростеру — около двадцати
        /// сантиметров, поэтому подъём свой у каждого, а не один на всех.
        /// </summary>
        internal static void MeasureChairLifts(BelieveOrNotConfig config)
        {
            var so = new SerializedObject(config);
            var lifts = so.FindProperty("chairLifts");
            lifts.arraySize = 0;
            var report = new StringBuilder("IGR-565: подъём кресла под сидящих, м\n");
            string[] names = BelieveOrNotSitClipBuilder.CharacterNames;
            for (int c = 0; c < names.Length; c++)
            {
                if (!BelieveOrNotSitClipBuilder.TryMeasureSeatContact(c, ChairCushionFootprint, out var avatar, out var contact))
                {
                    continue;
                }

                for (int i = 0; i < lifts.arraySize; i++)
                {
                    if (lifts.GetArrayElementAtIndex(i).FindPropertyRelative("avatar").objectReferenceValue == avatar)
                        throw new InvalidOperationException($"Аватар {avatar.name} общий у двух персонажей: подъём кресла по нему не различить.");
                }

                float lift = contact + ChairCushionSink - ChairSeatHeight;
                lifts.arraySize++;
                var entry = lifts.GetArrayElementAtIndex(lifts.arraySize - 1);
                entry.FindPropertyRelative("avatar").objectReferenceValue = avatar;
                entry.FindPropertyRelative("lift").floatValue = lift;
                report.AppendLine($"  {names[c],-8} касание {contact:F3}, подъём {lift:+0.000;-0.000}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        internal static void DressTable(GameObject top, BelieveOrNotConfig config)
        {
            var holder = Holder(top.transform);
            holder.position = top.transform.parent.position;
            var model = Child(holder, "RoundTable", Vector3.zero, Quaternion.identity);
            model.localScale = new Vector3(config.TableDiameter / TableDiameter, config.TableHeight / TableHeight,
                config.TableDiameter / TableDiameter);
        }

        internal static BelieveChairFit DressChair(GameObject blockout, Vector3 direction, float distance)
        {
            var holder = Holder(blockout.transform);
            var chair = new GameObject("BarrelChair").transform;
            chair.SetParent(holder, false);
            chair.position = blockout.transform.parent.TransformPoint(direction * distance);
            chair.rotation = blockout.transform.parent.rotation * Quaternion.LookRotation(-direction, Vector3.up);
            // Корпус и ножки — отдельные модели под своими узлами: подгонка под
            // сидящего двигает узел корпуса и тянет узел ножек, не трогая импорт FBX.
            var body = Child(chair, "BarrelChair", Vector3.zero, Quaternion.identity);
            body.name = "Body";
            var legs = Child(chair, "BarrelChairLegs", Vector3.zero, Quaternion.identity);
            legs.name = "Legs";
            var fit = chair.gameObject.AddComponent<BelieveChairFit>();
            fit.Configure(body, legs, ChairLegHeight);
            blockout.layer = 0;
            return fit;
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
