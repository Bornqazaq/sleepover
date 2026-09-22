using System.Collections.Generic;
using System.IO;
using Igruha.Core.Player;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Собирает <see cref="CharacterTorsoShape"/>: у каждого персонажа из
    /// <c>Prefabs/Player</c> обмеряет торс — стопку срезов от таза до шеи с
    /// радиусом по восьми секторам, — по которым
    /// <see cref="CharacterArmClearance"/> видит, что рука ушла внутрь тела.
    ///
    /// <b>Что считается торсом.</b> Вершины, чья главная кость лежит под
    /// тазом, поясницей, грудью или верхом груди. Голова, шея, руки и ноги
    /// в объём не входят: рука у головы — нормальная поза танца, а нога в
    /// животе невозможна.
    ///
    /// <b>Почему нижний квинтиль, а не максимум.</b> Радиус должен быть
    /// заведомо внутри тела. Взяв самую дальнюю вершину сектора, мы
    /// вытолкнули бы руку в пустоту там, где тело у́же; взяв нижний квинтиль,
    /// получаем поверхность в самом узком месте сектора. Минимум брать нельзя
    /// — одна случайная вершина у шва посадит радиус на ноль.
    ///
    /// <b>Всё меряется в позе привязки</b>, то есть в пространстве сетки, где
    /// персонаж стоит прямо, а потом переводится в локальное пространство той
    /// кости позвоночника, к которой срез привязан. Поэтому набор не зависит
    /// ни от клипа, ни от позы префаба.
    ///
    /// В редакторе сетка читается и без Read/Write, в сборке — нет. Отсюда и
    /// ассет: в игре объём берётся из него, а не из сетки.
    ///
    /// Пересобирать при замене модели персонажа. Тест
    /// <c>DanceArmClearanceTests.EveryCharacterHasTorsoShape</c> напомнит.
    /// </summary>
    internal static class CharacterTorsoShapeBuilder
    {
        private const string PlayerPrefabFolder = "Assets/_Project/Prefabs/Player";
        private const string LibraryFolder = "Assets/_Project/Resources";
        private const string LibraryPath = LibraryFolder + "/" + CharacterTorsoShape.ResourceName + ".asset";

        /// <summary>Срезов от таза до шеи. Восемь дают шаг около 7 см — мельче живота.</summary>
        private const int SliceCount = 8;

        /// <summary>Какая доля расстояний в ячейке уходит внутрь радиуса, %.</summary>
        private const int RadiusPercentile = 20;

        /// <summary>Меньше этого числа вершин в ячейке — данных нет, радиус берётся у соседей.</summary>
        private const int MinCellVertices = 10;

        /// <summary>Докуда тянется кончик ладони от запястья: доля самой дальней вершины кисти.</summary>
        private const float HandTipReach = 0.6f;

        /// <summary>Кости, вершины под которыми считаются торсом.</summary>
        private static readonly HumanBodyBones[] TorsoBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest
        };

        [MenuItem("Igruha/Персонажи/Собрать объём торса")]
        public static void Build()
        {
            var entries = new List<CharacterTorsoShape.Entry>();
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
                    CollectEntry(instance, path, seen, entries);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Save(entries.ToArray());
        }

        private static void CollectEntry(GameObject instance, string path, HashSet<Mesh> seen,
            List<CharacterTorsoShape.Entry> entries)
        {
            string character = Path.GetFileNameWithoutExtension(path);
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"Объём торса: у {character} нет человеческого аниматора — пропущен");
                return;
            }

            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null || skin.sharedMesh == null || !seen.Add(skin.sharedMesh))
            {
                return;
            }

            if (TryBuildEntry(character, animator, skin, out CharacterTorsoShape.Entry entry))
            {
                entries.Add(entry);
                Debug.Log($"Объём торса: {character} — {entry.Slices.Length} срезов");
            }
        }

        private static bool TryBuildEntry(string character, Animator animator, SkinnedMeshRenderer skin,
            out CharacterTorsoShape.Entry entry)
        {
            entry = default;

            Mesh mesh = skin.sharedMesh;
            Transform[] bones = skin.bones;
            Matrix4x4[] bindPoses = mesh.bindposes;
            if (bindPoses.Length != bones.Length)
            {
                Debug.LogWarning($"Объём торса: у {character} поз привязки {bindPoses.Length} на {bones.Length} костей — пропущен");
                return false;
            }

            // Метры на единицу пространства сетки: у наших моделей оно в сотни
            // раз мельче мирового, и без этого все радиусы — в попугаях.
            float scale = skin.transform.lossyScale.x;

            int hips = BoneIndex(animator, bones, HumanBodyBones.Hips);
            int neck = BoneIndex(animator, bones, HumanBodyBones.Neck);
            if (neck < 0)
            {
                neck = BoneIndex(animator, bones, HumanBodyBones.Head);
            }

            int leftLeg = BoneIndex(animator, bones, HumanBodyBones.LeftUpperLeg);
            if (hips < 0 || neck < 0 || leftLeg < 0)
            {
                Debug.LogWarning($"Объём торса: у {character} не найден позвоночник — пропущен");
                return false;
            }

            Vector3 bottom = Origin(bindPoses, hips);
            Vector3 top = Origin(bindPoses, neck);
            Vector3 axis = top - bottom;
            float height = axis.magnitude;
            if (height <= Mathf.Epsilon)
            {
                Debug.LogWarning($"Объём торса: у {character} таз и шея в одной точке — пропущен");
                return false;
            }

            axis /= height;
            Vector3 side = Origin(bindPoses, leftLeg) - bottom;
            side = Vector3.Normalize(side - axis * Vector3.Dot(side, axis));
            Vector3 front = Vector3.Cross(axis, side).normalized;

            HumanBodyBones[] nearest = NearestHumanBones(animator, bones);
            float[,] radii = MeasureRadii(mesh, nearest, bottom, axis, side, front, height, scale);

            entry = new CharacterTorsoShape.Entry
            {
                Mesh = mesh,
                BoneCount = bones.Length,
                Slices = BuildSlices(animator, bones, bindPoses, radii, bottom, axis, side, front, height, scale),
                LeftHandTip = HandTip(mesh, nearest, bindPoses, bones, animator, HumanBodyBones.LeftHand),
                RightHandTip = HandTip(mesh, nearest, bindPoses, bones, animator, HumanBodyBones.RightHand)
            };
            return true;
        }

        /// <summary>
        /// Радиус тела по каждой ячейке «срез × сектор», м. Пустые ячейки
        /// добираются у соседей по кругу: у тонкой шеи верхний срез иногда
        /// не набирает вершин в секторе.
        /// </summary>
        private static float[,] MeasureRadii(Mesh mesh, HumanBodyBones[] nearest, Vector3 bottom, Vector3 axis,
            Vector3 side, Vector3 front, float height, float scale)
        {
            int sectors = CharacterTorsoShape.SectorCount;
            var cells = new List<float>[SliceCount, sectors];
            for (int slice = 0; slice < SliceCount; slice++)
            {
                for (int sector = 0; sector < sectors; sector++)
                {
                    cells[slice, sector] = new List<float>();
                }
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int v = 0; v < vertices.Length; v++)
            {
                HumanBodyBones owner = nearest[DominantBone(weights[v])];
                if (System.Array.IndexOf(TorsoBones, owner) < 0)
                {
                    continue;
                }

                Vector3 offset = vertices[v] - bottom;
                float along = Vector3.Dot(offset, axis) / height;
                if (along < 0f || along >= 1f)
                {
                    continue;
                }

                Vector3 radial = offset - axis * (along * height);
                float u = Vector3.Dot(radial, side);
                float f = Vector3.Dot(radial, front);
                float angle = Mathf.Atan2(f, u) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                int sector = Mathf.Min((int)(angle / (360f / sectors)), sectors - 1);
                cells[(int)(along * SliceCount), sector].Add(Mathf.Sqrt(u * u + f * f) * scale);
            }

            var radii = new float[SliceCount, sectors];
            for (int slice = 0; slice < SliceCount; slice++)
            {
                for (int sector = 0; sector < sectors; sector++)
                {
                    List<float> cell = cells[slice, sector];
                    if (cell.Count < MinCellVertices)
                    {
                        continue;
                    }

                    cell.Sort();
                    radii[slice, sector] = cell[cell.Count * RadiusPercentile / 100];
                }
            }

            FillGaps(radii, sectors);
            return radii;
        }

        /// <summary>Пустая ячейка берёт среднее у соседних секторов, а если пусты и они — у соседнего среза.</summary>
        private static void FillGaps(float[,] radii, int sectors)
        {
            for (int slice = 0; slice < SliceCount; slice++)
            {
                for (int sector = 0; sector < sectors; sector++)
                {
                    if (radii[slice, sector] > 0f)
                    {
                        continue;
                    }

                    float before = radii[slice, (sector + sectors - 1) % sectors];
                    float after = radii[slice, (sector + 1) % sectors];
                    if (before > 0f && after > 0f)
                    {
                        radii[slice, sector] = (before + after) * 0.5f;
                    }
                    else if (before > 0f || after > 0f)
                    {
                        radii[slice, sector] = Mathf.Max(before, after);
                    }
                    else if (slice > 0)
                    {
                        radii[slice, sector] = radii[slice - 1, sector];
                    }
                }
            }
        }

        /// <summary>
        /// Срезы снизу вверх. Каждый привязан к ближайшей по высоте кости
        /// позвоночника и записан в её локальном пространстве: наклонился
        /// персонаж — наклонился и объём.
        /// </summary>
        private static CharacterTorsoShape.Slice[] BuildSlices(Animator animator, Transform[] bones,
            Matrix4x4[] bindPoses, float[,] radii, Vector3 bottom, Vector3 axis, Vector3 side, Vector3 front,
            float height, float scale)
        {
            var spine = new List<int>();
            foreach (HumanBodyBones bone in TorsoBones)
            {
                int index = BoneIndex(animator, bones, bone);
                if (index >= 0)
                {
                    spine.Add(index);
                }
            }

            int sectors = CharacterTorsoShape.SectorCount;
            var slices = new List<CharacterTorsoShape.Slice>(SliceCount);
            for (int slice = 0; slice < SliceCount; slice++)
            {
                Vector3 center = bottom + axis * (height * (slice + 0.5f) / SliceCount);
                int owner = NearestSpineBone(spine, bindPoses, center, axis);

                var values = new float[sectors];
                bool empty = true;
                for (int sector = 0; sector < sectors; sector++)
                {
                    values[sector] = radii[slice, sector];
                    empty &= values[sector] <= 0f;
                }

                if (empty)
                {
                    continue;
                }

                slices.Add(new CharacterTorsoShape.Slice
                {
                    Bone = owner,
                    Center = bindPoses[owner].MultiplyPoint3x4(center),
                    Axis = bindPoses[owner].MultiplyVector(axis).normalized,
                    Side = bindPoses[owner].MultiplyVector(side).normalized,
                    Front = bindPoses[owner].MultiplyVector(front).normalized,
                    HalfHeight = height / SliceCount * scale * 0.5f,
                    Radii = values
                });
            }

            return slices.ToArray();
        }

        private static int NearestSpineBone(List<int> spine, Matrix4x4[] bindPoses, Vector3 center, Vector3 axis)
        {
            int best = spine[0];
            float bestDistance = float.MaxValue;
            foreach (int index in spine)
            {
                float distance = Mathf.Abs(Vector3.Dot(Origin(bindPoses, index) - center, axis));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            return best;
        }

        /// <summary>
        /// Кончик ладони в локальном пространстве кости кисти. Кисть у нас
        /// одна кость без пальцев, и её начало — запястье: без этой точки
        /// пальцы уходили бы в живот незамеченными.
        /// </summary>
        private static Vector3 HandTip(Mesh mesh, HumanBodyBones[] nearest, Matrix4x4[] bindPoses, Transform[] bones,
            Animator animator, HumanBodyBones hand)
        {
            int index = BoneIndex(animator, bones, hand);
            if (index < 0)
            {
                return Vector3.zero;
            }

            Vector3 wrist = Origin(bindPoses, index);
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;

            Vector3 farthest = Vector3.zero;
            float best = 0f;
            for (int v = 0; v < vertices.Length; v++)
            {
                if (nearest[DominantBone(weights[v])] != hand)
                {
                    continue;
                }

                Vector3 offset = vertices[v] - wrist;
                float distance = offset.sqrMagnitude;
                if (distance > best)
                {
                    best = distance;
                    farthest = offset;
                }
            }

            return bindPoses[index].MultiplyVector(farthest * HandTipReach);
        }

        /// <summary>Положение начала кости в пространстве сетки.</summary>
        private static Vector3 Origin(Matrix4x4[] bindPoses, int bone)
        {
            return bindPoses[bone].inverse.MultiplyPoint3x4(Vector3.zero);
        }

        private static int BoneIndex(Animator animator, Transform[] bones, HumanBodyBones bone)
        {
            Transform transform = animator.GetBoneTransform(bone);
            return transform == null ? -1 : System.Array.IndexOf(bones, transform);
        }

        /// <summary>Ближайшая известная аватару кость-предок для каждой кости сетки.</summary>
        private static HumanBodyBones[] NearestHumanBones(Animator animator, Transform[] bones)
        {
            var map = new Dictionary<Transform, HumanBodyBones>();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                Transform transform = animator.GetBoneTransform(bone);
                if (transform != null && !map.ContainsKey(transform))
                {
                    map.Add(transform, bone);
                }
            }

            var nearest = new HumanBodyBones[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                nearest[i] = HumanBodyBones.LastBone;
                for (Transform t = bones[i]; t != null; t = t.parent)
                {
                    if (map.TryGetValue(t, out HumanBodyBones human))
                    {
                        nearest[i] = human;
                        break;
                    }
                }
            }

            return nearest;
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

        private static void Save(CharacterTorsoShape.Entry[] entries)
        {
            if (!AssetDatabase.IsValidFolder(LibraryFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            }

            var library = AssetDatabase.LoadAssetAtPath<CharacterTorsoShape>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterTorsoShape>();
                library.Replace(entries);
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else
            {
                library.Replace(entries);
                EditorUtility.SetDirty(library);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Объём торса: записано персонажей — {entries.Length}");
        }
    }
}
