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
        [Tooltip("Ключ персонажа, для которого посчитан контур")]
        [SerializeField] private string character = string.Empty;

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

        public string Character => character;

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
        public void Bake(string characterKey, int poseNumber, Vector2[] points, float poseHeight,
            float left, float right)
        {
            character = characterKey;
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
    /// и он ни с кем не совпадал.
    ///
    /// <b>Контур на персонажа, и объединять их нельзя.</b> Короткое время
    /// у пары вырез пёкся объединением двух силуэтов, чтобы в него лезли оба.
    /// На вид это провалилось: у высокого и низкого руки на разной высоте,
    /// объединение даёт дырку с четырьмя руками, и в стене она читается
    /// кляксой, а не позой. Вырез закреплён за игроком — по одному контуру
    /// на каждого, кто на дорожке.
    ///
    /// <b>Ключ — имя контроллера аниматора.</b> Персонажу не нужно ничего
    /// добавлять: контроллер у каждого свой, а префабы персонажей заморожены
    /// (igruha/CLAUDE.md, раздел 🔒 0).
    ///
    /// Ассет генерируется — <c>Editor/HoleInWallSilhouetteBaker</c>. Руками
    /// не править: перезапись затрёт.
    /// </summary>
    [CreateAssetMenu(menuName = "Igruha/Hole In Wall Silhouettes", fileName = "HoleInWallSilhouettes")]
    public sealed class HoleInWallSilhouettes : ScriptableObject
    {
        /// <summary>
        /// Ключ, к которому откатываемся, когда персонаж не опознан. Это
        /// объединение всего ростера — тот самый вырез «на самого большого»,
        /// который был единственным до 04.09. В игре его быть не должно:
        /// увидели кляксу вместо позы — значит ключ персонажа не нашёлся.
        /// </summary>
        public const string RosterCharacter = "*";

        [Tooltip("Запас по контуру, м. Записан пекарем — тем же числом, которым он раздул силуэт")]
        [SerializeField] private float margin;

        [Tooltip("Контуры: персонаж × поза. Генерируется, руками не править")]
        [SerializeField] private CutoutSilhouette[] silhouettes = Array.Empty<CutoutSilhouette>();

        /// <summary>Контуры по персонажам: ключ — персонаж, значение — массив по номеру позы минус один.</summary>
        private Dictionary<string, CutoutSilhouette[]> byCharacter;

        /// <summary>Запас по контуру, м: на столько вырез больше силуэта.</summary>
        public float Margin => margin;

        /// <summary>Сколько контуров в ассете. Для отчёта пекаря и проверок.</summary>
        public int Count => silhouettes != null ? silhouettes.Length : 0;

        private void OnEnable() => byCharacter = null;

        /// <summary>
        /// Контур позы для этого персонажа. Пусто — персонажа в ассете нет,
        /// и звать надо <see cref="Roster"/>.
        /// </summary>
        public CutoutSilhouette Find(string character, HoleInWallPose pose)
        {
            if (string.IsNullOrEmpty(character) || pose == HoleInWallPose.None)
            {
                return null;
            }

            EnsureIndex();

            if (!byCharacter.TryGetValue(character, out CutoutSilhouette[] poses))
            {
                return null;
            }

            int index = (int)pose - 1;
            return index >= 0 && index < poses.Length ? poses[index] : null;
        }

        /// <summary>Контур позы на весь ростер: запасной вариант для неопознанного состава.</summary>
        public CutoutSilhouette Roster(HoleInWallPose pose) => Find(RosterCharacter, pose);

        /// <summary>Записать всё разом. Зовётся только пекарем.</summary>
        public void Bake(CutoutSilhouette[] baked, float bakedMargin)
        {
            silhouettes = baked ?? Array.Empty<CutoutSilhouette>();
            margin = bakedMargin;
            byCharacter = null;
        }

        private void EnsureIndex()
        {
            if (byCharacter != null)
            {
                return;
            }

            byCharacter = new Dictionary<string, CutoutSilhouette[]>(silhouettes.Length);

            for (int i = 0; i < silhouettes.Length; i++)
            {
                CutoutSilhouette shape = silhouettes[i];
                if (shape == null || string.IsNullOrEmpty(shape.Character))
                {
                    continue;
                }

                if (!byCharacter.TryGetValue(shape.Character, out CutoutSilhouette[] poses))
                {
                    poses = new CutoutSilhouette[HoleInWallConfig.PoseCount];
                    byCharacter.Add(shape.Character, poses);
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
