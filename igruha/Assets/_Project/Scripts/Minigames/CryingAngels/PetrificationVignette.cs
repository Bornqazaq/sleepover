using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Края экрана «каменеют» по мере роста счётчика окаменения. Цифр нет
    /// намеренно: игрок должен чувствовать, что надо уходить, а не считать
    /// секунды — счёт в углу превращает панику в арифметику.
    ///
    /// Показывает состояние только своего игрока. Текстура градиента рисуется
    /// кодом: на каркасе заводить арт-ассет ради рамки незачем, а в арт-фазе
    /// её заменит настоящая (IGR-293).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class PetrificationVignette : MonoBehaviour
    {
        private const int TextureSize = 128;

        [Tooltip("Цвет окаменения по краям экрана")]
        [SerializeField] private Color stoneColor = new Color(0.45f, 0.44f, 0.42f, 1f);
        [Tooltip("Непрозрачность в момент окаменения")]
        [Range(0f, 1f)]
        [SerializeField] private float maxAlpha = 0.85f;
        [Tooltip("Насколько резко рамка набирает силу. >1 — дольше остаётся незаметной, потом резко наваливается")]
        [SerializeField] private float falloff = 2f;

        private Image image;
        private RunnerState tracked;
        private Texture2D generated;

        private void Awake()
        {
            image = GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = BuildVignetteSprite();
            SetAlpha(0f);
        }

        /// <summary>За кем следим. Ставится контроллером на раздаче ролей; null — рамку прячем.</summary>
        public void Track(RunnerState runner)
        {
            tracked = runner;
            SetAlpha(0f);
        }

        private void Update()
        {
            if (tracked == null)
            {
                return;
            }

            // Кривая, а не линия: до половины счётчика рамка почти незаметна,
            // а последнюю секунду наваливается — тогда она подгоняет, а не мозолит глаза.
            SetAlpha(Mathf.Pow(tracked.PetrifyProgress, falloff) * maxAlpha);
        }

        private void SetAlpha(float alpha)
        {
            Color c = stoneColor;
            c.a = alpha;
            image.color = c;
            image.enabled = alpha > 0.001f;
        }

        /// <summary>
        /// Радиальный градиент: прозрачный центр, плотные края. Одна текстура
        /// на всю игру, растягивается Image'ом на любой экран.
        /// </summary>
        private Sprite BuildVignetteSprite()
        {
            generated = new Texture2D(TextureSize, TextureSize, TextureFormat.Alpha8, false)
            {
                name = "PetrificationVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[TextureSize * TextureSize];
            Vector2 center = new Vector2(TextureSize * 0.5f, TextureSize * 0.5f);
            float maxDistance = center.magnitude;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDistance;
                    // Центр чистый до половины радиуса, дальше плавно к непрозрачному краю.
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, distance));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            generated.SetPixels32(pixels);
            generated.Apply(false, true);

            return Sprite.Create(generated, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        }

        private void OnDestroy()
        {
            if (generated != null)
            {
                Destroy(generated);
            }
        }
    }
}
