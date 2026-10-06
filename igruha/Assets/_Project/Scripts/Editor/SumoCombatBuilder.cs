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
            var data = new SumoMotionData[Enum.GetValues(typeof(SumoMotion)).Length];
            for (int m = 0; m < data.Length; m++) data[m] = BuildMotion((SumoMotion)m);
            var field = typeof(SumoMotionLibrary).GetField("motions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field.SetValue(library, data); EditorUtility.SetDirty(library);
            var config = AssetDatabase.LoadAssetAtPath<SumoConfig>(Settings + "SumoConfig.asset");
            var so = new SerializedObject(config); so.FindProperty("motions").objectReferenceValue = library;
            so.FindProperty("combatEffectMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Minigames/SumoRing/Materials/Dust.mat"); so.ApplyModifiedPropertiesWithoutUndo();
            var definition = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(Settings + "SumoRing.asset");
            var d = new SerializedObject(definition);
            Strings(d, "controlHints", new[] { "ЛКМ — толчок · зажми и отпусти ЛКМ — силовой", "ПКМ — защита · ПКМ перед попаданием — парирование", "ПКМ + ЛКМ / LT + RT — рывок плечом · после парирования — ответ", "WASD — движение · Space — прыжок · геймпад: RT / LT" });
            Strings(d, "tutorialSteps", new[] { "Толкай к краю. Зажми ЛКМ, чтобы пробить защиту.", "Защищайся лицом к удару. Точный блок открывает контратаку ЛКМ.", "Зажми защиту и нажми толчок — рывок. Не промахнись у края!" });
            Strings(d, "tutorialQuickHints", new[] { "ЛКМ — толчок / заряд", "ПКМ — блок / парирование", "ПКМ + ЛКМ — рывок" });
            d.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            Debug.Log("SUMO_COMBAT: humanoid clips, motion library and tutorial updated. Shared controllers unchanged.");
        }
        private static void Strings(SerializedObject obj, string name, string[] text)
        { var p = obj.FindProperty(name); p.arraySize = text.Length; for (int i = 0; i < text.Length; i++) p.GetArrayElementAtIndex(i).stringValue = text[i]; }
        private static SumoMotionData BuildMotion(SumoMotion motion)
        {
            string path = Folder + "/Sumo_" + motion + ".anim";
            var clip = new AnimationClip { name = "Sumo_" + motion, frameRate = 60 };
            float[] times = { 0, .08f, .16f, .24f, .30f, .32f, .40f, .48f, .56f, .60f, .68f, .76f, .84f, .92f, 1 };
            var muscles = new List<string>(Names);
            foreach (string name in HumanTrait.MuscleName) if (name.Contains("Stretched") || name.EndsWith(" Spread")) muscles.Add(name);
            var tracks = new SumoMuscleTrack[muscles.Count];
            for (int i = 0; i < muscles.Count; i++)
            {
                int muscle = Array.IndexOf(HumanTrait.MuscleName, muscles[i]);
                if (muscle < 0) throw new InvalidOperationException("Unknown humanoid muscle " + muscles[i]);
                var keys = new Keyframe[times.Length];
                for (int k = 0; k < times.Length; k++) keys[k] = new Keyframe(times[k], Value(motion, muscles[i], times[k]));
                bool constant = true;
                for (int k = 1; k < keys.Length; k++) constant &= Mathf.Approximately(keys[k].value, keys[0].value);
                if (constant) keys = new[] { keys[0], keys[keys.Length - 1] };
                var curve = new AnimationCurve(keys);
                for (int k = 0; k < keys.Length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscles[i]), curve);
                tracks[i] = new SumoMuscleTrack { Muscle = muscle, Leg = i >= 22 && i < Names.Length, Curve = curve };
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = motion == SumoMotion.Guard || motion == SumoMotion.Balance; settings.stopTime = 1; AnimationUtility.SetAnimationClipSettings(clip, settings);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; }
            return new SumoMotionData { EditableClip = clip, Tracks = tracks };
        }
        // Human muscle zero is a relaxed, bent-limb stance. Curves are poses, never physical displacement.
        private static float Ease(float start, float end, float time) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, time));
        private static float Value(SumoMotion motion, string muscle, float time)
        {
            float value = Guard(muscle);
            bool left = muscle.StartsWith("Left ");
            if (motion == SumoMotion.Guard)
                return value + (muscle == "Chest Front-Back" ? Mathf.Sin(time * Mathf.PI * 2) * .018f : 0);
            if (motion == SumoMotion.Balance) return Balance(muscle, time, left);
            if (motion == SumoMotion.Shoulder)
            {
                float load = Ease(0, .35f, time) * (1 - Ease(.65f, 1, time));
                if (muscle == "Spine Front-Back") value -= .38f * load;
                if (muscle == "Chest Front-Back") value -= .20f * load;
                if (muscle == "Chest Twist Left-Right") value += .40f * load;
                if (muscle == "Spine Twist Left-Right") value += .18f * load;
                if (muscle == "Neck Nod Down-Up") value += .08f * load;
                if (muscle.EndsWith("Arm Down-Up")) value -= .26f * load;
                if (muscle.EndsWith("Hand Down-Up")) value -= .35f * load;
                if (muscle.EndsWith("Shoulder Down-Up")) value += (left ? -.10f : .05f) * load;
                if (muscle.EndsWith("Arm Front-Back")) value += (left ? .24f : .12f) * load;
                if (muscle.EndsWith("Forearm Stretch")) value += (left ? .25f : .50f) * load;
                if (muscle.EndsWith("Lower Leg Stretch")) value -= .25f * load;
                if (muscle.EndsWith("Upper Leg Front-Back")) value += (left ? .16f : -.08f) * load;
                return Mathf.Clamp(value, -.95f, .95f);
            }
            if (motion == SumoMotion.Charge || motion == SumoMotion.Heavy || motion == SumoMotion.Quick || motion == SumoMotion.Counter)
            {
                // Heavy starts in the fully loaded pose: releasing a charge never unfolds
                // back to Guard before winding up again. Palms reach contact at sample .6.
                float thrust = Ease(.32f, .6f, time) * (1 - Ease(.68f, 1, time));
                float load = motion == SumoMotion.Charge ? Ease(0, 1, time)
                    : (motion == SumoMotion.Heavy ? 1 : Ease(0, .3f, time)) * (1 - Ease(.32f, .6f, time));
                if (motion == SumoMotion.Charge) thrust = 0;
                float power = motion == SumoMotion.Quick ? .7f : 1;
                if (muscle.EndsWith("Arm Front-Back")) value += .14f * load - .10f * thrust;
                if (muscle.EndsWith("Forearm Stretch")) value += -.32f * load + .88f * thrust;
                if (muscle.EndsWith("Arm Down-Up")) value += -.06f * load + .10f * thrust;
                if (muscle.EndsWith("Shoulder Down-Up")) value += -.08f * load + .12f * thrust;
                if (muscle.EndsWith("Hand Down-Up")) value += .18f * thrust;
                if (muscle == "Spine Front-Back") value += (-.20f * load + .46f * thrust) * power;
                if (muscle == "Chest Front-Back") value += (-.12f * load + .22f * thrust) * power;
                if (muscle.EndsWith("Lower Leg Stretch")) value += (-.24f * load + (left ? -.10f : .08f) * thrust) * power;
                if (muscle.EndsWith("Upper Leg Front-Back")) value += (.12f * load + (left ? .22f : -.14f) * thrust) * power;
                if (muscle.EndsWith("Upper Leg In-Out")) value += .05f * load;
                if (muscle.EndsWith("Foot Up-Down")) value += (left ? -.10f : .08f) * thrust * power;
                if (muscle == "Spine Twist Left-Right") value += -.06f * load + .06f * thrust;
                if (muscle == "Chest Twist Left-Right") value += motion == SumoMotion.Counter ? -.26f * load + .24f * thrust : -.08f * load + .10f * thrust;
            }
            else if (motion == SumoMotion.Parry)
            {
                float beat = Ease(0, .24f, time) * (1 - Ease(.38f, 1, time));
                if (muscle == "Chest Twist Left-Right") value -= .38f * beat;
                if (muscle == "Spine Twist Left-Right") value -= .18f * beat;
                if (muscle == "Left Arm Front-Back") value += .42f * beat;
                if (muscle == "Left Arm Down-Up") value += .32f * beat;
                if (muscle == "Left Forearm Stretch") value += .42f * beat;
                if (muscle == "Right Arm Front-Back") value -= .08f * beat;
                if (muscle == "Right Lower Leg Stretch") value -= .14f * beat;
            }
            else
            {
                float beat = Ease(0, .16f, time) * (1 - Ease(.38f, 1, time));
                float recoil = motion == SumoMotion.Brace ? .4f : motion == SumoMotion.Stumble ? 1 : .65f;
                float step = Mathf.Sin(time * Mathf.PI * (motion == SumoMotion.Stumble ? 4 : 2)) * Mathf.Sin(time * Mathf.PI);
                if (muscle == "Spine Front-Back") value -= .48f * beat * recoil;
                if (muscle == "Chest Front-Back") value -= .28f * beat * recoil;
                if (muscle.EndsWith("Arm Down-Up")) value += .32f * beat * recoil;
                if (muscle.EndsWith("Arm Front-Back")) value += .20f * beat * recoil;
                if (muscle.EndsWith("Forearm Stretch")) value -= .18f * beat;
                if (muscle.EndsWith("Upper Leg Front-Back")) value += (left ? 1 : -1) * .24f * step * recoil;
                if (muscle.EndsWith("Lower Leg Stretch")) value -= (.16f * beat + .12f * Mathf.Max(0, left ? step : -step)) * recoil;
                if (muscle == "Spine Left-Right") value += .06f * step * recoil;
            }
            return Mathf.Clamp(value, -.95f, .95f);
        }
        private static float Balance(string muscle, float time, bool left)
        {
            float sway = Mathf.Sin(time * Mathf.PI * 2);
            float paddle = Mathf.Sin((time + (left ? 0 : .35f)) * Mathf.PI * 2);
            float value = Guard(muscle);
            if (muscle == "Spine Front-Back") value = .08f;
            if (muscle == "Spine Left-Right") value = .08f * sway;
            if (muscle == "Chest Twist Left-Right") value = .10f * sway;
            if (muscle == "Neck Nod Down-Up") value = -.12f;
            if (muscle.EndsWith("Arm Down-Up")) value = -.05f + .14f * paddle;
            if (muscle.EndsWith("Arm Front-Back")) value = -.06f + .20f * paddle;
            if (muscle.EndsWith("Forearm Stretch")) value = .25f + .18f * paddle;
            if (muscle.EndsWith("Hand Down-Up")) value = .18f;
            if (muscle.EndsWith("Upper Leg Front-Back")) value += (left ? 1 : -1) * .12f * sway;
            if (muscle.EndsWith("Lower Leg Stretch")) value -= .12f + .10f * Mathf.Max(0, left ? sway : -sway);
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
