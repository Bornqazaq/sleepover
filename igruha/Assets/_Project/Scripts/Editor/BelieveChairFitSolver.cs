using System.Collections.Generic;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Подбирает кресло «Верю / не верю» под сидячую позу: на сколько поднять
    /// корпус и насколько придвинуть кресло к сидящему.
    ///
    /// Кресло двигается к сидящему, пока спинка не подойдёт к спине или край
    /// сиденья не упрётся в икры, а по высоте подушка встаёт под таз. Без
    /// сдвига сидящий оказывается на самом краю, и между спиной и спинкой
    /// видна пустота: поза держит голени под бёдрами, и край сиденья обязан
    /// стоять за икрами.
    ///
    /// Меряет по настоящим мешам кресла и по коже персонажа, а не по числам
    /// из скрипта Blender: переделанное кресло подгоняется заново той же
    /// пересборкой, без правки чисел.
    /// </summary>
    internal sealed class BelieveChairFitSolver
    {
        /// <summary>Шаг перебора сдвига, м.</summary>
        private const float ForwardStep = .005f;

        /// <summary>Дальше кресло не придвигается: иначе оно уехало бы из-под того, кто сидит на краю.</summary>
        private const float MaxForward = .25f;

        /// <summary>
        /// Насколько точка кожи должна уйти внутрь кресла, чтобы считаться
        /// пересечением, м. Мельче этого — подушка, промятая под тазом, и
        /// погрешность сетки кресла.
        /// </summary>
        private const float PenetrationTolerance = .012f;

        /// <summary>
        /// Запас после подбора, м: кресло не доезжает до спины или икр на эту
        /// величину. Поза дышит, а физика сдвигает сидящего на пару
        /// сантиметров от точки посадки.
        /// </summary>
        private const float FitClearance = .025f;

        /// <summary>Материал кантов: тонкие незамкнутые трубки, в объём кресла не входят.</summary>
        private const string PipingMaterial = "BTP_Seam";

        private readonly Volume body;
        private readonly Volume legs;
        private readonly float seatHeight;
        private readonly float legHeight;
        private readonly Rect cushion;
        private readonly float cushionSink;

        /// <param name="bodyModel">Модель корпуса (FBX), начало координат — точка посадки на полу.</param>
        /// <param name="legsModel">Модель ножек (FBX) с тем же началом координат.</param>
        /// <param name="seatHeight">Верх подушки без подъёма, м.</param>
        /// <param name="legHeight">Низ корпуса без подъёма, м.</param>
        /// <param name="cushion">Площадка подушки в плоскости X–Z кресла, м.</param>
        /// <param name="cushionSink">Насколько подушка проминается под тазом, м.</param>
        public BelieveChairFitSolver(GameObject bodyModel, GameObject legsModel, float seatHeight, float legHeight,
            Rect cushion, float cushionSink)
        {
            body = new Volume(bodyModel);
            legs = new Volume(legsModel);
            this.seatHeight = seatHeight;
            this.legHeight = legHeight;
            this.cushion = cushion;
            this.cushionSink = cushionSink;
        }

        /// <summary>
        /// Подобрать кресло под кожу сидящего (оси персонажа = оси кресла).
        /// Ложь — даже без сдвига кресло задевает тело; подъём и сдвиг тогда
        /// нулевой глубины, чтобы кресло хотя бы стояло под тазом.
        /// </summary>
        public bool Solve(Vector3[] skin, bool[] seatSkin, out float lift, out float forward)
        {
            List<Vector3> near = NearChair(skin);
            lift = 0f;
            forward = 0f;

            float deepest = -1f;
            for (float shift = 0f; shift <= MaxForward + 1e-4f; shift += ForwardStep)
            {
                if (!TryLift(skin, seatSkin, shift, out float candidate) || Penetrates(near, candidate, shift))
                {
                    break;
                }

                deepest = shift;
            }

            if (deepest < 0f)
            {
                TryLift(skin, seatSkin, 0f, out lift);
                return false;
            }

            forward = Mathf.Max(0f, deepest - FitClearance);
            TryLift(skin, seatSkin, forward, out lift);
            return true;
        }

        /// <summary>Подъём, при котором подушка, сдвинутая на <paramref name="shift"/>, встаёт под таз.</summary>
        private bool TryLift(Vector3[] skin, bool[] seatSkin, float shift, out float lift)
        {
            float contact = float.MaxValue;
            for (int i = 0; i < skin.Length; i++)
            {
                Vector3 p = skin[i];
                if (seatSkin[i] && p.y < contact && cushion.Contains(new Vector2(p.x, p.z - shift)))
                {
                    contact = p.y;
                }
            }

            lift = contact + cushionSink - seatHeight;
            return contact < float.MaxValue;
        }

        private bool Penetrates(List<Vector3> skin, float lift, float shift)
        {
            float legScale = BelieveChairFit.LegScale(lift, legHeight);
            float bodyRise = BelieveChairFit.BodyRise(lift, legHeight);
            for (int i = 0; i < skin.Count; i++)
            {
                Vector3 p = skin[i];
                if (body.IsDeepInside(new Vector3(p.x, p.y - bodyRise, p.z - shift), PenetrationTolerance) ||
                    legs.IsDeepInside(new Vector3(p.x, p.y / legScale, p.z - shift), PenetrationTolerance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Только кожа, до которой кресло может дотянуться при любом подъёме и сдвиге.</summary>
        private List<Vector3> NearChair(Vector3[] skin)
        {
            Bounds reach = body.Bounds;
            reach.Encapsulate(legs.Bounds);
            Vector3 min = reach.min;
            Vector3 max = reach.max;
            var near = new List<Vector3>(skin.Length / 4);
            foreach (Vector3 p in skin)
            {
                if (p.x > min.x && p.x < max.x && p.z > min.z && p.z < max.z + MaxForward)
                {
                    near.Add(p);
                }
            }

            return near;
        }

        /// <summary>
        /// Сплошной объём модели как набор вертикальных столбцов: в каждом —
        /// отрезки высот, где луч снизу вверх внутри меша. Вход и выход
        /// считаются по нормали грани, поэтому перекрывающиеся части
        /// (подушка на основании, основание в стенке бочки) складываются
        /// в объединение, а не гасят друг друга, как при подсчёте чётности.
        /// </summary>
        private sealed class Volume
        {
            private const float Cell = .01f;

            private readonly Vector2 origin;
            private readonly int columnsX;
            private readonly int columnsZ;
            private readonly float[][] spans;

            public Bounds Bounds { get; }

            public Volume(GameObject model)
            {
                var triangles = new List<(Vector3 A, Vector3 B, Vector3 C, bool Enters)>();
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    CollectTriangles(filter, triangles);
                }

                var bounds = new Bounds(triangles[0].A, Vector3.zero);
                foreach (var t in triangles)
                {
                    bounds.Encapsulate(t.A);
                    bounds.Encapsulate(t.B);
                    bounds.Encapsulate(t.C);
                }

                Bounds = bounds;
                origin = new Vector2(bounds.min.x, bounds.min.z);
                columnsX = Mathf.CeilToInt(bounds.size.x / Cell) + 1;
                columnsZ = Mathf.CeilToInt(bounds.size.z / Cell) + 1;
                spans = BuildSpans(triangles);
            }

            /// <summary>
            /// Точка внутри не меньше чем на <paramref name="depth"/> по всем
            /// шести направлениям — грубая, но честная оценка глубины.
            /// </summary>
            public bool IsDeepInside(Vector3 p, float depth)
            {
                return Contains(p) &&
                       Contains(p + Vector3.up * depth) && Contains(p - Vector3.up * depth) &&
                       Contains(p + Vector3.right * depth) && Contains(p - Vector3.right * depth) &&
                       Contains(p + Vector3.forward * depth) && Contains(p - Vector3.forward * depth);
            }

            private bool Contains(Vector3 p)
            {
                int x = Mathf.FloorToInt((p.x - origin.x) / Cell);
                int z = Mathf.FloorToInt((p.z - origin.y) / Cell);
                if (x < 0 || z < 0 || x >= columnsX || z >= columnsZ)
                {
                    return false;
                }

                float[] column = spans[x * columnsZ + z];
                if (column == null)
                {
                    return false;
                }

                for (int i = 0; i < column.Length; i += 2)
                {
                    if (p.y >= column[i] && p.y <= column[i + 1])
                    {
                        return true;
                    }
                }

                return false;
            }

            private static void CollectTriangles(MeshFilter filter, List<(Vector3, Vector3, Vector3, bool)> triangles)
            {
                Mesh mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null)
                {
                    return;
                }

                // Корень FBX-ассета стоит в начале координат, и его матрица —
                // ровно та, с которой модель лежит под узлом корпуса или ножек.
                Matrix4x4 toChair = filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Material[] materials = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (sub < materials.Length && materials[sub] != null && materials[sub].name == PipingMaterial)
                    {
                        continue;
                    }

                    int[] indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        float facing = toChair.MultiplyVector(normals[a] + normals[b] + normals[c]).y;
                        if (Mathf.Abs(facing) < 1e-4f)
                        {
                            continue;
                        }

                        // Луч идёт снизу вверх: в тело он входит через грань, смотрящую вниз.
                        triangles.Add((toChair.MultiplyPoint3x4(vertices[a]), toChair.MultiplyPoint3x4(vertices[b]),
                            toChair.MultiplyPoint3x4(vertices[c]), facing < 0f));
                    }
                }
            }

            private float[][] BuildSpans(List<(Vector3 A, Vector3 B, Vector3 C, bool Enters)> triangles)
            {
                var hits = new List<(float Y, bool Enters)>[columnsX * columnsZ];
                foreach (var t in triangles)
                {
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(t.A.x, t.B.x, t.C.x) - origin.x) / Cell));
                    int x1 = Mathf.Min(columnsX - 1, Mathf.FloorToInt((Mathf.Max(t.A.x, t.B.x, t.C.x) - origin.x) / Cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(t.A.z, t.B.z, t.C.z) - origin.y) / Cell));
                    int z1 = Mathf.Min(columnsZ - 1, Mathf.FloorToInt((Mathf.Max(t.A.z, t.B.z, t.C.z) - origin.y) / Cell));
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int z = z0; z <= z1; z++)
                        {
                            var centre = new Vector2(origin.x + (x + .5f) * Cell, origin.y + (z + .5f) * Cell);
                            if (TryHeightAt(t.A, t.B, t.C, centre, out float y))
                            {
                                int index = x * columnsZ + z;
                                (hits[index] ??= new List<(float, bool)>()).Add((y, t.Enters));
                            }
                        }
                    }
                }

                var result = new float[hits.Length][];
                var column = new List<float>();
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i] == null)
                    {
                        continue;
                    }

                    hits[i].Sort((p, q) => p.Y.CompareTo(q.Y));
                    column.Clear();
                    int depth = 0;
                    foreach (var hit in hits[i])
                    {
                        int next = depth + (hit.Enters ? 1 : -1);
                        if (depth <= 0 && next > 0)
                        {
                            column.Add(hit.Y);
                        }
                        else if (depth > 0 && next <= 0)
                        {
                            column.Add(hit.Y);
                        }

                        depth = next;
                    }

                    if (column.Count % 2 == 1)
                    {
                        column.RemoveAt(column.Count - 1);
                    }

                    result[i] = column.Count > 0 ? column.ToArray() : null;
                }

                return result;
            }

            /// <summary>Высота треугольника над точкой плоскости X–Z, если точка внутри его проекции.</summary>
            private static bool TryHeightAt(Vector3 a, Vector3 b, Vector3 c, Vector2 p, out float y)
            {
                y = 0f;
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-10f)
                {
                    return false;
                }

                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / d;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / d;
                float w = 1f - u - v;
                if (u < 0f || v < 0f || w < 0f)
                {
                    return false;
                }

                y = u * a.y + v * b.y + w * c.y;
                return true;
            }
        }
    }
}
