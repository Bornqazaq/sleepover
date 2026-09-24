using System;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Исправляет веса торса в модели Толстого (IGR-583).
    /// Исходный Mixamo-скин привязал участки живота к ключицам, вплоть до
    /// веса 1. При поднятии рук они уходили вверх, образуя складки насквозь.
    /// Торс получает плавную смесь таза и груди; у плеч и бёдер сохраняется
    /// плавный переход к исходным весам. Геометрия, UV, скелет и клипы прежние.
    /// Выполняется при импорте, без чтения сетки или расходов в игре.
    /// </summary>
    internal sealed class FatSkinWeightRepair : AssetPostprocessor
    {
        // Именно из этого FBX префаб берёт сетку. Другие Fat@ содержат клипы,
        // а Art/Models/Fat.fbx — модель без скелета, в игре она не используется.
        private const string ModelPath = "Assets/_Project/Art/Animations/Fat@Neutral Idle.fbx";

        // Все расстояния — доли высоты торса или расстояния плеча от оси тела,
        // в позе привязки. У этого FBX координаты сетки в сотни раз меньше метра.
        private const float HipBlendHalfHeight = 0.12f;
        private const float ChestBlendStartBelowArm = 0.30f;
        private const float ChestBlendEndBelowArm = 0.04f;
        private const float SpineBlendStartBelowHips = 0.10f;
        private const float FullTorsoHalfWidth = 1.20f;
        private const float OuterTorsoHalfWidth = 1.55f;
        private const int InfluenceCount = 4;

        public override uint GetVersion() => 2;

        private void OnPostprocessModel(GameObject model)
        {
            if (!string.Equals(assetPath, ModelPath, StringComparison.Ordinal))
            {
                return;
            }

            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Repair(skin);
            }
        }

        private static void Repair(SkinnedMeshRenderer skin)
        {
            Mesh mesh = skin.sharedMesh;
            Transform[] bones = skin.bones;
            if (mesh == null || bones.Length == 0)
            {
                return;
            }

            int hips = FindBone(bones, "Hips");
            int neck = FindBone(bones, "Neck");
            int chest = FindBone(bones, "Spine2");
            int arm = FindBone(bones, "LeftArm");
            Matrix4x4[] bindPoses = mesh.bindposes;
            if (hips < 0 || neck < 0 || chest < 0 || arm < 0 || bindPoses.Length != bones.Length)
            {
                Debug.LogError("Fat: скелет изменился, исправление весов торса требует проверки.");
                return;
            }

            Vector3 origin = BoneOrigin(bindPoses, hips);
            Vector3 axis = BoneOrigin(bindPoses, neck) - origin;
            float height = axis.magnitude;
            if (height <= Mathf.Epsilon)
            {
                return;
            }

            axis /= height;
            Vector3 shoulder = BoneOrigin(bindPoses, arm) - origin;
            Vector3 side = Vector3.ProjectOnPlane(shoulder, axis).normalized;
            float halfWidth = Mathf.Abs(Vector3.Dot(shoulder, side));
            float armHeight = Vector3.Dot(shoulder, axis);
            if (halfWidth <= Mathf.Epsilon)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            if (weights.Length != vertices.Length)
            {
                Debug.LogError("Fat: число весов не совпадает с числом вершин.");
                return;
            }

            var accumulated = new float[bones.Length];
            var indices = new int[InfluenceCount];
            var strongest = new float[InfluenceCount];
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 offset = vertices[v] - origin;
                float along = Vector3.Dot(offset, axis);
                float lateral = Mathf.Abs(Vector3.Dot(offset, side));
                float lower = Ramp(-height * HipBlendHalfHeight, height * HipBlendHalfHeight, along);
                float upper = 1f - Ramp(armHeight - height * ChestBlendStartBelowArm,
                    armHeight - height * ChestBlendEndBelowArm, along);
                float sides = 1f - Ramp(halfWidth * FullTorsoHalfWidth, halfWidth * OuterTorsoHalfWidth, lateral);
                float blend = lower * upper * sides;
                if (blend <= 0f)
                {
                    continue;
                }

                Array.Clear(accumulated, 0, accumulated.Length);
                BoneWeight original = weights[v];
                float retained = 1f - blend;
                accumulated[original.boneIndex0] += original.weight0 * retained;
                accumulated[original.boneIndex1] += original.weight1 * retained;
                accumulated[original.boneIndex2] += original.weight2 * retained;
                accumulated[original.boneIndex3] += original.weight3 * retained;

                // Широкое смешивание по всему торсу сохраняет выпуклый живот.
                // Узкие полосы между соседними позвонками дают резкий перегиб:
                // при наклоне футболка снова складывается внутрь шорт.
                float chestWeight = Ramp(-height * SpineBlendStartBelowHips, armHeight, along);
                accumulated[hips] += blend * (1f - chestWeight);
                accumulated[chest] += blend * chestWeight;
                weights[v] = Pack(accumulated, indices, strongest);
            }

            mesh.boneWeights = weights;
        }

        private static BoneWeight Pack(float[] weights, int[] indices, float[] strongest)
        {
            Array.Clear(indices, 0, indices.Length);
            Array.Clear(strongest, 0, strongest.Length);
            for (int bone = 0; bone < weights.Length; bone++)
            {
                for (int slot = 0; slot < InfluenceCount; slot++)
                {
                    if (weights[bone] <= strongest[slot])
                    {
                        continue;
                    }

                    for (int next = InfluenceCount - 1; next > slot; next--)
                    {
                        indices[next] = indices[next - 1];
                        strongest[next] = strongest[next - 1];
                    }

                    indices[slot] = bone;
                    strongest[slot] = weights[bone];
                    break;
                }
            }

            float total = strongest[0] + strongest[1] + strongest[2] + strongest[3];
            return new BoneWeight
            {
                boneIndex0 = indices[0], boneIndex1 = indices[1],
                boneIndex2 = indices[2], boneIndex3 = indices[3],
                weight0 = strongest[0] / total, weight1 = strongest[1] / total,
                weight2 = strongest[2] / total, weight3 = strongest[3] / total
            };
        }

        private static float Ramp(float from, float to, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        private static Vector3 BoneOrigin(Matrix4x4[] poses, int bone) =>
            poses[bone].inverse.MultiplyPoint3x4(Vector3.zero);

        private static int FindBone(Transform[] bones, string name) =>
            Array.FindIndex(bones, bone => bone.name.EndsWith(":" + name, StringComparison.Ordinal));
    }
}
