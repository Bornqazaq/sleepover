using UnityEngine;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>
    /// Одна банка на полке. Носитель идентификатора и внешности, больше ничего:
    /// куда её можно поставить и что это значит — дело <see cref="CanShelf"/>.
    ///
    /// <b>Физики у банки нет и не будет.</b> Ни Rigidbody, ни коллайдера: она
    /// всегда либо в слоте, либо в руке. Пол клетки решётчатый, и упавшая банка
    /// уехала бы сквозь него в яму, обнулив игроку раунд, — а по сети к этому
    /// добавилась бы синхронизация физического тела ради одной шутки.
    /// Поэтому же банка не <c>PickupItem</c>: там бросок на ЛКМ.
    ///
    /// <b>Как поменять серую заготовку на настоящую банку (арт-фаза, 9.21):</b>
    /// положить модель ребёнком вместо примитива и оставить <see cref="Configure"/>
    /// красить её материал. Логика про внешность не знает.
    /// </summary>
    public sealed class Can : MonoBehaviour
    {
        private Renderer visual;
        private MaterialPropertyBlock block;

        /// <summary>Идентификатор банки — её индекс в палитре конфига. По нему считаются совпадения.</summary>
        public int CanId { get; private set; }

        /// <summary>Символ банки. Единственное, по чему её различает дальтоник, поэтому едет вместе с цветом.</summary>
        public string Symbol { get; private set; }

        public Color Color { get; private set; }

        /// <summary>Слот, в котором банка стоит.</summary>
        public int SlotIndex { get; set; } = -1;

        /// <summary>
        /// Как банка выделена сейчас. Единственная обратная связь выбора:
        /// без неё игрок не знает, какую банку тронет, и промахивается
        /// вслепую — на плейтесте 21.08 это оказалось главной причиной
        /// неудобства, а не сама схема управления.
        /// </summary>
        public enum Highlight
        {
            /// <summary>Стоит в ряду как все.</summary>
            None,

            /// <summary>На неё наведён курсор — нажатие сработает по ней.</summary>
            Cursor,

            /// <summary>Отмечена под обмен: следующая выбранная встанет на её место.</summary>
            Marked
        }

        /// <summary>На сколько банка приподнимается под курсором, м.</summary>
        private const float CursorLift = 0.05f;

        /// <summary>На сколько приподнимается отмеченная. Заметно выше курсора: два состояния нельзя путать.</summary>
        private const float MarkedLift = 0.14f;

        /// <summary>Насколько подсветка высветляет цвет банки, 0…1.</summary>
        private const float CursorTint = 0.35f;
        private const float MarkedTint = 0.6f;

        private Highlight highlight = Highlight.None;

        private void Awake()
        {
            visual = GetComponentInChildren<Renderer>();
        }

        /// <summary>
        /// Выделить банку. Подъём плюс высветление, а не обводка: на серых
        /// заготовках фазы 2 обводки нет, а поднятая банка читается с любого
        /// ракурса и переживёт замену модели на арт-фазе.
        /// </summary>
        public void SetHighlight(Highlight state)
        {
            if (highlight == state)
            {
                return;
            }

            highlight = state;
            transform.localPosition = Vector3.up * LiftFor(state);
            ApplyColor(TintFor(state));
        }

        private static float LiftFor(Highlight state)
        {
            switch (state)
            {
                case Highlight.Cursor: return CursorLift;
                case Highlight.Marked: return MarkedLift;
                default: return 0f;
            }
        }

        private Color TintFor(Highlight state)
        {
            switch (state)
            {
                case Highlight.Cursor: return Color.Lerp(Color, UnityEngine.Color.white, CursorTint);
                case Highlight.Marked: return Color.Lerp(Color, UnityEngine.Color.white, MarkedTint);
                default: return Color;
            }
        }

        /// <summary>
        /// Задать банке её место в палитре. Цвет ставится через
        /// <see cref="MaterialPropertyBlock"/>, а не через <c>material</c>:
        /// обращение к <c>material</c> плодит копию материала на каждую банку,
        /// а их до восьми полок по пять штук.
        /// </summary>
        public void Configure(int canId, CansOrderConfig.CanKind kind)
        {
            CanId = canId;
            Symbol = kind.symbol;
            Color = kind.color;
            name = $"Can_{canId}_{kind.displayName}";
            highlight = Highlight.None;

            ApplyColor(kind.color);
        }

        private void ApplyColor(Color color)
        {
            if (visual == null)
            {
                visual = GetComponentInChildren<Renderer>();
            }

            if (visual == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            visual.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(LegacyColorId, color);
            visual.SetPropertyBlock(block);
        }

        /// <summary>URP Lit красится через _BaseColor, встроенный шейдер — через _Color. Ставим оба: заготовка переживёт смену материала на арт-фазе.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    }
}
