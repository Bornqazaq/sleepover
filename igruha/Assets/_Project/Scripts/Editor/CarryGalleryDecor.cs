using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Fascia fittings below open gallery edges. No posts or raised kerbs.</summary>
    internal static class CarryGalleryDecor
    {
        private const float FittingSpacing=5.2f, FirstFitting=2.4f;
        private const string Folder="Assets/_Project/Art/CarryItem/HeistRoutes/Meshes";

        internal static void Build(Transform gallery,Vector3[] left,Vector3[] right,
            Material steel,Material blue,Material amber,Material graphite)
        {
            var metal=new Batch();var paint=new Batch();var lens=new Batch();var rubber=new Batch();
            for(int edge=0;edge<2;edge++)
            {
                var points=edge==0?left:right;float walked=0,next=FirstFitting;int index=0;
                for(int i=1;i<points.Length;i++)
                {
                    Vector3 delta=points[i]-points[i-1];float length=delta.magnitude;
                    while(next<=walked+length)
                    {
                        Vector3 p=Vector3.Lerp(points[i-1],points[i],(next-walked)/length);
                        Vector3 outward=(Vector3.Lerp(left[i-1],left[i],(next-walked)/length)
                            -Vector3.Lerp(right[i-1],right[i],(next-walked)/length)).normalized*(edge==0?1:-1);
                        Quaternion q=Quaternion.LookRotation(Vector3.Cross(outward,Vector3.up),Vector3.up);
                        // Local +X faces away from the deck; every fitting stays below its top.
                        metal.Box(p,q,new Vector3(-.025f,-.18f,0),new Vector3(.10f,.29f,.62f));
                        rubber.Box(p,q,new Vector3(.035f,-.14f,0),new Vector3(.035f,.16f,.46f));
                        for(int n=-1;n<=1;n++)
                            lens.Box(p,q,new Vector3(.057f,-.14f,n*.135f),new Vector3(.02f,.095f,.10f));
                        foreach(float z in new[]{-.255f,.255f})
                            metal.Box(p,q,new Vector3(.035f,-.18f,z),new Vector3(.025f,.055f,.055f));
                        // Folded blue cable cassette, secured to the underside of the slab.
                        if(index%2==0)
                        {
                            paint.Box(p,q,new Vector3(-.13f,-.42f,0),new Vector3(.24f,.16f,1.25f));
                            rubber.Box(p,q,new Vector3(-.003f,-.425f,0),new Vector3(.014f,.05f,1.08f));
                            foreach(float z in new[]{-.46f,.46f})
                            {
                                metal.Box(p,q,new Vector3(-.12f,-.35f,z),new Vector3(.31f,.045f,.075f));
                                metal.Box(p,q,new Vector3(-.12f,-.49f,z),new Vector3(.31f,.045f,.075f));
                                metal.Box(p,q,new Vector3(.026f,-.42f,z),new Vector3(.045f,.18f,.075f));
                            }
                            Vector3 previous=p+q*new Vector3(-.1f,-.49f,-.42f);
                            for(int n=1;n<=16;n++)
                            {
                                float t=n/16f;
                                Vector3 current=p+q*new Vector3(-.1f,-.49f-.43f*Mathf.Sin(t*Mathf.PI),-.42f+t*.84f);
                                rubber.Tube(previous,current,.018f);previous=current;
                            }
                        }
                        index++;next+=FittingSpacing;
                    }
                    walked+=length;
                }
            }
            Directory.CreateDirectory(Folder);
            string side=gallery.name.EndsWith("north")?"North":"South";
            metal.Save(gallery,side+" edge brackets",steel);
            paint.Save(gallery,side+" service cassettes",blue);
            lens.Save(gallery,side+" amber reflectors",amber);
            rubber.Save(gallery,side+" cable loops",graphite);
        }

        private sealed class Batch
        {
            private readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
            private readonly List<Vector2> uv=new List<Vector2>();
            private readonly List<int> triangles=new List<int>();
            internal void Box(Vector3 origin,Quaternion q,Vector3 p,Vector3 size)
                =>CarryRoundedEdges.AppendBox(vertices,normals,uv,triangles,origin+q*p,size,q);
            internal void Tube(Vector3 a,Vector3 b,float radius)
            {
                const int sides=8;int start=vertices.Count;Quaternion q=Quaternion.LookRotation(b-a);
                for(int end=0;end<2;end++)for(int i=0;i<sides;i++)
                {
                    float angle=i*Mathf.PI*2/sides;Vector3 n=q*new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                    vertices.Add((end==0?a:b)+n*radius);normals.Add(n);uv.Add(new Vector2(i/(float)sides,end));
                }
                for(int i=0;i<sides;i++)
                {
                    int a0=start+i,b0=start+(i+1)%sides;
                    triangles.AddRange(new[]{a0,b0,b0+sides,a0,b0+sides,a0+sides});
                }
            }
            internal void Save(Transform parent,string name,Material material)
            {
                string path=Folder+"/"+name.Replace(' ','_')+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool fresh=mesh==null;
                if(fresh)mesh=new Mesh{name=name};else mesh.Clear();
                mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
                if(fresh)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
                var g=new GameObject(name);g.transform.SetParent(parent,false);g.layer=LayerMask.NameToLayer("Cover");
                g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;
                g.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
        }
    }
}
