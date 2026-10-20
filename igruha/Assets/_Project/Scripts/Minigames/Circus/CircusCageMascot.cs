using Igruha.Core.Minigame;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Circus
{
    /// <summary>Local presentation of the cage's already replicated penalty. No new network state.</summary>
    [DefaultExecutionOrder(260)]
    [RequireComponent(typeof(CageStation))]
    public sealed class CircusCageMascot : MonoBehaviour
    {
        public const float ShowSeconds = 5.1f;
        public const float LaughStarts = 0f;
        public const float LaughEnds = 5.03f;
        [SerializeField] private CircusMascotActor mascotPrefab;
        [SerializeField] private Material angerMaterial;
        [SerializeField] private Material puffMaterial;
        [SerializeField] private CircusAngerLibrary headLibrary;
        [SerializeField] private Vector3 localAnchor = new Vector3(0, .55f, 2.05f);
        private CageStation cage;
        private CircusMascotActor actor;
        private CircusAngerEffect anger;
        private PlayerController target;
        private double startedAt = double.NegativeInfinity;
        private bool presenting;
        public bool IsPresenting => presenting;
        public bool IsLaughing => presenting && Elapsed >= LaughStarts && Elapsed < LaughEnds;
        public double StartedAt => startedAt;
        public float Elapsed => (float)(NetworkClock.Now - startedAt);
        public PlayerController Target => target;
        public CircusMascotActor Actor => actor;
        public CircusAngerEffect Anger => anger;
        public int PresentationsStarted { get; private set; }

        private void Awake() => cage = GetComponent<CageStation>();
        private void OnEnable()
        {
            if (cage == null) cage = GetComponent<CageStation>();
            cage.DescentStarted += Begin;
            cage.OccupantReleased += Clear;
            cage.DoorsOpened += Clear;
        }

        private void Begin(CageStation source, double start, float duration)
        {
            float elapsed = (float)(NetworkClock.Now - start);
            if (mascotPrefab == null || source.Occupant == null || elapsed >= ShowSeconds ||
                (target == source.Occupant && System.Math.Abs(startedAt - start) < .02)) return;
            Clear(source);
            target = source.Occupant; startedAt = start; presenting = true; PresentationsStarted++;
            if (actor == null) actor = Instantiate(mascotPrefab, transform);
            actor.transform.localPosition = localAnchor;
            actor.gameObject.SetActive(true);
            if (anger == null) anger = gameObject.AddComponent<CircusAngerEffect>();
            anger.Bind(target, headLibrary, angerMaterial, puffMaterial);
            actor.Begin(target.transform, elapsed, (uint)(transform.GetSiblingIndex() + 73));
        }

        private void LateUpdate()
        {
            if (!presenting) return;
            float t = Elapsed;
            if (target == null || cage.Occupant != target || cage.DoorsOpen || t >= ShowSeconds)
            { Clear(cage); return; }
            actor.transform.localPosition = localAnchor;
            actor.Sample(Mathf.Max(0, t), anger != null ? anger.HeadPosition : target.Position + Vector3.up * 1.5f);
            anger?.SetHeat(HeatAt(t));
        }

        public static float HeatAt(float elapsed) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(LaughStarts, LaughStarts + .45f, elapsed)) *
            (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(LaughEnds - .35f, ShowSeconds, elapsed)));

        private void Clear(CageStation source)
        {
            presenting = false;
            if (actor != null) actor.Stop();
            if (anger != null) anger.Clear();
            target = null;
        }

        private void OnDisable()
        {
            if (cage != null)
            {
                cage.DescentStarted -= Begin; cage.OccupantReleased -= Clear; cage.DoorsOpened -= Clear;
            }
            Clear(cage);
            if (actor != null) actor.Stop(false);
        }
    }
}
