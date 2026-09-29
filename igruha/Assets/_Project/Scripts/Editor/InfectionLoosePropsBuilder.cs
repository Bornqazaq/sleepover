using UnityEditor;
using UnityEngine;
using Igruha.Core.Items;
using Igruha.Minigames.Infection;

namespace Igruha.EditorTools
{
    public static class InfectionLoosePropsBuilder
    {
        private const string BrickPath = "Assets/_Project/Prefabs/Minigames/CarryItem/Brick.prefab";
        private static readonly Vector3[] Positions =
        {
            new Vector3(-6, .35f, -8), new Vector3(5, .35f, -8),
            new Vector3(-6, .35f, 4), new Vector3(5, .35f, 4),
            new Vector3(-13, .35f, 1), new Vector3(13, .35f, 1)
        };
        public static void Build()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Infection")
                throw new System.InvalidOperationException("Open Infection first.");
            var old = GameObject.Find("_LooseProps"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("_LooseProps");
            var component = root.AddComponent<InfectionLooseProps>();
            var so = new SerializedObject(component); var items = so.FindProperty("items"); items.arraySize = Positions.Length;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrickPath);
            for (int i = 0; i < Positions.Length; i++)
            {
                var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                item.name = "CourtyardBrick_" + (i + 1);
                item.transform.SetPositionAndRotation(Positions[i], Quaternion.Euler(0, i * 37, 0));
                item.transform.localScale *= 1.5f;
                items.GetArrayElementAtIndex(i).objectReferenceValue = item.GetComponent<PickupItem>();
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
