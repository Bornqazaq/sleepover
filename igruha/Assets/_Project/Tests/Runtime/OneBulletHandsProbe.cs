using System.Collections;
using Igruha.Core.CameraSystems;
using Igruha.Core.Session;
using Igruha.Minigames.OneBullet;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Tests
{
    /// <summary>Used only by --onebullet-check hands: real punches, movement, jump and rendered captures.</summary>
    public sealed class OneBulletHandsProbe : MonoBehaviour
    {
        private OneBulletMinigame game;
        private OneBulletFirstPerson view;
        private FirstPersonCameraRig rig;
        private bool punched, knocked, jumped, walking, guarded, held, aimed, fired;
        private bool moved, airborne;
        private string folder;
        private bool capture;
        private Igruha.Core.Player.PlayerController motor;
        public void Initialize(OneBulletMinigame owner, OneBulletFirstPerson presentation, FirstPersonCameraRig cameraRig)
        {
            game = owner; view = presentation; rig = cameraRig;
            LaunchArguments.TryGetValue("--onebullet-screenshots", out folder);
            capture = !string.IsNullOrEmpty(folder) && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (capture) System.IO.Directory.CreateDirectory(folder);
            game.Shot += OnShot;
        }
        public bool Step(double t, OneBulletParticipant local)
        {
            int id = game.LocalId;
            if (motor == null) { motor = local.Motor; motor.KnockdownStarted += OnKnockdown; }
            if (local.Motor.IsKnockedDown) knocked = true;
            if (t > 1 && t < 3 && local.Motor.NormalizedSpeed > .1f) moved = true;
            if (t > 6.5 && t < 8 && !local.Motor.IsGrounded) airborne = true;
            if (t < 11)
            {
                local.Motor.SetFacing(id == 0 ? 0 : 180);
                if (t < 1 || (t > 4 && t < 4.8) || (t > 8 && t < 8.8))
                {
                    local.Motor.TeleportTo(new Vector3(-19.2f, .1f, -19.2f + id * .8f), Quaternion.Euler(0, id == 0 ? 0 : 180, 0));
                    rig.SetView(id == 0 ? 0 : 180, 0);
                }
                if (t > 1 && t < 3) local.Input.DriveMove(Vector2.right * .5f);
                if (t > 2 && !walking)
                { walking = true; StartCoroutine(Sample("walk", 0)); }
                if (t > 4.8 && !guarded)
                { guarded = true; StartCoroutine(Sample("guard", 0)); }
                if (t > (id == 0 ? 5 : 9) && !punched)
                {
                    punched = true; local.Push.RequestPush();
                    foreach (var target in game.Participants)
                        if (target != local)
                            Debug.Log("ONE_BULLET HANDS CONTACT distance=" + Vector3.Distance(local.Motor.Position, target.Motor.Position) +
                                " angle=" + Vector3.Angle(local.Motor.transform.forward, target.Motor.Position - local.Motor.Position) +
                                " immune=" + target.Motor.ImpulseImmune);
                    StartCoroutine(Sample("punch", local.Motor.Config.PunchImpactDelay));
                    StartCoroutine(Sample("impact", local.Motor.Config.PunchImpactDelay + .2f));
                }
                if (t > 6.5 && id == 0 && !jumped)
                { jumped = true; local.Input.DriveJump(); StartCoroutine(Sample("jump", .15f)); }
                return true;
            }
            if (game.LocalArmed && !held)
            { held = true; StartCoroutine(Sample("grip", .3f)); }
            if (view.IsAiming && view.AimBlend > .99f && !aimed)
            { aimed = true; StartCoroutine(Sample("aim", 0)); }
            return false;
        }
        private void OnKnockdown(Igruha.Core.Player.KnockdownType type)
        {
            if (Igruha.Core.Minigame.NetworkClock.Now - game.Round.BeginsAt < 11) knocked = true;
        }
        private void OnShot(Vector3 origin, Vector3 end, bool hit)
        {
            if (!view.WeaponVisible) return;
            fired = true; StartCoroutine(Sample("recoil", .025f));
        }
        private IEnumerator Sample(string label, float delay)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            yield return null;
            if (!view.HandsVisible) Debug.LogError("ONE_BULLET HANDS FAIL invisible " + label);
            Debug.Log("ONE_BULLET HANDS " + label + " visible=" + view.HandsVisible);
            if (capture) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, game.LocalId + "-" + label + ".png"));
        }
        public void Report()
        {
            bool valid = punched && knocked && moved && walking && guarded && held && fired && (game.LocalId != 0 || (jumped && airborne && aimed));
            Debug.Log("ONE_BULLET HANDS FINAL valid=" + valid + " punched=" + punched + " knocked=" + knocked + " moved=" + moved + " airborne=" + airborne + " aimed=" + aimed);
            if (!valid) Debug.LogError("ONE_BULLET HANDS FAIL incomplete actions");
            if (view.HandsVisible) Debug.LogError("ONE_BULLET HANDS FAIL visible in results");
        }
        private void OnDestroy()
        {
            if (game != null) game.Shot -= OnShot;
            if (motor != null) motor.KnockdownStarted -= OnKnockdown;
        }
    }
}
