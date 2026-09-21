using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Шкала силы броска — полоса внизу экрана у того, кто целится.
    ///
    /// Раньше она была геометрией в мире и висела перед лицом чёрной палкой,
    /// закрывая дорожку: мировой объект нельзя показать одному человеку, не
    /// испортив кадр всем остальным. Экран решает обе задачи сразу — шкалу
    /// видит только владелец станции, и в сцене от неё не остаётся ничего.
    ///
    /// Рисуется через IMGUI, как экран запуска и настройки голоса: своего
    /// Canvas у хаба нет, а заводить его ради одной полосы незачем.
    /// </summary>
    public sealed class HubActivityPowerGauge : MonoBehaviour
    {
        [Tooltip("Ширина полосы, доля ширины экрана")]
        [SerializeField] private float widthFraction = 0.24f;

        [Tooltip("Высота полосы, пикселей")]
        [SerializeField] private float height = 16f;

        [Tooltip("Отступ снизу, пикселей")]
        [SerializeField] private float bottomMargin = 64f;

        [Tooltip("Подпись над шкалой")]
        [SerializeField] private string caption = "ЛКМ — сила броска";

        private static readonly Color FrameColor = new Color(0.07f, 0.06f, 0.05f, 0.82f);
        private static readonly Color FillColor = new Color(0.85f, 0.62f, 0.24f, 1f);
        private static readonly Color CaptionColor = new Color(0.95f, 0.86f, 0.68f, 0.9f);

        private Texture2D pixel;
        private GUIStyle captionStyle;
        private float power;
        private bool visible;

        private void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void OnDestroy()
        {
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;

            if (!value)
            {
                power = 0f;
            }
        }

        /// <summary>Сила 0…1.</summary>
        public void SetPower(float value) => power = Mathf.Clamp01(value);

        private void OnGUI()
        {
            if (!visible || pixel == null)
            {
                return;
            }

            float width = Screen.width * widthFraction;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - bottomMargin - height;

            const float border = 2f;
            Draw(new Rect(x - border, y - border, width + border * 2f, height + border * 2f), FrameColor);
            Draw(new Rect(x, y, width * power, height), FillColor);

            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                };
            }

            captionStyle.normal.textColor = CaptionColor;
            GUI.Label(new Rect(x, y - 22f, width, 20f), caption, captionStyle);
        }

        private void Draw(Rect rect, Color color)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = before;
        }
    }
}
