using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.Tests.Editor
{
    /// <summary>IGR-583: живот не должен ехать за ключицами при поднятии рук.</summary>
    public sealed class CharacterSkinWeightTests
    {
        private static readonly string[] Characters = { "Aza", "Boss", "Fat", "Girl", "Milez", "MyBoy", "Shlanga", "Player" };
        private static string PrefabPath(string character) => $"Assets/_Project/Prefabs/Player/{character}.prefab";
        private GameObject instance;
        private Mesh before;
        private Mesh after;

        [TearDown]
        public void Clean()
        {
            if (instance != null) Object.DestroyImmediate(instance);
            if (before != null) Object.DestroyImmediate(before);
            if (after != null) Object.DestroyImmediate(after);
        }

        [Test]
        public void RaisingShouldersDoesNotPullTheBellyApart([ValueSource(nameof(Characters))] string character)
        {
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character)));
            Animator animator = instance.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
            animator.enabled = false;

            SkinnedMeshRenderer skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = skin.sharedMesh;
            Transform[] bones = skin.bones;
            Matrix4x4[] poses = mesh.bindposes;
            int hips = Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.Hips));
            int neck = Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.Neck));
            Vector3 origin = poses[hips].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 axis = poses[neck].inverse.MultiplyPoint3x4(Vector3.zero) - origin;
            float height = axis.magnitude;
            axis /= height;

            before = new Mesh();
            after = new Mesh();
            skin.BakeMesh(before, true);
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder).rotation =
                Quaternion.AngleAxis(60f, instance.transform.forward) *
                animator.GetBoneTransform(HumanBodyBones.LeftShoulder).rotation;
            animator.GetBoneTransform(HumanBodyBones.RightShoulder).rotation =
                Quaternion.AngleAxis(-60f, instance.transform.forward) *
                animator.GetBoneTransform(HumanBodyBones.RightShoulder).rotation;
            skin.BakeMesh(after, true);

            Vector3[] rest = mesh.vertices;
            Vector3[] first = before.vertices;
            Vector3[] raised = after.vertices;
            float worst = 0f;
            int checkedVertices = 0;
            for (int v = 0; v < rest.Length; v++)
            {
                float along = Vector3.Dot(rest[v] - origin, axis) / height;
                // Полоса живота значительно ниже рукава; здесь нет вершин рук.
                if (along < 0.2f || along > 0.45f) continue;
                checkedVertices++;
                worst = Mathf.Max(worst, skin.transform.TransformVector(raised[v] - first[v]).magnitude);
            }

            Assert.That(checkedVertices, Is.GreaterThan(1000), "Проверка не нашла область живота");
            Assert.That(worst, Is.LessThan(0.01f),
                $"Ключицы уводят кожу живота на {worst * 100f:F1} см — неверные веса модели");
        }

        [Test]
        public void ImportedWeightsAreNormalizedAndReferenceExistingBones([ValueSource(nameof(Characters))] string character)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character));
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            int invalid = 0;
            int boneCount = skin.bones.Length;
            foreach (BoneWeight w in weights)
            {
                float total = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (float.IsNaN(total) || Mathf.Abs(total - 1f) > 0.00001f ||
                    w.weight0 < 0f || w.weight1 < 0f || w.weight2 < 0f || w.weight3 < 0f ||
                    w.boneIndex0 < 0 || w.boneIndex0 >= boneCount ||
                    w.boneIndex1 < 0 || w.boneIndex1 >= boneCount ||
                    w.boneIndex2 < 0 || w.boneIndex2 >= boneCount ||
                    w.boneIndex3 < 0 || w.boneIndex3 >= boneCount)
                {
                    invalid++;
                }
            }

            Assert.That(weights.Length, Is.EqualTo(skin.sharedMesh.vertexCount));
            Assert.That(invalid, Is.Zero, "Импорт создал некорректные веса кожи");
        }
    }
}
