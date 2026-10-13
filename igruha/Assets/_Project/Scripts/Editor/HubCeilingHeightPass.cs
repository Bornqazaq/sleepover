using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Applies the approved ceiling height to the existing Hub without rebuilding its contents.</summary>
    public static class HubCeilingHeightPass
    {
        private const string UndoName = "Raise Hub ceiling";

        [MenuItem("Igruha/Хаб/Поднять потолок до утверждённой высоты")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            Transform ceiling = HubCompactPass.Require("_Room/Ceiling").transform;
            float delta = HubCompactPass.RoomHeight - ceiling.position.y;
            if (Mathf.Abs(delta) < .0001f) return;

            Transform root = HubCompactPass.Require(HubCompactPass.RootName).transform;
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var walls = new[] { "N", "S", "W", "E" }
                .Select(side => HubCompactPass.Require("_Room/Wall_" + side).transform).ToArray();
            var columns = root.GetComponentsInChildren<BoxCollider>().Where(c => c.name == "ColumnCollider").ToArray();
            var wallMesh = RequireMesh(filters, "PanelledRoom_HO_WallOak");
            var trimMesh = RequireMesh(filters, "PanelledRoom_HO_Walnut");
            var ceilingMesh = RequireMesh(filters, "PanelledRoom_HO_CeilingLinen");
            var beamMesh = RequireMesh(filters, "CeilingBeams_HO_Walnut");
            var columnMeshes = filters.Where(f => f.name.StartsWith("Column_")).ToArray();
            var lighting = root.Find("Lighting");
            if (columns.Length != 4 || columnMeshes.Length != 8 || lighting == null)
                throw new InvalidOperationException("Expected four Hub columns and existing lighting.");

            ChangeMesh(wallMesh, delta, stretch: true);
            ChangeMesh(trimMesh, delta, upperOnly: true);
            ChangeMesh(ceilingMesh, delta);
            ChangeMesh(beamMesh, delta);
            foreach (var filter in columnMeshes)
                ChangeMesh(filter.sharedMesh, delta, stretch: filter.name.EndsWith("_HO_Walnut"),
                    upperOnly: filter.name.EndsWith("_HO_Oak"));

            MoveUp(ceiling, delta);
            foreach (Transform wall in walls)
            {
                MoveUp(wall, delta * .5f);
                Vector3 scale = wall.localScale; scale.y += delta; wall.localScale = scale;
            }
            foreach (var column in columns)
            {
                MoveUp(column.transform, delta * .5f);
                Undo.RecordObject(column, UndoName);
                Vector3 size = column.size; size.y += delta; column.size = size;
            }
            // Keep each shade's camera collider and its light pool attached to the ceiling.
            foreach (Transform light in lighting)
                if (light.name == "Pendant" || light.name.EndsWith("_Pool") || light.name == "CeilingBounce")
                    MoveUp(light, delta);
            foreach (Transform child in root)
                if (child.name.StartsWith("StringLights_")) MoveUp(child, delta);
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>())
            {
                if (collider.sharedMesh != beamMesh) continue;
                Undo.RecordObject(collider, UndoName);
                collider.sharedMesh = null; collider.sharedMesh = beamMesh;
            }
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub ceiling raised by " + delta.ToString("F2") + " m, including walls, columns and hanging lights.");
        }

        private static Mesh RequireMesh(MeshFilter[] filters, string name)
        {
            var filter = filters.SingleOrDefault(f => f.name == name);
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Missing Hub mesh: " + name);
            return filter.sharedMesh;
        }

        private static void MoveUp(Transform target, float delta)
        {
            Undo.RecordObject(target, UndoName);
            target.position += Vector3.up * delta;
        }

        private static void ChangeMesh(Mesh mesh, float delta, bool stretch = false, bool upperOnly = false)
        {
            Undo.RecordObject(mesh, UndoName);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Vector3[] normals = mesh.normals;
            Bounds bounds = mesh.bounds;
            float scale = (bounds.size.y + delta) / bounds.size.y;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (stretch)
                {
                    vertices[i].y = bounds.min.y + (vertices[i].y - bounds.min.y) * scale;
                    // Vertical faces retain world-scale wood grain; horizontal caps keep their UVs.
                    if (i < uv.Length && i < normals.Length && Mathf.Abs(normals[i].y) < .5f) uv[i].y *= scale;
                }
                else if (!upperOnly || vertices[i].y > 2f) vertices[i].y += delta;
            }
            mesh.vertices = vertices;
            if (stretch) mesh.uv = uv;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
        }
    }
}
