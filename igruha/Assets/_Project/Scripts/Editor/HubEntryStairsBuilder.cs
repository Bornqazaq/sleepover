using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Igruha.EditorTools.HubCompactPass;

namespace Igruha.EditorTools
{
    /// <summary>South-facing entry door, straight stair flight and railing on the open room side.</summary>
    public static class HubEntryStairsBuilder
    {
        private const float Width = 2.5f, StepDepth = .3125f, StepRise = .1875f;
        private const int StepCount = 8;
        private const float RailX = 1.21f, LandingHeight = 1.5205f;
        private static readonly Vector3 LandingPosition = new(-8.55f, 0, -8.55f);

        [MenuItem("Igruha/Хаб/Выровнять развёрнутую лестницу и перенести перила")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            Build(Require(RootName).transform);
            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void Build(Transform hub)
        {
            var previous = hub.Find("EntryStairs");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var stairs = Group(hub, "EntryStairs");
            stairs.localPosition = LandingPosition;
            var surface = stairs.gameObject.AddComponent<Igruha.Core.Audio.SurfaceAudio>();
            var serializedSurface = new SerializedObject(surface);
            serializedSurface.FindProperty("kind").enumValueIndex = (int)Igruha.Core.Audio.SurfaceKind.Wood;
            serializedSurface.ApplyModifiedPropertiesWithoutUndo();
            var g = new HubOriginalGeometry();
            for (int i = 0; i < StepCount; i++)
            {
                float top = (i + 1) * StepRise;
                float z = Width * .5f + StepCount * StepDepth - (i + .5f) * StepDepth;
                var center = new Vector3(0, top * .5f, z);
                var size = new Vector3(Width, top, StepDepth);
                g.Box(center, size, M("Walnut"));
                var tread = new Vector3(0, top + .01f, z);
                var treadSize = new Vector3(Width - .01f, .021f, StepDepth + .0125f);
                g.Box(tread, treadSize, M("FloorOak"));
                // Physics includes the tread height, so feet rest on the visible surface.
                Solid(stairs, "Step_" + i, center + Vector3.up * .01025f,
                    size + Vector3.up * .0205f);
            }
            var landing = new Vector3(0, LandingHeight - .1f, 0);
            var landingSize = new Vector3(Width, .20f, Width);
            g.Box(landing, landingSize, M("Oak"));
            Solid(stairs, "Landing", landing, landingSize);
            var support = new Vector3(Width * .5f - .09f, .7f, 0);
            g.Box(support, new Vector3(.2f, 1.4f, Width), M("WallOak"));
            Solid(stairs, "LandingSupport", support, new Vector3(.2f, 1.4f, Width));
            for (int i = 0; i < 7; i++)
            {
                float z = 3.57f - i * .75f;
                float top = 1.12f + Mathf.Min(1.4f, i * .47f);
                g.Box(new Vector3(RailX, top - .45f, z), new Vector3(.085f, .9f, .085f), M("Walnut"));
            }
            var bottomRail = new Vector3(RailX, 1.12f, 3.57f);
            var topRail = new Vector3(RailX, 2.52f, Width * .5f);
            g.Line(bottomRail, topRail, .095f, M("Oak"));
            g.Line(topRail, new Vector3(RailX, 2.52f, -1.20f), .095f, M("Oak"));
            g.Box(topRail, new Vector3(.105f, .105f, .18f), M("Oak"));
            var rail = Solid(stairs, "SlopedRail", (bottomRail + topRail) * .5f - Vector3.up * .45f,
                new Vector3(.09f, .9f, Vector3.Distance(bottomRail, topRail)));
            rail.transform.localRotation = Quaternion.Euler(Mathf.Atan2(topRail.y - bottomRail.y, bottomRail.z - topRail.z) * Mathf.Rad2Deg, 0, 0);
            Solid(stairs, "LandingRail", new Vector3(RailX, 2.07f, 0), new Vector3(.09f, .90f, Width));
            // Door and frame sit against the south wall, directly behind the landing.
            g.Box(new Vector3(0, LandingHeight + .99f, -1.17f), new Vector3(1.02f, 1.96f, .08f), M("Sage"));
            foreach (float x in new[] { -.57f, .57f })
                g.Box(new Vector3(x, 2.52f, -1.12f), new Vector3(.11f, 2.08f, .16f), M("Oak"));
            g.Box(new Vector3(0, 3.54f, -1.12f), new Vector3(1.26f, .12f, .16f), M("Oak"));
            g.Build(stairs, "StairWood");
            // The previous plant position falls inside the newly turned stair flight.
            foreach (Transform child in hub)
                if (child.name == "Plant" && Vector3.Distance(child.localPosition, new Vector3(-9.22f, 0, -5.4f)) < .05f)
                    child.localPosition = new Vector3(-9.22f, 0, -3.9f);
        }
    }
}
