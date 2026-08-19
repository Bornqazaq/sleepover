using UnityEngine;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>
    /// Отвлекалки: тик, рёв, толпа, прожекторы.
    ///
    /// Без них «Секундомер» — соревнование в устном счёте, и выигрывает тот,
    /// кто ровнее проговаривает «двадцать один, двадцать два». Тикающий звук
    /// в неправильном ритме — не украшение, а главная механика: он звучит
    /// убедительно, почти совпадает с секундами и врёт.
    ///
    /// <b>Период тика приходит снаружи и не выбирается здесь.</b> Если бы каждая
    /// машина крутила свой ритм, игроки мерили бы время под разную подсказку,
    /// и подраунд перестал бы быть честным. Рулетку крутит авторитет, сюда
    /// приезжает готовое число.
    ///
    /// Звуки — процедурные заглушки, чтобы механику можно было проверить ушами
    /// уже на каркасе. В 8.23 они меняются на настоящие: достаточно назначить
    /// клипы в поля, генерация тогда сама отключится.
    /// </summary>
    public sealed class DistractionDirector : MonoBehaviour
    {
        [Header("Звук — пусто значит процедурная заглушка")]
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip roarClip;
        [SerializeField] private AudioClip crowdClip;

        [Header("Громкость на максимуме интенсивности")]
        [SerializeField] private float tickVolume = 0.55f;
        [SerializeField] private float roarVolume = 0.8f;
        [SerializeField] private float crowdVolume = 0.25f;

        [Header("Прожектор")]
        [Tooltip("Сколько секунд длится пробег луча")]
        [SerializeField] private float sweepDuration = 1.2f;
        [Tooltip("Высота, с которой светит прожектор, м")]
        [SerializeField] private float spotlightHeight = 17f;

        private AudioSource tickSource;
        private AudioSource roarSource;
        private AudioSource crowdSource;
        private Light spotlight;

        private float intensity;
        private float tickPeriod = 1f;
        private float tickTimer;
        private float roarTimer;
        private float sweepTimer;
        private float sweepLeft;
        private float sweepFrom;
        private float sweepTo;
        private bool running;

        private Vector2 roarInterval = new Vector2(6f, 12f);
        private Vector2 sweepInterval = new Vector2(5f, 9f);
        private float arenaRadius = 8.64f;
        private System.Random random;

        /// <summary>Период тика текущего подраунда, с. Наружу — для замеров приёмки.</summary>
        public float TickPeriod => tickPeriod;

        /// <summary>Интенсивность текущего подраунда, 0..1.</summary>
        public float Intensity => intensity;

        private void Awake()
        {
            tickSource = CreateSource("Tick", tickClip != null ? tickClip : GenerateTick(), false);
            roarSource = CreateSource("Roar", roarClip != null ? roarClip : GenerateRoar(), false);
            crowdSource = CreateSource("Crowd", crowdClip != null ? crowdClip : GenerateCrowd(), true);
            CreateSpotlight();
            random = new System.Random(unchecked(GetInstanceID() * 31));
        }

        /// <summary>Границы интервалов и радиус арены — из конфигов игры.</summary>
        public void Configure(Vector2 roars, Vector2 sweeps, float radius)
        {
            roarInterval = roars;
            sweepInterval = sweeps;
            arenaRadius = radius;
        }

        /// <summary>
        /// Новый подраунд. <paramref name="period"/> — уже выбранный авторитетом
        /// период тика, <paramref name="level"/> — интенсивность 0..1.
        /// </summary>
        public void BeginSubround(float period, float level)
        {
            tickPeriod = Mathf.Max(0.05f, period);
            intensity = Mathf.Clamp01(level);
            running = intensity > 0f;

            tickTimer = 0f;
            roarTimer = NextInterval(roarInterval);
            sweepTimer = NextInterval(sweepInterval);

            if (crowdSource != null)
            {
                crowdSource.volume = crowdVolume * intensity;
                if (running && !crowdSource.isPlaying)
                {
                    crowdSource.Play();
                }
                else if (!running && crowdSource.isPlaying)
                {
                    crowdSource.Stop();
                }
            }
        }

        /// <summary>Тишина и темнота: раунд кончился.</summary>
        public void Stop()
        {
            running = false;
            intensity = 0f;
            sweepLeft = 0f;
            crowdSource?.Stop();
            if (spotlight != null)
            {
                spotlight.enabled = false;
            }
        }

        private void Update()
        {
            if (!running)
            {
                return;
            }

            float dt = Time.deltaTime;

            tickTimer -= dt;
            if (tickTimer <= 0f)
            {
                tickTimer += tickPeriod;
                if (tickSource != null)
                {
                    tickSource.volume = tickVolume * intensity;
                    tickSource.Play();
                }
            }

            roarTimer -= dt;
            if (roarTimer <= 0f)
            {
                roarTimer = NextInterval(roarInterval);
                if (roarSource != null)
                {
                    roarSource.volume = roarVolume * intensity;
                    roarSource.Play();
                }
            }

            sweepTimer -= dt;
            if (sweepTimer <= 0f)
            {
                sweepTimer = NextInterval(sweepInterval);
                StartSweep();
            }

            AdvanceSweep(dt);
        }

        private void StartSweep()
        {
            sweepLeft = sweepDuration;
            sweepFrom = (float)random.NextDouble() * 360f;
            sweepTo = sweepFrom + 120f + (float)random.NextDouble() * 120f;
            if (spotlight != null)
            {
                spotlight.enabled = true;
            }
        }

        private void AdvanceSweep(float dt)
        {
            if (sweepLeft <= 0f || spotlight == null)
            {
                return;
            }

            sweepLeft -= dt;
            float t = 1f - Mathf.Clamp01(sweepLeft / sweepDuration);
            float angle = Mathf.Lerp(sweepFrom, sweepTo, t);
            Vector3 target = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (arenaRadius * 0.7f);
            spotlight.transform.rotation = Quaternion.LookRotation(target - spotlight.transform.position);
            spotlight.intensity = Mathf.Lerp(0f, 6f * intensity, Mathf.Sin(t * Mathf.PI));

            if (sweepLeft <= 0f)
            {
                spotlight.enabled = false;
            }
        }

        private float NextInterval(Vector2 range)
        {
            return Mathf.Lerp(range.x, range.y, (float)random.NextDouble());
        }

        private AudioSource CreateSource(string sourceName, AudioClip clip, bool loop)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            // 2D: тик и толпа звучат одинаково из любой клетки, привязывать
            // их к точке в мире незачем.
            source.spatialBlend = 0f;
            return source;
        }

        private void CreateSpotlight()
        {
            var go = new GameObject("SweepSpotlight");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, spotlightHeight, 0f);
            spotlight = go.AddComponent<Light>();
            spotlight.type = LightType.Spot;
            spotlight.spotAngle = 22f;
            spotlight.range = spotlightHeight * 2f;
            spotlight.color = new Color(1f, 0.96f, 0.85f);
            spotlight.enabled = false;
        }

        // ===== Процедурные заглушки звука =====
        // Нужны затем, чтобы тик можно было проверить ушами уже на каркасе:
        // «период не равен секунде» — это пункт приёмки, а не вопрос вкуса.

        private static AudioClip GenerateTick()
        {
            const int rate = 44100;
            int samples = rate / 20;
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float decay = Mathf.Exp(-t * 60f);
                data[i] = Mathf.Sin(2f * Mathf.PI * 1400f * t) * decay;
            }

            var clip = AudioClip.Create("TickStub", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip GenerateRoar()
        {
            const int rate = 44100;
            int samples = (int)(rate * 0.7f);
            var data = new float[samples];
            var noise = new System.Random(1234);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Sin(Mathf.PI * (t / 0.7f));
                float low = Mathf.Sin(2f * Mathf.PI * 90f * t) * 0.6f;
                float grit = (float)(noise.NextDouble() * 2.0 - 1.0) * 0.4f;
                data[i] = (low + grit) * envelope;
            }

            var clip = AudioClip.Create("RoarStub", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip GenerateCrowd()
        {
            const int rate = 44100;
            int samples = rate * 2;
            var data = new float[samples];
            var noise = new System.Random(4321);
            float previous = 0f;
            for (int i = 0; i < samples; i++)
            {
                float white = (float)(noise.NextDouble() * 2.0 - 1.0);
                // Фильтр низких частот: белый шум звучит как эфир, приглушённый — как гул зала.
                previous = Mathf.Lerp(previous, white, 0.02f);
                data[i] = previous * 3f;
            }

            var clip = AudioClip.Create("CrowdStub", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
