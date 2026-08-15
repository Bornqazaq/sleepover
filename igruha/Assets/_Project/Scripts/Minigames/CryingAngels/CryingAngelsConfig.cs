using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Числа баланса «Плачущих ангелов» (спека, раздел 8). Один ассет на игру,
    /// крутится без пересборки.
    ///
    /// Здесь лежит только то, у чего нет дома в Core — иначе получилось бы два
    /// источника правды и балансировка «не там». Остальное правится по месту:
    /// длительность раунда и число игроков — <see cref="Igruha.Core.Minigame.MinigameDefinition"/>
    /// (CryingAngelsDefinition), угол и дальность луча — VisionCone на Водящем,
    /// скорость и высота приседа — CharacterConfig, пороги застревания — StuckDetector.
    /// </summary>
    [CreateAssetMenu(fileName = "CryingAngelsConfig", menuName = "Igruha/Minigames/Crying Angels Config")]
    public sealed class CryingAngelsConfig : ScriptableObject
    {
        [Header("Раунд")]
        [Tooltip("Стартовый отсчёт: фонарь выключен, Бегущие расходятся, с")]
        [SerializeField] private float startCountdown = 3f;

        [Header("Водящий — поворот")]
        [Tooltip("Потолок скорости поворота при минимуме Бегущих (2), °/сек")]
        [SerializeField] private float turnSpeedAtFewestRunners = 60f;
        [Tooltip("Потолок скорости поворота при максимуме Бегущих (7), °/сек")]
        [SerializeField] private float turnSpeedAtMostRunners = 90f;
        [Tooltip("Вертикальный обзор, ±°. Чисто визуальный: на засветку не влияет")]
        [SerializeField] private float verticalLookLimit = 20f;

        [Header("Водящий — касание")]
        [Tooltip("Радиус засчитываемого касания Водящего по горизонтали, юниты")]
        [SerializeField] private float touchRadius = 1.08f;
        [Tooltip("Радиус, с которого Водящий слышит шаги Бегущего, юниты")]
        [SerializeField] private float stepHearingRadius = 4.32f;

        [Header("Бегущий — окаменение")]
        [Tooltip("Сколько секунд под лучом до окаменения")]
        [SerializeField] private float petrifyThreshold = 4f;
        [Tooltip("Множитель накопления счётчика, пока луч на игроке")]
        [SerializeField] private float petrifyGainMultiplier = 1f;
        [Tooltip("Множитель отката счётчика, пока луча нет. 0.5 — полный откат за 8 сек")]
        [SerializeField] private float petrifyDecayMultiplier = 0.5f;
        [Tooltip("Анимация окаменения до телепорта на точку спавна, с")]
        [SerializeField] private float petrifyAnimationDuration = 0.5f;

        [Header("Бегущий — движение")]
        [Tooltip("Множитель к CharacterConfig.MaxSpeed именно в этой мини-игре")]
        [SerializeField] private float runnerSpeedMultiplier = 1f;
        [Tooltip("Сколько разных поз заморозки перебирается случайно")]
        [SerializeField] private int freezePoseCount = 6;

        [Header("Флаги плейтеста")]
        [Tooltip("Статуи окаменевших остаются на арене как укрытия. По умолчанию выкл: к концу раунда укрытий вдвое больше, и Водящий слабеет ровно тогда, когда должен додавливать")]
        [SerializeField] private bool statuesRemainAsCover;
        [Tooltip("Луч меняет цвет по мере накопления счётчика самой «горячей» цели — без этого Водящий не понимает, что счётчик существует")]
        [SerializeField] private bool beamColorFeedback = true;
        [Tooltip("Цвет луча, когда счётчик цели пуст")]
        [SerializeField] private Color beamColorIdle = Color.white;
        [Tooltip("Цвет луча в момент окаменения цели")]
        [SerializeField] private Color beamColorPetrifying = Color.red;

        public float StartCountdown => startCountdown;
        public float VerticalLookLimit => verticalLookLimit;
        public float TouchRadius => touchRadius;
        public float StepHearingRadius => stepHearingRadius;
        public float PetrifyThreshold => petrifyThreshold;
        public float PetrifyGainMultiplier => petrifyGainMultiplier;
        public float PetrifyDecayMultiplier => petrifyDecayMultiplier;
        public float PetrifyAnimationDuration => petrifyAnimationDuration;
        public float RunnerSpeedMultiplier => runnerSpeedMultiplier;
        public int FreezePoseCount => Mathf.Max(1, freezePoseCount);
        public bool StatuesRemainAsCover => statuesRemainAsCover;
        public bool BeamColorFeedback => beamColorFeedback;
        public Color BeamColorIdle => beamColorIdle;
        public Color BeamColorPetrifying => beamColorPetrifying;

        /// <summary>
        /// Потолок скорости поворота Водящего под фактическое число Бегущих:
        /// линейно между двумя опорными точками. Против двоих Водящий с быстрым
        /// поворотом почти не пропускает — роль становится имбой на 3–4 игроках.
        ///
        /// Границы приходят снаружи (из MinigameDefinition), чтобы кривая не
        /// хардкодила «2» и «7» и жила на любом лобби 2–8.
        /// </summary>
        public float GetTurnSpeed(int runnerCount, int fewestRunners, int mostRunners)
        {
            if (mostRunners <= fewestRunners)
            {
                return turnSpeedAtFewestRunners;
            }

            float t = Mathf.InverseLerp(fewestRunners, mostRunners, runnerCount);
            return Mathf.Lerp(turnSpeedAtFewestRunners, turnSpeedAtMostRunners, t);
        }
    }
}
