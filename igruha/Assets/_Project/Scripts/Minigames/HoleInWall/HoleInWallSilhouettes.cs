using System;
using System.Collections.Generic;
using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>
    /// Форма одного выреза: настоящий контур силуэта в метрах.
    ///
    /// <b>Ломаная открытая, а не замкнутая.</b> Она идёт от левой ступни
    /// вверх, по контуру позы, и вниз к правой ступне; низ не хранится, потому
    /// что низ выреза — это пол платформы. Так вырез и режется в стене:
    /// не дыркой внутри полотна, а вырезом от нижней кромки, и стене остаётся
    /// вставить эту ломаную в свой прямоугольник — без мостов и без второго
    /// контура.
    ///
    /// <b>Координаты — метры от того места, где стоит игрок.</b> X отсчитан
    /// от центра тела, Y от пола. Поэтому вырез ставится на дорожку одним
    /// смещением, и это смещение и есть «где встать»: то же число, с которым
    /// вердикт сравнивает позицию игрока.
    ///
    /// <b>Асимметрия сохранена.</b> «Чайник» с рукой в бок достаёт влево
    /// дальше, чем вправо, и <see cref="LeftReach"/> с <see cref="RightReach"/>
    /// разные. Габарит <see cref="Size"/> при этом симметричный — удвоенный
    /// худший вылет: по нему считается разнос вырезов, а он обязан работать
    /// и после зеркального переворота 7-й стены.
    /// </summary>
    [Serializable]
    public sealed class CutoutSilhouette
    {
        [Tooltip("Состав дорожки, для которого посчитан контур: ключ персонажа или пары")]
        [SerializeField] private string composition = string.Empty;

        [Tooltip("Номер позы, 1…4")]
        [SerializeField] private int pose;

        [Tooltip("Контур в метрах: от левой ступни вверх и вниз к правой. Низа нет — низ выреза это пол")]
        [SerializeField] private Vector2[] outline = Array.Empty<Vector2>();

        [Tooltip("Высота контура, м")]
        [SerializeField] private float height;

        [Tooltip("Самый дальний вылет влево от центра тела, м. Число отрицательное")]
        [SerializeField] private float leftReach;

        [Tooltip("Самый дальний вылет вправо от центра тела, м")]
        [SerializeField] private float rightReach;

        public string Composition => composition;

        public int Pose => pose;

        /// <summary>
        /// Точки контура. Массив отдаётся как есть, без копии: контур читается
        /// на каждой перестройке стены, а копия каждый раз — это мусор в кадре.
        /// Менять его нельзя.
        /// </summary>
        public Vector2[] Outline => outline;

        public float Height => height;

        public float LeftReach => leftReach;

        public float RightReach => rightReach;

        /// <summary>Описанный прямоугольник выреза, м. Ширина симметричная — удвоенный худший вылет.</summary>
        public Vector2 Size => new Vector2(2f * Mathf.Max(-leftReach, rightReach), height);

        /// <summary>Контур посчитан и пригоден к употреблению.</summary>
        public bool Valid => outline != null && outline.Length >= 3 && height > 0.01f;

        /// <summary>Заполнить контур. Зовётся только пекарем: ассет генерируемый.</summary>
        public void Bake(string trackComposition, int poseNumber, Vector2[] points, float poseHeight,
            float left, float right)
        {
            composition = trackComposition;
            pose = poseNumber;
            outline = points;
            height = poseHeight;
            leftReach = left;
            rightReach = right;
        }
    }

    /// <summary>
    /// Формы вырезов: настоящие контуры поз, свои у каждого состава дорожки.
    ///
    /// <b>Почему не один контур на всех.</b> Персонажи разного роста и ширины —
    /// Карлан 1.61 м, Шланга 1.96 м, Fat самый широкий, — и вырез, посчитанный
    /// объединением всех восьмерых, был велик каждому: в него пролезали все,
    /// и он ни с кем не совпадал. Теперь контур считается под состав дорожки,
    /// а дорожек с разными составами столько, сколько бывает пар.
    ///
    /// <b>Состав, а не персонаж.</b> В вырез на дорожке пары лезут двое,
    /// и заранее не известно, кто в какой: пара договаривается сама, а на
    /// 7-й стене вырезы ещё и меняются местами. Поэтому у пары контур —
    /// объединение силуэтов обоих, посчитанное на этапе сборки ассета одним
    /// битмапом. У одиночки состав из одного, и контур — ровно его.
    ///
    /// <b>Ключ состава — имя контроллера аниматора</b>, у пары два имени
    /// по алфавиту через плюс. Персонажу не нужно ничего добавлять: контроллер
    /// у каждого свой, а префабы персонажей заморожены
    /// (igruha/CLAUDE.md, раздел 🔒 0).
    ///
    /// Ассет генерируется — <c>Editor/HoleInWallSilhouetteBaker</c>. Руками
    /// не править: перезапись затрёт.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Hole In Wall Silhouettes", fileName = "HoleInWallSilhouettes")]
    public sealed class HoleInWallSilhouettes : ScriptableObject
    {
        /// <summary>
        /// Ключ состава, к которому откатываемся, когда персонаж на дорожке
        /// не опознан. Это объединение всего ростера — тот самый вырез
        /// «на самого большого», который был единственным до 04.09.
        /// </summary>
        public const string RosterComposition = "*";

        [Tooltip("Запас по контуру, м. Записан пекарем — тем же числом, которым он раздул силуэт")]
        [SerializeField] private float margin;

        [Tooltip("Контуры: состав × поза. Генерируется, руками не править")]
        [SerializeField] private CutoutSilhouette[] silhouettes = Array.Empty<CutoutSilhouette>();

        /// <summary>Контуры по составам: ключ — состав, значение — массив по номеру позы минус один.</summary>
        private Dictionary<string, CutoutSilhouette[]> byComposition;

        /// <summary>Запас по контуру, м: на столько вырез больше силуэта.</summary>
        public float Margin => margin;

        /// <summary>Сколько контуров в ассете. Для отчёта пекаря и проверок.</summary>
        public int Count => silhouettes != null ? silhouettes.Length : 0;

        private void OnEnable() => byComposition = null;

        /// <summary>
        /// Контур позы для этого состава. Пусто — состава в ассете нет,
        /// и звать надо <see cref="Roster"/>.
        /// </summary>
        public CutoutSilhouette Find(string composition, HoleInWallPose pose)
        {
            if (string.IsNullOrEmpty(composition) || pose == HoleInWallPose.None)
            {
                return null;
            }

            EnsureIndex();

            if (!byComposition.TryGetValue(composition, out CutoutSilhouette[] poses))
            {
                return null;
            }

            int index = (int)pose - 1;
            return index >= 0 && index < poses.Length ? poses[index] : null;
        }

        /// <summary>Контур позы на весь ростер: запасной вариант для неопознанного состава.</summary>
        public CutoutSilhouette Roster(HoleInWallPose pose) => Find(RosterComposition, pose);

        /// <summary>Записать всё разом. Зовётся только пекарем.</summary>
        public void Bake(CutoutSilhouette[] baked, float bakedMargin)
        {
            silhouettes = baked ?? Array.Empty<CutoutSilhouette>();
            margin = bakedMargin;
            byComposition = null;
        }

        private void EnsureIndex()
        {
            if (byComposition != null)
            {
                return;
            }

            byComposition = new Dictionary<string, CutoutSilhouette[]>(silhouettes.Length);

            for (int i = 0; i < silhouettes.Length; i++)
            {
                CutoutSilhouette shape = silhouettes[i];
                if (shape == null || string.IsNullOrEmpty(shape.Composition))
                {
                    continue;
                }

                if (!byComposition.TryGetValue(shape.Composition, out CutoutSilhouette[] poses))
                {
                    poses = new CutoutSilhouette[HoleInWallConfig.PoseCount];
                    byComposition.Add(shape.Composition, poses);
                }

                int index = shape.Pose - 1;
                if (index >= 0 && index < poses.Length)
                {
                    poses[index] = shape;
                }
            }
        }
    }
}
