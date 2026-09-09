using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Scene-local visual moonbeams; they never affect the keeper's vision tests.</summary>
    internal static class CryingAngelsGalleryEffects
    {
        private const string MeshPath = CryingAngelsGalleryAssets.Art + "/Models/CA_LightShaft.asset";
        private const int PlaneCount = 3;
        private const int WindowPairs = 2;
        private const float WindowOffset = 2.76f;
        private const float WindowHeight = 8.7f;

        internal static void Build(Transform gallery, float radius)
        {
            var material = CryingAngelsGalleryAssets.EnsureMaterial("CA_Moonbeams", "Igruha/CryingAngels/LightShaft");
            material.SetColor("_BaseColor",new Color(.14f,.40f,.20f,.04f));
            EditorUtility.SetDirty(material);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if(mesh==null)
            {
                mesh=MakeBeam(); AssetDatabase.CreateAsset(mesh,MeshPath);
            }
            var root=new GameObject("WindowShafts");root.transform.SetParent(gallery,false);
            for(int i=0;i<WindowPairs;i++)
            {
                float angle=(i*180f)*Mathf.Deg2Rad;
                Vector3 outward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 tangent=new Vector3(-outward.z,0,outward.x);
                for(int side=-1;side<=1;side+=2)
                {
                    var go=new GameObject("Moonbeam_"+i+"_"+side);go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=outward*(radius-.50f)+tangent*(WindowOffset*side)+Vector3.up*WindowHeight;
                    Vector3 direction=-outward*6f-Vector3.up*8.25f;
                    go.transform.localRotation=Quaternion.LookRotation(direction);
                    go.transform.localScale=new Vector3(1f,1f,direction.magnitude);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                }
            }
        }

        private static Mesh MakeBeam()
        {
            var vertices=new Vector3[PlaneCount*4];var uv=new Vector2[vertices.Length];var indices=new int[PlaneCount*6];
            for(int i=0;i<PlaneCount;i++)
            {
                Vector3 right=Quaternion.Euler(0,0,i*180f/PlaneCount)*Vector3.right;
                int v=i*4;vertices[v]=-right*.32f;vertices[v+1]=right*.32f;
                vertices[v+2]=right*2.6f+Vector3.forward;vertices[v+3]=-right*2.6f+Vector3.forward;
                uv[v]=new Vector2(0,0);uv[v+1]=new Vector2(1,0);uv[v+2]=new Vector2(1,1);uv[v+3]=new Vector2(0,1);
                int t=i*6;indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
            }
            var mesh=new Mesh {name="CA_LightShaft",vertices=vertices,uv=uv,triangles=indices};mesh.RecalculateBounds();return mesh;
        }
    }
}
