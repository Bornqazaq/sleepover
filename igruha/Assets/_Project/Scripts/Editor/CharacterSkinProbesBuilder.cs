using System.Collections.Generic;
using System.IO;
using Igruha.Core.Player;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает <see cref="CharacterSkinProbes"/>: у каждого персонажа из
    /// <c>Prefabs/Player</c> отбирает несколько сотен вершин кожи, по которым
    /// подъём модели видит, где кожа касается пола.
    ///
    /// <b>Какие вершины.</b> Каждая вершина отнесена к кости с наибольшим
    /// весом. Для каждой кости берутся крайние вершины её области по 26
    /// направлениям — осям, диагоналям граней и углам — в пространстве самой
    /// кости. Как бы кость ни повернулась, нижняя точка её области окажется
    /// среди отобранных или рядом с ними. Отбор в позе привязки, поэтому
    /// набор не зависит от клипа.
    ///
    /// В редакторе сетка читается и без Read/Write, в сборке — нет. Поэтому
    /// сборщик и существует: в игре точки берутся из ассета, а не из сетки.
    ///
    /// Пересобирать при замене модели персонажа или его сетки. Тест
    /// <c>KnockdownPoseTests.EveryCharacterHasSkinProbes</c> напомнит.
    /// </summary>
    internal static class CharacterSkinProbesBuilder
    {
        private const string PlayerPrefabFolder = "Assets/_Project/Prefabs/Player";
        private const string LibraryFolder = "Assets/_Project/Resources";
        private const string LibraryPath = LibraryFolder + "/" + CharacterSkinProbes.ResourceName + ".asset";

        private const int LeftLegGroup = 0;
        private const int RightLegGroup = 1;
        private const int LeftArmGroup = 2;
        private const int RightArmGroup = 3;
        private const int TorsoGroup = 4;

        [MenuItem("Igruha/Персонажи/Собрать опорные точки кожи")]
        public static void Build()
        {
            var entries = new List<CharacterSkinProbes.Entry>();
            var seen = new HashSet<Mesh>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<PlayerController>() == null)
                {
                    continue;
                }

                // Кости человека знает только живой аниматор: на ассете
                // префаба GetBoneTransform ничего не находит.
                var instance = (GameObject)Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    CollectEntries(instance, path, seen, entries);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Save(entries.ToArray());
        }

        private static void CollectEntries(GameObject instance, string path, HashSet<Mesh> seen,
            List<CharacterSkinProbes.Entry> entries)
        {
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"Опоры кожи: у {path} нет человеческого аниматора — пропущен");
                return;
            }

            Dictionary<Transform, HumanBodyBones> human = HumanBones(animator);

            foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh;
                if (mesh == null || !seen.Add(mesh))
                {
                    continue;
                }

                CharacterSkinProbes.Entry entry = BuildEntry(skin, human);
                entries.Add(entry);
                Debug.Log($"Опоры кожи: {Path.GetFileNameWithoutExtension(path)} — {entry.Probes.Length} точек из {mesh.vertexCount} вершин");
            }
        }

        private static CharacterSkinProbes.Entry BuildEntry(SkinnedMeshRenderer skin, Dictionary<Transform, HumanBodyBones> human)
        {
            Mesh mesh = skin.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindPoses = mesh.bindposes;
            Transform[] bones = skin.bones;

            if (bones.Length - 1 > CharacterSkinProbes.Probe.MaxBoneIndex)
            {
                throw new System.InvalidOperationException(
                    $"{mesh.name}: костей {bones.Length}, в упаковку помещается {CharacterSkinProbes.Probe.MaxBoneIndex + 1}");
            }

            Vector3[] directions = Directions();
            var best = new float[bindPoses.Length, directions.Length];
            var pick = new int[bindPoses.Length, directions.Length];
            for (int b = 0; b < bindPoses.Length; b++)
            {
                for (int d = 0; d < directions.Length; d++)
                {
                    best[b, d] = float.MinValue;
                    pick[b, d] = -1;
                }
            }

            for (int v = 0; v < vertices.Length; v++)
            {
                int bone = DominantBone(weights[v]);
                Vector3 local = bindPoses[bone].MultiplyPoint3x4(vertices[v]);
                for (int d = 0; d < directions.Length; d++)
                {
                    float reach = Vector3.Dot(local, directions[d]);
                    if (reach > best[bone, d])
                    {
                        best[bone, d] = reach;
                        pick[bone, d] = v;
                    }
                }
            }

            var chosen = new SortedSet<int>();
            for (int b = 0; b < bindPoses.Length; b++)
            {
                for (int d = 0; d < directions.Length; d++)
                {
                    if (pick[b, d] >= 0)
                    {
                        chosen.Add(pick[b, d]);
                    }
                }
            }

            var probes = new List<CharacterSkinProbes.Probe>(chosen.Count);
            foreach (int v in chosen)
            {
                BoneWeight w = weights[v];
                probes.Add(new CharacterSkinProbes.Probe
                {
                    Position = vertices[v],
                    Weights = Normalized(w),
                    Bones = CharacterSkinProbes.Probe.PackBones(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3),
                    Group = GroupOf(bones[DominantBone(w)], human)
                });
            }

            return new CharacterSkinProbes.Entry
            {
                Mesh = mesh,
                BindPoses = bindPoses,
                Probes = probes.ToArray()
            };
        }

        /// <summary>Оси, диагонали граней и углы — 26 направлений.</summary>
        private static Vector3[] Directions()
        {
            var result = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x != 0 || y != 0 || z != 0)
                        {
                            result.Add(new Vector3(x, y, z).normalized);
                        }
                    }
                }
            }

            return result.ToArray();
        }

        private static int DominantBone(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float weight = w.weight0;

            if (w.weight1 > weight)
            {
                bone = w.boneIndex1;
                weight = w.weight1;
            }

            if (w.weight2 > weight)
            {
                bone = w.boneIndex2;
                weight = w.weight2;
            }

            if (w.weight3 > weight)
            {
                bone = w.boneIndex3;
            }

            return bone;
        }

        /// <summary>Веса, приведённые к сумме 1: у импорта бывает сумма чуть мимо единицы.</summary>
        private static Vector4 Normalized(BoneWeight w)
        {
            var weights = new Vector4(w.weight0, w.weight1, w.weight2, w.weight3);
            float sum = weights.x + weights.y + weights.z + weights.w;
            return sum > 0f ? weights / sum : new Vector4(1f, 0f, 0f, 0f);
        }

        private static Dictionary<Transform, HumanBodyBones> HumanBones(Animator animator)
        {
            var map = new Dictionary<Transform, HumanBodyBones>();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null && !map.ContainsKey(t))
                {
                    map.Add(t, bone);
                }
            }

            return map;
        }

        /// <summary>Часть тела кости: ближайший предок, известный аватару, решает.</summary>
        private static int GroupOf(Transform bone, Dictionary<Transform, HumanBodyBones> human)
        {
            for (Transform t = bone; t != null; t = t.parent)
            {
                if (human.TryGetValue(t, out HumanBodyBones humanBone))
                {
                    return GroupOf(humanBone);
                }
            }

            return TorsoGroup;
        }

        private static int GroupOf(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.LeftToes:
                    return LeftLegGroup;
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.RightToes:
                    return RightLegGroup;
            }

            string name = bone.ToString();
            if (name.StartsWith("Left") && !name.Contains("Eye"))
            {
                return LeftArmGroup;
            }

            if (name.StartsWith("Right") && !name.Contains("Eye"))
            {
                return RightArmGroup;
            }

            return TorsoGroup;
        }

        private static void Save(CharacterSkinProbes.Entry[] entries)
        {
            if (!AssetDatabase.IsValidFolder(LibraryFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            }

            var library = AssetDatabase.LoadAssetAtPath<CharacterSkinProbes>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterSkinProbes>();
                library.Replace(entries);
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else
            {
                library.Replace(entries);
                EditorUtility.SetDirty(library);
            }

            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log($"Опоры кожи: собрано персонажей — {entries.Length}, ассет {LibraryPath}");
        }
    }
}
