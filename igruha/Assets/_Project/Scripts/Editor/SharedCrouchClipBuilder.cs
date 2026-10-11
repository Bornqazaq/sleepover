using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Общий присед на месте и поправки под аватары персонажей.</summary>
    internal static class SharedCrouchClipBuilder
    {
        private const string SourceClipPath = "Assets/_Project/Art/Animations/Aza@Sneaking Forward.fbx";
        public const string OutputClipPath = "Assets/_Project/Art/Animations/CrouchWalkForward.anim";
        private const string CharacterFolder = "Assets/_Project/Art/Animations/Crouch";
        private const float RootYawFixDegrees = -11.2f;
        private static readonly string[] Characters = { "Karlan", "Boss", "Shlanga", "Fat", "Milez", "MyBoy", "Aza", "Girl" };
        private static readonly string[] RootRotation = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

        internal static string CharacterClipFile(string character) => $"Crouch/{character}_Crouch.anim";

        [MenuItem("Igruha/Player/Rebuild Shared Crouch Clip")]
        internal static bool Build()
        {
            AnimationClip source = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(SourceClipPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { source = clip; break; }
            if (source == null) { Debug.LogError($"SharedCrouchClipBuilder: не найден {SourceClipPath}."); return false; }

            AnimationClip shared = CopyClip(source, "CrouchWalkForward");
            RotateRootYaw(shared, RootYawFixDegrees);
            MakeInPlace(shared);
            SaveClip(shared, OutputClipPath);
            if (!AssetDatabase.IsValidFolder(CharacterFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Animations", "Crouch");

            foreach (string character in Characters)
            {
                AnimationClip adjusted = ForwardCrouchClipBuilder.Create(AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath), character);
                SaveClip(adjusted, CharacterFolder + "/" + character + "_Crouch.anim");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SharedCrouchClipBuilder: принятая походка Карлана применена к восьми аватарам; прямые стопы и колени.");
            return true;
        }

        private static AnimationClip CopyClip(AnimationClip source, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = source.frameRate };
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));
            return clip;
        }

        private static void MakeInPlace(AnimationClip clip)
        {
            // Удаляем поступательное перемещение из позы, сохраняя небольшое
            // периодическое покачивание. Rigidbody единолично задаёт движение.
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z") continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                float start = curve.Evaluate(0f), velocity = (curve.Evaluate(clip.length) - start) / clip.length;
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value -= start + velocity * keys[i].time;
                    keys[i].inTangent -= velocity;
                    keys[i].outTangent -= velocity;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalPositionXZ = true;
            // Иначе Animator заново вычисляет «фронт» клипа и съедает
            // персональный доворот RootQ при извлечении движения корня.
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        internal static void RotateRootYaw(AnimationClip clip, float degrees)
        {
            var curves = new AnimationCurve[4];
            var bindings = new EditorCurveBinding[4];
            var times = new SortedSet<float>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                int index = Array.IndexOf(RootRotation, binding.propertyName);
                if (index < 0) continue;
                bindings[index] = binding;
                curves[index] = AnimationUtility.GetEditorCurve(clip, binding);
                foreach (Keyframe key in curves[index].keys) times.Add(key.time);
            }
            for (int i = 0; i < 4; i++)
                if (curves[i] == null) throw new InvalidOperationException("Присед: нет кривой " + RootRotation[i]);
            Quaternion correction = Quaternion.Euler(0f, degrees, 0f);
            var rebuilt = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            foreach (float time in times)
            {
                Quaternion q = correction * new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time), curves[3].Evaluate(time));
                rebuilt[0].AddKey(time, q.x); rebuilt[1].AddKey(time, q.y); rebuilt[2].AddKey(time, q.z); rebuilt[3].AddKey(time, q.w);
            }
            for (int i = 0; i < 4; i++) AnimationUtility.SetEditorCurve(clip, bindings[i], rebuilt[i]);
            clip.EnsureQuaternionContinuity();
        }

        private static void SaveClip(AnimationClip clip, string path)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
