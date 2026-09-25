using System;
using System.Collections.Generic;
using Igruha.Core.Player;
using Igruha.Minigames.OneBullet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Extracts only arm triangles from read-only character sources; shared avatars are never saved.</summary>
    public static class OneBulletViewArmsBuilder
    {
        public const string Folder = "Assets/_Project/Art/Minigames/OneBullet/ViewArms";
        private const string RosterPath = "Assets/_Project/Settings/Gameplay/CharacterRoster.asset";
        private const float ArmWeight = .72f;
        [MenuItem("Igruha/Minigames/One Bullet rebuild view arms")]
        public static void BuildAll() => Build(-1);
        public static void Build(int only)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Minigames/OneBullet", "ViewArms");
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            for (int i = 0; i < roster.Characters.Count; i++)
            {
                if (only >= 0 && i != only) continue;
                var character = roster.Characters[i];
                var source = Object.Instantiate(character.Prefab);
                source.name = "Arms_" + character.DisplayName;
                source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                try
                {
                    var animator = source.GetComponentInChildren<Animator>();
                    var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    int part = 0;
                    foreach (var renderer in renderers)
                    {
                        var mesh = Extract(renderer, left, right);
                        if (mesh == null) { Object.DestroyImmediate(renderer); continue; }
                        MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                        string path = Folder + "/" + source.name + "_" + part++ + ".asset";
                        // Recreate the native buffers as vertex counts change; CopySerialized leaves stale skin buffers.
                        AssetDatabase.CreateAsset(mesh, path);
                        renderer.sharedMesh = mesh;
                        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                        renderer.updateWhenOffscreen = true;
                        renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 10);
                    }
                    var rig = source.AddComponent<OneBulletViewArms>();
                    var fields = new SerializedObject(rig);
                    ConfigureArm(fields.FindProperty("left"), animator, false);
                    ConfigureArm(fields.FindProperty("right"), animator, true);
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    var behaviours = source.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int c = behaviours.Length - 1; c >= 0; c--) if (behaviours[c] != null && behaviours[c] != rig) Object.DestroyImmediate(behaviours[c]);
                    foreach (var component in source.GetComponentsInChildren<Component>(true))
                        if (component != null && !(component is Transform) && !(component is SkinnedMeshRenderer) && component != rig)
                            Object.DestroyImmediate(component);
                    foreach (var t in source.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                    string prefabPath = Folder + "/" + source.name + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                    AssetDatabase.SaveAssets();
                    for (int meshIndex = 0; meshIndex < part; meshIndex++)
                        AssetDatabase.ImportAsset(Folder + "/" + source.name + "_" + meshIndex + ".asset", ImportAssetOptions.ForceUpdate);
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                    Debug.Log("View arms baked: " + character.DisplayName + ", meshes=" + part);
                }
                finally { Object.DestroyImmediate(source); }
            }
            AssetDatabase.SaveAssets();
            var view = Object.FindFirstObjectByType<OneBulletFirstPerson>();
            if (view != null) { Configure(view); EditorSceneManager.MarkSceneDirty(view.gameObject.scene); EditorSceneManager.SaveScene(view.gameObject.scene); }
        }
        public static void Configure(OneBulletFirstPerson view)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            var data = new SerializedObject(view);
            data.FindProperty("roster").objectReferenceValue = roster;
            var prefabs = data.FindProperty("armPrefabs"); prefabs.arraySize = roster.Characters.Count;
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Arms_" + roster.Characters[i].DisplayName + ".prefab");
                prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab != null ? prefab.GetComponent<OneBulletViewArms>() : null;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void ConfigureArm(SerializedProperty target, Animator animator, bool right)
        {
            Transform hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform little = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (little == null) little = animator.GetBoneTransform(right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal);
            Transform thumb = animator.GetBoneTransform(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            Vector3 forward = ((middle != null ? middle : index).position - hand.position).normalized;
            // Most avatars use a single mitten chain; Girl has no thumb bone either.
            Vector3 back = little != null ? Vector3.Cross(forward, little.position - index.position) :
                thumb != null ? Vector3.Cross(forward, index.position - thumb.position) :
                Vector3.ProjectOnPlane(animator.transform.right, forward);
            back = back.normalized * (right ? 1 : -1);
            target.FindPropertyRelative("upper").objectReferenceValue = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            target.FindPropertyRelative("lower").objectReferenceValue = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            target.FindPropertyRelative("hand").objectReferenceValue = hand;
            target.FindPropertyRelative("separateIndex").boolValue = middle != null;
            target.FindPropertyRelative("handReach").floatValue = HandReach(animator, hand);
            target.FindPropertyRelative("palmCorrection").quaternionValue = Quaternion.Inverse(Quaternion.LookRotation(forward, back)) * hand.rotation;
            var fingers = target.FindPropertyRelative("fingers"); fingers.arraySize = 0;
            int first = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            for (int digit = 0; digit < 5; digit++) for (int joint = 0; joint < 3; joint++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)(first + digit * 3 + joint));
                if (bone == null) continue;
                var direction = bone.childCount > 0 ? (bone.GetChild(0).position - bone.position).normalized : forward;
                var inward = digit == 0 ? ((middle != null ? middle : index).position - bone.position).normalized : -back;
                var axis = bone.InverseTransformDirection(Vector3.Cross(direction, inward).normalized);
                int n = fingers.arraySize++; var f = fingers.GetArrayElementAtIndex(n);
                f.FindPropertyRelative("bone").objectReferenceValue = bone;
                f.FindPropertyRelative("curlAxis").vector3Value = axis;
                f.FindPropertyRelative("digit").intValue = digit; f.FindPropertyRelative("joint").intValue = joint;
            }
        }
        private static float HandReach(Animator animator, Transform hand)
        {
            float reach = 0;
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int handIndex = Array.IndexOf(renderer.bones, hand);
                if (handIndex < 0) continue;
                var mesh = renderer.sharedMesh; var weights = mesh.boneWeights; var vertices = mesh.vertices;
                var matrix = mesh.bindposes[handIndex];
                var digits = new bool[renderer.bones.Length];
                for (int i = 0; i < digits.Length; i++)
                    digits[i] = renderer.bones[i] == hand || renderer.bones[i].IsChildOf(hand);
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    float weight = (digits[w.boneIndex0] ? w.weight0 : 0) + (digits[w.boneIndex1] ? w.weight1 : 0) +
                        (digits[w.boneIndex2] ? w.weight2 : 0) + (digits[w.boneIndex3] ? w.weight3 : 0);
                    if (weight > .95f) reach = Mathf.Max(reach, matrix.MultiplyPoint3x4(vertices[i]).magnitude);
                }
            }
            return reach;
        }
        private static Mesh Extract(SkinnedMeshRenderer renderer, Transform left, Transform right)
        {
            Mesh source = renderer.sharedMesh;
            if (source == null) return null;
            var arm = new bool[renderer.bones.Length];
            for (int i = 0; i < arm.Length; i++)
                arm[i] = renderer.bones[i] != null && (renderer.bones[i] == left || renderer.bones[i] == right || renderer.bones[i].IsChildOf(left) || renderer.bones[i].IsChildOf(right));
            var weights = source.boneWeights;
            var keep = new bool[weights.Length];
            for (int i = 0; i < keep.Length; i++)
            {
                var w = weights[i];
                keep[i] = (arm[w.boneIndex0] ? w.weight0 : 0) + (arm[w.boneIndex1] ? w.weight1 : 0) +
                          (arm[w.boneIndex2] ? w.weight2 : 0) + (arm[w.boneIndex3] ? w.weight3 : 0) >= ArmWeight;
            }
            var map = new Dictionary<int, int>(); var oldIndices = new List<int>();
            var triangles = new List<int>[source.subMeshCount];
            for (int sub = 0; sub < triangles.Length; sub++)
            {
                triangles[sub] = new List<int>(); var input = source.GetTriangles(sub);
                for (int i = 0; i < input.Length; i += 3)
                {
                    if (!keep[input[i]] || !keep[input[i + 1]] || !keep[input[i + 2]]) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int old = input[i + j];
                        if (!map.TryGetValue(old, out int next)) { next = oldIndices.Count; oldIndices.Add(old); map.Add(old, next); }
                        triangles[sub].Add(next);
                    }
                }
            }
            if (oldIndices.Count == 0) return null;
            var positions = source.vertices; var normals = source.normals; var uv = source.uv; var tangents = source.tangents;
            int count = oldIndices.Count;
            var v = new Vector3[count]; var n = new Vector3[count]; var t = new Vector4[count]; var tex = new Vector2[count]; var skin = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                int old = oldIndices[i]; v[i] = positions[old]; n[i] = normals[old]; tex[i] = uv[old];
                var w = weights[old];
                if (!arm[w.boneIndex0]) w.weight0 = 0;
                if (!arm[w.boneIndex1]) w.weight1 = 0;
                if (!arm[w.boneIndex2]) w.weight2 = 0;
                if (!arm[w.boneIndex3]) w.weight3 = 0;
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                w.weight0 /= sum; w.weight1 /= sum; w.weight2 /= sum; w.weight3 /= sum;
                skin[i] = w;
                if (tangents.Length == positions.Length) t[i] = tangents[old];
            }
            var mesh = new Mesh { name = "FirstPersonArms", indexFormat = IndexFormat.UInt32, vertices = v, normals = n, uv = tex, boneWeights = skin, bindposes = source.bindposes };
            if (tangents.Length == positions.Length) mesh.tangents = t;
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
