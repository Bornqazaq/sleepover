using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Вырез в стене: поза, место по ширине дорожки и подсветка контура.
    ///
    /// Сама дырка — это отсутствие панели, её геометрию строит
    /// <see cref="SweepingWall"/>. Здесь живёт контур: три яркие рамки
    /// на передней грани стены, по которым силуэт читается с максимальной
    /// дистанции подъезда. Требование LDD, а не украшение: не читается
    /// силуэт — не играется игра.
    ///
    /// Рамки лежат в сцене и собираются построителем арены, а код их только
    /// переставляет: так вырезы видно в редакторе, не запуская игру.
    /// </summary>
    /// <remarks>
    /// <b>Нижней перекладины у рамки нет.</b> Низ всех вырезов лежит на полу
    /// платформы, и линия там ушла бы внутрь настила — видно её не было бы,
    /// а зазор под ней читался бы как щель, в которую можно подлезть.
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

        private static readonly Color OutlineColor = new Color(1f, 0.92f, 0.25f);
        private static readonly Color BlinkColor = new Color(1f, 0.25f, 0.20f);

        [Tooltip("Левая стойка контура")]
        [SerializeField] private Transform leftPost;
        [Tooltip("Правая стойка контура")]
        [SerializeField] private Transform rightPost;
        [Tooltip("Верхняя перекладина контура")]
        [SerializeField] private Transform lintel;

        private Renderer[] frames;
        private float blinkUntil;

        /// <summary>Поза, которую требует этот вырез.</summary>
        public HoleInWallPose Pose { get; private set; } = HoleInWallPose.None;

        /// <summary>Центр выреза по ширине дорожки, м от её центра.</summary>
        public float Offset { get; private set; }

        /// <summary>Габариты дырки, м.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Вырез есть на этой стене. Ложь — второй вырез у дорожки одиночки.</summary>
        public bool Active { get; private set; }

        private void Awake()
        {
            CacheFrames();
        }

        private void CacheFrames()
        {
            if (frames != null)
            {
                return;
            }

            if (leftPost == null || rightPost == null || lintel == null)
            {
                Debug.LogError($"{name}: контур выреза не собран — построить арену заново пунктом меню", this);
                frames = System.Array.Empty<Renderer>();
                return;
            }

            frames = new[]
            {
                leftPost.GetComponent<Renderer>(),
                rightPost.GetComponent<Renderer>(),
                lintel.GetComponent<Renderer>()
            };
        }

        /// <summary>Поставить вырез: поза и место. Габариты берутся из таблицы силуэтов.</summary>
        public void Apply(HoleInWallConfig config, HoleInWallPose pose, float offset, float wallThickness)
        {
            CacheFrames();

            Pose = pose;
            Offset = offset;
            Size = config.SilhouetteSize(pose);
            Active = pose != HoleInWallPose.None && Size.x > 0f && Size.y > 0f;

            SetFramesVisible(Active);
            if (!Active || frames.Length == 0)
            {
                return;
            }

            float halfWidth = Size.x * 0.5f;
            float front = -wallThickness * 0.5f - FrameLift;

            leftPost.localPosition = new Vector3(offset - halfWidth, Size.y * 0.5f, front);
            leftPost.localScale = new Vector3(FrameThickness, Size.y, FrameThickness);

            rightPost.localPosition = new Vector3(offset + halfWidth, Size.y * 0.5f, front);
            rightPost.localScale = new Vector3(FrameThickness, Size.y, FrameThickness);

            lintel.localPosition = new Vector3(offset, Size.y, front);
            lintel.localScale = new Vector3(Size.x + FrameThickness, FrameThickness, FrameThickness);

            blinkUntil = 0f;
            SetColor(OutlineColor);
        }

        /// <summary>Убрать вырез со стены: у одиночки второго нет.</summary>
        public void Hide()
        {
            Active = false;
            Pose = HoleInWallPose.None;
            SetFramesVisible(false);
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
            if (!Active || frames == null || frames.Length == 0)
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

        private void SetFramesVisible(bool visible)
        {
            if (leftPost != null)
            {
                leftPost.gameObject.SetActive(visible);
            }

            if (rightPost != null)
            {
                rightPost.gameObject.SetActive(visible);
            }

            if (lintel != null)
            {
                lintel.gameObject.SetActive(visible);
            }
        }

        private void SetColor(Color color)
        {
            Material material = HoleInWallMaterials.Opaque(color);
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null)
                {
                    frames[i].sharedMaterial = material;
                }
            }
        }
    }
}
