using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Core.Ambient
{
    /// <summary>
    /// Стая птиц по кругу над сценой. Дети этого объекта — птицы; у каждой
    /// птицы дети <c>WingL</c>/<c>WingR</c> машут. Чисто декоративно, без
    /// коллайдеров и сети: фаза берётся из <see cref="NetworkClock"/>, чтобы
    /// картинка у всех совпадала, но ни один игровой исход от неё не зависит.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientFlock : MonoBehaviour
    {
        private const string LeftWing = "WingL";
        private const string RightWing = "WingR";

        [Tooltip("Радиус круга, м")]
        [SerializeField] private float radius = 40f;
        [Tooltip("Время полного круга, с")]
        [SerializeField] private float period = 48f;
        [Tooltip("Размах подъёма и спуска стаи, м")]
        [SerializeField] private float heightSwing = 6f;
        [Tooltip("Разброс птиц по радиусу и высоте, м")]
        [SerializeField] private float spread = 5f;
        [Tooltip("Взмахов в секунду")]
        [SerializeField] private float flapRate = 2.6f;
        [Tooltip("Угол взмаха, градусы")]
        [SerializeField] private float flapAngle = 38f;

        private Transform[] birds;
        private Transform[] leftWings;
        private Transform[] rightWings;
        private float[] offsets;

        public void Configure(float circleRadius, float circlePeriod, float swing, float scatter)
        {
            radius = circleRadius;
            period = circlePeriod;
            heightSwing = swing;
            spread = scatter;
        }

        private void Awake()
        {
            int count = transform.childCount;
            birds = new Transform[count];
            leftWings = new Transform[count];
            rightWings = new Transform[count];
            offsets = new float[count];
            for (int i = 0; i < count; i++)
            {
                birds[i] = transform.GetChild(i);
                leftWings[i] = birds[i].Find(LeftWing);
                rightWings[i] = birds[i].Find(RightWing);
                // Детерминированный разброс: одинаковый у всех машин и при каждом запуске.
                offsets[i] = Mathf.Repeat(i * 0.618034f, 1f);
            }
        }

        private void Update()
        {
            if (birds == null || period <= 0f)
            {
                return;
            }

            float t = (float)(NetworkClock.Now % (period * 1000.0)) / period;
            float flapTime = (float)(NetworkClock.Now % 1000.0) * flapRate * Mathf.PI * 2f;
            for (int i = 0; i < birds.Length; i++)
            {
                float lag = offsets[i] * 0.22f;
                float angle = (t - lag) * Mathf.PI * 2f;
                float r = radius + (offsets[i] - 0.5f) * spread * 2f;
                float y = Mathf.Sin(angle * 2f + offsets[i] * 6f) * heightSwing + (offsets[i] - 0.5f) * spread;
                var position = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
                var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                birds[i].localPosition = position;
                birds[i].localRotation = Quaternion.LookRotation(tangent, Vector3.up);

                float flap = Mathf.Sin(flapTime + offsets[i] * 9f) * flapAngle;
                if (leftWings[i] != null)
                {
                    leftWings[i].localRotation = Quaternion.Euler(0f, 0f, flap);
                }

                if (rightWings[i] != null)
                {
                    rightWings[i].localRotation = Quaternion.Euler(0f, 0f, -flap);
                }
            }
        }
    }
}
