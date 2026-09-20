using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Spatial anticipation and contact cues, all driven by the strike clock.</summary>
    public sealed class CircusBearFeedback : MonoBehaviour
    {
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioClip growl;
        [SerializeField] private AudioClip swipe;
        [Tooltip("Удар лапой. Пачка вариаций: три подряд одинаковых удара слышны как один зацикленный")]
        [SerializeField] private AudioClip[] impacts;
        [SerializeField] private AudioClip step;
        [SerializeField] private ParticleSystem impactDust;
        [SerializeField] private ParticleSystem footDust;
        [SerializeField] private TrailRenderer clawTrail;
        [SerializeField] private Transform[] paws;
        private readonly float[] previousHeights = new float[4];
        private readonly float[] stepCooldowns = new float[4];
        private PitBear bear;
        private float attackStartedAt = -100f;
        private bool swiped = true;

        /// <summary>Какая вариация удара звучала прошлый раз — чтобы не повторить её подряд.</summary>
        private int lastImpact;

        private void Awake() => bear = GetComponent<PitBear>();

        public void Attack(float elapsed)
        {
            attackStartedAt = Time.time - elapsed;
            swiped = elapsed > PitBear.ContactSeconds;
            if (voice != null && growl != null && elapsed < .35f) voice.PlayOneShot(growl, .72f);
        }

        public void Impact(Vector3 point)
        {
            AudioClip hit = PickImpact();
            if (voice != null && hit != null) voice.PlayOneShot(hit, .95f);
            if (impactDust != null)
            {
                impactDust.transform.position = point;
                impactDust.Emit(22);
            }
        }

        /// <summary>
        /// Вариация удара — случайная, но не та же, что прошлый раз. Бить лапой
        /// медведь может несколько раз подряд, и один и тот же файл в этом ритме
        /// перестаёт звучать как удар.
        /// </summary>
        private AudioClip PickImpact()
        {
            if (impacts == null || impacts.Length == 0) return null;
            if (impacts.Length == 1) return impacts[0];

            int index = Random.Range(0, impacts.Length - 1);
            if (index >= lastImpact) index++;
            lastImpact = index;
            return impacts[index];
        }

        private void LateUpdate()
        {
            float age = Time.time - attackStartedAt;
            if (!swiped && age >= .47f)
            {
                swiped = true;
                if (voice != null && swipe != null) voice.PlayOneShot(swipe, .8f);
            }
            if (clawTrail != null) clawTrail.emitting = age >= .47f && age < .9f;
            if (paws == null || bear == null) return;
            bool moving = bear.State == PitBear.BearState.Chase || bear.State == PitBear.BearState.Patrol;
            for (int i = 0; i < paws.Length && i < previousHeights.Length; i++)
            {
                if (paws[i] == null) continue;
                float height = paws[i].position.y - transform.position.y;
                stepCooldowns[i] -= Time.deltaTime;
                if (moving && stepCooldowns[i] <= 0 && height < .27f && previousHeights[i] >= .27f)
                {
                    stepCooldowns[i] = .23f;
                    if (voice != null && step != null) voice.PlayOneShot(step, bear.State == PitBear.BearState.Chase ? .25f : .14f);
                    if (footDust != null)
                    {
                        footDust.transform.position = new Vector3(paws[i].position.x, transform.position.y + .035f, paws[i].position.z);
                        footDust.Emit(3);
                    }
                }
                previousHeights[i] = height;
            }
        }

        private void OnDisable()
        {
            if (voice != null) voice.Stop();
            if (clawTrail != null) { clawTrail.emitting = false; clawTrail.Clear(); }
        }
    }
}
