using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>
    /// Размеры цирковой арены (спека «Секундомера», раздел 3). Арена общая с
    /// «Порядком банок», поэтому конфиг и билдер живут в Circus, а не в папке
    /// конкретной игры: там кнопка в клетке меняется на полку с банками,
    /// остальное идентично.
    ///
    /// Всё задаётся в ШП — ширинах персонажа, как в спеке. Капсулы приведены
    /// к радиусу 0.36, поэтому 1 ШП = 0.72 м; пересчёт в метры делают свойства,
    /// а руками в инспекторе крутятся те же числа, что стоят в таблицах спеки.
    ///
    /// Ноль по Y — дно ямы (опилки). От него считаются высоты клеток и табло.
    /// </summary>
    [CreateAssetMenu(fileName = "CircusArenaConfig", menuName = "Igruha/Minigames/Circus Arena Config")]
    public sealed class CircusArenaConfig : ScriptableObject
    {
        /// <summary>1 ШП. Капсулы всех пяти персонажей приведены к радиусу 0.36.</summary>
        public const float MetersPerBodyWidth = 0.72f;

        /// <summary>Радиус капсулы персонажа. По нему считается, куда игрок физически может встать.</summary>
        public const float CharacterCapsuleRadius = 0.36f;

        [Header("Шатёр")]
        [Tooltip("Диаметр шатра, ШП")]
        [SerializeField] private float tentDiameter = 50f;
        [Tooltip("Высота до колосников — фермы с прожекторами, на которой висят цепи клеток, ШП")]
        [SerializeField] private float riggingHeight = 24f;

        [Header("Яма")]
        [Tooltip("Радиус ямы, ШП. Обязан накрывать кольцо клеток целиком, иначе выпавший приземляется мимо опилок")]
        [SerializeField] private float pitRadius = 12f;
        [Tooltip("Высота кирпичного борта над полом шатра, ШП")]
        [SerializeField] private float pitRimHeight = 1.5f;
        [Tooltip("Насколько дно ямы утоплено ниже пола шатра, ШП. Вместе с бортом даёт стенку изнутри: прыжок 1.47 м не должен её брать, иначе выпавший убегает от медведя на пол шатра")]
        [SerializeField] private float pitRecess = 1.5f;

        [Header("Клетки")]
        [Tooltip("Радиус кольца клеток, ШП")]
        [SerializeField] private float cageRingRadius = 10f;
        [Tooltip("Сколько якорей клеток по кольцу. Клеток создаётся по числу игроков, якорей — всегда столько")]
        [SerializeField] private int cageAnchorCount = 8;
        [Tooltip("Внутренний размер клетки в плане, ШП")]
        [SerializeField] private float cageInnerSize = 4f;
        [Tooltip("Высота клетки внутри, ШП. Обязана быть выше прыжка, иначе из клетки выпрыгивают")]
        [SerializeField] private float cageInnerHeight = 4f;
        [Tooltip("Высота нижней ступени — на ней медведь достаёт лапой до решётки, ШП")]
        [SerializeField] private float cageBaseHeight = 3f;
        [Tooltip("Шаг опускания клетки за одну ошибку, ШП")]
        [SerializeField] private float cageLevelStep = 3f;
        [Tooltip("Сколько шагов над нижней ступенью у верхней. Лимит ошибок 3 (лобби 2–3) даёт 3 шага, лимит 2 (лобби 4–8) — два. Билдер ставит клетки на верхнюю, дальше уровнем управляют правила")]
        [SerializeField] private int maxLevelSteps = 3;
        [Tooltip("Сколько секунд занимает спуск на один шаг")]
        [SerializeField] private float cageLevelStepDuration = 2f;

        [Header("Табло")]
        [Tooltip("Высота центра табло над дном ямы, ШП")]
        [SerializeField] private float scoreboardHeight = 11f;
        [Tooltip("Ширина одной грани табло, ШП")]
        [SerializeField] private float scoreboardFaceWidth = 8f;
        [Tooltip("Высота одной грани табло, ШП")]
        [SerializeField] private float scoreboardFaceHeight = 3f;

        [Header("Проверки билдера")]
        [Tooltip("Минимальный зазор между соседними клетками, м. Меньше — кольцо читается сплошной стеной, и пустая клетка перестаёт быть сигналом «отсюда выпали». 1.7 — то, что даёт штатная геометрия: см. GetCageGap")]
        [SerializeField] private float minCageGap = 1.7f;
        [Tooltip("Высота прыжка персонажа, м. Замерено на CharacterConfig; билдер сверяет с ней борт ямы и высоту клетки")]
        [SerializeField] private float measuredJumpHeight = 1.47f;

        public float TentRadius => tentDiameter * 0.5f * MetersPerBodyWidth;
        public float RiggingHeight => riggingHeight * MetersPerBodyWidth;
        public float PitRadius => pitRadius * MetersPerBodyWidth;
        public float PitRimHeight => pitRimHeight * MetersPerBodyWidth;
        public float PitRecess => pitRecess * MetersPerBodyWidth;
        public float CageRingRadius => cageRingRadius * MetersPerBodyWidth;
        public int CageAnchorCount => Mathf.Max(1, cageAnchorCount);
        public float CageInnerSize => cageInnerSize * MetersPerBodyWidth;
        public float CageInnerHeight => cageInnerHeight * MetersPerBodyWidth;
        public float CageLevelStep => cageLevelStep * MetersPerBodyWidth;
        public float CageLevelStepDuration => Mathf.Max(0.01f, cageLevelStepDuration);
        public int MaxLevelSteps => Mathf.Max(0, maxLevelSteps);
        public float ScoreboardHeight => scoreboardHeight * MetersPerBodyWidth;
        public float ScoreboardFaceWidth => scoreboardFaceWidth * MetersPerBodyWidth;
        public float ScoreboardFaceHeight => scoreboardFaceHeight * MetersPerBodyWidth;
        public float MinCageGap => minCageGap;
        public float MeasuredJumpHeight => measuredJumpHeight;

        /// <summary>
        /// Y дна клетки, стоящей на ступени <paramref name="levelSteps"/> над нижней.
        /// Правила игры считают ступень как «лимит ошибок − сделано ошибок»:
        /// так последняя ступень перед вылетом всегда нижняя, при любом лимите.
        /// </summary>
        public float GetCageBottomHeight(int levelSteps)
        {
            return (cageBaseHeight + cageLevelStep * Mathf.Max(0, levelSteps)) * MetersPerBodyWidth;
        }

        /// <summary>Пол шатра выше дна ямы на глубину ямы — по нему ходят до падения и стоят трибуны.</summary>
        public float TentFloorHeight => PitRecess;

        /// <summary>Верх кирпичного борта над дном ямы. Изнутри это и есть стенка, которую нельзя перепрыгнуть.</summary>
        public float PitRimTop => PitRecess + PitRimHeight;

        /// <summary>Угол якоря клетки с номером <paramref name="index"/>, градусы.</summary>
        public float GetAnchorAngle(int index) => 360f / CageAnchorCount * index;

        /// <summary>Позиция якоря клетки на кольце. Y — дно клетки на верхней ступени.</summary>
        public Vector3 GetAnchorPosition(int index)
        {
            Vector3 direction = Quaternion.Euler(0f, GetAnchorAngle(index), 0f) * Vector3.forward;
            return direction * CageRingRadius + Vector3.up * GetCageBottomHeight(MaxLevelSteps);
        }

        /// <summary>
        /// Свободное расстояние между соседними клетками, м.
        ///
        /// Считается по хорде, а не по длине дуги, и с учётом того, что клетка
        /// квадратная и развёрнута гранью к центру: в сторону соседа она торчит
        /// не на половину стороны, а на проекцию обеих половин. Наивная формула
        /// «длина дуги минус ширина клетки» даёт 2.77 м на штатных размерах,
        /// тогда как реальный просвет — 1.75 м.
        /// </summary>
        public float GetCageGap()
        {
            if (CageAnchorCount < 2)
            {
                return float.PositiveInfinity;
            }

            float halfSector = Mathf.PI / CageAnchorCount;
            float centreDistance = 2f * CageRingRadius * Mathf.Sin(halfSector);
            float reach = CageInnerSize * 0.5f * (Mathf.Cos(halfSector) + Mathf.Sin(halfSector));
            return centreDistance - reach * 2f;
        }

        /// <summary>
        /// Насколько далеко от центра арены может оказаться капсула игрока,
        /// стоящего в клетке. Именно это число обязано укладываться в радиус ямы:
        /// падает игрок, а не пустые углы клетки, и вплотную к прутьям капсула
        /// не прижимается — мешает её собственный радиус.
        /// </summary>
        public float GetPlayerReachFromCentre()
        {
            float inner = CageInnerSize * 0.5f - CharacterCapsuleRadius;
            if (inner <= 0f)
            {
                return CageRingRadius;
            }

            float radial = CageRingRadius + inner;
            return Mathf.Sqrt(radial * radial + inner * inner);
        }
    }
}
