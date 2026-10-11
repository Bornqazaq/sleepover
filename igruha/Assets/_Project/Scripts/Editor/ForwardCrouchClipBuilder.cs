using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.EditorTools
{
    /// <summary>Принятая походка Карлана, запечённая под каждый аватар: прямые стопы и колени.</summary>
    internal static class ForwardCrouchClipBuilder
    {
        private const int Samples = 96;
        private const float HalfStanceWidth = 0.115f;
        private const float ReferenceHeight = 1.65f;
        private const float ChestBendScale = 0.5f;
        private const float UpperChestBendScale = 0.35f;
        private const float NeckNod = 0.7f;
        private const float HeadNod = 0.2f;
        private const float ToeYawTolerance = 2f;
        private const int RetargetCorrectionPasses = 3;

        [MenuItem("Igruha/Player/Rebuild Karlan Crouch Only")]
        internal static void BuildOnly()
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SharedCrouchClipBuilder.OutputClipPath);
            if (source == null) throw new InvalidOperationException("Нет исходного общего клипа приседа");
            AnimationClip clip = Create(source, "Karlan");
            string path = "Assets/_Project/Art/Animations/" + SharedCrouchClipBuilder.CharacterClipFile("Karlan");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(clip);
            }
            AssetDatabase.SaveAssets();
        }

        internal static AnimationClip Create(AnimationClip source, string character)
        {
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = character + "_Crouch";
            // Сохраняем исходное движение корпуса; ослабляем только избыточный наклон воротника.
            ScaleCurve(clip, "Chest Front-Back", ChestBendScale);
            ScaleCurve(clip, "UpperChest Front-Back", UpperChestBendScale);
            SetCurve(clip, "Neck Nod Down-Up", AnimationCurve.Constant(0f, clip.length, NeckNod));
            SetCurve(clip, "Head Nod Down-Up", AnimationCurve.Constant(0f, clip.length, HeadNod));

            Scene scene = EditorSceneManager.NewPreviewScene();
            string prefabName = character == "Karlan" ? "Player" : character;
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/" + prefabName + ".prefab"));
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var controller = new AnimatorController();
            var machine = new AnimatorStateMachine();
            AnimatorState state = machine.AddState("Bake");
            machine.defaultState = state;
            state.motion = clip;
            controller.AddLayer(new AnimatorControllerLayer { name = "Base", defaultWeight = 1f, stateMachine = machine });
            Animator animator = instance.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            try
            {
                animator.Rebind();
                SharedCrouchClipBuilder.RotateRootYaw(clip, -StrideAngle(animator));
                animator.Rebind();
                // Та же относительная ширина шага при разном росте персонажей.
                float halfStance = HalfStanceWidth * instance.GetComponent<CapsuleCollider>().height / ReferenceHeight;
                BakeLegs(clip, animator, halfStance);
                // Некоторые аватары добавляют свой небольшой доворот при
                // обратном переводе костей в muscles. Поправляем только остаток,
                // сохраняя исходные значения кривых и принятую походку Карлана.
                for (int pass = 0; pass < RetargetCorrectionPasses; pass++)
                {
                    animator.Rebind();
                    if (MaxToeYaw(animator) < ToeYawTolerance) break;
                    BakeLegs(clip, animator, halfStance, true);
                }
                return clip;
            }
            catch { UnityEngine.Object.DestroyImmediate(clip); throw; }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(state);
                UnityEngine.Object.DestroyImmediate(machine);
                UnityEngine.Object.DestroyImmediate(controller);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static float StrideAngle(Animator animator)
        {
            var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            var points = new Vector3[2, Samples];
            var means = new Vector3[2];
            for (int i = 0; i < Samples; i++)
            {
                Sample(animator, (float)i / Samples);
                for (int foot = 0; foot < 2; foot++) { points[foot, i] = feet[foot].position; means[foot] += points[foot, i] / Samples; }
            }
            float angle = 0f;
            for (int foot = 0; foot < 2; foot++)
            {
                float xx = 0f, zz = 0f, xz = 0f;
                for (int i = 0; i < Samples; i++)
                {
                    Vector3 p = points[foot, i] - means[foot];
                    xx += p.x * p.x; zz += p.z * p.z; xz += p.x * p.z;
                }
                angle += 0.5f * Mathf.Atan2(2f * xz, zz - xx) * Mathf.Rad2Deg;
            }
            return angle * 0.5f;
        }

        private static float MaxToeYaw(Animator animator)
        {
            var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            float yaw = 0f;
            for (int frame = 0; frame < Samples; frame++)
            {
                Sample(animator, (float)frame / Samples);
                for (int foot = 0; foot < 2; foot++)
                {
                    Vector3 direction = toes[foot].position - feet[foot].position;
                    yaw = Mathf.Max(yaw, Mathf.Abs(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg));
                }
            }
            return yaw;
        }

        private static void BakeLegs(AnimationClip clip, Animator animator, float halfStance, bool preserveMuscleDelta = false)
        {
            string[] names = HumanTrait.MuscleName;
            var curves = new Dictionary<int, AnimationCurve>();
            for (int i = 0; i < names.Length; i++)
                if (names[i].Contains(" Leg ") || names[i].Contains(" Foot ") || names[i].Contains(" Toes ")) curves.Add(i, new AnimationCurve());
            var originalCurves = new Dictionary<int, AnimationCurve>();
            if (preserveMuscleDelta)
                foreach (int index in curves.Keys)
                    originalCurves[index] = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), names[index]));
            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
            {
                var pose = new HumanPose();
                var before = new HumanPose();
                for (int frame = 0; frame <= Samples; frame++)
                {
                    // Последний кадр совпадает с первым: шов цикла не разводит ноги.
                    float fraction = frame == Samples ? 0f : (float)frame / Samples;
                    Sample(animator, fraction);
                    if (preserveMuscleDelta) handler.GetHumanPose(ref before);
                    StraightenLeg(animator, true, halfStance);
                    StraightenLeg(animator, false, halfStance);
                    handler.GetHumanPose(ref pose);
                    foreach (var pair in curves)
                    {
                        float value = pose.muscles[pair.Key];
                        if (preserveMuscleDelta) value += originalCurves[pair.Key].Evaluate(clip.length * fraction) - before.muscles[pair.Key];
                        pair.Value.AddKey(new Keyframe(clip.length * frame / Samples, value));
                    }
                }
            }
            foreach (var pair in curves)
            {
                // Одинаковая касательная на стыке цикла; без неё плотные ключи
                // с нулевыми касательными дают мелкие рывки между кадрами.
                Keyframe[] keys = pair.Value.keys;
                float step = clip.length / Samples;
                for (int i = 0; i < keys.Length; i++)
                {
                    int previous = i == 0 || i == Samples ? Samples - 1 : i - 1;
                    int next = i == 0 || i == Samples ? 1 : i + 1;
                    keys[i].inTangent = keys[i].outTangent = (keys[next].value - keys[previous].value) / (2f * step);
                }
                pair.Value.keys = keys;
                SetCurve(clip, names[pair.Key], pair.Value);
            }
        }

        private static void StraightenLeg(Animator animator, bool left, float halfStance)
        {
            Transform thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform knee = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Transform foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Transform toes = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            Vector3 target = foot.position;
            target.x = left ? -halfStance : halfStance;
            Vector3 toeDirection = toes.position - foot.position;
            Quaternion footRotation = foot.rotation;
            Vector3 origin = thigh.position;
            float upper = Vector3.Distance(origin, knee.position), lower = Vector3.Distance(knee.position, foot.position);
            Vector3 direction = (target - origin).normalized;
            float distance = Mathf.Min(Vector3.Distance(origin, target), upper + lower - 0.0001f);
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            Vector3 bend = Vector3.ProjectOnPlane(Vector3.forward, direction).normalized;
            Vector3 newKnee = origin + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            thigh.rotation = Quaternion.FromToRotation(knee.position - origin, newKnee - origin) * thigh.rotation;
            knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, target - knee.position) * knee.rotation;
            Vector3 forwardToe = new Vector3(0f, toeDirection.y, new Vector2(toeDirection.x, toeDirection.z).magnitude);
            foot.rotation = Quaternion.FromToRotation(toeDirection, forwardToe) * footRotation;
        }

        private static void Sample(Animator animator, float fraction) { animator.Play("Bake", 0, fraction); animator.Update(0f); }
        private static void SetCurve(AnimationClip clip, string muscle, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscle), curve);

        private static void ScaleCurve(AnimationClip clip, string muscle, float scale)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscle);
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++) { keys[i].value *= scale; keys[i].inTangent *= scale; keys[i].outTangent *= scale; }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
