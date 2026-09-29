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
        [Header("Combat — local to Sumo")]
        [SerializeField] private SumoMotionLibrary motions;
        [SerializeField] private Material combatEffectMaterial;
        public SumoMotionLibrary Motions => motions;
        public Material CombatEffectMaterial => combatEffectMaterial;
        [SerializeField, Min(.1f)] private float combatReach = .55f;
        [SerializeField, Range(20, 180)] private float attackArc = 110f;
        [SerializeField, Range(20, 180)] private float guardArc = 120f;
        [SerializeField, Min(.05f)] private float quickWindup = .22f;
        [SerializeField, Min(.05f)] private float heavyWindup = .32f;
        [SerializeField, Min(.05f)] private float counterWindup = .14f;
        [SerializeField, Min(.1f)] private float chargeSeconds = .65f;
        [SerializeField, Min(.1f)] private float maximumChargeSeconds = 1.8f;
        [SerializeField, Min(.05f)] private float recoverySeconds = .38f;
        [SerializeField, Min(.05f)] private float heavyRecoverySeconds = .65f;
        [SerializeField, Min(.05f)] private float parryWindow = .2f;
        [SerializeField, Min(.05f)] private float parryCooldown = .65f;
        [SerializeField, Min(.05f)] private float counterWindow = .5f;
        [SerializeField, Min(.05f)] private float staggerSeconds = .24f;
        [SerializeField, Min(.05f)] private float parriedStaggerSeconds = .6f;
        [SerializeField, Min(.05f)] private float guardBreakSeconds = .65f;
        [SerializeField, Range(0, 1)] private float guardPushMultiplier = .25f;
        [SerializeField, Min(.1f)] private float guardSpeed = 2.3f;
        [SerializeField, Min(.1f)] private float chargeSpeed = 1.6f;
        [SerializeField, Min(.1f)] private float quickPushSpeed = 5.5f;
        [SerializeField, Min(.1f)] private float heavyPushSpeed = 8f;
        [SerializeField, Min(.1f)] private float counterPushSpeed = 9f;
        [SerializeField, Min(.05f)] private float quickSlideSeconds = .32f;
        [SerializeField, Min(.05f)] private float heavySlideSeconds = .52f;
        public float CombatReach => combatReach;
        public float AttackArc => attackArc;
        public float GuardArc => guardArc;
        public float QuickWindup => quickWindup;
        public float HeavyWindup => heavyWindup;
        public float CounterWindup => counterWindup;
        public float ChargeSeconds => chargeSeconds;
        public float MaximumChargeSeconds => maximumChargeSeconds;
        public float RecoverySeconds => recoverySeconds;
        public float HeavyRecoverySeconds => heavyRecoverySeconds;
        public float ParryWindow => parryWindow;
        public float ParryCooldown => parryCooldown;
        public float CounterWindow => counterWindow;
        public float StaggerSeconds => staggerSeconds;
        public float ParriedStaggerSeconds => parriedStaggerSeconds;
        public float GuardBreakSeconds => guardBreakSeconds;
        public float GuardPushMultiplier => guardPushMultiplier;
        public float GuardSpeed => guardSpeed;
        public float ChargeSpeed => chargeSpeed;
        public float QuickPushSpeed => quickPushSpeed;
        public float HeavyPushSpeed => heavyPushSpeed;
        public float CounterPushSpeed => counterPushSpeed;
        public float QuickSlideSeconds => quickSlideSeconds;
        public float HeavySlideSeconds => heavySlideSeconds;
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
