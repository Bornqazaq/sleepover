using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Радиальный градиент для экранных рамок: прозрачный центр, плотные края.
    /// Рисуется кодом один раз на компонент — заводить арт-ассет ради рамки
    /// незачем. Одна фабрика на рамку окаменения и на кромку луча, чтобы обе
    /// не держали по своей копии одного и того же цикла по пикселям.
    /// </summary>
    internal static class RadialVignetteSprite
    {
        private const int TextureSize = 128;

        /// <summary>
        /// <paramref name="clearRadius"/> — доля радиуса, до которой центр полностью
        /// прозрачен, 0..1. Вызывающий владеет текстурой спрайта и уничтожает её сам.
        /// </summary>
        public static Sprite Create(string name, float clearRadius, out Texture2D texture)
        {
            texture = new Texture2D(TextureSize, TextureSize, TextureFormat.Alpha8, false)
            {
                name = name,
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
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clearRadius, 1f, distance));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        }
    }
}
