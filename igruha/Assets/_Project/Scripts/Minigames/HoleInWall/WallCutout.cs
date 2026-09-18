using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Вырез в стене: поза, место по ширине дорожки и подсветка контура.
    ///
    /// Саму дырку режет в полотне <see cref="SweepingWall"/>. Здесь живёт контур: цветная лента с тёмной кромкой
    /// на передней грани стены, обводящая дырку по её настоящей форме.
    /// Требование LDD, а не украшение: не читается силуэт — не играется игра.
    /// </summary>
    /// <remarks>
    /// <b>Контур повторяет позу, а не прямоугольник.</b> До арт-фазы дырка была
    /// прямоугольной, и контуром служили три куба из сцены. Теперь лента идёт
    /// по той же ломаной, которой стена режет полотно (<see cref="CutoutShapes"/>),
    /// то есть по настоящему контуру силуэта — своему у каждого состава
    /// дорожки. Три старых куба остались в ссылках и гасятся: сцена одета
    /// и запечена, пересобирать её ради них значит потерять арт
    /// подфаз 4.1–4.6.
    ///
    /// <b>Вырез принадлежит игроку, и это видно цветом.</b> Цвет контура связан с аркой и табло
    /// дорожки: светлый оттенок — нулевое место, тёмный — первое. Зеркальный переворот 7-й стены меняет вырезы местами,
    /// но не цветами, — значит паре придётся перебежать крест-накрест
    /// на тросе, и видно это заранее, ещё на подъезде.
    /// </remarks>
    public sealed class WallCutout : MonoBehaviour
    {
        /// <summary>Толщина рамки контура, м.</summary>
        public const float FrameThickness = 0.12f;

        /// <summary>На сколько рамка вынесена перед гранью стены, м. Иначе она тонет в панели.</summary>
        private const float FrameLift = 0.012f;
        private const float HoleClearance = 0.006f;
        private static readonly float[] RibbonWidths = { HoleClearance, .022f, .095f, FrameThickness };
        private static readonly float[] RibbonInk = { 0f, 1f, 1f, 0f };
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Сколько раз в секунду моргает контур после подвоха.</summary>
        private const float BlinkRate = 6f;
        private static readonly Color BlinkColor = Color.white;

        // Hue belongs to the lane; a darker shade distinguishes the second player.
        [SerializeField] private int laneIndex;

        [SerializeField] private Transform leftPost;
        [Tooltip("Правая стойка прямоугольного контура")]
        [SerializeField] private Transform rightPost;
        [Tooltip("Верхняя перекладина прямоугольного контура")]
        [SerializeField] private Transform lintel;
        [SerializeField] private Material outlineMaterial;

        private Renderer outlineRenderer;
        private Mesh outlineMesh;
        private MaterialPropertyBlock colorBlock;
        private float blinkUntil;

        /// <summary>Номер выреза. Им же выбирается цвет: вырез принадлежит месту на платформе.</summary>
        private int slot;

        private static readonly System.Collections.Generic.List<Vector3> Vertices =
            new System.Collections.Generic.List<Vector3>(1024);

        private static readonly System.Collections.Generic.List<int> Triangles =
            new System.Collections.Generic.List<int>(1536);
        private static readonly System.Collections.Generic.List<Color> Colors =
            new System.Collections.Generic.List<Color>(1024);

        /// <summary>Поза, которую требует этот вырез.</summary>
        public HoleInWallPose Pose { get; private set; } = HoleInWallPose.None;

        /// <summary>Центр выреза по ширине дорожки, м от её центра.</summary>
        public float Offset { get; private set; }

        /// <summary>Габариты дырки, м. Это описанный прямоугольник: сама дырка внутри него имеет форму позы.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Вырез есть на этой стене. Ложь — второй вырез у дорожки одиночки.</summary>
        public bool Active { get; private set; }

        private void Awake()
        {
            EnsureOutline();
        }

        private void OnDestroy()
        {
            if (outlineMesh != null)
            {
                Destroy(outlineMesh);
            }
        }

        /// <summary>
        /// Завести объект контура и погасить три прямоугольные рамки каркаса.
        ///
        /// Рамки остаются в сцене и в ссылках намеренно: сцена «Дырки в стене»
        /// одета и запечена, и пересобирать её ради трёх кубов — это потерять
        /// весь арт подфаз 4.1–4.6. Гасим их здесь, а построитель арены новых
        /// уже не заводит.
        /// </summary>
        private void EnsureOutline()
        {
            if (outlineRenderer != null)
            {
                return;
            }

            HideLegacyFrames();

            var go = new GameObject("Outline");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;

            outlineMesh = new Mesh { name = "CutoutOutline" };
            outlineMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = outlineMesh;

            outlineRenderer = go.AddComponent<MeshRenderer>();
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.sharedMaterial = outlineMaterial;
            colorBlock = new MaterialPropertyBlock();
        }

        /// <summary>Цвет этого выреза: по его номеру, он же место на платформе.</summary>
        private Color OwnColor => HoleInWallPalette.LaneOutline(laneIndex, slot);

        private void HideLegacyFrames()
        {
            if (leftPost != null)
            {
                leftPost.gameObject.SetActive(false);
            }

            if (rightPost != null)
            {
                rightPost.gameObject.SetActive(false);
            }

            if (lintel != null)
            {
                lintel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Поставить вырез: чей он, какая поза и где. Контур и габарит берутся
        /// у форм этого игрока.
        /// </summary>
        /// <param name="cutoutSlot">Номер выреза: он же место на платформе, он же цвет контура</param>
        public void Apply(HoleInWallConfig config, CutoutShapes shapes, int cutoutSlot,
            HoleInWallPose pose, float offset, float wallThickness)
        {
            EnsureOutline();
            HideLegacyFrames();

            slot = cutoutSlot;
            Pose = pose;
            Offset = offset;
            Size = shapes != null ? shapes.Size(pose) : config.SilhouetteSize(pose);

            Vector2[] outline = pose != HoleInWallPose.None && shapes != null ? shapes.Outline(pose) : null;
            Active = outline != null && outline.Length >= 2;

            outlineRenderer.gameObject.SetActive(Active);
            if (!Active)
            {
                return;
            }

            outlineRenderer.transform.localPosition =
                new Vector3(offset, 0f, -wallThickness * 0.5f - FrameLift);

            BuildOutlineMesh(outline);

            blinkUntil = 0f;
            SetColor(OwnColor);
        }

        /// <summary>
        /// Собрать из ломаной контура ленту толщиной <see cref="FrameThickness"/>.
        ///
        /// Общие вершины соединяют углы без перекрывающихся квадратов.
        /// Вся ширина лежит снаружи выреза, на сплошной части стены,
        /// поэтому лента не проецируется на тело внутри отверстия.
        ///
        /// <b>Нижней перекладины нет</b>, и её неоткуда взять: ломаная выреза
        /// открытая — она идёт от левой ступни вверх и вниз к правой, потому
        /// что низ выреза это пол платформы. Линия там ушла бы внутрь настила,
        /// а зазор под ней читался бы как щель, в которую можно подлезть.
        /// </summary>
        private void BuildOutlineMesh(Vector2[] path)
        {
            Vertices.Clear();
            Triangles.Clear();
            Colors.Clear();
            // One joined strip, entirely on the solid side of the opening. The old
            // overlapping segment quads projected halfway inside the hole and over skin.
            float area = 0;
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 next = path[(i + 1) % path.Length];
                area += path[i].x * next.y - next.x * path[i].y;
            }
            float outward = area < 0 ? 1 : -1;
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 before = (i == 0 ? path[1] - path[0] : path[i] - path[i - 1]).normalized;
                Vector2 after = (i == path.Length - 1 ? before : path[i + 1] - path[i]).normalized;
                Vector2 n0 = new Vector2(-before.y, before.x) * outward;
                Vector2 n1 = new Vector2(-after.y, after.x) * outward;
                Vector2 join = (n0 + n1).normalized;
                join /= Mathf.Max(.5f, Vector2.Dot(join, n1));
                float widthScale = CornerWidthScale(path, i, join);
                for (int band = 0; band < RibbonWidths.Length; band++)
                {
                    Add(path[i] + join * (RibbonWidths[band] * widthScale));
                    Colors.Add(new Color(RibbonInk[band], 0, 0, 1));
                }
                if (i == 0) continue;
                for (int band = 0; band < RibbonWidths.Length - 1; band++)
                {
                    int a = (i - 1) * RibbonWidths.Length + band, b = a + RibbonWidths.Length;
                    // Clockwise in XY faces the player (-Z).
                    if (outward > 0) { Triangles.Add(a); Triangles.Add(a + 1); Triangles.Add(b + 1); Triangles.Add(a); Triangles.Add(b + 1); Triangles.Add(b); }
                    else { Triangles.Add(a); Triangles.Add(b + 1); Triangles.Add(a + 1); Triangles.Add(a); Triangles.Add(b); Triangles.Add(b + 1); }
                }
            }

            outlineMesh.Clear();
            outlineMesh.SetVertices(Vertices);
            outlineMesh.SetColors(Colors);
            outlineMesh.SetTriangles(Triangles, 0);
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateBounds();
        }

        // At a tight V (hand next to head), an outward miter can cross the
        // opposite edge of the same opening. Narrow the ribbon before that edge.
        private static float CornerWidthScale(Vector2[] path, int vertex, Vector2 outward)
        {
            const float GapShare = .45f;
            float scale = 1f;
            for (int edge = 0; edge < path.Length - 1; edge++)
            {
                if (edge == vertex || edge + 1 == vertex) continue;
                Vector2 direction = path[edge + 1] - path[edge];
                float denominator = Cross(outward, direction);
                if (Mathf.Abs(denominator) < .000001f) continue;
                Vector2 delta = path[edge] - path[vertex];
                float reach = Cross(delta, direction) / denominator;
                float along = Cross(delta, outward) / denominator;
                if (reach > HoleClearance && along >= 0 && along <= 1)
                    scale = Mathf.Min(scale, reach * GapShare / FrameThickness);
            }
            return scale;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void Add(Vector2 point) => Vertices.Add(new Vector3(point.x, point.y, 0f));

        /// <summary>Убрать вырез со стены: у одиночки второго нет.</summary>
        public void Hide()
        {
            Active = false;
            Pose = HoleInWallPose.None;
            HideLegacyFrames();

            if (outlineRenderer != null)
            {
                outlineRenderer.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Моргнуть контуром: рисунок только что поменялся подвохом. Заметность
        /// смены обязательна — на неё у игрока секунда с небольшим.
        /// </summary>
        public void Blink(float seconds)
        {
            blinkUntil = Time.time + Mathf.Max(0f, seconds);
        }

        private void Update()
        {
            if (!Active || outlineRenderer == null)
            {
                return;
            }

            if (Time.time >= blinkUntil)
            {
                if (blinkUntil > 0f)
                {
                    blinkUntil = 0f;
                    SetColor(OwnColor);
                }

                return;
            }

            bool bright = Mathf.Repeat(Time.time * BlinkRate, 1f) < 0.5f;
            SetColor(bright ? BlinkColor : OwnColor);
        }

        private void SetColor(Color color)
        {
            // Светящийся, а не просто крашеный: контур обязан читаться
            // с 30 ШП — с максимальной дистанции подъезда стены (бриф).
            colorBlock.SetColor(BaseColorId, color);
            outlineRenderer.SetPropertyBlock(colorBlock);
        }
    }
}
