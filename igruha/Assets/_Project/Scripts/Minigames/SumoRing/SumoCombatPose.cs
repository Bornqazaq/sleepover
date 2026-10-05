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
        public void Bind(SumoFighter owner, SumoMotionLibrary motions)
        {
            fighter = owner; library = motions; animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || motions == null) { enabled = false; return; }
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot); rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
            filteredMuscles = new float[HumanTrait.MuscleCount];
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
            bool show = true;
            switch (state.Phase)
            {
                case SumoCombatPhase.Guard: motion = SumoMotion.Guard; sample = Mathf.Repeat((float)(now - state.Since), 1); break;
                case SumoCombatPhase.Charge: motion = SumoMotion.Charge; sample = Mathf.Clamp01((float)(now - state.Since) / fighter.Config.ChargeSeconds); break;
                case SumoCombatPhase.Windup:
                    motion = AttackMotion(state.Attack); sample = Mathf.Clamp01((float)((now - state.Since) / (state.Until - state.Since))) * .6f; break;
                case SumoCombatPhase.Recovery:
                    motion = AttackMotion(state.Attack); sample = .6f + .4f * Mathf.Clamp01((float)(now - state.Since) / RecoveryPoseSeconds); break;
                case SumoCombatPhase.Stagger: motion = SumoMotion.Recoil; sample = Mathf.Clamp01((float)((now - state.Since) / (state.Until - state.Since))); break;
                default: show = false; break;
            }
            float contactAge = (float)(NetworkClock.Now - fighter.ContactAt);
            if (fighter.ContactAsTarget && contactAge >= 0 && contactAge < ContactPoseSeconds && state.Phase != SumoCombatPhase.Windup && state.Phase != SumoCombatPhase.Recovery)
            {
                if (fighter.LastContact == SumoContact.Parry) { motion = SumoMotion.Parry; sample = contactAge / ContactPoseSeconds; show = true; }
                else if (fighter.LastContact == SumoContact.Block) { motion = SumoMotion.Brace; sample = contactAge / ContactPoseSeconds; show = true; }
            }
            weight = Mathf.MoveTowards(weight, show ? 1 : 0, Time.deltaTime / BlendSeconds);
            if (weight <= 0 || !animator.isInitialized) { hasFilteredPose = false; return; }
            var data = library.Get(motion); if (data == null || data.Tracks == null) return;
            handler.GetHumanPose(ref pose);
            if (!hasFilteredPose)
            {
                System.Array.Copy(pose.muscles, filteredMuscles, filteredMuscles.Length);
                hasFilteredPose = true;
            }
            float smoothing = 1 - Mathf.Exp(-Time.deltaTime / MuscleSmoothingSeconds);
            float legs = fighter.Motor.IsCrouched || !fighter.IsGrounded ? 0 : Mathf.Clamp01(1 - fighter.PlanarSpeed / 2.5f);
            float footHeight = leftFoot != null && rightFoot != null ? Mathf.Min(leftFoot.position.y, rightFoot.position.y) : 0;
            foreach (var track in data.Tracks)
            {
                float w = weight * (track.Leg ? legs : 1);
                int muscle = track.Muscle;
                filteredMuscles[muscle] = Mathf.Lerp(filteredMuscles[muscle], track.Curve.Evaluate(sample), smoothing);
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
