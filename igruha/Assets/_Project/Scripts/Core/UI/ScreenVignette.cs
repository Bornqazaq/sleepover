using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Local, reversible screen darkness. Place before HUD siblings.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class ScreenVignette : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float fadeSeconds = 0.5f;
        private const int TextureSize = 64;
        private Image overlay;
        private Texture2D texture;
        private Sprite sprite;
        private float start, target, elapsed;
        public float Amount { get; private set; }

        private void Awake()
        {
            overlay = GetComponent<Image>();
            overlay.raycastTarget = false;
            texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            { name = "ScreenVignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float radius = new Vector2((x + 0.5f) / TextureSize * 2f - 1f,
                        (y + 0.5f) / TextureSize * 2f - 1f).magnitude;
                    pixels[y * TextureSize + x] = new Color32(0, 0, 0,
                        (byte)(255f * Mathf.Lerp(0.75f, 1f, Mathf.SmoothStep(0f, 1f, radius))));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), Vector2.one * 0.5f);
            overlay.sprite = sprite;
            SetImmediate(0f);
        }

        public void SetAmount(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(target, value)) return;
            start = Amount;
            target = value;
            elapsed = 0f;
            if (fadeSeconds <= 0f) SetImmediate(value);
        }

        public void SetImmediate(float value)
        {
            target = start = Amount = Mathf.Clamp01(value);
            elapsed = fadeSeconds;
            Paint();
        }

        public void Advance(float deltaTime)
        {
            elapsed += Mathf.Max(0f, deltaTime);
            Amount = Mathf.Lerp(start, target, fadeSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeSeconds));
            Paint();
        }

        private void Update() => Advance(Time.unscaledDeltaTime);
        private void Paint()
        {
            if (overlay == null) return;
            overlay.color = new Color(1f, 1f, 1f, Amount);
            overlay.enabled = Amount > 0f;
        }
        private void OnDisable() => SetImmediate(0f);
        private void OnDestroy()
        {
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
        }
    }
}
