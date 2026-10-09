using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Core.Hub.Activities;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Rebuilds only ping-pong. Existing furniture/materials and other activities are preserved.</summary>
    public static class HubPingPongBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Hub.unity";
        private const string RootName = "_HubPingPong";
        private const string Folder = "Assets/_Project/Art/Hub/PingPong";
        private const string AudioFolder = "Assets/_Project/Audio/Hub/PingPong";
        private const float StandDistance = 2.06f;
        private const float StandLateralOffset = .72f;
        private static readonly Vector3 Center = new Vector3(-.55f, -.01f, 4.60f);

        [MenuItem("Igruha/Хаб/Собрать пинг-понг")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub.unity outside Play Mode.");
            GameObject furniture = GameObject.Find("_HubOriginal/Games/PingPong");
            if (furniture == null) throw new InvalidOperationException("Original ping-pong table missing.");
            HubCozyMaterials.EnsureFolder(Folder);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Transform root = new GameObject(RootName).transform;
            root.position = Center;
            furniture.transform.SetPositionAndRotation(Center, Quaternion.identity);
            foreach (Renderer r in furniture.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "HO_Red" || r.name == "HO_Oak") r.enabled = false;
                if (r.name == "HO_Paper") RemoveDecorativeBall(r.GetComponent<MeshFilter>());
            }

            var controller = new GameObject("PingPongTable");
            controller.transform.SetParent(root, false);
            controller.AddComponent<NetworkObject>();
            var table = controller.AddComponent<PingPongTable>();
            var view = root.gameObject.AddComponent<PingPongPresentation>();
            Transform ball = Shape(root, "Ball", PrimitiveType.Sphere, Vector3.zero,
                Vector3.one * (PingPongRules.BallRadius * 2), HubOriginalAssets.Mat("White"));
            Transform leftPaddle = Paddle(root, "LeftPaddle");
            Transform rightPaddle = Paddle(root, "RightPaddle");
            Material cueMaterial = CueMaterial();
            Renderer leftCue = Shape(root, "LeftTimingMarker", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(.2f, .004f, .2f), cueMaterial).GetComponent<Renderer>();
            Renderer rightCue = Shape(root, "RightTimingMarker", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(.2f, .004f, .2f), cueMaterial).GetComponent<Renderer>();
            leftCue.enabled = rightCue.enabled = false;
            PingPongSeat left = Seat(root, table, 0);
            PingPongSeat right = Seat(root, table, 1);
            var sound = root.gameObject.AddComponent<MinigameAudioPlayer>();
            Set(sound, "library", SoundLibrary());
            Set(sound, "voices", 4);
            Set(sound, "maxDistance", 14f);
            Set(table, "left", left); Set(table, "right", right); Set(table, "presentation", view);
            Set(view, "table", table); Set(view, "ball", ball);
            Set(view, "leftPaddle", leftPaddle); Set(view, "rightPaddle", rightPaddle);
            Set(view, "leftCue", leftCue); Set(view, "rightCue", rightCue); Set(view, "audioPlayer", sound);
            ball.localPosition = new Vector3(-.54f, PingPongRules.TableHeight + PingPongRules.BallRadius, .38f);
            RestPaddle(leftPaddle, 0); RestPaddle(rightPaddle, 1);
            ConfigureView();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Ping-pong built: two scene seats, shared flight controller, timing markers and spatial sound.");
        }

        /// <summary>Upgrade the local camera without rebuilding the scene's network object identities.</summary>
        public static void ConfigureView()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ScenePath)
                throw new InvalidOperationException("Open Hub.unity outside Play Mode.");
            GameObject root = GameObject.Find(RootName);
            var view = root.GetComponent<PingPongCamera>();
            if (view == null) view = root.AddComponent<PingPongCamera>();
            Transform child = root.transform.Find("PingPongView");
            if (child == null)
            {
                child = new GameObject("PingPongView").transform;
                child.SetParent(root.transform, false);
            }
            var rig = child.GetComponent<CinemachineCamera>();
            if (rig == null) rig = child.gameObject.AddComponent<CinemachineCamera>();
            rig.Priority.Value = -100;
            rig.Priority.Enabled = true;
            LensSettings lens = rig.Lens;
            lens.FieldOfView = 60;
            lens.NearClipPlane = .05f;
            lens.FarClipPlane = 100;
            rig.Lens = lens;
            rig.enabled = false;
            Set(view, "table", root.GetComponentInChildren<PingPongTable>());
            Set(view, "tableRig", rig);
            Set(view, "brain", Object.FindFirstObjectByType<CinemachineBrain>());
            Set(view, "cameraController", Object.FindFirstObjectByType<MinigameCameraController>());
            Set(root.GetComponent<PingPongPresentation>(), "localCamera", view);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static PingPongSeat Seat(Transform root, PingPongTable table, byte side)
        {
            Transform stand = new GameObject(side == 0 ? "LeftStand" : "RightStand").transform;
            stand.SetParent(root, false);
            // Stand beside the flight line so the fixed third-person framing does not hide the ball behind a head.
            stand.localPosition = new Vector3(side == 0 ? -StandDistance : StandDistance, .01f,
                side == 0 ? -StandLateralOffset : StandLateralOffset);
            stand.localRotation = Quaternion.Euler(0, side == 0 ? 90 : -90, 0);
            var go = new GameObject(side == 0 ? "PingPongLeftSeat" : "PingPongRightSeat");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(stand.position, stand.rotation);
            var zone = go.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.center = Vector3.up * .9f;
            zone.size = new Vector3(1.25f, 1.8f, 1.2f);
            go.AddComponent<NetworkObject>();
            var seat = go.AddComponent<PingPongSeat>();
            Set(seat, "standPoint", stand); Set(seat, "activityName", "пинг-понг");
            Set(seat, "table", table); Set(seat, "side", (int)side);
            Set(seat, "leaveRadius", 2.2f);
            return seat;
        }

        private static Transform Paddle(Transform parent, string name)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            Transform wood = Shape(root, "Blade", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(.25f, .013f, .29f), HubOriginalAssets.Mat("Oak"));
            wood.localRotation = Quaternion.Euler(0, 0, 90);
            foreach (float sign in new[] { -1f, 1f })
            {
                Transform rubber = Shape(root, sign < 0 ? "BlackRubber" : "RedRubber", PrimitiveType.Cylinder,
                    new Vector3(sign * .016f, 0, 0), new Vector3(.235f, .003f, .275f),
                    HubOriginalAssets.Mat(sign < 0 ? "Ink" : "Red"));
                rubber.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Shape(root, "Handle", PrimitiveType.Cube, new Vector3(0, -.19f, 0),
                new Vector3(.035f, .19f, .047f), HubOriginalAssets.Mat("Oak"));
            return root;
        }

        private static void RestPaddle(Transform paddle, byte side)
        {
            paddle.localPosition = new Vector3(side == 0 ? -.83f : .83f, .91f, side == 0 ? .36f : -.39f);
            paddle.localRotation = Quaternion.Euler(0, 0, -90);
        }

        private static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static Material CueMaterial()
        {
            string path = Folder + "/TimingMarker.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(HubOriginalAssets.Mat("White")) { name = "PingPongTimingMarker" };
            material.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static MinigameSfxLibrary SoundLibrary()
        {
            SfxLibraryBuilder.Build(Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/art/hub-pingpong-sfx.json")));
            return AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(AudioFolder + "/SfxLibrary.asset");
        }

        private static void RemoveDecorativeBall(MeshFilter filter)
        {
            string path = Folder + "/TableLines.asset";
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(HubOriginalAssets.Folder + "/Meshes/PingPong_HO_Paper.asset");
            Mesh mesh = Object.Instantiate(source);
            mesh.name = "PingPongTableLines";
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var kept = new List<int>(triangles.Length);
            var decoration = new Bounds(new Vector3(-.54f, .907f, .38f), Vector3.one * .06f);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 center = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3;
                if (decoration.Contains(center)) continue;
                kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]);
            }
            mesh.SetTriangles(kept, 0); mesh.RecalculateBounds();
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            filter.sharedMesh = saved;
            AssetDatabase.SaveAssetIfDirty(saved);
        }

        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(Object target, string property, string value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(Object target, string property, int value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Set(Object target, string property, float value)
        {
            var so = new SerializedObject(target); so.FindProperty(property).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
