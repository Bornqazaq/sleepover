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
    /// кодом (<see cref="RadialVignetteSprite"/>): на каркасе заводить арт-ассет
    /// ради рамки незачем, а в арт-фазе её заменит настоящая (IGR-293).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class PetrificationVignette : MonoBehaviour
    {
        // Центр чистый до половины радиуса, дальше плавно к непрозрачному краю.
        private const float ClearRadius = 0.45f;

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
            image.sprite = RadialVignetteSprite.Create("PetrificationVignette", ClearRadius, out generated);
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

        private void OnDestroy()
        {
            if (generated != null)
            {
                Destroy(generated);
            }
        }
    }
}
