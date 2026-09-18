using UnityEngine;

namespace Igruha.Minigames.Infection
{
    /// <summary>
    /// Числа «Заражения» в одном месте: всё, что подбирается на плейтесте,
    /// крутится здесь без пересборки.
    ///
    /// Порядок настройки — из спеки, раздел 8: сначала
    /// <see cref="InfectedSpeedMultiplier"/> (он решает, играбельна игра вообще:
    /// 0.9 — заражённые беспомощны, 1.0 — чистые обречены), потом
    /// <see cref="InfectionBonusSeconds"/> (он решает, продолжают ли заражённые
    /// ловить или садятся в угол). Всё остальное после.
    /// </summary>
    [CreateAssetMenu(fileName = "InfectionConfig", menuName = "Igruha/Minigames/Infection Config")]
    public sealed class InfectionConfig : ScriptableObject
    {
        [Header("Раунд")]
        [Tooltip("Разбегание: все чистые, Нулевого ещё нет. Нужно, чтобы он не заразил соседа по спавну на первой секунде")]
        [SerializeField] private float scatterSeconds = 3f;

        [Tooltip("Грейс Нулевого: заражён, мигает, но заражать не может")]
        [SerializeField] private float graceSeconds = 2f;

        [Header("Заражение")]
        [Tooltip("Доля от обычной скорости у заражённого. Чистый всегда чуть быстрее")]
        [Range(0.8f, 1f)]
        [SerializeField] private float infectedSpeedMultiplier = 0.95f;

        [Tooltip("Радиус касания между центрами капсул, м")]
        [SerializeField] private float touchRadius = 1.2f;

        [Tooltip("Переходное состояние: всплеск краски, игрок не управляем, не заражает и не может быть перезаражён")]
        [SerializeField] private float infectingSeconds = 0.7f;

        [Tooltip("Кулдаун после собственного заражения: свежезаражённый не пятнает своего преследователя мгновенно")]
        [SerializeField] private float freshInfectionCooldown = 1f;

        [Header("Очки")]
        [Tooltip("Бонус за каждое личное заражение, сек")]
        [SerializeField] private float infectionBonusSeconds = 6f;

        [Tooltip("Компенсация Нулевому: чистого времени у него нет в принципе")]
        [SerializeField] private float patientZeroCompensationSeconds = 10f;

        [Header("Читаемость")]
        [Tooltip("С какого расстояния Чистый слышит сердцебиение приближающегося Заражённого, м")]
        [SerializeField] private float heartbeatRadius = 5f;

        [Tooltip("Период мигания Нулевого в грейсе, с")]
        [SerializeField] private float blinkPeriod = 0.25f;

        [Header("Арена")]
        [Tooltip("Масштаб расстановки. Первое лечение безнадёжного финала на восьмерых — 1.1–1.15, и только потом скорости")]
        [Range(0.8f, 1.4f)]
        [SerializeField] private float arenaScale = 1f;

        [Tooltip("Скорость вращения карусели, градусов в секунду")]
        [SerializeField] private float carouselDegreesPerSecond = 30f;

        [Tooltip("Множитель скорости в песочнице — для всех, и для догоняющего тоже")]
        [Range(0.3f, 1f)]
        [SerializeField] private float sandSpeedMultiplier = 0.7f;

        [Tooltip("Период колебания качелей, с")]
        [SerializeField] private float swingPeriod = 3.2f;

        public float ScatterSeconds => scatterSeconds;
        public float GraceSeconds => graceSeconds;
        public float InfectedSpeedMultiplier => infectedSpeedMultiplier;
        public float TouchRadius => touchRadius;
        public float InfectingSeconds => infectingSeconds;
        public float FreshInfectionCooldown => freshInfectionCooldown;
        public float InfectionBonusSeconds => infectionBonusSeconds;
        public float PatientZeroCompensationSeconds => patientZeroCompensationSeconds;
        public float HeartbeatRadius => heartbeatRadius;
        public float BlinkPeriod => blinkPeriod;
        public float ArenaScale => arenaScale;
        public float CarouselDegreesPerSecond => carouselDegreesPerSecond;
        public float SandSpeedMultiplier => sandSpeedMultiplier;
        public float SwingPeriod => swingPeriod;
    }
}
