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

        private Light beam;
        private KeeperBeamCone cone;

        private void Awake()
        {
            beam = GetComponent<Light>();
            cone = GetComponentInChildren<KeeperBeamCone>(true);
            beam.type = LightType.Spot;
            beam.intensity = intensity;
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
            beam.range = range;
            ResolveCone()?.Build(coneAngle, range);
        }

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
