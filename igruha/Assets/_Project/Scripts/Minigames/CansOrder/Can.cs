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

        /// <summary>Слот, в котором банка стоит. −1 — банка в руке.</summary>
        public int SlotIndex { get; set; } = -1;

        /// <summary>Банка в руке, а не на полке.</summary>
        public bool InHand => SlotIndex < 0;

        private void Awake()
        {
            visual = GetComponentInChildren<Renderer>();
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
            block.SetColor(BaseColorId, kind.color);
            block.SetColor(LegacyColorId, kind.color);
            visual.SetPropertyBlock(block);
        }

        /// <summary>URP Lit красится через _BaseColor, встроенный шейдер — через _Color. Ставим оба: заготовка переживёт смену материала на арт-фазе.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    }
}
