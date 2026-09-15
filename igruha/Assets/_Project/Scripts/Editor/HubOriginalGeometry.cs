using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>UV-mapped architectural surfaces, batched by material. Physics is built separately.</summary>
    internal sealed class HubOriginalGeometry
    {
        private sealed class Batch { internal readonly List<Vector3> V = new(); internal readonly List<Vector2> U = new(); internal readonly List<int> T = new(); }
        private readonly Dictionary<Material, Batch> batches = new();
        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material mat, Vector2 tiling)
        {
            if (!batches.TryGetValue(mat, out var v)) batches.Add(mat, v = new Batch());
            int n = v.V.Count; v.V.AddRange(new[] {a,b,c,d});
            v.U.AddRange(new[] {Vector2.zero,new Vector2(0,tiling.y),tiling,new Vector2(tiling.x,0)});
            v.T.AddRange(new[] {n,n+1,n+2,n,n+2,n+3});
        }
        internal void Box(Vector3 p, Vector3 size, Material mat, Quaternion? rotation = null)
        {
            Quaternion q = rotation ?? Quaternion.identity;
            var x=q*Vector3.right*size.x*.5f; var y=q*Vector3.up*size.y*.5f; var z=q*Vector3.forward*size.z*.5f;
            Quad(p-x-y-z,p-x+y-z,p+x+y-z,p+x-y-z,mat,new Vector2(size.x, size.y));
            Quad(p+x-y+z,p+x+y+z,p-x+y+z,p-x-y+z,mat,new Vector2(size.x, size.y));
            Quad(p-x-y+z,p-x+y+z,p-x+y-z,p-x-y-z,mat,new Vector2(size.z, size.y));
            Quad(p+x-y-z,p+x+y-z,p+x+y+z,p+x-y+z,mat,new Vector2(size.z, size.y));
            Quad(p-x+y-z,p-x+y+z,p+x+y+z,p+x+y-z,mat,new Vector2(size.x*.7f, size.z*.7f));
            Quad(p-x-y+z,p-x-y-z,p+x-y-z,p+x-y+z,mat,new Vector2(size.x, size.z));
        }
        internal void Line(Vector3 a, Vector3 b, float width, Material mat) => Box((a+b)*.5f,new Vector3(width,width,Vector3.Distance(a,b)),mat,Quaternion.LookRotation(b-a));
        internal void Disc(Vector3 center, Vector3 right, Vector3 up, Material mat, int segments=40)
        {
            for(int i=0;i<segments;i++) {float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;Quad(center,center+right*Mathf.Cos(a)+up*Mathf.Sin(a),center+right*Mathf.Cos(b)+up*Mathf.Sin(b),center,mat,Vector2.one);}
        }
        internal void Sphere(Vector3 center,float radius,Material mat)
        {
            const int rings=8,sides=12;
            Vector3 Point(int ring,int side) {float phi=ring*Mathf.PI/rings,a=side*Mathf.PI*2/sides;return center+new Vector3(Mathf.Sin(phi)*Mathf.Cos(a),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(a))*radius;}
            for(int j=0;j<rings;j++)for(int i=0;i<sides;i++)Quad(Point(j,i),Point(j,i+1),Point(j+1,i+1),Point(j+1,i),mat,Vector2.one);
        }
        internal void Build(Transform parent,string name,bool cameraSolid=false,bool castShadows=true)
        {
            foreach(var pair in batches)
            {
                var mesh=new Mesh {name=name+"_"+pair.Key.name,indexFormat=pair.Value.V.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(pair.Value.V);mesh.SetUVs(0,pair.Value.U);mesh.SetTriangles(pair.Value.T,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                string path=HubOriginalAssets.Folder+"/Meshes/"+mesh.name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
                var go=new GameObject(saved.name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=saved;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;renderer.shadowCastingMode=castShadows?ShadowCastingMode.On:ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
                if(cameraSolid){go.layer=LayerMask.NameToLayer("CameraOnly");var col=go.AddComponent<MeshCollider>();col.sharedMesh=saved;col.excludeLayers=~0;}
            }
        }
    }
}
