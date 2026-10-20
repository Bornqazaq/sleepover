using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Own mascot assets and additive head skins; roster and player prefabs are read-only.</summary>
    public static class CircusMascotBuilder
    {
        public const string Art = "Assets/_Project/Art/CircusNight/Mascot";
        public const string PrefabPath = Art + "/CN_Mascot.prefab";
        public const string LibraryPath = Art + "/CN_AngerHeads.asset";
        private const string ModelPath = Art + "/CN_Mascot.fbx";

        [MenuItem("Igruha/Цирк/Талисман и реакция проигравшего — обе сцены")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the active scene and leave Play Mode first.");
            Directory.CreateDirectory(Art + "/Heads"); AssetDatabase.Refresh();
            BuildModel(); BuildHeads();
            string original = EditorSceneManager.GetActiveScene().path;
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    ApplyActive(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
                AssetDatabase.SaveAssets();
                // Dense avatar skins use binary assets. The serialized local ID is
                // different from CreateAsset's ID, so rebind library references afterwards.
                var library = AssetDatabase.LoadAssetAtPath<CircusAngerLibrary>(LibraryPath);
                var paths = library.Entries.Select(e => AssetDatabase.GetAssetPath(e.headMesh)).ToArray();
                foreach (string path in paths)
                {
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    var copy = Object.Instantiate(mesh); copy.name = mesh.name;
                    UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { copy }, path, false);
                    Object.DestroyImmediate(copy);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                var data = new SerializedObject(library); var entries = data.FindProperty("entries");
                for (int i = 0; i < paths.Length; i++)
                    entries.GetArrayElementAtIndex(i).FindPropertyRelative("headMesh").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Mesh>(paths[i]);
                data.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            }
            finally { if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original); }
            Debug.Log("CIRCUS_MASCOT_BUILT both scenes, eight head skins, shared descent clock");
        }

        [MenuItem("Igruha/Цирк/Обновить модель талисмана — обе сцены")]
        public static void RefreshPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the active scene and leave Play Mode first.");
            AssetDatabase.Refresh(); BuildModel();
            string original = EditorSceneManager.GetActiveScene().path;
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    ApplyActive(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
                AssetDatabase.SaveAssets();
            }
            finally { if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original); }
            Debug.Log("CIRCUS_MASCOT_PRESENTATION_UPDATED horse, front window, both scenes");
        }

        internal static void ApplyActive()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<CircusMascotActor>(PrefabPath);
            if (prefab == null) return;
            foreach (var cage in Object.FindObjectsByType<CageStation>(FindObjectsSortMode.None))
            {
                var effect = cage.GetComponent<CircusCageMascot>();
                if (effect == null) effect = cage.gameObject.AddComponent<CircusCageMascot>();
                var data = new SerializedObject(effect);
                Set(data, "mascotPrefab", prefab);
                Set(data, "headLibrary", AssetDatabase.LoadAssetAtPath<CircusAngerLibrary>(LibraryPath));
                Set(data, "angerMaterial", AssetDatabase.LoadAssetAtPath<Material>(Art + "/CN_AngerTint.mat"));
                Set(data, "puffMaterial", AssetDatabase.LoadAssetAtPath<Material>(Art + "/CN_Steam.mat"));
                data.FindProperty("localAnchor").vector3Value = new Vector3(0, .55f, 2.05f);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static void BuildModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true; importer.importBlendShapes = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = clip.name.EndsWith("Mascot_Idle");
                clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(Art + "/CN_Mascot_Albedo.png");
            textureImporter.maxTextureSize = 4096;
            textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
            textureImporter.SaveAndReimport();
            var body = Material("CN_Mascot", "Universal Render Pipeline/Lit");
            body.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/CN_Mascot_Albedo.png"));
            body.SetColor("_BaseColor", new Color(.70f, .70f, .70f)); body.SetFloat("_Smoothness", .08f);
            body.SetFloat("_SpecularHighlights", 0); body.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            body.SetFloat("_EnvironmentReflections", 0); body.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            body.EnableKeyword("_EMISSION"); body.SetColor("_EmissionColor", new Color(.85f, .85f, .85f));
            body.SetTexture("_EmissionMap", body.GetTexture("_BaseMap"));
            // URP validation disables _EMISSION when GI flags contain no emissive mode.
            body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            UnityEditor.BaseShaderGUI.SetMaterialKeywords(body);
            EditorUtility.SetDirty(body);
            var tint = Material("CN_AngerTint", "Igruha/Circus/Head anger overlay");
            var steam = Material("CN_Steam", "Igruha/Circus/Cartoon steam");
            string controllerPath = Art + "/CN_Mascot.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            foreach (var state in stateMachine.states) stateMachine.RemoveState(state.state);
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            {
                var state = stateMachine.AddState(clip.name.EndsWith("Mascot_Idle") ? "Idle" : "Taunt");
                state.motion = clip; state.writeDefaultValues = true;
                if (state.name == "Idle") stateMachine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            var root = new GameObject("Circus mascot");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                model.name = "Model"; model.transform.localScale = Vector3.one * .42f;
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.sharedMaterials = Enumerable.Repeat(body, skin.sharedMesh.subMeshCount).ToArray();
                    skin.updateWhenOffscreen = true;
                }
                var actor = root.AddComponent<CircusMascotActor>(); var data = new SerializedObject(actor);
                var bones = model.GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
                Set(data, "model", model.transform); Set(data, "animator", animator); Set(data, "head", bones["Head"]);
                Set(data, "rightHand", bones["Hand.R"]); Set(data, "indexBase", bones["Index1.R"]); Set(data, "indexTip", bones["Index2.R"]);
                Set(data, "leftLid", bones["UpperLid.L"].GetComponent<SkinnedMeshRenderer>());
                Set(data, "rightLid", bones["UpperLid.R"].GetComponent<SkinnedMeshRenderer>());
                Set(data, "face", bones["Horse_Meshy_Skin"].GetComponent<SkinnedMeshRenderer>());
                var arrival = CircusAngerEffect.CreatePuffs(root.transform, "Entrance puff", steam, true);
                arrival.transform.localPosition = Vector3.up * .85f;
                var puffMain = arrival.main; puffMain.startLifetime = .85f;
                puffMain.startSize = new ParticleSystem.MinMaxCurve(.32f, .56f);
                var puffShape = arrival.shape; puffShape.scale = new Vector3(.9f, 1.8f, .65f);
                Set(data, "arrival", arrival);
                var audio = root.AddComponent<AudioSource>();
                audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Minigames/Circus/creepy-laugh-single-male-aspic-long.wav");
                audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 1; audio.volume = .7f;
                audio.pitch = 1f; audio.minDistance = 1.2f; audio.maxDistance = 10; audio.rolloffMode = AudioRolloffMode.Linear;
                Set(data, "laugh", audio); data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildHeads()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>("Assets/_Project/Settings/Gameplay/CharacterRoster.asset");
            var entries = new List<CircusAngerLibrary.Entry>();
            foreach (var character in roster.Characters)
            {
                if (!character.IsAvailable) throw new InvalidOperationException("Missing avatar " + character.DisplayName);
                var instance = Object.Instantiate(character.Prefab);
                try
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    var head = animator.GetBoneTransform(HumanBodyBones.Head); int found = 0;
                    foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh = ExtractHead(skin, head);
                        if (mesh == null) continue;
                        string path = Art + "/Heads/" + character.Prefab.name + "_" + found++ + ".asset";
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                        else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
                        entries.Add(new CircusAngerLibrary.Entry { character = character.DisplayName, sourceMesh = skin.sharedMesh, headMesh = saved });
                    }
                    if (found == 0) throw new InvalidOperationException("No head skin: " + character.DisplayName);
                }
                finally { Object.DestroyImmediate(instance); }
            }
            var library = AssetDatabase.LoadAssetAtPath<CircusAngerLibrary>(LibraryPath);
            if (library == null) { library = ScriptableObject.CreateInstance<CircusAngerLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath); }
            var data = new SerializedObject(library); var array = data.FindProperty("entries"); array.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("character").stringValue = entries[i].character;
                entry.FindPropertyRelative("sourceMesh").objectReferenceValue = entries[i].sourceMesh;
                entry.FindPropertyRelative("headMesh").objectReferenceValue = entries[i].headMesh;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Mesh ExtractHead(SkinnedMeshRenderer skin, Transform head)
        {
            var source = skin.sharedMesh; var weights = source.boneWeights;
            if (weights.Length != source.vertexCount) return null;
            var headBones = skin.bones.Select(b => b != null && (b == head || b.IsChildOf(head))).ToArray();
            var mask = weights.Select(w => (headBones[w.boneIndex0] ? w.weight0 : 0) + (headBones[w.boneIndex1] ? w.weight1 : 0) +
                (headBones[w.boneIndex2] ? w.weight2 : 0) + (headBones[w.boneIndex3] ? w.weight3 : 0)).ToArray();
            var indices = new List<int>(); var remap = new Dictionary<int, int>(); var triangles = new List<int[]>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var src = source.GetTriangles(sub); var kept = new List<int>();
                for (int i = 0; i < src.Length; i += 3)
                {
                    if (mask[src[i]] < .15f || mask[src[i + 1]] < .15f || mask[src[i + 2]] < .15f) continue;
                    for (int n = 0; n < 3; n++)
                    {
                        int index = src[i + n];
                        if (!remap.TryGetValue(index, out int mapped)) { mapped = indices.Count; remap.Add(index, mapped); indices.Add(index); }
                        kept.Add(mapped);
                    }
                }
                triangles.Add(kept.ToArray());
            }
            if (indices.Count == 0) return null;
            var vertices = source.vertices; var normals = source.normals; var tangents = source.tangents; var uv = source.uv;
            var result = new Mesh { name = source.name + " head anger", indexFormat = IndexFormat.UInt32 };
            result.vertices = indices.Select(i => vertices[i]).ToArray(); result.normals = indices.Select(i => normals[i]).ToArray();
            if (tangents.Length == vertices.Length) result.tangents = indices.Select(i => tangents[i]).ToArray();
            if (uv.Length == vertices.Length) result.uv = indices.Select(i => uv[i]).ToArray();
            result.colors = indices.Select(i => new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .85f, mask[i])))).ToArray();
            result.boneWeights = indices.Select(i => weights[i]).ToArray(); result.bindposes = source.bindposes;
            result.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) result.SetTriangles(triangles[i], i);
            result.RecalculateBounds(); return result;
        }

        private static Material Material(string name, string shader)
        {
            string path = Art + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
        private static void Set(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    }
}
