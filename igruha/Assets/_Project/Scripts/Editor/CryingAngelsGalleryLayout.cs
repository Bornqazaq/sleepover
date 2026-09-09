using System;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Art-directed cover positions explicitly authorized for the night gallery.</summary>
    internal static class CryingAngelsGalleryLayout
    {
        private const string Path = CryingAngelsGalleryAssets.Art + "/CA_NightLayout.json";
        [Serializable] private sealed class Layout { public float referenceRadius; public Exhibit[] covers; }
        [Serializable] private sealed class Exhibit { public string name, model; public Vector3 position, size; public float yaw; }

        internal static void Build(Transform parent, float radius)
        {
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(Path);
            if(asset==null) throw new InvalidOperationException("Missing authored gallery layout: "+Path);
            var layout=JsonUtility.FromJson<Layout>(asset.text);
            if(layout==null || layout.covers==null || layout.referenceRadius<=0) throw new InvalidOperationException("Invalid gallery layout.");
            for(int i=parent.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
            float ratio=radius/layout.referenceRadius;
            foreach(var row in layout.covers)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name=row.name;go.layer=LayerMask.NameToLayer("Cover");go.transform.SetParent(parent,false);
                go.transform.localPosition=Vector3.Scale(row.position,new Vector3(ratio,1,ratio));
                go.transform.localRotation=Quaternion.Euler(0,row.yaw,0);
                go.transform.localScale=Vector3.Scale(row.size,new Vector3(ratio,1,ratio));
                go.GetComponent<Renderer>().enabled=false;
                var visual=CryingAngelsGalleryAssets.Place(row.model,go.transform,Vector3.zero,Quaternion.identity);
                visual.name="_GalleryVisual";
                CryingAngelsGalleryBuilder.FitToUnitBox(visual);
            }
        }

        internal static void AddDetails(Transform gallery, float radius)
        {
            var root=new GameObject("ScatteredRemains");root.transform.SetParent(gallery,false);
            var random=new System.Random(933);
            const int FragmentGroups=65;
            for(int i=0;i<FragmentGroups;i++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2;
                float r=3.8f+(float)random.NextDouble()*(radius-5.5f);
                var go=CryingAngelsGalleryAssets.Place("CA_StoneFragments",root.transform,new Vector3(Mathf.Sin(angle)*r,.025f,Mathf.Cos(angle)*r),Quaternion.Euler(0,(float)random.NextDouble()*360,0));
                float scale=.55f+(float)random.NextDouble()*.65f;go.transform.localScale=new Vector3(scale,.8f,scale);
            }
            for(int i=0;i<12;i++)
            {
                float angle=i*30f+11.25f;
                Vector3 dir=Quaternion.Euler(0,angle,0)*Vector3.forward;
                var banner=CryingAngelsGalleryAssets.Place("CA_TatteredBanner",root.transform,dir*(radius-.5f)+Vector3.up*4.8f,Quaternion.Euler(0,angle+180,0));
                banner.transform.localScale=new Vector3(1.25f,1.4f,1f);
            }
        }
    }
}
