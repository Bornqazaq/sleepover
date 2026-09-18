using UnityEngine;

namespace Igruha.Minigames.Infection
{
    /// <summary>
    /// Зелёная краска на заражённом — главный элемент читаемости всей
    /// мини-игры: от кого бежать, игрок узнаёт цветом с любого расстояния
    /// и с любого ракурса.
    ///
    /// <b>Префабы персонажей заморожены</b> (<c>igruha/CLAUDE.md</c>, раздел 0),
    /// поэтому краска кладётся поверх — через <see cref="MaterialPropertyBlock"/>
    /// на рендерерах аватара. Материалы не подменяются и не создаются: ни один
    /// ассет персонажа при этом не меняется, а снять краску — значит вернуть
    /// пустой блок.
    ///
    /// Мигание в грейсе — то же самое, только цвет ходит между обычным и
    /// зелёным: две секунды, за которые все успевают понять, кто Нулевой.
    ///
    /// Вид, а не правило: на состояние раунда не влияет и по сети не едет.
    /// В фазе 3 краска зажигается от реплицированной фазы игрока.
    /// </summary>
    public sealed class InfectionPaintView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Зелень заражения. Единственный зелёный в кадре — в палитре арены его нет.</summary>
        private static readonly Color PaintColor = new Color(0.36f, 0.85f, 0.18f);

        [Tooltip("Насколько густо краска перекрывает обычный цвет персонажа")]
        [Range(0f, 1f)]
        [SerializeField] private float paintStrength = 0.85f;

        private Renderer[] renderers = System.Array.Empty<Renderer>();

        /// <summary>
        /// Свой цвет каждого куска модели. Красить «в белое» при отмывании
        /// нельзя: у персонажей материалы цветные, и белый блок перекрасил бы
        /// их в белое вместо возврата к исходному.
        /// </summary>
        private Color[] originalColors = System.Array.Empty<Color>();

        private MaterialPropertyBlock block;
        private InfectionPaintEffects effects;
        public void AttachEffects(InfectionPaintEffects value) { effects=value; }
        private bool infected;
        private bool blinking;
        private float blinkPeriod = 0.25f;

        public void Bind(GameObject avatar, float blinkPeriodSeconds)
        {
            infected=false; blinking=false;
            renderers = avatar.GetComponentsInChildren<Renderer>(true);
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i] != null ? renderers[i].sharedMaterial : null;
                originalColors[i] = ReadColor(material);
            }

            block = new MaterialPropertyBlock();
            blinkPeriod = Mathf.Max(0.05f, blinkPeriodSeconds);
            Apply(false);
        }

        /// <summary>Покрасить или отмыть. Отмывание нужно только в конце раунда: лечения в игре нет.</summary>
        public void SetInfected(bool value)
        {
            if (infected == value)
            {
                return;
            }

            infected = value;
            if(effects!=null) effects.SetPainted(value);
            Apply(infected);
        }

        public void SetBlinking(bool value)
        {
            if (blinking == value)
            {
                return;
            }

            blinking = value;
            if(value && effects!=null) effects.SignalZero();
            if (!blinking)
            {
                Apply(infected);
            }
        }

        private void Update()
        {
            if (!blinking)
            {
                return;
            }

            bool on = Mathf.Repeat(Time.time, blinkPeriod * 2f) < blinkPeriod;
            Apply(on);
        }

        private void Apply(bool painted)
        {
            if (renderers == null || block == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color own = i < originalColors.Length ? originalColors[i] : Color.white;
                Color tint = painted ? Color.Lerp(own, PaintColor, paintStrength) : own;

                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Цвет материала: URP зовёт его <c>_BaseColor</c>, часть шейдеров и
        /// старые материалы — <c>_Color</c>. Нет ни того, ни другого — значит
        /// красить нечего, и за исходный берём белый.
        /// </summary>
        private static Color ReadColor(Material material)
        {
            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty(BaseColorId))
            {
                return material.GetColor(BaseColorId);
            }

            return material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
        }

        private void OnDestroy()
        {
            // Персонаж переезжает в хаб живым — краска обязана остаться здесь.
            Apply(false);
        }
    }
}
