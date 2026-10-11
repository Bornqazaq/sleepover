using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class OneBulletPavingTests
    {
        [Test]
        public void CourtyardEntrancesHaveOneVisibleFloorSurface()
        {
            // Samples in the actual overlap reported at the column, and at the well entrance.
            var points = new[] { new Vector2(-9.45f,-7.3f), new Vector2(-7.3f,-9.45f),
                new Vector2(-9.45f,-11.7f), new Vector2(11.8f,9.75f) };
            var counts = new int[points.Length];
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/_Project/Art/Minigames/OneBullet/Geometry" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("_Paving_")) continue;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var vertices = mesh.vertices; var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]]; var b = vertices[triangles[i+1]]; var c = vertices[triangles[i+2]];
                    if (Vector3.Cross(b-a,c-a).normalized.y < .8f) continue;
                    float d = (b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                    if (Mathf.Abs(d) < .000001f) continue;
                    for (int p = 0; p < points.Length; p++)
                    {
                        float u = ((b.z-c.z)*(points[p].x-c.x)+(c.x-b.x)*(points[p].y-c.z))/d;
                        float v = ((c.z-a.z)*(points[p].x-c.x)+(a.x-c.x)*(points[p].y-c.z))/d;
                        if (u > .00001f && v > .00001f && u+v < .99999f) counts[p]++;
                    }
                }
            }
            foreach (int count in counts) Assert.AreEqual(1, count, "No gap and no overlapping top surfaces.");
        }
    }
}
