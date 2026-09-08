using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Палитра интерфейса: один набор цветов на все девять мини-игр.
    ///
    /// Живёт в коде, а не в инспекторе, по той же причине, что и формат камеры
    /// (<c>igruha/CLAUDE.md</c>, 2a): канвас копируется из шаблона в каждую
    /// сцену, YAML слияние веток не переживает, и правка цвета руками чинит
    /// одну сцену из девяти. Здесь же цвет читает и рантайм (таймер краснеет
    /// на последних секундах), и сборщик интерфейса — им нужен один источник.
    /// </summary>
    public static class UiSkin
    {
        /// <summary>Затемнение сцены под модальным экраном.</summary>
        public static readonly Color Scrim = new Color(0.02f, 0.025f, 0.04f, 0.82f);

        /// <summary>Подложка карточки.</summary>
        public static readonly Color Card = new Color(0.071f, 0.078f, 0.106f, 0.98f);

        /// <summary>Обводка карточки — на тон светлее подложки, а не белая.</summary>
        public static readonly Color CardEdge = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>Плашка поверх карточки: капсула таймера, клавиша, строка статуса.</summary>
        public static readonly Color Plate = new Color(1f, 1f, 1f, 0.07f);

        /// <summary>Поле ввода: темнее подложки, чтобы читалось как «сюда печатают».</summary>
        public static readonly Color Field = new Color(0.03f, 0.035f, 0.05f, 0.9f);

        /// <summary>Плашка на фоне сцены, а не карточки: ей нужна собственная плотность.</summary>
        public static readonly Color PlateOnScene = new Color(0.055f, 0.063f, 0.086f, 0.86f);

        /// <summary>Акцент игры — тот же жёлтый, которым подсвечен сектор колеса эмоций.</summary>
        public static readonly Color Accent = new Color(0.98f, 0.75f, 0.15f, 1f);

        /// <summary>Текст поверх акцента: тёмный, потому что акцент светлый.</summary>
        public static readonly Color AccentInk = new Color(0.07f, 0.06f, 0.03f, 1f);

        public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color TextSecondary = new Color(0.72f, 0.76f, 0.83f, 1f);
        public static readonly Color TextMuted = new Color(0.55f, 0.59f, 0.67f, 1f);

        /// <summary>Последние секунды раунда.</summary>
        public static readonly Color Danger = new Color(1f, 0.35f, 0.33f, 1f);

        public static readonly Color Gold = new Color(1f, 0.79f, 0.28f, 1f);
        public static readonly Color Silver = new Color(0.80f, 0.84f, 0.90f, 1f);
        public static readonly Color Bronze = new Color(0.85f, 0.55f, 0.32f, 1f);

        /// <summary>Цвет кружка места. Ниже третьего — общий приглушённый.</summary>
        public static Color Place(int place)
        {
            switch (place)
            {
                case 1: return Gold;
                case 2: return Silver;
                case 3: return Bronze;
                default: return Plate;
            }
        }

        /// <summary>Цвет цифр в кружке места: на светлой медали нужен тёмный.</summary>
        public static Color PlaceInk(int place)
        {
            return place <= 3 ? AccentInk : TextPrimary;
        }
    }
}
