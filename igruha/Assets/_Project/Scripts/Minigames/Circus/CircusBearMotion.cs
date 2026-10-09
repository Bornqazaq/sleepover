using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>Authored animal poses with a smooth gaze and
    /// a body-aware strike. This affects the original bear only, never a player rig.</summary>
    [DefaultExecutionOrder(180)]
    public sealed class CircusBearMotion : MonoBehaviour
    {
        private const float PawRadius = .18f;
        [SerializeField] private PitBear bear;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform lumbar;
        private sealed class Leg
        {
            internal Transform Upper, Lower, Paw, Toes;
            internal Vector3 ContactOffset;
            internal float UpperLength, LowerLength;
        }
        private readonly Leg[] legs = new Leg[4];
        private readonly Transform[] lids = new Transform[2];
        private readonly Transform[] ears = new Transform[2];
        private readonly CircusBearContactShape contactShape = new CircusBearContactShape();
        private PlayerController fittedVictim;
        private Vector3 previousPosition, previousContact;
        private float previousYaw, headTurn;
        private float lifeTime;
        private bool initialized, contactKnown;
        private Vector3 sampledSurface,sampledShoulder;
        private float sampledAge,sampledReach,sampledShoulderDistance;
        private bool sampledReachable;
        public Vector3 StrikePawCenter { get; private set; }
        public float ContactDistance { get; private set; } = float.PositiveInfinity;
        public int ContactFrame { get; private set; } = -1;
        public uint ContactVersion { get; private set; }
        public PlayerController ContactVictim { get; private set; }

        public string DescribeContact()
        {
            string victim="none",pose="none";
            if(fittedVictim!=null)
            {
                victim=fittedVictim.name+" transform="+fittedVictim.transform.position.ToString("F3")+
                    " physics="+fittedVictim.Position.ToString("F3")+" yaw="+fittedVictim.transform.eulerAngles.y.ToString("F2");
                var a=fittedVictim.GetComponentInChildren<Animator>();
                if(a!=null && a.isInitialized)
                {
                    var state=a.GetCurrentAnimatorStateInfo(0);var clips=a.GetCurrentAnimatorClipInfo(0);
                    pose=(clips.Length>0?clips[0].clip.name:state.shortNameHash.ToString())+":"+state.normalizedTime.ToString("F3")+
                        " transition="+a.IsInTransition(0)+" culling="+a.cullingMode+
                        " model="+a.transform.localPosition.ToString("F3")+" scale="+a.transform.localScale.ToString("F3");
                    var skin=a.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if(skin!=null)pose+=" visible="+skin.isVisible;
                    if(a.isHuman)
                    {
                        var hips=a.GetBoneTransform(HumanBodyBones.Hips);
                        var chestBone=a.GetBoneTransform(HumanBodyBones.Chest);
                        if(hips!=null)pose+=" hips="+hips.position.ToString("F3");
                        if(chestBone!=null)pose+=" chest="+chestBone.position.ToString("F3");
                    }
                }
            }
            Leg leg=legs[1];
            return "victim="+victim+" pose="+pose+" sampleAge="+sampledAge.ToString("F3")+
                " surface="+sampledSurface.ToString("F3")+" shoulder="+sampledShoulder.ToString("F3")+
                " reach="+sampledShoulderDistance.ToString("F3")+"/"+sampledReach.ToString("F3")+
                " lengths="+(leg!=null?leg.UpperLength.ToString("F3")+"+"+leg.LowerLength.ToString("F3"):"none")+
                " reachable="+sampledReachable+" pad="+StrikePawCenter.ToString("F3")+" gap="+ContactDistance.ToString("F4");
        }

        public float ApproachDistance(PlayerController victim)
        {
            if (victim == null) return 2.35f;
            if (fittedVictim != victim)
            {
                fittedVictim = victim; contactShape.Bind(victim); contactKnown = false;
            }
            Vector3 pad = contactShape.SurfaceToward(bear.transform.position + Vector3.up, PawRadius);
            Vector3 outside = pad - victim.Position; outside.y = 0;
            // Effective horizontal reach at the braced shoulder, including the
            // pad offset. A thin runner must not be swiped at a large body's gap.
            return Mathf.Clamp(1.70f + outside.magnitude, 2.0f, 2.85f);
        }

        public bool HasBodyClearance(PlayerController victim)
        {
            if(victim==null || contactShape.BoundPlayer!=victim)return false;
            Transform root=bear.PresentationRoot;
            Vector3 delta=victim.Position-root.position;delta.y=0;
            Vector3 towardBear=delta.sqrMagnitude>.0001f ? -delta.normalized : -root.forward;
            Vector3 pad=contactShape.SurfaceToward(root.position+Vector3.up,PawRadius);
            float nearExtent=Mathf.Max(0,Vector3.Dot(pad-victim.Position,towardBear));
            // The authored shoulder/neck mass ends within 1.6 m of the root.
            // Add the victim's current near-side extent (already padded for a
            // paw), keeping the torso outside that plane while the foreleg swats.
            // This is separate from the .12 m paw-contact tolerance.
            float clearance=Mathf.Max(1.7f,1.6f+nearExtent);
            return Vector3.Dot(delta,root.forward)>=clearance;
        }

        private void Awake()
        {
            var bones = GetComponentsInChildren<Transform>(true);
            foreach (var bone in bones)
            {
                if (bone.name == "Lid.L") lids[0] = bone;
                if (bone.name == "Lid.R") lids[1] = bone;
                if (bone.name == "Ear.L") ears[0] = bone;
                if (bone.name == "Ear.R") ears[1] = bone;
            }
            lifeTime = bear != null && bear.name.EndsWith("_Second") ? 1.7f : 0;
            string[] names = { "Fore", "Fore", "Hind", "Hind" };
            for (int i = 0; i < legs.Length; i++)
            {
                string suffix = i % 2 == 0 ? ".L" : ".R";
                var leg = new Leg(); legs[i] = leg;
                foreach (var bone in bones)
                {
                    if (bone.name == names[i] + "Upper" + suffix) leg.Upper = bone;
                    if (bone.name == names[i] + "Lower" + suffix) leg.Lower = bone;
                    if (bone.name == names[i] + "Paw" + suffix) leg.Paw = bone;
                    if (bone.name == names[i] + "Toes" + suffix) leg.Toes = bone;
                }
                if (leg.Upper == null || leg.Lower == null || leg.Paw == null) continue;
                leg.UpperLength = Vector3.Distance(leg.Upper.position, leg.Lower.position);
                leg.LowerLength = Vector3.Distance(leg.Lower.position, leg.Paw.position);
                leg.ContactOffset = leg.Toes != null ? leg.Paw.InverseTransformPoint(leg.Toes.position) * .65f : Vector3.zero;
            }
        }

        private void OnEnable() { initialized = false; contactKnown = false; }

        public void ResetPresentationHistory()
        {
            initialized=false;contactKnown=false;
            ContactFrame=-1;ContactVictim=null;ContactDistance=float.PositiveInfinity;
        }

        private void LateUpdate()
        {
            EvaluatePose(Time.deltaTime);
        }

        private void EvaluatePose(float deltaTime)
        {
            ContactFrame = -1; ContactVictim = null; ContactDistance = float.PositiveInfinity;
            if (bear == null || deltaTime <= 0) return;
            float dt = Mathf.Min(deltaTime, .1f);
            Transform root = bear.PresentationRoot;
            Vector3 displacement = initialized ? root.position - previousPosition : Vector3.zero;
            bool teleported = displacement.sqrMagnitude > 4;
            float yaw = root.eulerAngles.y;
            float turn = initialized && !teleported ? Mathf.Clamp(Mathf.DeltaAngle(previousYaw, yaw) / dt, -220, 220) : 0;
            previousYaw = yaw; previousPosition = root.position; initialized = true;
            bool locomotion = bear.State == PitBear.BearState.Patrol || bear.State == PitBear.BearState.Chase;
            bool striking = bear.State == PitBear.BearState.Attack;
            bool turning = Mathf.Abs(turn) > 8 && bear.State != PitBear.BearState.Taunt;
            lifeTime += dt;
            // Facial life continues during locomotion and pauses; it is not tied
            // to the short repeating walk cycle. The two animals have different phases.
            if (!striking && bear.State != PitBear.BearState.Taunt)
            {
                float blink = Mathf.Max(0, 1 - Mathf.Abs(Mathf.Repeat(lifeTime, 4.9f) - 3.6f) / .095f);
                for (int i = 0; i < 2; i++)
                {
                    if (lids[i] != null && blink > 0)
                    {
                        Vector3 scale = lids[i].localScale;
                        scale.y *= 1 + 2 * blink;
                        lids[i].localScale = scale;
                    }
                    float flick = Mathf.Max(0, 1 - Mathf.Abs(Mathf.Repeat(lifeTime + i * 1.3f, 6.1f) - 2.2f) / .24f);
                    if (ears[i] != null) ears[i].localRotation *= Quaternion.Euler(5 * flick, 0, (i == 0 ? 3 : -3) * flick);
                }
                if (head != null) head.localRotation *= Quaternion.Euler(.65f * Mathf.Sin(lifeTime * 2.15f), 0, 0);
            }
            float bearing = turn * .065f;
            if (bear.PresentationVictim != null)
            {
                Vector3 toward = bear.PresentationVictim.Position - root.position; toward.y = 0;
                if (toward.sqrMagnitude > .01f) bearing = Mathf.Clamp(Vector3.SignedAngle(root.forward, toward, Vector3.up), -18, 18);
            }
            headTurn = Mathf.Lerp(headTurn, locomotion || turning ? bearing : 0, 1 - Mathf.Exp(-9 * dt));
            if (locomotion || turning)
            {
                if (neck != null) neck.rotation = Quaternion.AngleAxis(headTurn * .4f, Vector3.up) * neck.rotation;
                if (head != null) head.rotation = Quaternion.AngleAxis(headTurn * .6f, Vector3.up) * head.rotation;
            }
            if (bear.PresentationVictim != fittedVictim)
            {
                fittedVictim = bear.PresentationVictim;
                contactShape.Bind(fittedVictim); contactKnown = false;
            }
            // Locomotion and support paws belong to the baked gait. Locking an
            // animated paw to a second world-space anchor made it stretch, then
            // snap loose at MaximumPlantOffset, especially during turns/blends.
            // Only the striking forepaw adapts to the victim's actual surface.
            Leg strikeLeg = legs[1];
            if (striking && strikeLeg != null && strikeLeg.Paw != null)
                FitStrike(strikeLeg);
            if (!striking) { contactKnown = false; ContactDistance = float.PositiveInfinity; }
        }

        private void FitStrike(Leg leg)
        {
            Vector3 animated = leg.Paw.TransformPoint(leg.ContactOffset);
            if (fittedVictim == null) { StrikePawCenter = animated; ContactDistance = float.PositiveInfinity; return; }
            float age = bear.AttackAge;
            contactShape.RefreshPose();
            float approach = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PitBear.SwipeSeconds, PitBear.ContactSeconds, age));
            float release = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PitBear.ContactSeconds + .14f, PitBear.ContactSeconds + .32f, age));
            Vector3 approachPoint = bear.PresentationRoot.position + Vector3.up;
            Vector3 surface = contactShape.SurfaceToward(approachPoint, PawRadius);
            // Keep the authored miss; never stretch the foreleg to a player who escaped.
            float reachable = Vector3.Distance(surface, leg.Upper.position) <= leg.UpperLength + leg.LowerLength + .33f ? 1 : 0;
            sampledAge=age;sampledSurface=surface;sampledShoulder=leg.Upper.position;
            sampledShoulderDistance=Vector3.Distance(surface,leg.Upper.position);
            sampledReach=leg.UpperLength+leg.LowerLength+.33f;sampledReachable=reachable>0;
            Vector3 wanted = Vector3.Lerp(animated, surface, approach * release * reachable);
            wanted = contactShape.ProjectOutside(wanted, approachPoint, PawRadius);
            if (contactKnown) wanted = contactShape.SweepOutside(previousContact, wanted, approachPoint, PawRadius);
            // IK is evaluated after the authored clip, so the root's wall margin
            // alone cannot keep the extended paw and claws out of the masonry.
            wanted = bear.ConstrainPawCenter(wanted);
            Quaternion rotation = leg.Paw.rotation;
            Vector3 ankle = wanted - leg.Paw.TransformVector(leg.ContactOffset);
            Solve(leg, ankle, rotation);
            StrikePawCenter = leg.Paw.TransformPoint(leg.ContactOffset);
            ContactDistance = contactShape.SurfaceGap(StrikePawCenter, PawRadius);
            ContactFrame = Time.frameCount; ContactVersion++; ContactVictim = fittedVictim;
            previousContact = StrikePawCenter; contactKnown = true;
        }

        private static void Solve(Leg leg, Vector3 target, Quaternion pawRotation)
        {
            Vector3 start = leg.Upper.position;
            Vector3 direction = target - start;
            float distance = Mathf.Clamp(direction.magnitude, .025f, leg.UpperLength + leg.LowerLength - .006f);
            direction.Normalize();
            Vector3 pole = leg.Lower.position - start;
            pole -= direction * Vector3.Dot(pole, direction);
            if (pole.sqrMagnitude < .00001f) pole = Vector3.Cross(direction, leg.Upper.right);
            pole.Normalize();
            float along = (leg.UpperLength * leg.UpperLength - leg.LowerLength * leg.LowerLength + distance * distance) / (2 * distance);
            Vector3 joint = start + direction * along + pole * Mathf.Sqrt(Mathf.Max(0, leg.UpperLength * leg.UpperLength - along * along));
            leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - start, joint - start) * leg.Upper.rotation;
            leg.Lower.rotation = Quaternion.FromToRotation(leg.Paw.position - leg.Lower.position, start + direction * distance - leg.Lower.position) * leg.Lower.rotation;
            leg.Paw.rotation = pawRotation;
        }
    }
}
