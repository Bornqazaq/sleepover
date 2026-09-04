using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Вырез в стене: поза, место по ширине дорожки и подсветка контура.
    ///
    /// Сама дырка — это отсутствие плит, её геометрию строит
    /// <see cref="SweepingWall"/>. Здесь живёт контур: светящаяся лента
    /// на передней грани стены, обводящая дырку по её настоящей форме.
    /// Требование LDD, а не украшение: не читается силуэт — не играется игра.
    /// </summary>
    /// <remarks>
    /// <b>Контур повторяет позу, а не прямоугольник.</b> До арт-фазы дырка была
    /// прямоугольной, и контуром служили три куба из сцены. Теперь форма
    /// берётся из <see cref="HoleInWallPoseShapes"/>, число сегментов зависит
    /// от позы, и лента собирается мешем в рантайме. Три старых куба остались
    /// в ссылках и гасятся: сцена одета и запечена, пересобирать её ради них
    /// значит потерять арт подфаз 4.1–4.6.
    ///
    /// <b>Поза 4 в зеркале отыгрывается сама.</b> Асимметричный силуэт
    /// отражается вместе с вырезом, потому что отражается всё содержимое
    /// дорожки; отдельной клавиши «выпад влево» не появляется.
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
        /// Цвет контура. Голубой неон палитры (бриф: «край выреза обведён
        /// неоном»), а не жёлтый блокаута: на белой глянцевой плите стены жёлтое
        /// теряется, а голубое — единственное холодное пятно в кадре.
        /// </summary>
        private static readonly Color OutlineColor = HoleInWallPalette.NeonCyan;

        /// <summary>
        /// Цвет моргания после подвоха. Второй неон палитры: он на другом конце
        /// круга от контура, и подмена рисунка читается сменой холодного на
        /// горячее, а не только частотой мигания.
        /// </summary>
        private static readonly Color BlinkColor = HoleInWallPalette.NeonPink;

        [Tooltip("Левая стойка прямоугольного контура. Осталась от каркаса — см. шапку класса")]
        [SerializeField] private Transform leftPost;
        [Tooltip("Правая стойка прямоугольного контура")]
        [SerializeField] private Transform rightPost;
        [Tooltip("Верхняя перекладина прямоугольного контура")]
        [SerializeField] private Transform lintel;

        private Renderer outlineRenderer;
        private Mesh outlineMesh;
        private float blinkUntil;

        /// <summary>Точки ломаной контура. Поле, а не локальная: контур пересобирается в кадре подвоха.</summary>
        private readonly System.Collections.Generic.List<Vector2> outlinePath =
            new System.Collections.Generic.List<Vector2>(HoleInWallPoseShapes.RowCount * 4);

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
            outlineRenderer.sharedMaterial = HoleInWallMaterials.Emissive(OutlineColor);
        }

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

        /// <summary>Поставить вырез: поза и место. Габариты и форма берутся из конфига.</summary>
        public void Apply(HoleInWallConfig config, HoleInWallPose pose, float offset, float wallThickness)
        {
            EnsureOutline();
            HideLegacyFrames();

            Pose = pose;
            Offset = offset;
            Size = config.SilhouetteSize(pose);
            Active = pose != HoleInWallPose.None && Size.x > 0f && Size.y > 0f;

            outlineRenderer.gameObject.SetActive(Active);
            if (!Active)
            {
                return;
            }

            outlineRenderer.transform.localPosition =
                new Vector3(offset, 0f, -wallThickness * 0.5f - FrameLift);

            BuildOutlinePath(config.PoseShapes, pose, Size);
            BuildOutlineMesh();

            blinkUntil = 0f;
            SetColor(OutlineColor);
        }

        /// <summary>
        /// Ломаная контура в системе координат выреза: снизу вверх по левой
        /// границе силуэта, поверху и вниз по правой.
        ///
        /// <b>Нижней перекладины нет.</b> Низ всех вырезов лежит на полу
        /// платформы, и линия там ушла бы внутрь настила — видно её не было бы,
        /// а зазор под ней читался бы как щель, в которую можно подлезть.
        ///
        /// Формы нет — рисуем прямоугольник, как на каркасе: контур обязан
        /// быть всегда, даже если ассет силуэтов ещё не собран.
        /// </summary>
        private void BuildOutlinePath(HoleInWallPoseShapes shapes, HoleInWallPose pose, Vector2 size)
        {
            outlinePath.Clear();
            float halfWidth = size.x * 0.5f;

            if (shapes == null || !shapes.Has(pose))
            {
                outlinePath.Add(new Vector2(-halfWidth, 0f));
                outlinePath.Add(new Vector2(-halfWidth, size.y));
                outlinePath.Add(new Vector2(halfWidth, size.y));
                outlinePath.Add(new Vector2(halfWidth, 0f));
                return;
            }

            int rows = HoleInWallPoseShapes.RowCount;
            float rowHeight = size.y / rows;

            // Левая граница снизу вверх: стойка полосы, затем ступенька к следующей.
            for (int row = 0; row < rows; row++)
            {
                shapes.TryWidestSpan(pose, (float)row / rows, (row + 1f) / rows, out float spanLeft, out _);
                float x = spanLeft * halfWidth;
                outlinePath.Add(new Vector2(x, row * rowHeight));
                outlinePath.Add(new Vector2(x, (row + 1) * rowHeight));
            }

            // Правая граница сверху вниз.
            for (int row = rows - 1; row >= 0; row--)
            {
                shapes.TryWidestSpan(pose, (float)row / rows, (row + 1f) / rows, out _, out float spanRight);
                float x = spanRight * halfWidth;
                outlinePath.Add(new Vector2(x, (row + 1) * rowHeight));
                outlinePath.Add(new Vector2(x, row * rowHeight));
            }
        }

        /// <summary>
        /// Собрать из ломаной ленту толщиной <see cref="FrameThickness"/>.
        ///
        /// Каждый отрезок ломаной осевой, поэтому лента — это прямоугольник,
        /// вытянутый на полтолщины за оба конца. Вылет и заполняет углы:
        /// без него на каждом повороте оставалась бы дырка в самом контуре.
        /// </summary>
        private void BuildOutlineMesh()
        {
            Vertices.Clear();
            Triangles.Clear();
            const float Half = FrameThickness * 0.5f;

            for (int i = 1; i < outlinePath.Count; i++)
            {
                Vector2 from = outlinePath[i - 1];
                Vector2 to = outlinePath[i];

                float minX = Mathf.Min(from.x, to.x) - Half;
                float maxX = Mathf.Max(from.x, to.x) + Half;
                float minY = Mathf.Min(from.y, to.y) - Half;
                float maxY = Mathf.Max(from.y, to.y) + Half;

                if (maxX - minX <= 0f || maxY - minY <= 0f)
                {
                    continue;
                }

                int start = Vertices.Count;
                Vertices.Add(new Vector3(minX, minY, 0f));
                Vertices.Add(new Vector3(minX, maxY, 0f));
                Vertices.Add(new Vector3(maxX, maxY, 0f));
                Vertices.Add(new Vector3(maxX, minY, 0f));

                // Лицом к игроку: стена едет в −Z, значит передняя грань смотрит туда же.
                Triangles.Add(start);
                Triangles.Add(start + 2);
                Triangles.Add(start + 1);
                Triangles.Add(start);
                Triangles.Add(start + 3);
                Triangles.Add(start + 2);
            }

            outlineMesh.Clear();
            outlineMesh.SetVertices(Vertices);
            outlineMesh.SetTriangles(Triangles, 0);
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateBounds();
        }

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
                    SetColor(OutlineColor);
                }

                return;
            }

            bool bright = Mathf.Repeat(Time.time * BlinkRate, 1f) < 0.5f;
            SetColor(bright ? BlinkColor : OutlineColor);
        }

        private void SetColor(Color color)
        {
            // Светящийся, а не просто крашеный: контур обязан читаться
            // с 30 ШП — с максимальной дистанции подъезда стены (бриф).
            outlineRenderer.sharedMaterial = HoleInWallMaterials.Emissive(color);
        }
    }
}
