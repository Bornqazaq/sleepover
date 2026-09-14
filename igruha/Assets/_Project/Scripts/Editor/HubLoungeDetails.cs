using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>Soft furnishings and small lived-in details on existing furniture.</summary>
    internal static class HubLoungeDetails
    {
        internal static void Apply(Transform parent)
        {
            Throw(parent);
            Cushion(parent);
            Books(parent);
            Seams(parent);
        }

        private static void Throw(Transform parent)
        {
            HubRoomPass.Require("_HubCozy/SofaThrow").GetComponent<Renderer>().enabled = false;
            // The throw replaces the flat pillow underneath it; its original collider remains untouched.
            HubRoomPass.Require("_Pit/Cushion_R").GetComponent<Renderer>().enabled = false;
            // Measured sofa: back y=.492, seat y≈-.13, front z=-2.149. Draped sheet follows these surfaces.
            var profile = new[] { new Vector2(-3.075f, -.06f), new Vector2(-3.063f, .41f),
                new Vector2(-3.04f, .478f), new Vector2(-2.90f, .514f), new Vector2(-2.86f, .508f),
                new Vector2(-2.795f, .449f), new Vector2(-2.785f, .420f), new Vector2(-2.713f, .174f),
                new Vector2(-2.705f, .07f), new Vector2(-2.69f, -.062f), new Vector2(-2.64f, -.100f),
                new Vector2(-2.27f, -.100f), new Vector2(-2.20f, -.116f), new Vector2(-2.154f, -.161f),
                new Vector2(-2.128f, -.326f), new Vector2(-2.137f, -.43f) };
            const int across = 36, subdivisions = 6;
            const float width = .73f, left = .29f;
            int along = (profile.Length - 1) * subdivisions;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var faces = new[] { new List<int>(), new List<int>(), new List<int>() };
            float distance = 0; Vector2 previous = profile[0];
            for (int row = 0; row <= along; row++)
            {
                float t = row / (float)subdivisions; int section = Mathf.Min(Mathf.FloorToInt(t), profile.Length - 2);
                float s = t - section;
                Vector2 p = Vector2.Lerp(profile[section], profile[section + 1], s);
                distance += Vector2.Distance(p, previous); previous = p;
                Vector2 tangent = (profile[section + 1] - profile[section]).normalized;
                for (int col = 0; col <= across; col++)
                {
                    float u = col / (float)across;
                    float fold = Mathf.Sin(u * Mathf.PI * 10 + distance * 2.1f) * .006f + Mathf.Sin(u * Mathf.PI * 4) * .004f;
                    vertices.Add(new Vector3(left + width * u, p.y + fold * tangent.x, p.x - fold * tangent.y));
                    uv.Add(new Vector2(width * u, distance) / .14f);
                    if (row == along || col == across) continue;
                    int a = row * (across + 1) + col, b = a + across + 1;
                    float band = Mathf.Repeat(distance + .12f, .63f);
                    int stripe = (col >= 4 && col <= 6) || (col >= 29 && col <= 31) || band < .028f ? 1
                        : col == 8 || col == 27 || (band > .047f && band < .060f) ? 2 : 0;
                    faces[stripe].AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            }
            string path = HubLoungeSurfaces.Folder + "/Meshes/HL_DrapedThrow.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "HL_DrapedThrow" }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = 3;
            for (int i = 0; i < faces.Length; i++) mesh.SetTriangles(faces[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var sage = HubLoungeSurfaces.Fabric("ThrowSage", "577769", .50f);
            var go = HubCozyGeometry.MeshObject("DrapedThrow", parent, mesh, sage);
            go.GetComponent<Renderer>().sharedMaterials = new[] { sage,
                HubLoungeSurfaces.Fabric("ThrowCream", "D7C8A7", .50f), HubLoungeSurfaces.Fabric("ThrowOchre", "BB965D", .50f) };
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void Cushion(Transform parent)
        {
            Mesh mesh = HubRoomPass.Require("_Pit/Cushion_L").GetComponent<MeshFilter>().sharedMesh;
            var go = HubCozyGeometry.MeshObject("UprightCushion", parent, mesh, HubLoungeSurfaces.Fabric("PillowRust", "B67B59", .45f));
            go.transform.rotation = Quaternion.Euler(69, 8, -8); go.transform.localScale = Vector3.one * .65f;
            Bounds bounds = go.GetComponent<Renderer>().bounds;
            go.transform.position += new Vector3(-.38f, -.115f, -2.58f) - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        private static void Books(Transform parent)
        {
            var pages = HubCozyMaterials.Surface("HL_Paper", "D9CEB6", .04f);
            var mustard = HubCozyMaterials.Surface("HL_BookOchre", "B49151", .08f);
            var green = HubCozyMaterials.Surface("HL_BookGreen", "40594B", .08f);
            var coral = HubCozyMaterials.Surface("HL_BookCoral", "9B6351", .08f);
            var geo = new HubRoomGeometry();
            // Existing lower coffee-table shelf top is y=-.441; the tabletop underside is y=-.214.
            Book(geo, new Vector3(.13f, -.409f, -.75f), new Vector3(.46f, .052f, .31f), -8, green, pages);
            Book(geo, new Vector3(.12f, -.359f, -.745f), new Vector3(.39f, .045f, .28f), 5, mustard, pages);
            Book(geo, new Vector3(-.35f, -.423f, -.72f), new Vector3(.26f, .030f, .29f), -17, coral, pages);
            // Two slim game guides sit within the existing TV cabinet's footprint, beside its DVD stack.
            Book(geo, new Vector3(-.68f, -.078f, 2.66f), new Vector3(.32f, .036f, .27f), 7, coral, pages);
            Book(geo, new Vector3(-.67f, -.043f, 2.655f), new Vector3(.29f, .030f, .24f), -2, green, pages);
            geo.Build(parent, "LoungeBooks", true, HubLoungeSurfaces.Folder);
        }

        private static void Book(HubRoomGeometry geo, Vector3 center, Vector3 size, float yaw, Material cover, Material pages)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            geo.Box(center, new Vector3(size.x - .013f, size.y - .009f, size.z - .010f), pages, rotation);
            foreach (float sign in new[] { -1f, 1f })
                geo.Box(center + Vector3.up * (size.y * .5f - .002f) * sign, new Vector3(size.x, .004f, size.z), cover, rotation);
            geo.Box(center + rotation * Vector3.left * (size.x * .5f - .004f), new Vector3(.008f, size.y, size.z), cover, rotation);
            geo.Box(center + rotation * new Vector3(0, size.y * .5f + .001f, -.018f), new Vector3(size.x * .55f, .002f, .028f), pages, rotation);
        }

        private static void Seams(Transform parent)
        {
            var geo = new HubRoomGeometry();
            var seam = HubCozyMaterials.Surface("HL_LinenPiping", "B7A88D", .03f);
            // Piping lies on the measured front lip of the two seat pads, away from the arms.
            foreach (float x in new[] { -.51f, .51f })
            {
                // Stop the front piping where the throw covers this pad.
                float end = x < 0 ? x + .46f : .26f;
                geo.Segment(new Vector3(x - .46f, -.147f, -2.186f), new Vector3(end, -.147f, -2.186f), .009f, seam);
                geo.Segment(new Vector3(x - .46f, -.147f, -2.186f), new Vector3(x - .48f, -.157f, -2.218f), .009f, seam);
                if (x < 0) geo.Segment(new Vector3(x + .46f, -.147f, -2.186f), new Vector3(x + .48f, -.157f, -2.218f), .009f, seam);
            }
            geo.Build(parent, "SofaPiping", false, HubLoungeSurfaces.Folder);
        }
    }
}
