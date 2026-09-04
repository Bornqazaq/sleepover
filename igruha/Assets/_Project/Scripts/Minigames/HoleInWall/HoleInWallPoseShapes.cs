using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Форма выреза по позам: не прямоугольник, а контур самой позы.
    ///
    /// Хранится профилем — на каждой из <see cref="RowCount"/> горизонтальных
    /// полос записано, докуда силуэт достаёт влево и вправо. Внутренние дырки
    /// (между ног, между рукой и телом) не хранятся намеренно: в стене они дали
    /// бы столбики, между которыми надо было бы протискиваться, а вырез обязан
    /// оставаться проходимым целиком.
    ///
    /// <b>Числа нормированные, а не метрические.</b> Влево-вправо — в долях
    /// полуширины выреза, вверх — в долях его высоты. Габарит выреза остаётся
    /// за геймдизайнером (<c>HoleInWallConfig.SilhouetteSize</c>), а этот ассет
    /// задаёт только форму. Разъехаться они не могут: размер один, берётся
    /// из конфига, форма просто по нему растягивается.
    ///
    /// <b>Асимметрия сохранена.</b> Обе границы нормированы одним и тем же
    /// числом, поэтому «Чайник» с рукой в бок остаётся кривым ровно настолько,
    /// насколько кривой была поза.
    ///
    /// Ассет генерируется — <c>Editor/HoleInWallPoseClipBuilder</c> печёт его
    /// теми же обмерами, которыми считает габариты. Руками не править:
    /// перезапись затрёт.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Hole In Wall Pose Shapes", fileName = "HoleInWallPoseShapes")]
    public sealed class HoleInWallPoseShapes : ScriptableObject
    {
        /// <summary>
        /// На сколько полос разрезана поза по высоте. Двадцать — это 13 см на
        /// вырезе «Свечки»: с максимальной дистанции подъезда (30 ШП) ступенька
        /// такого размера занимает около десяти пикселей, то есть силуэт читается
        /// человеком, а не лесенкой.
        /// </summary>
        public const int RowCount = 20;

        [Tooltip("Левая граница силуэта по полосам, доли полуширины выреза. Длина = число поз × RowCount")]
        [SerializeField] private float[] left = System.Array.Empty<float>();

        [Tooltip("Правая граница силуэта по полосам, доли полуширины выреза")]
        [SerializeField] private float[] right = System.Array.Empty<float>();

        /// <summary>Профиль этой позы записан и пригоден к использованию.</summary>
        public bool Has(HoleInWallPose pose)
        {
            int start = StartIndex(pose);
            return start >= 0 && start + RowCount <= left.Length && left.Length == right.Length;
        }

        /// <summary>
        /// Самый широкий пролёт силуэта на отрезке высоты, в долях полуширины
        /// выреза. Отрезок задаётся долями высоты выреза.
        ///
        /// Берётся именно <b>самый широкий</b> из попавших полос, а не средний:
        /// стена режется полосами своей высоты, и узкая полоса на границе
        /// срезала бы позе плечо. Ошибиться в большую сторону — это лишний
        /// сантиметр дырки, в меньшую — застрявший в стене игрок.
        /// </summary>
        public bool TryWidestSpan(HoleInWallPose pose, float fromHeight, float toHeight,
            out float spanLeft, out float spanRight)
        {
            spanLeft = 0f;
            spanRight = 0f;

            if (!Has(pose) || toHeight <= 0f || fromHeight >= 1f)
            {
                return false;
            }

            int start = StartIndex(pose);
            int firstRow = Mathf.Clamp(Mathf.FloorToInt(fromHeight * RowCount), 0, RowCount - 1);
            int lastRow = Mathf.Clamp(Mathf.CeilToInt(toHeight * RowCount) - 1, firstRow, RowCount - 1);

            spanLeft = float.MaxValue;
            spanRight = float.MinValue;

            for (int row = firstRow; row <= lastRow; row++)
            {
                spanLeft = Mathf.Min(spanLeft, left[start + row]);
                spanRight = Mathf.Max(spanRight, right[start + row]);
            }

            return spanRight > spanLeft;
        }

        /// <summary>
        /// Записать профиль позы. Зовётся только билдером клипов: ассет
        /// генерируемый, и другого законного способа его наполнить нет.
        /// </summary>
        public void Bake(HoleInWallPose pose, float[] rowLeft, float[] rowRight)
        {
            int start = StartIndex(pose);
            if (start < 0 || rowLeft == null || rowRight == null ||
                rowLeft.Length != RowCount || rowRight.Length != RowCount)
            {
                Debug.LogError($"{name}: профиль позы {pose} не записан — ожидалось {RowCount} полос.", this);
                return;
            }

            int total = HoleInWallConfig.PoseCount * RowCount;
            if (left.Length != total)
            {
                left = new float[total];
                right = new float[total];
            }

            System.Array.Copy(rowLeft, 0, left, start, RowCount);
            System.Array.Copy(rowRight, 0, right, start, RowCount);
        }

        /// <summary>Индекс первой полосы позы в плоском массиве. −1 — позы нет.</summary>
        private static int StartIndex(HoleInWallPose pose)
        {
            int index = (int)pose - 1;
            return index < 0 || index >= HoleInWallConfig.PoseCount ? -1 : index * RowCount;
        }
    }
}
