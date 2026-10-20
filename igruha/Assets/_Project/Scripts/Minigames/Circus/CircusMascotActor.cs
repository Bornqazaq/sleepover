using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Authored full-body laughter with live gaze, pointing and eyelids.</summary>
    public sealed class CircusMascotActor : MonoBehaviour
    {
        private const float ModelScale = .42f;
        private const float TauntLength = 3.6f;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform model;
        [SerializeField] private Transform head, rightHand, indexBase, indexTip;
        [SerializeField] private SkinnedMeshRenderer leftLid, rightLid;
        [SerializeField] private SkinnedMeshRenderer face;
        private int smileIndex = -1;
        [SerializeField] private ParticleSystem arrival;
        [SerializeField] private AudioSource laugh;
        private static readonly int Idle = Animator.StringToHash("Idle");
        private static readonly int Taunt = Animator.StringToHash("Taunt");
        private bool soundStarted, visible;
        private float blinkOffset;
        public bool IsVisible => visible;
        public int PuffParticleCount => arrival != null ? arrival.particleCount : 0;
        public float BlinkWeight { get; private set; }
        public float SmileWeight { get; private set; }
        public Vector3 PointDirection => indexTip != null && indexBase != null ? (indexTip.position - indexBase.position).normalized : transform.forward;
        public Vector3 PointOrigin => indexBase != null ? indexBase.position : transform.position;

        public void Begin(Transform victim, float elapsed, uint seed)
        {
            smileIndex = face != null ? face.sharedMesh.GetBlendShapeIndex("LaughSmile") : -1;
            soundStarted = false; blinkOffset = (seed % 7) * .037f;
            visible = true;
            model.gameObject.SetActive(true);
            model.localScale = Vector3.one * ModelScale;
            model.localPosition = Vector3.zero;
            if (laugh != null) laugh.Stop();
            animator.speed = 0; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (arrival != null)
            {
                arrival.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                arrival.useAutoRandomSeed = false; arrival.randomSeed = seed;
                if (elapsed < .5f) arrival.Emit(40);
            }
            Sample(Mathf.Max(0, elapsed), victim.position + Vector3.up * 1.5f);
        }

        public void Sample(float elapsed, Vector3 headPosition)
        {
            Vector3 facing = headPosition - transform.position; facing.y = 0;
            if (facing.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(facing);
            // Pop in/out at full size; only the cloud expands.
            model.localScale = Vector3.one * ModelScale;
            model.localPosition = Vector3.zero;
            // Keep the original quick pointing gesture, then stretch the sustained laugh.
            float clipTime = elapsed < 1f ? Mathf.Max(0, elapsed - .25f) :
                Mathf.Lerp(.75f, TauntLength, Mathf.InverseLerp(1f, CircusCageMascot.ShowSeconds, elapsed));
            animator.Play(elapsed < .25f ? Idle : Taunt, 0, elapsed < .25f ? 0 : clipTime / TauntLength);
            animator.Update(0);
            if (head != null)
            {
                Vector3 gaze = headPosition - head.position;
                if (gaze.sqrMagnitude > .01f)
                {
                    Quaternion correction = Quaternion.FromToRotation(transform.forward, gaze.normalized);
                    float angle = Quaternion.Angle(Quaternion.identity, correction);
                    head.rotation = Quaternion.Slerp(Quaternion.identity, correction, Mathf.Min(1, 22f / Mathf.Max(.01f, angle))) * head.rotation;
                }
            }
            if (elapsed > .8f && elapsed < CircusCageMascot.ShowSeconds - .35f && rightHand != null)
            {
                Vector3 desired = headPosition - indexBase.position;
                if (desired.sqrMagnitude > .01f)
                {
                    Quaternion correction = Quaternion.FromToRotation(PointDirection, desired.normalized);
                    float angle = Quaternion.Angle(Quaternion.identity, correction);
                    rightHand.rotation = Quaternion.Slerp(Quaternion.identity, correction, Mathf.Min(1, 60f / Mathf.Max(.01f, angle))) * rightHand.rotation;
                }
            }
            BlinkWeight = Mathf.Max(Pulse(elapsed, .23f + blinkOffset), Mathf.Max(Pulse(elapsed, 1.47f + blinkOffset), Mathf.Max(Pulse(elapsed, 3.20f + blinkOffset), Pulse(elapsed, 4.45f + blinkOffset)))) * 100;
            if (elapsed > .8f && elapsed < CircusCageMascot.LaughEnds) BlinkWeight = Mathf.Max(BlinkWeight, 28 + 12 * Mathf.Sin(elapsed * 12));
            float smileIn = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .55f, elapsed));
            float smileOut = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(CircusCageMascot.LaughEnds - .25f, CircusCageMascot.ShowSeconds, elapsed));
            SmileWeight = smileIn * smileOut * (78 + 22 * Mathf.Sin(elapsed * 17.27876f));
            if (face != null && smileIndex >= 0) face.SetBlendShapeWeight(smileIndex, SmileWeight);
            if (leftLid != null) leftLid.SetBlendShapeWeight(0, BlinkWeight);
            if (rightLid != null) rightLid.SetBlendShapeWeight(0, BlinkWeight * .97f);
            if (!soundStarted && elapsed >= CircusCageMascot.LaughStarts)
            {
                soundStarted = true;
                if (laugh != null && laugh.clip != null && elapsed - CircusCageMascot.LaughStarts < laugh.clip.length)
                {
                    // Late network delivery joins the same recording instead of restarting it.
                    laugh.time = Mathf.Max(0, elapsed - CircusCageMascot.LaughStarts);
                    laugh.Play();
                }
            }
        }

        private static float Pulse(float t, float at) => Mathf.Max(0, 1 - Mathf.Abs(t - at) / .10f);
        public void Stop(bool puff = true)
        {
            if (laugh != null) laugh.Stop();
            model.gameObject.SetActive(false);
            if (arrival != null && visible && puff) arrival.Emit(40);
            visible = false;
            if (!puff) gameObject.SetActive(false);
        }

        private void Update()
        {
            // Keep the exit cloud alive after hiding the horse; reuse the same instance.
            if (!visible && (arrival == null || !arrival.IsAlive(true))) gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            visible = false;
            if (laugh != null) laugh.Stop();
            if (arrival != null) arrival.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
