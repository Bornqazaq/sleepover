using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Вырез в стене: поза, место по ширине дорожки и подсветка контура.
    ///
    /// Саму дырку режет в полотне <see cref="SweepingWall"/>. Здесь живёт контур: светящаяся лента
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
    /// <b>Вырез принадлежит игроку, и это видно цветом.</b> Контур красится
    /// в цвет половины пола, на которой игрок стоит: розовый — нулевое место,
    /// голубой — первое. Зеркальный переворот 7-й стены меняет вырезы местами,
    /// но не цветами, — значит паре придётся перебежать крест-накрест
    /// на тросе, и видно это заранее, ещё на подъезде.
    /// </remarks>
    public sealed class WallCutout : MonoBehaviour
    {
        /// <summary>Толщина рамки контура, м.</summary>
        public const float FrameThickness = 0.12f;

        /// <summary>На сколько рамка вынесена перед гранью стены, м. Иначе она тонет в панели.</summary>
        private const float FrameLift = 0.03f;

        /// <summary>Сколько раз в секунду моргает контур после подвоха.</summary>
        private const float BlinkRate = 6f;

        /// <summary>
        /// Цвета контура по номеру выреза — они же цвета половин пола
        /// платформы (<c>HoleInWallPalette.FloorOwn</c> и <c>FloorPartner</c>).
        ///
        /// ⚠️ Это не украшение, а вся подача принадлежности: вырез закреплён
        /// за игроком, и узнаёт он свой по тому, что контур того же цвета,
        /// что пол под ногами. Нулевое место на платформе розовое, первое —
        /// голубое (<c>HoleInWallArenaBuilder.BuildFloorHalves</c>). Разойдутся
        /// эти два места — игрок побежит не в свою дырку.
        ///
        /// Неон здесь чистый, а не подмешанный как у пола: контур обязан
        /// читаться с 30 ШП, разметка под ногами — нет.
        /// </summary>
        private static readonly Color[] OutlineColors =
        {
            HoleInWallPalette.NeonPink,
            HoleInWallPalette.NeonCyan
        };

        /// <summary>
        /// Цвет моргания после подвоха — белый.
        ///
        /// Оба неона заняты принадлежностью, и мигать одним из них значит
        /// сказать «вырез сменил хозяина», чего не происходит. Белый ничей
        /// и читается вспышкой на обоих.
        /// </summary>
        private static readonly Color BlinkColor = Color.white;

        [Tooltip("Левая стойка прямоугольного контура. Осталась от каркаса — см. шапку класса")]
        [SerializeField] private Transform leftPost;
        [Tooltip("Правая стойка прямоугольного контура")]
        [SerializeField] private Transform rightPost;
        [Tooltip("Верхняя перекладина прямоугольного контура")]
        [SerializeField] private Transform lintel;

        private Renderer outlineRenderer;
        private Mesh outlineMesh;
        private float blinkUntil;

        /// <summary>Номер выреза. Им же выбирается цвет: вырез принадлежит месту на платформе.</summary>
        private int slot;

        private static readonly System.Collections.Generic.List<Vector3> Vertices =
            new System.Collections.Generic.List<Vector3>(1024);

        private static readonly System.Collections.Generic.List<int> Triangles =
            new System.Collections.Generic.List<int>(1536);

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
            outlineRenderer.sharedMaterial = HoleInWallMaterials.Emissive(OwnColor);
        }

        /// <summary>Цвет этого выреза: по его номеру, он же место на платформе.</summary>
        private Color OwnColor => OutlineColors[Mathf.Clamp(slot, 0, OutlineColors.Length - 1)];

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
        /// Каждый отрезок ломаной осевой: лента — прямоугольник вокруг него,
        /// вытянутый на полтолщины за оба конца. Вылет и заполняет углы: без
        /// него на каждом повороте оставалась бы дырка в самом контуре.
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
            const float Half = FrameThickness * 0.5f;

            for (int i = 1; i < path.Length; i++)
            {
                Vector2 from = path[i - 1];
                Vector2 to = path[i];
                Vector2 along = to - from;
                float length = along.magnitude;

                if (length < 0.0001f)
                {
                    continue;
                }

                along /= length;
                var across = new Vector2(-along.y, along.x);
                Vector2 back = from - along * Half;
                Vector2 ahead = to + along * Half;

                // ⚠️ Обход ПО часовой стрелке в XY: такая грань смотрит в −Z,
                // то есть навстречу игроку. Против часовой лента развернулась
                // бы изнанкой и пропала — URP Lit односторонний.
                int start = Vertices.Count;
                Add(back - across * Half);
                Add(back + across * Half);
                Add(ahead + across * Half);
                Add(ahead - across * Half);

                Triangles.Add(start);
                Triangles.Add(start + 1);
                Triangles.Add(start + 2);
                Triangles.Add(start);
                Triangles.Add(start + 2);
                Triangles.Add(start + 3);
            }

            outlineMesh.Clear();
            outlineMesh.SetVertices(Vertices);
            outlineMesh.SetTriangles(Triangles, 0);
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateBounds();
        }

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
            outlineRenderer.sharedMaterial = HoleInWallMaterials.Emissive(color);
        }
    }
}
