using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Render-only copies with wood on the worktop and stair treads; preserves every source triangle.</summary>
    internal static class HubEntrySurfaces
    {
        internal static Material Wood(string name)
        {
            var material = HubEntryPass.Surface(name, "FFFFFF", .18f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_Project/Art/Hub/Cozy/Textures/PolygonShops_Building_Wood_01.png"));
            material.SetTextureScale("_BaseMap", new Vector2(.23f, .35f));
            return material;
        }

        internal static void Dress(string path, Material wood, Material frame, bool treads)
        {
            var go = HubRoomPass.Require(path);
            var filter = go.GetComponent<MeshFilter>();
            string target = HubEntryPass.Folder + "/Meshes/HE_" + go.name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(target);
            if (mesh == null)
            {
                mesh = Object.Instantiate(filter.sharedMesh); mesh.name = "HE_" + go.name;
                var vertices = mesh.vertices; var normals = mesh.normals; var triangles = mesh.triangles;
                var top = new List<int>(); var sides = new List<int>(); var uv = new Vector2[vertices.Length];
                float maxY = go.GetComponent<Renderer>().bounds.max.y;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = filter.transform.TransformPoint(vertices[i]);
                    Vector3 n = filter.transform.TransformDirection(normals[i]);
                    uv[i] = Mathf.Abs(n.y) > .5f ? new Vector2(p.z, p.x) / .8f
                        : Mathf.Abs(n.x) > .5f ? new Vector2(p.z, p.y) / .8f : new Vector2(p.x, p.y) / .8f;
                }
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[i]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                    bool isTop = treads ? Vector3.Cross(b - a, c - a).normalized.y > .8f : (a.y + b.y + c.y) / 3 > maxY - .085f;
                    var face = isTop ? top : sides;
                    face.Add(triangles[i]); face.Add(triangles[i + 1]); face.Add(triangles[i + 2]);
                }
                mesh.uv = uv; mesh.subMeshCount = 2; mesh.SetTriangles(top, 0); mesh.SetTriangles(sides, 1);
                mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh, target);
            }
            filter.sharedMesh = mesh;
            go.GetComponent<Renderer>().sharedMaterials = new[] { wood, frame };
        }
    }
}
