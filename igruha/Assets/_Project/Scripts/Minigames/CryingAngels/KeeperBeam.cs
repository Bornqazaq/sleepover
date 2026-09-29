using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Фонарь Водящего: конус света, который видят все. Геометрия света берётся
    /// из тех же чисел, по которым считается засветка (угол и дальность
    /// VisionCone), иначе игроки прячутся по картинке, а ловит их другой конус —
    /// самое обидное расхождение, какое может быть в этой игре.
    ///
    /// Здесь только визуал. Кого именно засветило, решает VisionCone на сервере.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class KeeperBeam : MonoBehaviour
    {
        [Tooltip("Яркость включённого фонаря")]
        [SerializeField] private float intensity = 6f;

        // Light.range is the point where Unity fades illumination to zero.
        // Keep that fade beyond the playable wall, while gameplay/cone length stay exact.
        private const float LightRangePadding = 1.65f;
        private const float ClearSightConeFraction = 0.8f;
        private float gameplayRange;
        private int sightBlockers;

        private Light beam;
        private KeeperBeamCone cone;

        private void Awake()
        {
            beam = GetComponent<Light>();
            cone = GetComponentInChildren<KeeperBeamCone>(true);
            beam.type = LightType.Spot;
            beam.intensity = intensity;
            sightBlockers = LayerMask.GetMask("Ground", "Cover");
            SetVisible(false);
        }

        /// <summary>
        /// Подогнать конус света под параметры проверки засветки.
        /// Угол — полный (не полу-угол), дальность — в юнитах.
        /// </summary>
        public void Configure(float coneAngle, float range)
        {
            if (beam == null)
            {
                beam = GetComponent<Light>();
            }

            beam.spotAngle = coneAngle;
            gameplayRange = range;
            beam.range = range * LightRangePadding;
            ResolveCone()?.Build(coneAngle, range);
        }

        /// <summary>Visible body inside the current bright cone, not its swept sector.</summary>
        public bool ClearlySees(Collider target) => beam != null && beam.enabled &&
            KeeperSightConfirmation.FullyVisible(beam.transform, beam.spotAngle * ClearSightConeFraction,
                gameplayRange, target, sightBlockers);

        /// <summary>Фонарь горит. Выключен на стартовом отсчёте и после конца раунда.</summary>
        public void SetVisible(bool visible)
        {
            if (beam == null)
            {
                beam = GetComponent<Light>();
            }

            beam.enabled = visible;
            ResolveCone()?.SetVisible(visible);
        }

        /// <summary>Кратковременный всполох фонаря (скример касания): множитель к базовой яркости, 1 — норма.</summary>
        public void SetIntensityScale(float scale)
        {
            if (beam == null)
            {
                beam = GetComponent<Light>();
            }

            beam.intensity = intensity * Mathf.Max(0f, scale);
        }

        /// <summary>Цвет луча — обратная связь по счётчику окаменения (14.7).</summary>
        public void SetColor(Color color)
        {
            if (beam == null)
            {
                beam = GetComponent<Light>();
            }

            beam.color = color;
            ResolveCone()?.SetColor(color);
        }

        private KeeperBeamCone ResolveCone()
        {
            if (cone == null)
            {
                cone = GetComponentInChildren<KeeperBeamCone>(true);
            }

            return cone;
        }
    }
}
