using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>The confirmed contact gets a brief two-shot. Pursuit and missed
    /// swipes keep the player's orbit. Only the circus camera's blends are changed.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CircusAttackPresentation : MonoBehaviour
    {
        private const float SideDistance = 5.1f;
        private const float CameraClearance = .18f;
        private const float ObstaclePadding = .08f;
        private const float MinimumShotDistance = 3.4f;
        private const float EnterBlendSeconds = .16f;
        private const float ExitBlendSeconds = .24f;
        private const float FollowSmoothSeconds = .14f;
        private const float SideAngleStep = 30f;
        private const float FrontBias = .18f;
        private const float MaximumFieldOfView = 68f;
        private const float ContactFollowSeconds = 1.05f;
        private const float MaximumShotSeconds = CircusKnockout.PresentationSeconds + .2f;
        private static readonly AnimationCurve BlendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private MinigameCameraController cameras;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private Transform attackView;
        [SerializeField] private PitBear bear;
        [SerializeField] private PitBear secondBear;
        private PitBear firstBear;
        [SerializeField] private TMP_Text cue;
        [SerializeField] private GameObject shelfHud;
        private PlayerController local;
        private CircusKnockout knockout;
        private CinemachineCamera shotCamera;
        private Camera output;
        private Transform restoreTarget;
        private CameraMode restoreMode;
        private int localId = -1, solidMask;
        private bool inPit, shot, shotAttempted, hudWasActive, blendPathClear, occlusionReported;
        private readonly CircusShotGeometry geometry = new CircusShotGeometry();
        private float originalFieldOfView, shotStarted;
        private Vector3 center, centerVelocity;
        private string rejection;
        public bool OwnsCamera => shot;

        private void Awake()
        {
            firstBear = bear;
            solidMask = LayerMask.GetMask("Ground", "Cover", "PlayerBarrier");
            if (attackView != null) shotCamera = attackView.GetComponent<CinemachineCamera>();
            if (shotCamera != null) originalFieldOfView = shotCamera.Lens.FieldOfView;
        }

        private void OnEnable() => CinemachineCore.BlendCreatedEvent.AddListener(ConfigureBlend);

        public void Bind(PlayerController player, int id)
        {
            ResetPresentation();
            local = player; localId = id;
            knockout = player != null ? player.GetComponent<CircusKnockout>() : null;
            output = Camera.main;
            geometry.Bind(bear, player);
        }

        public void BeginPit()
        {
            if (inPit) return;
            inPit = true;
            if (shelfHud != null) { hudWasActive = shelfHud.activeSelf; shelfHud.SetActive(false); }
        }

        private void Update()
        {
            if (!inPit) return;
            if (local == null || bear == null) { ResetPresentation(); return; }
            bool watching = spectator != null && spectator.IsActive;
            bool caught = knockout != null && knockout.IsEliminated;
            if (!caught && !shot && secondBear != null)
            {
                PitBear closest = firstBear;
                if (closest == null || ThreatScore(secondBear) > ThreatScore(closest)) closest = secondBear;
                UseBear(closest);
            }
            if (cue == null) return;
            bool targeted = bear.PresentationTargetId == localId && bear.State == PitBear.BearState.Attack;
            cue.enabled = !watching;
            cue.text = caught ? "БРУНО ПОЙМАЛ ТЕБЯ" : targeted ? "ЗАМАХ — УКЛОНЯЙСЯ!" :
                bear.State == PitBear.BearState.Watching ? "ПРИЗЕМЛИСЬ И ВСТАВАЙ" :
                bear.State == PitBear.BearState.WindUp ? "МЕДВЕДИ ГОТОВЯТСЯ К РЫВКУ" : "ДВА МЕДВЕДЯ — НЕ СТОЙ НА МЕСТЕ!";
            cue.color = caught ? new Color(1, .47f, .3f) : targeted ? new Color(1, .77f, .3f) : new Color(1, .91f, .72f);
        }

        private float ThreatScore(PitBear source)
        {
            if (source == null || local == null) return float.NegativeInfinity;
            float score = -Vector3.SqrMagnitude(source.transform.position - local.Position);
            if (source.PresentationTargetId == localId && source.State == PitBear.BearState.Attack) score += 1000;
            return score;
        }

        public void UseBear(PitBear source)
        {
            if (source == null || source == bear || shot) return;
            bear = source;
            geometry.Bind(bear, local);
        }

        private void BeginShot()
        {
            if (cameras == null || attackView == null || shotCamera == null) return;
            geometry.RefreshBounds();
            Vector3 bearPoint = geometry.BearBounds.center, playerPoint = geometry.PlayerBounds.center;
            centerVelocity = Vector3.zero;
            Vector3 forward = local.transform.position - bear.PresentationRoot.position; forward.y = 0;
            if (forward.sqrMagnitude < .01f) forward = bear.PresentationRoot.forward;
            forward.Normalize();
            geometry.CaptureContactEnvelope(forward * 2f, bear.PresentationRoot.position.y, solidMask);
            center = geometry.ContactBounds.center;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 oldPosition = output != null ? output.transform.position : center + side * SideDistance;
            Vector3 oldDirection = oldPosition - center; oldDirection.y = 0; oldDirection.Normalize();
            float bestScore = float.NegativeInfinity;
            float bestFieldOfView = originalFieldOfView;
            Vector3 bestPosition = Vector3.zero;
            // Choose once. Geometry and continuity with the existing view decide
            // the shoulder; the shot never orbits to the other side mid-impact.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int angle = -3; angle <= 3; angle++)
                {
                    Vector3 horizontal = Quaternion.AngleAxis(angle * SideAngleStep, Vector3.up) * (side * sign);
                    horizontal = (horizontal + forward * FrontBias).normalized;
                    for (int elevation = 0; elevation < 4; elevation++)
                    {
                        Vector3 direction = (horizontal + Vector3.up * (.04f + elevation * .14f)).normalized;
                        float required = geometry.ContactDistance(center, direction, originalFieldOfView, Aspect);
                        float wantedDistance = Mathf.Max(SideDistance, required);
                        Vector3 candidate = ClearPosition(center, center + direction * wantedDistance);
                        float distance = Vector3.Distance(candidate, center);
                        float fieldOfView = Mathf.Max(originalFieldOfView, geometry.RequiredFieldOfView(candidate, center, Aspect, true));
                        if (distance < MinimumShotDistance || fieldOfView > MaximumFieldOfView)
                        {
                            Trace("Reject frame s=" + sign + " a=" + angle + " h=" + elevation +
                                "; available=" + distance.ToString("F2") + "; needed=" + required.ToString("F2") +
                                "; focus=" + center.ToString("F2") + "; candidate=" + candidate.ToString("F2"));
                            continue;
                        }
                        if (!ClearView(candidate))
                        {
                            Trace("Reject occlusion s=" + sign + " a=" + angle + " h=" + elevation +
                                "; " + rejection + "; candidate=" + candidate.ToString("F2"));
                            continue;
                        }
                        float score = Vector3.Dot(horizontal, oldDirection) * .25f - Mathf.Abs(distance - SideDistance) * .18f;
                        // Prefer separated silhouettes. End-on views are reserved
                        // for a player pinned against the curved pit boundary.
                        score += Mathf.Abs(Vector3.Dot(playerPoint - bearPoint, Vector3.Cross(Vector3.up, horizontal))) * .6f;
                        if (geometry.ClearAnticipatedView(candidate, bear.PresentationRoot.position.y, solidMask)) score += 4;
                        score -= (fieldOfView - originalFieldOfView) * .035f;
                        if (Mathf.Abs(angle) == 3) score -= 2;
                        // A slightly lower view sees under a hanging hatch; raise
                        // it only when the lower angle has less clear geometry.
                        score -= elevation * .07f;
                        if (score <= bestScore) continue;
                        bestScore = score; bestPosition = candidate; bestFieldOfView = fieldOfView;
                    }

                }
            }
            if (float.IsNegativeInfinity(bestScore))
            {
                Trace("No clear contact view; player=" + geometry.PlayerBounds + "; bear=" + geometry.BearBounds);
                return;
            }

            restoreMode = cameras.CurrentMode;
            restoreTarget = cameras.CurrentTarget;
            shotCamera.Lens.FieldOfView = bestFieldOfView;
            attackView.SetPositionAndRotation(bestPosition, Quaternion.LookRotation(center - bestPosition));
            blendPathClear = ClearSegment(oldPosition, bestPosition, CameraClearance) &&
                geometry.ClearActorSegment(oldPosition, bestPosition, CameraClearance);
            shot = true; shotStarted = Time.time; occlusionReported = false;
            cameras.Apply(CameraMode.TopDown, local.CameraTarget);
            // Contact is resolved after IK in LateUpdate(190). Refresh this one
            // transition now so its impact cut reaches the same rendered frame.
            if (output != null && output.TryGetComponent<CinemachineBrain>(out var brain) && brain.isActiveAndEnabled)
                brain.ManualUpdate();
            Trace("Contact view; target=" + localId + "; blend=" + blendPathClear + "; camera=" + bestPosition.ToString("F2") +
                "; focus=" + center.ToString("F2") + "; player=" + geometry.PlayerBounds + "; bear=" + geometry.BearBounds);
        }

        private void LateUpdate()
        {
            // BodyHidden and spectator handoff run at 150. Release in this same
            // LateUpdate, without returning to the fallen body's orbit in between.
            bool watching = spectator != null && spectator.IsActive;
            if (shot && (watching || knockout == null || !knockout.IsPresenting ||
                Time.time - shotStarted >= MaximumShotSeconds))
                EndShot(watching);
            // A telegraph, miss or another player's hit never takes control.
            if (inPit && !shotAttempted && local != null && bear != null && knockout != null && knockout.IsPresenting &&
                !watching)
            {
                shotAttempted = true;
                BeginShot();
            }
            if (!shot || local == null || bear == null || attackView == null || Time.deltaTime <= 0) return;
            // Once the swipe has recovered, retain its final composition until
            // the body disappears. A new pursuit must not pull focus off the fall.
            if (Time.time - shotStarted >= ContactFollowSeconds) return;
            geometry.RefreshBounds();
            center = Vector3.SmoothDamp(center, geometry.CombinedBounds.center, ref centerVelocity, FollowSmoothSeconds);
            // Keep the chosen world point. Following the flying player moved the
            // lens behind a hatch/rim; only aim and lens framing may now change.
            float needed = geometry.RequiredFieldOfView(attackView.position, center, Aspect, false);
            shotCamera.Lens.FieldOfView = Mathf.Max(shotCamera.Lens.FieldOfView, Mathf.Min(MaximumFieldOfView, needed));
            attackView.rotation = Quaternion.LookRotation(center - attackView.position);
            if (!occlusionReported && !ClearView(attackView.position))
            {
                occlusionReported = true;
                Trace("Contact point visibility changed; age=" + (Time.time - shotStarted).ToString("F2") + "; " + rejection);
            }
        }

        private float Aspect => output != null ? output.aspect : 16f / 9f;

        private Vector3 ClearPosition(Vector3 focus, Vector3 desired)
        {
            Vector3 delta = desired - focus;
            float distance = delta.magnitude;
            // A high diagonal ray can clear the pit wall and put the lens outside
            // the arena. Its ground projection must stay on the same side too.
            float floor = bear.PresentationRoot.position.y + .45f;
            Vector3 groundFrom = new Vector3(focus.x, floor, focus.z);
            Vector3 groundTo = new Vector3(desired.x, floor, desired.z);
            Vector3 groundDelta = groundTo - groundFrom;
            float groundDistance = groundDelta.magnitude;
            if (groundDistance > .001f && Physics.SphereCast(groundFrom, CameraClearance, groundDelta / groundDistance,
                out RaycastHit groundHit, groundDistance, solidMask, QueryTriggerInteraction.Ignore))
            {
                desired = focus + delta * Mathf.Clamp01((groundHit.distance - ObstaclePadding) / groundDistance);
                delta = desired - focus; distance = delta.magnitude;
            }
            if (distance > .001f && Physics.SphereCast(focus, CameraClearance, delta / distance,
                out RaycastHit hit, distance, solidMask, QueryTriggerInteraction.Ignore))
                return focus + delta / distance * Mathf.Max(0, hit.distance - ObstaclePadding);
            return desired;
        }

        private bool ClearView(Vector3 position)
        {
            rejection = null;
            if (Physics.CheckSphere(position, CameraClearance, solidMask, QueryTriggerInteraction.Ignore))
            { rejection = "lens intersects solid collider"; return false; }
            if (!geometry.ClearActorViews(position, bear.PresentationRoot.position.y, solidMask))
            { rejection = "actor silhouette: " + geometry.LastObstruction; return false; }
            return true;
        }

        private bool ClearSegment(Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            return distance < .001f || !Physics.SphereCast(from, radius, delta / distance,
                out _, distance, solidMask, QueryTriggerInteraction.Ignore) && geometry.ClearVisualSegment(from, to, radius);
        }

        private void ConfigureBlend(CinemachineCore.BlendEventParams args)
        {
            if (shotCamera == null || args.Blend == null || !args.Blend.Uses(shotCamera)) return;
            bool entering = ReferenceEquals(args.Blend.CamB, shotCamera);
            bool clear = entering ? blendPathClear : args.Blend.CamA != null && args.Blend.CamB != null &&
                ClearSegment(args.Blend.CamA.State.GetFinalPosition(), args.Blend.CamB.State.GetFinalPosition(), CameraClearance) &&
                geometry.ClearActorSegment(args.Blend.CamA.State.GetFinalPosition(), args.Blend.CamB.State.GetFinalPosition(), CameraClearance);
            args.Blend.Duration = clear ? (entering ? EnterBlendSeconds : ExitBlendSeconds) : 0;
            args.Blend.BlendCurve = BlendCurve;
        }

        private void EndShot(bool watching)
        {
            shot = false;
            Trace("Return from contact view; spectator=" + watching + "; duration=" + (Time.time - shotStarted).ToString("F2"));
            // A newly activated spectator owns its own target and must not be reset
            // to the eliminated local body by this presentation's cleanup.
            bool spectatorHasTarget = watching && spectator != null && spectator.Target != null;
            if (!spectatorHasTarget && cameras != null && cameras.CurrentMode == CameraMode.TopDown)
                cameras.Apply(restoreMode, restoreTarget != null ? restoreTarget : local != null ? local.CameraTarget : null);
            restoreTarget = null;
        }

        public void ResetPresentation()
        {
            if (shot) EndShot(spectator != null && spectator.IsActive);
            // Spectator.Deactivate restores the camera which preceded spectating.
            // That may have been this shot; it must not survive into the next round.
            if (inPit && cameras != null && local != null && (spectator == null || !spectator.IsActive))
                cameras.Apply(CameraMode.ThirdPerson, local.CameraTarget);
            if (inPit && shelfHud != null) shelfHud.SetActive(hudWasActive);
            inPit = false; shotAttempted = false;
            if (shotCamera != null && originalFieldOfView > 0) shotCamera.Lens.FieldOfView = originalFieldOfView;
            if (cue != null) cue.enabled = false;
        }

        private void OnDisable()
        {
            ResetPresentation();
            CinemachineCore.BlendCreatedEvent.RemoveListener(ConfigureBlend);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusView " + Time.time.ToString("F2") + "] " + message, this);
    }
}
