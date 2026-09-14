using System;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;
using Igruha.Minigames.Stopwatch;

namespace Igruha.EditorTools
{
    /// <summary>Original tin cans and button pedestal, preserving interaction and all five symbols.</summary>
    internal static class CircusNightProps
    {
        internal const string CanPrefab = CircusNightAssets.Prefabs + "/CN_Can.prefab";
        private const string OriginalCan = "Assets/_Project/Prefabs/Minigames/CansOrder/Can.prefab";

        internal static void BuildCanPrefab()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(OriginalCan);
            if(source==null)throw new InvalidOperationException("The can symbols prefab is missing.");
            var copy=UnityEngine.Object.Instantiate(source);copy.name="CN_Can";
            try
            {
                var old=copy.transform.Find("Body");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                GameObject body=Add(copy.transform,"CN_TinBody",new Vector3(0,-.12f,0));
                Add(copy.transform,"CN_TinTrim",new Vector3(0,-.12f,0));
                var data=new SerializedObject(copy.GetComponent<Can>());
                data.FindProperty("body").objectReferenceValue=body.GetComponentInChildren<MeshRenderer>();
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(copy,CanPrefab);
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }

        internal static void Apply(Transform arena)
        {
            foreach(var shelf in arena.GetComponentsInChildren<CanShelf>(true))
            {
                var data=new SerializedObject(shelf);data.FindProperty("canPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(CanPrefab);data.ApplyModifiedPropertiesWithoutUndo();
                foreach(var r in shelf.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if(r.name=="Board" || r.name=="Bench")r.sharedMaterial=CircusNightAssets.Material("CN_WalnutLight");
                    else if(r.name.StartsWith("Support_"))r.sharedMaterial=CircusNightAssets.Material("CN_BlackIron");
                }
            }
            foreach(var button in arena.GetComponentsInChildren<CageButton>(true))
            {
                var barrel=button.transform.Find("Barrel");
                if(barrel!=null)foreach(var renderer in barrel.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
                var previous=button.transform.Find("CN_ButtonPedestal");
                if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
                Add(button.transform,"CN_ButtonPedestal",Vector3.zero);
                var rim=button.transform.Find("Rim");if(rim!=null)rim.GetComponent<MeshRenderer>().sharedMaterial=CircusNightAssets.Material("CN_AgedBrass");
            }
        }

        private static GameObject Add(Transform parent,string name,Vector3 localPosition)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(CircusNightAssets.Prefabs+"/"+name+".prefab");
            if(source==null)throw new InvalidOperationException("Missing original cage prop: "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);go.name=name;go.transform.localPosition=localPosition;
            return go;
        }
    }
}
