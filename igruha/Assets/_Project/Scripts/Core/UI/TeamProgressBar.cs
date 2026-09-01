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
        [SerializeField] private Color teamAColor = TeamPalette.TeamA;
        [SerializeField] private Color teamBColor = TeamPalette.TeamB;
        [Tooltip("Подложка своей команды. Тёмная и плотная: на ней читается подпись")]
        [SerializeField] private Color ownFrameColor = new Color(0.05f, 0.05f, 0.05f, 0.8f);
        [Tooltip("Подложка чужой команды — та же, но бледнее")]
        [SerializeField] private Color otherFrameColor = new Color(0.05f, 0.05f, 0.05f, 0.35f);
        [Tooltip("Подпись своей команды")]
        [SerializeField] private Color ownLabelColor = Color.white;
        [Tooltip("Подпись чужой команды")]
        [SerializeField] private Color otherLabelColor = new Color(1f, 1f, 1f, 0.6f);

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

        /// <summary>
        /// Своя команда отличается двумя признаками сразу: подложка плотнее и
        /// подпись ярче.
        ///
        /// Одной подсветкой рамки не обойтись. Белая непрозрачная рамка своей
        /// команды съедала белую же подпись — на скриншоте прогона видно белую
        /// полосу без единой цифры. Подложка тёмная у обеих, различается
        /// плотностью, и текст читается в любом случае.
        /// </summary>
        private void ApplyFrames()
        {
            bool ownIsA = localTeam == TeamSide.A;
            bool ownIsB = localTeam == TeamSide.B;

            if (frameA != null)
            {
                frameA.color = ownIsA ? ownFrameColor : otherFrameColor;
            }

            if (frameB != null)
            {
                frameB.color = ownIsB ? ownFrameColor : otherFrameColor;
            }

            if (labelA != null)
            {
                labelA.color = ownIsA ? ownLabelColor : otherLabelColor;
            }

            if (labelB != null)
            {
                labelB.color = ownIsB ? ownLabelColor : otherLabelColor;
            }
        }
    }
}
