using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    [CreateAssetMenu(menuName = "Igruha/Sumo Config")]
    public sealed class SumoConfig : ScriptableObject
    {
        [SerializeField, Min(.1f)] private float characterWidth = .72f;
        [SerializeField, Min(1f)] private float outerDiameterWidths = 24f;
        [SerializeField, Min(1f)] private float centreDiameterWidths = 6f;
        [SerializeField, Min(1f)] private float platformHeightWidths = 3f;
        [SerializeField, Range(12, 48)] private int sectors = 24;
        [SerializeField] private float[] collapseTimes = { 10, 20, 30, 45, 60, 75 };
        [SerializeField, Min(.1f)] private float warningSeconds = 2f;
        [SerializeField, Min(.1f)] private float collapseSweepSeconds = .5f;
        [SerializeField, Min(.1f)] private float countdownSeconds = 3f;
        [SerializeField, Min(.1f)] private float fallSeconds = 1.5f;
        [SerializeField, Min(.1f)] private float pushCooldown = 1f;
        [SerializeField, Min(.05f)] private float fallTolerance = .35f;
        public float Radius => characterWidth * outerDiameterWidths * .5f;
        public float CentreRadius => characterWidth * centreDiameterWidths * .5f;
        public float Height => characterWidth * platformHeightWidths;
        public float RingWidth => (Radius - CentreRadius) / RingCount;
        public int RingCount => collapseTimes.Length;
        public int Sectors => sectors;
        public float Warning => warningSeconds;
        public float Sweep => collapseSweepSeconds;
        public float Countdown => countdownSeconds;
        public float FallSeconds => fallSeconds;
        public float PushCooldown => pushCooldown;
        public float FallTolerance => fallTolerance;
        public float CollapseAt(int ring) => collapseTimes[ring];
        public double SegmentFallsAt(int ring, int sector) => CollapseAt(ring) + sector * (double)Sweep / Mathf.Max(1, sectors - 1);
        public float OuterRadius(int ring) => Radius - ring * RingWidth;
        public int NextRing(double elapsed)
        {
            for (int i = 0; i < RingCount; i++) if (elapsed < CollapseAt(i)) return i;
            return RingCount;
        }
        public float SupportRadius(Vector3 position, double elapsed)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(position.z, position.x), Mathf.PI * 2);
            int sector = Mathf.Min(sectors - 1, Mathf.FloorToInt(angle / (Mathf.PI * 2) * sectors));
            for (int ring = 0; ring < RingCount; ring++)
                if (elapsed < SegmentFallsAt(ring, sector)) return OuterRadius(ring);
            return CentreRadius;
        }
        public bool HasFallen(Vector3 feet, double elapsed)
        {
            // A jump stays legal above a missing sector. Only a descent below the
            // fighting surface counts; once below it, returning onto the lip cannot rescue it.
            return feet.y < Height - fallTolerance;
        }
    }
}
