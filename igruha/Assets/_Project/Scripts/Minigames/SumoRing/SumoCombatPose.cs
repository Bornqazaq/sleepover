using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    /// <summary>Humanoid overlay before shared arm clearance and foot grounding, on every peer.</summary>
    [DefaultExecutionOrder(-10)]
    public sealed class SumoCombatPose : MonoBehaviour
    {
        private const float BlendSeconds = .085f, ContactPoseSeconds = .3f, RecoveryPoseSeconds = .3f;
        private const float MuscleSmoothingSeconds = .04f;
        private const float BalanceLoopSeconds = 1.15f, RecoveryStepTail = .12f;
        private SumoFighter fighter;
        private SumoMotionLibrary library;
        private Animator animator;
        private Transform leftFoot, rightFoot, hips;
        private HumanPoseHandler handler;
        private HumanPose pose;
        private SumoMotion motion;
        private float sample, weight;
        private AnimatorCullingMode originalCulling;
        private bool changedCulling, hasFilteredPose;
        private float[] filteredMuscles;
        private int spineFrontBack, spineLeftRight;
        public void Bind(SumoFighter owner, SumoMotionLibrary motions)
        {
            fighter = owner; library = motions; animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || motions == null) { enabled = false; return; }
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot); rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
            filteredMuscles = new float[HumanTrait.MuscleCount];
            spineFrontBack = System.Array.IndexOf(HumanTrait.MuscleName, "Spine Front-Back");
            spineLeftRight = System.Array.IndexOf(HumanTrait.MuscleName, "Spine Left-Right");
            originalCulling = animator.cullingMode; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; changedCulling = true;
        }
        private void OnDestroy()
        {
            handler?.Dispose();
            if (animator != null && changedCulling) animator.cullingMode = originalCulling;
        }
        private void LateUpdate()
        {
            if (fighter == null || handler == null || fighter.Participant.Dead || fighter.Motor.IsKnockedDown) { weight = 0; hasFilteredPose = false; return; }
            var state = fighter.VisualState;
            double now = fighter.VisualNow;
            float targetWeight = 1;
            switch (state.Phase)
            {
                case SumoCombatPhase.Guard: motion = SumoMotion.Guard; sample = Mathf.Repeat((float)(now - state.Since), 1); break;
                case SumoCombatPhase.Charge: motion = SumoMotion.Charge; sample = Mathf.Clamp01((float)(now - state.Since) / fighter.Config.ChargeSeconds); break;
                case SumoCombatPhase.Windup:
                    motion = AttackMotion(state.Attack); sample = Mathf.Clamp01((float)((now - state.Since) / (state.Until - state.Since))) * .6f; break;
                case SumoCombatPhase.Recovery:
                    motion = AttackMotion(state.Attack); sample = .6f + .4f * Mathf.Clamp01((float)(now - state.Since) / RecoveryPoseSeconds); break;
                case SumoCombatPhase.Stagger: motion = SumoMotion.Recoil; sample = Mathf.Clamp01((float)((now - state.Since) / (state.Until - state.Since))); break;
                default:
                    targetWeight = fighter.BalanceWeight;
                    if (targetWeight > 0) { motion = SumoMotion.Balance; sample = Mathf.Repeat((float)(now / BalanceLoopSeconds), 1); }
                    break;
            }
            float contactAge = (float)(NetworkClock.Now - fighter.ContactAt);
            bool reacting = false;
            if (fighter.ContactAsTarget && contactAge >= 0 && state.Phase != SumoCombatPhase.Windup && state.Phase != SumoCombatPhase.Recovery)
            {
                if (fighter.LastContact == SumoContact.Parry && contactAge < ContactPoseSeconds)
                { motion = SumoMotion.Parry; sample = contactAge / ContactPoseSeconds; targetWeight = 1; }
                else if (fighter.LastContact == SumoContact.Block && contactAge < ContactPoseSeconds)
                { motion = SumoMotion.Brace; sample = contactAge / ContactPoseSeconds; targetWeight = 1; }
                else if ((fighter.LastContact == SumoContact.Push || fighter.LastContact == SumoContact.GuardBreak || fighter.LastContact == SumoContact.Counter)
                    && state.Phase != SumoCombatPhase.Guard && state.Phase != SumoCombatPhase.Charge && contactAge < fighter.ContactSlide + RecoveryStepTail)
                {
                    reacting = true;
                    motion = fighter.ContactAttack == SumoAttack.Quick ? SumoMotion.Recoil : SumoMotion.Stumble;
                    sample = contactAge / (fighter.ContactSlide + RecoveryStepTail); targetWeight = 1;
                }
            }
            weight = Mathf.MoveTowards(weight, targetWeight, Time.deltaTime / BlendSeconds);
            if (weight <= 0 || !animator.isInitialized) { hasFilteredPose = false; return; }
            var data = library.Get(motion); if (data == null || data.Tracks == null) return;
            handler.GetHumanPose(ref pose);
            if (!hasFilteredPose)
            {
                System.Array.Copy(pose.muscles, filteredMuscles, filteredMuscles.Length);
                hasFilteredPose = true;
            }
            float smoothing = 1 - Mathf.Exp(-Time.deltaTime / MuscleSmoothingSeconds);
            float legs = fighter.Motor.IsCrouched || !fighter.IsGrounded ? 0 : reacting ? .65f : Mathf.Clamp01(1 - fighter.PlanarSpeed / 2.5f);
            float footHeight = leftFoot != null && rightFoot != null ? Mathf.Min(leftFoot.position.y, rightFoot.position.y) : 0;
            Vector3 direction = animator.transform.InverseTransformDirection(reacting ? fighter.ContactDirection : fighter.EdgeDirection);
            foreach (var track in data.Tracks)
            {
                float w = weight * (track.Leg ? legs : 1);
                int muscle = track.Muscle;
                float target = track.Curve.Evaluate(sample);
                if (reacting)
                {
                    // Lean in the actual shove direction, including hits from the side/back.
                    float beat = Mathf.Sin(Mathf.Clamp01(sample) * Mathf.PI);
                    if (muscle == spineFrontBack) target = .14f + (target - .14f) * -direction.z;
                    if (muscle == spineLeftRight) target += direction.x * .22f * beat;
                }
                else if (motion == SumoMotion.Balance)
                {
                    if (muscle == spineFrontBack) target -= direction.z * .14f;
                    if (muscle == spineLeftRight) target -= direction.x * .14f;
                }
                filteredMuscles[muscle] = Mathf.Lerp(filteredMuscles[muscle], target, smoothing);
                pose.muscles[muscle] = Mathf.Lerp(pose.muscles[muscle], filteredMuscles[muscle], w);
            }
            handler.SetHumanPose(ref pose);
            // Retargeted bent knees must not lift short avatars off the floor. Preserve the
            // live animator's lowest foot; shared skin grounding still runs after this overlay.
            if (legs > 0 && leftFoot != null && rightFoot != null && hips != null)
            {
                float change = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - footHeight;
                hips.position -= Vector3.up * change;
            }
        }
        private static SumoMotion AttackMotion(SumoAttack attack) => attack == SumoAttack.Heavy ? SumoMotion.Heavy : attack == SumoAttack.Counter ? SumoMotion.Counter : SumoMotion.Quick;
    }
}
