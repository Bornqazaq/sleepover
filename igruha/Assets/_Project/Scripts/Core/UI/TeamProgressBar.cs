using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Двойная полоса прогресса команд: по одной на команду, рядом.
    ///
    /// Полос именно две, а не одна на двоих: одна общая читается как
    /// перетягивание каната и врёт там, где обе команды могут расти
    /// одновременно, а «Переноска предмета» — ровно этот случай. Игрок должен
    /// видеть и свой прогресс, и чужой, не подбегая к чужому баку.
    ///
    /// Значения приходят снаружи: полоса не знает, что она показывает — литры,
    /// отгаданные слова или закрытые дырки.
    /// </summary>
    public sealed class TeamProgressBar : MonoBehaviour
    {
        [Header("Команда A")]
        [SerializeField] private Image fillA;
        [SerializeField] private TMP_Text labelA;
        [Tooltip("Рамка вокруг полосы. Загорается у своей команды")]
        [SerializeField] private Graphic frameA;

        [Header("Команда B")]
        [SerializeField] private Image fillB;
        [SerializeField] private TMP_Text labelB;
        [SerializeField] private Graphic frameB;

        [Header("Цвета")]
        [SerializeField] private Color teamAColor = new Color(0.25f, 0.55f, 1f);
        [SerializeField] private Color teamBColor = new Color(1f, 0.45f, 0.2f);
        [Tooltip("Рамка своей команды")]
        [SerializeField] private Color ownFrameColor = Color.white;
        [Tooltip("Рамка чужой команды")]
        [SerializeField] private Color otherFrameColor = new Color(1f, 1f, 1f, 0.25f);

        private TeamSide localTeam = TeamSide.None;

        /// <summary>Последние показанные значения. По ним отсекаются лишние правки текста и заливки.</summary>
        private int shownA = -1;
        private int shownB = -1;

        private void Awake()
        {
            if (fillA != null)
            {
                fillA.type = Image.Type.Filled;
                fillA.color = teamAColor;
            }

            if (fillB != null)
            {
                fillB.type = Image.Type.Filled;
                fillB.color = teamBColor;
            }

            ApplyFrames();
        }

        /// <summary>За какую команду играет тот, кто смотрит на этот экран.</summary>
        public void SetLocalTeam(TeamSide side)
        {
            localTeam = side;
            ApplyFrames();
        }

        /// <summary>
        /// Показать счёт команды. <paramref name="value"/> и
        /// <paramref name="capacity"/> — в единицах самой игры, а не в долях:
        /// полоса заодно пишет их цифрами, и округлять доли обратно ей нечем.
        ///
        /// Текст и заливка правятся, только когда значение действительно
        /// изменилось: иначе на каждый кадр слива уходила бы строка в мусор.
        /// </summary>
        public void SetValue(TeamSide side, int value, int capacity)
        {
            int clamped = Mathf.Clamp(value, 0, Mathf.Max(1, capacity));
            float fraction = capacity > 0 ? clamped / (float)capacity : 0f;

            if (side == TeamSide.A)
            {
                if (shownA == clamped)
                {
                    return;
                }

                shownA = clamped;
                Apply(fillA, labelA, fraction, clamped, capacity);
                return;
            }

            if (side != TeamSide.B || shownB == clamped)
            {
                return;
            }

            shownB = clamped;
            Apply(fillB, labelB, fraction, clamped, capacity);
        }

        /// <summary>Сбросить обе полосы в ноль — старт раунда.</summary>
        public void ResetBars(int capacity)
        {
            shownA = -1;
            shownB = -1;
            SetValue(TeamSide.A, 0, capacity);
            SetValue(TeamSide.B, 0, capacity);
        }

        private static void Apply(Image fill, TMP_Text label, float fraction, int value, int capacity)
        {
            if (fill != null)
            {
                fill.fillAmount = fraction;
            }

            if (label != null)
            {
                label.SetText("{0} / {1}", value, capacity);
            }
        }

        private void ApplyFrames()
        {
            if (frameA != null)
            {
                frameA.color = localTeam == TeamSide.A ? ownFrameColor : otherFrameColor;
            }

            if (frameB != null)
            {
                frameB.color = localTeam == TeamSide.B ? ownFrameColor : otherFrameColor;
            }
        }
    }
}
