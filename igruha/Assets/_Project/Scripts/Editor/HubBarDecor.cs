using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    internal static class HubBarDecor
    {
        internal static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = center; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        internal static void Lettering(Transform parent, string name, string text, Vector3 center,
            float width, float height, Color color, float size, float spacing = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center; go.transform.rotation = Quaternion.Euler(0, -90, 0);
            var label = go.AddComponent<TextMeshPro>();
            label.font = HubBarAssets.LetteringFont();
            label.text = text; label.fontSize = size; label.characterSpacing = spacing;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = color;
            label.rectTransform.sizeDelta = new Vector2(width, height);
            label.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            label.ForceMeshUpdate(); // Include the final lettering geometry in the immediate art audit.
        }

        internal static void Popcorn(Transform parent)
        {
            var red = HubCozyMaterials.Surface("HB_PopcornRed", "9F4137", .16f);
            var gold = HubCozyMaterials.Surface("HB_Brass", "B69259", .32f);
            var cream = HubCozyMaterials.Surface("HB_WarmCream", "F5D698", .10f);
            // Fits inside the existing ice-cream machine's solid envelope, above its counter.
            const float x = -9.48f, z = 4.05f;
            Box(parent, "Popcorn_Base", new Vector3(x, 1.055f, z), new Vector3(.59f, .13f, .50f), red);
            Box(parent, "Popcorn_Tray", new Vector3(x, 1.13f, z), new Vector3(.54f, .025f, .45f), gold);
            Box(parent, "Popcorn_Canopy", new Vector3(x, 1.825f, z), new Vector3(.62f, .17f, .54f), red);
            foreach (float dx in new[] { -.25f, .25f }) foreach (float dz in new[] { -.20f, .20f })
                Box(parent, "Popcorn_Post", new Vector3(x + dx, 1.44f, z + dz), new Vector3(.026f, .61f, .026f), gold);
            // Open serving face; lightly tinted sides make the contents visible from the bar.
            Material glass = HubCozyMaterials.Surface("HB_PopcornGlass", "DFE9CE", .25f);
            glass.SetFloat("_Surface", 1); glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); glass.SetFloat("_ZWrite", 0);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.renderQueue = (int)RenderQueue.Transparent;
            glass.SetOverrideTag("RenderType", "Transparent"); glass.SetShaderPassEnabled("ShadowCaster", false);
            Color tint = glass.GetColor("_BaseColor"); tint.a = .09f; glass.SetColor("_BaseColor", tint);
            foreach (float dz in new[] { -.22f, .22f })
                Box(parent, "Popcorn_Glass", new Vector3(x, 1.445f, z + dz), new Vector3(.49f, .59f, .005f), glass)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Box(parent, "Popcorn_Back", new Vector3(x - .26f, 1.445f, z), new Vector3(.005f, .59f, .44f), glass)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var kettle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(kettle.GetComponent<Collider>());
            kettle.name = "Popcorn_Kettle"; kettle.transform.SetParent(parent, false);
            kettle.transform.position = new Vector3(x, 1.55f, z); kettle.transform.localScale = new Vector3(.24f, .06f, .22f);
            kettle.GetComponent<Renderer>().sharedMaterial = gold;
            Box(parent, "Popcorn_KettleStem", new Vector3(x, 1.69f, z), new Vector3(.018f, .19f, .018f), gold);
            Lettering(parent, "Popcorn_Label", "POPCORN", new Vector3(x + .316f, 1.827f, z), .48f, .14f,
                HubCozyMaterials.Hex("FFE6A7"), .8f, 6);
            Pile(parent, "HB_PopcornPile", new Vector3(x, 1.15f, z), new Vector3(.46f, .11f, .36f), 140, cream);
        }

        private static void Pile(Transform parent, string name, Vector3 origin, Vector3 extent, int count, Material material)
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            var random = new System.Random(31415);
            var corners = new[] { Vector3.up, Vector3.right, Vector3.forward, Vector3.left, Vector3.back, Vector3.down };
            int[] faces = { 0,2,1, 0,3,2, 0,4,3, 0,1,4, 5,1,2, 5,2,3, 5,3,4, 5,4,1 };
            for (int i = 0; i < count; i++)
            {
                var center = new Vector3(((float)random.NextDouble() - .5f) * extent.x,
                    (float)random.NextDouble() * extent.y, ((float)random.NextDouble() - .5f) * extent.z);
                float radius = .018f + (float)random.NextDouble() * .012f;
                int start = vertices.Count;
                Quaternion rotation = Quaternion.Euler(random.Next(180), random.Next(180), random.Next(180));
                foreach (var v in corners) vertices.Add(center + rotation * v * radius);
                foreach (int index in faces) indices.Add(start + index);
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = HubCozyGeometry.MeshObject(name, parent, HubBarAssets.SaveMesh(name, mesh), material);
            go.transform.position = origin;
        }

        internal static void PopcornCup(Transform parent, Vector3 basePoint)
        {
            var red = HubCozyMaterials.Surface("HB_PopcornRed", "9F4137", .16f);
            var cream = HubCozyMaterials.Surface("HB_WarmCream", "F5D698", .10f);
            var vertices = new List<Vector3>();
            var redFaces = new List<int>(); var creamFaces = new List<int>();
            const int sides = 12;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                int start = vertices.Count;
                vertices.Add(new Vector3(Mathf.Cos(a) * .061f, 0, Mathf.Sin(a) * .061f));
                vertices.Add(new Vector3(Mathf.Cos(b) * .061f, 0, Mathf.Sin(b) * .061f));
                vertices.Add(new Vector3(Mathf.Cos(b) * .091f, .21f, Mathf.Sin(b) * .091f));
                vertices.Add(new Vector3(Mathf.Cos(a) * .091f, .21f, Mathf.Sin(a) * .091f));
                var faces = i % 2 == 0 ? redFaces : creamFaces;
                faces.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.subMeshCount = 2;
            mesh.SetTriangles(redFaces, 0); mesh.SetTriangles(creamFaces, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var cup = HubCozyGeometry.MeshObject("Popcorn_Cup", parent, HubBarAssets.SaveMesh("HB_PopcornCup", mesh), red);
            cup.GetComponent<Renderer>().sharedMaterials = new[] { red, cream };
            cup.transform.position = basePoint;
            Pile(parent, "HB_CupPopcorn", basePoint + Vector3.up * .205f, new Vector3(.105f, .03f, .105f), 24, cream);
        }

        internal static void Garland(Transform parent)
        {
            const int steps = 40, sides = 6, bulbs = 11;
            var vertices = new List<Vector3>(); var indices = new List<int>();
            // Pipe_W extends to x=-11.01: keep the cable and globes in front of that measured surface.
            Func<float, Vector3> point = t => new Vector3(-10.91f, 3.78f - .13f * Mathf.Sin(t * Mathf.PI), -.70f + 6.5f * t);
            for (int i = 0; i <= steps; i++)
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2 / sides;
                vertices.Add(point(i / (float)steps) + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .008f);
                if (i == steps) continue;
                int a = i * sides + side, b = i * sides + (side + 1) % sides;
                indices.AddRange(new[] { a, b, b + sides, a, b + sides, a + sides });
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var wire = HubCozyMaterials.Surface("HB_GarlandWire", "302C23", .05f);
            HubCozyGeometry.MeshObject("Bar_GarlandCable", parent, HubBarAssets.SaveMesh("HB_GarlandCable", mesh), wire);
            var globe = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/Hub/Cozy/HC_Globe.asset")
                ?? throw new InvalidOperationException("Apply the first cozy hub pass before adding its bar garland.");
            var glow = HubCozyMaterials.Surface("HB_GarlandGlow", "FFD28B", .05f, glow: 1.6f);
            for (int i = 0; i < bulbs; i++)
            {
                Vector3 p = point((i + .5f) / bulbs);
                Box(parent, "Bar_GarlandSocket_" + i, p + Vector3.down * .035f, new Vector3(.025f, .07f, .025f), wire);
                var go = HubCozyGeometry.MeshObject("Bar_GarlandBulb_" + i, parent, globe, glow);
                go.transform.position = p + Vector3.down * .095f; go.transform.localScale = new Vector3(.08f, .10f, .08f);
                go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (float t in new[] { 0f, 1f })
                Box(parent, "Bar_GarlandAnchor", point(t) + Vector3.left * .055f, new Vector3(.12f, .025f, .025f), wire);
        }
    }
}
