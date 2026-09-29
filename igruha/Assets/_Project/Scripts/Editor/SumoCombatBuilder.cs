using System;
using System.Collections.Generic;
using System.IO;
using Igruha.Core.Minigame;
using Igruha.Minigames.SumoRing;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>Authors separate native humanoid clips and sampled curves. Never edits shared controllers or character prefabs.</summary>
    public static class SumoCombatBuilder
    {
        private const string Folder = "Assets/_Project/Art/Animations/Sumo";
        private const string Settings = "Assets/_Project/Settings/Gameplay/Minigames/";
        private static readonly string[] Names = {
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right", "Chest Front-Back", "Chest Twist Left-Right", "Neck Nod Down-Up",
            "Left Shoulder Down-Up", "Right Shoulder Down-Up", "Left Arm Down-Up", "Right Arm Down-Up", "Left Arm Front-Back", "Right Arm Front-Back",
            "Left Arm Twist In-Out", "Right Arm Twist In-Out", "Left Forearm Stretch", "Right Forearm Stretch", "Left Forearm Twist In-Out", "Right Forearm Twist In-Out",
            "Left Hand Down-Up", "Right Hand Down-Up", "Left Hand In-Out", "Right Hand In-Out",
            "Left Upper Leg Front-Back", "Right Upper Leg Front-Back", "Left Upper Leg In-Out", "Right Upper Leg In-Out", "Left Lower Leg Stretch", "Right Lower Leg Stretch",
            "Left Foot Up-Down", "Right Foot Up-Down" };

        [MenuItem("Igruha/Minigames/Update Sumo combat motions and tutorial")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before writing combat assets.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var library = AssetDatabase.LoadAssetAtPath<SumoMotionLibrary>(Folder + "/SumoMotions.asset");
            if (library == null) { library = ScriptableObject.CreateInstance<SumoMotionLibrary>(); AssetDatabase.CreateAsset(library, Folder + "/SumoMotions.asset"); }
            var data = new SumoMotionData[8];
            for (int m = 0; m < data.Length; m++) data[m] = BuildMotion((SumoMotion)m);
            var field = typeof(SumoMotionLibrary).GetField("motions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.SetValue(library, data); EditorUtility.SetDirty(library);
            var config = AssetDatabase.LoadAssetAtPath<SumoConfig>(Settings + "SumoConfig.asset");
            var so = new SerializedObject(config); so.FindProperty("motions").objectReferenceValue = library;
            so.FindProperty("combatEffectMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Minigames/SumoRing/Materials/Dust.mat"); so.ApplyModifiedPropertiesWithoutUndo();
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(Settings + "SumoRing.asset");
            var d = new SerializedObject(definition);
            Strings(d, "controlHints", new[] { "ЛКМ — толчок · зажми и отпусти ЛКМ — силовой", "ПКМ — защита · ПКМ перед попаданием — парирование", "После парирования нажми ЛКМ — контратака", "WASD — движение · Space — прыжок · геймпад: RT / LT" });
            Strings(d, "tutorialSteps", new[] { "Толкай к краю. Зажми ЛКМ, чтобы пробить защиту.", "Защищайся лицом к удару. Точный блок открывает контратаку ЛКМ.", "Береги спину и следи за трещинами: край осыпается." });
            Strings(d, "tutorialQuickHints", new[] { "ЛКМ — толчок / заряд", "ПКМ — блок / парирование", "Отбил → ЛКМ — ответ" });
            d.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            Debug.Log("SUMO_COMBAT: eight humanoid clips, motion library and tutorial updated. Shared controllers unchanged.");
        }
        private static void Strings(SerializedObject obj, string name, string[] text)
        { var p = obj.FindProperty(name); p.arraySize = text.Length; for (int i = 0; i < text.Length; i++) p.GetArrayElementAtIndex(i).stringValue = text[i]; }
        private static SumoMotionData BuildMotion(SumoMotion motion)
        {
            string path = Folder + "/Sumo_" + motion + ".anim";
            var clip = new AnimationClip { name = "Sumo_" + motion, frameRate = 60 };
            float[] times = { 0, .18f, .42f, .6f, .74f, 1 };
            var muscles = new List<string>(Names);
            foreach (string name in HumanTrait.MuscleName) if (name.Contains("Stretched") || name.EndsWith(" Spread")) muscles.Add(name);
            var tracks = new SumoMuscleTrack[muscles.Count];
            for (int i = 0; i < muscles.Count; i++)
            {
                int muscle = Array.IndexOf(HumanTrait.MuscleName, muscles[i]);
                if (muscle < 0) throw new InvalidOperationException("Unknown humanoid muscle " + muscles[i]);
                var keys = new Keyframe[times.Length];
                for (int k = 0; k < times.Length; k++) keys[k] = new Keyframe(times[k], Value(motion, muscles[i], times[k]));
                var curve = new AnimationCurve(keys);
                for (int k = 0; k < times.Length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscles[i]), curve);
                tracks[i] = new SumoMuscleTrack { Muscle = muscle, Leg = i >= 22 && i < Names.Length, Curve = curve };
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = motion == SumoMotion.Guard; settings.stopTime = 1; AnimationUtility.SetAnimationClipSettings(clip, settings);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
            return new SumoMotionData { EditableClip = clip, Tracks = tracks };
        }
        // Human muscle zero is a relaxed, bent-limb stance. Curves are poses, never physical displacement.
        private static float Value(SumoMotion motion, string muscle, float time)
        {
            float value = Guard(muscle);
            float wind = Mathf.Sin(Mathf.Clamp01(time / .42f) * Mathf.PI * .5f);
            float strike = Mathf.Clamp01((time - .42f) / .18f) * (1 - Mathf.Clamp01((time - .74f) / .26f));
            float pulse = Mathf.Sin(Mathf.Clamp01(time) * Mathf.PI);
            bool left = muscle.StartsWith("Left ");
            if (motion == SumoMotion.Guard)
                return value + (muscle == "Chest Front-Back" ? Mathf.Sin(time * Mathf.PI * 2) * .018f : 0);
            if (motion == SumoMotion.Charge || motion == SumoMotion.Heavy || motion == SumoMotion.Quick || motion == SumoMotion.Counter)
            {
                float load = motion == SumoMotion.Charge ? time : wind * (1 - strike) * (1 - Mathf.Clamp01((time - .74f) / .26f));
                float power = motion == SumoMotion.Quick ? .72f : 1;
                if (muscle.EndsWith("Arm Front-Back")) value += .20f * load - .30f * strike;
                if (muscle.EndsWith("Forearm Stretch")) value += -.40f * load + .80f * strike;
                if (muscle.EndsWith("Arm Down-Up")) value += -.08f * load + .12f * strike;
                if (muscle.EndsWith("Hand Down-Up")) value += .12f * strike;
                if (muscle == "Spine Front-Back") value += (-.24f * load + .42f * strike) * power;
                if (muscle == "Chest Front-Back") value += (-.16f * load + .24f * strike) * power;
                if (muscle.EndsWith("Upper Leg Front-Back")) value += left ? -.10f * load + .08f * strike : .05f * load;
                if (muscle == "Chest Twist Left-Right") value += motion == SumoMotion.Counter ? -.30f * load + .20f * strike : .06f * load;
            }
            else if (motion == SumoMotion.Parry)
            {
                if (muscle == "Chest Twist Left-Right") value = -.35f * pulse;
                if (muscle == "Spine Twist Left-Right") value = -.16f * pulse;
                if (muscle == "Left Arm Front-Back") value += .38f * pulse;
                if (muscle == "Left Arm Down-Up") value += .32f * pulse;
                if (muscle == "Left Forearm Stretch") value += .4f * pulse;
                if (muscle == "Right Arm Front-Back") value -= .12f * pulse;
            }
            else
            {
                float recoil = motion == SumoMotion.Brace ? .45f : 1;
                if (muscle == "Spine Front-Back") value -= .45f * pulse * recoil;
                if (muscle == "Chest Front-Back") value -= .3f * pulse * recoil;
                if (muscle.EndsWith("Arm Down-Up")) value += .28f * pulse * recoil;
                if (muscle.EndsWith("Forearm Stretch")) value -= .2f * pulse;
                if (muscle == "Left Upper Leg Front-Back") value += .25f * pulse * recoil;
                if (muscle == "Right Upper Leg Front-Back") value -= .2f * pulse * recoil;
            }
            return Mathf.Clamp(value, -.95f, .95f);
        }
        private static float Guard(string name)
        {
            if (name == "Spine Front-Back") return .14f;
            if (name == "Chest Front-Back") return .10f;
            if (name == "Neck Nod Down-Up") return -.08f;
            if (name.Contains("Stretched")) return .55f;
            if (name.EndsWith(" Spread")) return .08f;
            if (name.EndsWith("Arm Down-Up")) return -.35f;
            if (name.EndsWith("Arm Front-Back")) return -.38f;
            if (name.EndsWith("Arm Twist In-Out")) return .12f;
            if (name.EndsWith("Forearm Stretch")) return -.05f;
            if (name.EndsWith("Forearm Twist In-Out")) return 0;
            if (name.EndsWith("Hand Down-Up")) return .45f;
            if (name.EndsWith("Upper Leg In-Out")) return .20f;
            if (name.EndsWith("Upper Leg Front-Back")) return -.10f;
            if (name.EndsWith("Lower Leg Stretch")) return .50f;
            if (name.EndsWith("Foot Up-Down")) return .06f;
            return 0;
        }
    }
}
