using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Igruha.EditorTools
{
    /// <summary>Architectural finish over the measured 24 m hub shell. All additions are visual only.</summary>
    public static class HubRoomPass
    {
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        internal const string RootName = "_HubRoomCozy";
        private const float InnerWall = 11.8f, CeilingBottom = 3.9f;
        private static readonly float[] BeamZ = { -11.55f, -7.5f, -1.45f, 4.6f, 11.55f };

        [MenuItem("Igruha/Хаб/Уют — потолок и отделка комнаты")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub outside Play Mode before dressing the room.");
            foreach (string path in new[] { "_Room/Ceiling", "_Zones/_Structure", "_HubCozy", "_HubBarCozy" }) Require(path);
            string physics = HubCozyPass.PhysicsSnapshot();
            var previous = GameObject.Find(RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject(RootName).transform;
            Ceiling(root);
            Perimeter(root);
            HubRoomDecor.Apply(root);
            HubRoomFurnishings.Apply(root);
            Lighting(root);
            if (physics != HubCozyPass.PhysicsSnapshot()) throw new InvalidOperationException("Room art changed gameplay colliders.");
            string audit = Audit();
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub room finish applied. " + audit);
        }

        internal static GameObject Require(string path) => GameObject.Find(path)
            ?? throw new InvalidOperationException("Missing Hub object: " + path);

        internal static Material Surface(string name, string color, float glow = 0)
            => HubCozyMaterials.Surface("HR_" + name, color, .12f, glow: glow);

        private static void Ceiling(Transform parent)
        {
            var cream = Surface("IvoryCeiling", "E1CFAD", .10f);
            var alternate = Surface("IvoryCeilingAlt", "DBCAA9", .10f);
            var oak = Surface("OakBeam", "855738");
            var edge = Surface("OakEdge", "A5764C");
            var dark = Surface("BeamShadow", "553A29");
            var brass = Surface("AgedBrass", "A6844E");
            HubCozyMaterials.Assign(Require("_Room/Ceiling"), Surface("CeilingRecess", "A38E70", .07f));
            // Four bays: primary cross-beams land on the existing column axes at z=-7.5 and +4.6.
            for (int bay = 0; bay < BeamZ.Length - 1; bay++)
            {
                var geo = new HubRoomGeometry();
                float length = BeamZ[bay + 1] - BeamZ[bay], z = (BeamZ[bay] + BeamZ[bay + 1]) * .5f;
                const int boards = 48;
                float pitch = 23.1f / boards;
                for (int board = 0; board < boards; board++)
                    geo.Box(new Vector3(-11.55f + pitch * (board + .5f), CeilingBottom - .024f, z),
                        new Vector3(pitch - .009f, .028f, length - .20f), board % 4 == 0 ? alternate : cream);
                foreach (float x in new[] { -8.5f, -2.75f, 0, 2.75f, 8.5f })
                {
                    geo.Box(new Vector3(x, 3.815f, z), new Vector3(.095f, .12f, length), oak);
                    geo.Box(new Vector3(x, 3.749f, z), new Vector3(.065f, .014f, length), edge);
                }
                geo.Build(parent, "CeilingBay_" + bay, false);
            }
            var beams = new HubRoomGeometry();
            foreach (float z in BeamZ)
            {
                beams.Box(new Vector3(0, 3.755f, z), new Vector3(23.6f, .29f, .26f), oak);
                beams.Box(new Vector3(0, 3.604f, z), new Vector3(23.6f, .018f, .21f), edge);
            }
            foreach (float x in new[] { -5.5f, 5.5f })
            {
                beams.Box(new Vector3(x, 3.73f, 0), new Vector3(.32f, .34f, 23.6f), oak);
                beams.Box(new Vector3(x, 3.553f, 0), new Vector3(.27f, .014f, 23.6f), edge);
                foreach (float z in new[] { -7.5f, 4.6f })
                {
                    beams.Box(new Vector3(x, 3.48f, z), new Vector3(.49f, .13f, .49f), dark);
                    beams.Box(new Vector3(x, 3.405f, z), new Vector3(.46f, .024f, .46f), brass);
                    beams.Box(new Vector3(x, .26f, z), new Vector3(.43f, .038f, .43f), brass);
                }
            }
            for (int i = 1; i <= 8; i++)
            {
                var p = Require("_Lighting/PendantLamp_" + i).transform.position;
                var center = new Vector3(p.x, 3.847f, p.z);
                beams.Disc(center, Vector3.right * .22f, Vector3.forward * .22f, oak);
                beams.Disc(center + Vector3.down * .006f, Vector3.right * .09f, Vector3.forward * .09f, brass);
            }
            beams.Build(parent, "TimberFrame");
        }

        private static void Perimeter(Transform parent)
        {
            var oak = Surface("OakBeam", "855738");
            var dark = Surface("BeamShadow", "553A29");
            var edge = Surface("OakEdge", "A5764C");
            var cove = Surface("CoveGlow", "FFCE87", .55f);
            // Existing industrial pipe renderers are replaced by a continuous shallow timber service fascia.
            foreach (Transform item in Require("_Zones/_Structure").transform)
                if (item.name.StartsWith("Pipe_")) foreach (var r in item.GetComponentsInChildren<Renderer>()) r.enabled = false;
            for (int side = 0; side < 4; side++)
            {
                Quaternion rotation = Quaternion.Euler(0, side * 90, 0);
                var geo = new HubRoomGeometry();
                Action<Vector3, Vector3, Material> box = (p, size, mat) => geo.Box(rotation * p, size, mat, rotation);
                box(new Vector3(0, 3.70f, 11.42f), new Vector3(23.6f, .36f, .75f), dark);
                box(new Vector3(0, 3.655f, 11.025f), new Vector3(23.6f, .20f, .07f), oak);
                box(new Vector3(0, 3.535f, 10.99f), new Vector3(23.6f, .04f, .12f), edge);
                box(new Vector3(0, 3.785f, 10.976f), new Vector3(23.6f, .018f, .018f), cove);
                box(new Vector3(0, .12f, InnerWall - .027f), new Vector3(23.6f, .24f, .055f), dark);
                box(new Vector3(0, .253f, InnerWall - .037f), new Vector3(23.6f, .026f, .075f), edge);
                geo.Build(parent, "WallTrim_" + side);
            }
        }

        private static void Lighting(Transform parent)
        {
            // Explicit local shadow sizes fit the existing atlas without automatic resolution reductions.
            foreach (int i in new[] { 1, 2, 5, 6 })
                ShadowTier(Require("_Lighting/PendantLamp_" + i).GetComponentInChildren<Light>(), i == 1
                    ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh
                    : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
            ShadowTier(Require("_HubBarCozy/Bar_WallLight").GetComponent<Light>(),
                UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
            // Broad upward bounce makes the new ceiling readable without changing the frozen camera/pipeline.
            foreach (float x in new[] { -3.8f, 3.8f }) foreach (float z in new[] { -5.5f, 5.5f })
            {
                var go = new GameObject("CeilingBounce_" + x + "_" + z);
                go.transform.SetParent(parent, false); go.transform.position = new Vector3(x, 1.5f, z);
                go.transform.rotation = Quaternion.Euler(-90, 0, 0);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot; light.color = HubCozyMaterials.Hex("FFE0B4");
                light.intensity = 18; light.range = 14; light.spotAngle = 155; light.innerSpotAngle = 110;
                light.shadows = LightShadows.None;
                go.AddComponent<UniversalAdditionalLightData>();
            }
        }

        private static void ShadowTier(Light light, int tier)
        {
            // The existing URP asset uses High=1024 and Medium=512. Keep the shared asset untouched.
            var data = light.GetComponent<UniversalAdditionalLightData>();
            if (data == null) data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
            var serialized = new SerializedObject(data);
            serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = tier;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static string Audit()
        {
            var root = Require(RootName);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Decorative colliders found.");
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers)
                if (r.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported))
                    throw new InvalidOperationException("Missing or unsupported room material: " + r.name);
            int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3);
            return "Decoration colliders: 0; renderers: " + renderers.Length + "; triangles: " + triangles + ". " + HubCozyPass.Audit();
        }
    }
}
