using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Core.Ambient
{
    /// <summary>
    /// Декоративное движение окружения: медленный поворот стрелы крана,
    /// покачивание подвеса и флага, мигание маяка, сползание облаков по куполу.
    ///
    /// Это не игровой объект и не сетевое состояние: движение чисто визуальное,
    /// не имеет коллайдеров-ловушек и ничего не решает. Время берётся из
    /// <see cref="NetworkClock"/> только ради одинаковой картинки у хоста и
    /// клиента — краны на всех машинах смотрят в одну сторону без единого байта
    /// трафика. Всё считается от стартового локального состояния объекта, поэтому
    /// компонент безопасно вешать на любой уже расставленный предмет,
    /// в том числе несколько на один: стрела крана поворачивается и мигает маяком разом.
    /// </summary>
    public sealed class AmbientMotion : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Постоянное вращение вокруг оси: стрела крана, лопасти.</summary>
            Spin,

            /// <summary>Качание по синусу вокруг оси, с лёгкой второй гармоникой: флаг, подвес, растяжка.</summary>
            Sway,

            /// <summary>Плавное смещение вдоль оси: люлька подъёмника, гак.</summary>
            Bob,

            /// <summary>Мигание свечения материала: авиационный маяк, проблесковая лампа.</summary>
            Blink,

            /// <summary>Сдвиг текстуры: облака на куполе неба, вода.</summary>
            ScrollUv
        }

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        [SerializeField] private Mode mode = Mode.Sway;
        [Tooltip("Ось движения в локальных координатах")]
        [SerializeField] private Vector3 axis = Vector3.up;
        [Tooltip("Размах: градусы для Spin/Sway, метры для Bob, доля яркости для Blink, тайлы в секунду для ScrollUv")]
        [SerializeField] private float amplitude = 10f;
        [Tooltip("Период полного цикла, с. Для Spin — время одного оборота")]
        [SerializeField] private float period = 12f;
        [Tooltip("Сдвиг фазы, доли периода: соседние объекты не двигаются синхронно")]
        [SerializeField] private float phase;
        [Tooltip("Вторая ось для Sway: небольшой крен поперёк основного качания")]
        [SerializeField] private Vector3 secondaryAxis = Vector3.forward;
        [Tooltip("Размах по второй оси, градусы. Ноль — качание в одной плоскости")]
        [SerializeField] private float secondaryAmplitude;
        [Tooltip("Blink: доля периода, когда лампа горит")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float dutyCycle = 0.2f;

        private Quaternion baseRotation;
        private Vector3 basePosition;
        private Renderer target;
        private MaterialPropertyBlock block;
        private Color emissionOn;
        private Vector4 baseTiling;
        private bool lastLit = true;

        public void Configure(Mode motionMode, Vector3 motionAxis, float motionAmplitude, float motionPeriod,
            float motionPhase = 0f, Vector3 secondAxis = default, float secondAmplitude = 0f, float duty = 0.2f)
        {
            mode = motionMode;
            axis = motionAxis;
            amplitude = motionAmplitude;
            period = motionPeriod;
            phase = motionPhase;
            secondaryAxis = secondAxis == default ? Vector3.forward : secondAxis;
            secondaryAmplitude = secondAmplitude;
            dutyCycle = duty;
        }

        private void Awake()
        {
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
            if (mode == Mode.Blink || mode == Mode.ScrollUv)
            {
                target = GetComponentInChildren<Renderer>();
                block = new MaterialPropertyBlock();
                if (target != null && target.sharedMaterial != null)
                {
                    Material material = target.sharedMaterial;
                    baseTiling = material.HasProperty(BaseMapStId) ? material.GetVector(BaseMapStId) : new Vector4(1, 1, 0, 0);
                    // Лампа обычно один из нескольких материалов меша: берём тот, что светится.
                    emissionOn = Color.white;
                    Material[] shared = target.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] != null && shared[i].IsKeywordEnabled("_EMISSION") && shared[i].HasProperty(EmissionColorId))
                        {
                            emissionOn = shared[i].GetColor(EmissionColorId);
                            break;
                        }
                    }
                }
            }
        }

        private void Update()
        {
            if (period <= 0f)
            {
                return;
            }

            float t = (float)(NetworkClock.Now % (period * 1000.0)) / period + phase;
            switch (mode)
            {
                case Mode.Spin:
                    transform.localRotation = baseRotation * Quaternion.AngleAxis(t * 360f, axis);
                    break;

                case Mode.Sway:
                {
                    float primary = Mathf.Sin(t * Mathf.PI * 2f) * amplitude;
                    Quaternion swing = Quaternion.AngleAxis(primary, axis);
                    if (secondaryAmplitude != 0f)
                    {
                        float secondary = Mathf.Sin(t * Mathf.PI * 2f * 1.63f + 1.3f) * secondaryAmplitude;
                        swing *= Quaternion.AngleAxis(secondary, secondaryAxis);
                    }

                    transform.localRotation = baseRotation * swing;
                    break;
                }

                case Mode.Bob:
                    transform.localPosition = basePosition + axis.normalized * (Mathf.Sin(t * Mathf.PI * 2f) * amplitude);
                    break;

                case Mode.Blink:
                {
                    if (target == null)
                    {
                        return;
                    }

                    bool lit = t - Mathf.Floor(t) < dutyCycle;
                    if (lit == lastLit)
                    {
                        return;
                    }

                    lastLit = lit;
                    target.GetPropertyBlock(block);
                    block.SetColor(EmissionColorId, lit ? emissionOn * Mathf.Max(amplitude, 0.01f) : Color.black);
                    target.SetPropertyBlock(block);
                    break;
                }

                case Mode.ScrollUv:
                {
                    if (target == null)
                    {
                        return;
                    }

                    // Смещение растёт монотонно; период здесь — лишь масштаб времени.
                    float offset = (float)(NetworkClock.Now % 100000.0) * amplitude;
                    target.GetPropertyBlock(block);
                    block.SetVector(BaseMapStId, new Vector4(baseTiling.x, baseTiling.y,
                        baseTiling.z + offset * axis.x, baseTiling.w + offset * axis.y));
                    target.SetPropertyBlock(block);
                    break;
                }
            }
        }
    }
}
