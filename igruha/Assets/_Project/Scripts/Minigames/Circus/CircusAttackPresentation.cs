using TMPro;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;

namespace Igruha.Minigames.Circus
{
    /// <summary>A short arena shot makes the committed swipe readable even when
    /// the ordinary orbit is pressed against the pit wall. Uses the minigame
    /// camera switch; never edits the shared character rig or input bindings.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class CircusAttackPresentation : MonoBehaviour
    {
        private const float SideDistance = 4.4f;
        private const float ForwardOffset = .65f;
        private const float CameraRadius = 8f;
        private const float CameraHeight = 3.8f;
        [SerializeField] private MinigameCameraController cameras;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private Transform attackView;
        [SerializeField] private PitBear bear;
        [SerializeField] private TMP_Text cue;
        [SerializeField] private GameObject shelfHud;
        private PlayerController local;
        private CircusKnockout knockout;
        private int localId = -1;
        private bool inPit, shot, hudWasActive;
        private Vector3 shotOffset;
        public bool OwnsCamera => shot;

        public void Bind(PlayerController player, int id)
        {
            ResetPresentation();
            local = player; localId = id;
            knockout = player != null ? player.GetComponent<CircusKnockout>() : null;
        }

        public void BeginPit()
        {
            if (inPit) return;
            inPit = true;
            if (shelfHud != null) { hudWasActive = shelfHud.activeSelf; shelfHud.SetActive(false); }
        }

        private void Update()
        {
            if (!inPit || local == null || bear == null) return;
            bool watching = spectator != null && spectator.IsActive;
            bool caught = knockout != null && knockout.IsEliminated;
            bool targeted = bear.PresentationTargetId == localId && bear.State == PitBear.BearState.Attack;
            if (!watching && (targeted || (caught && knockout.IsPresenting)))
            {
                if (!shot) BeginShot();
                PositionShot();
            }
            else if (shot) EndShot(watching);

            if (cue == null) return;
            cue.enabled = !watching;
            cue.text = caught ? "БРУНО ПОЙМАЛ ТЕБЯ" : targeted ? "ЗАМАХ — УКЛОНЯЙСЯ!" :
                bear.State == PitBear.BearState.Watching ? "ПРИЗЕМЛИСЬ И ВСТАВАЙ" :
                bear.State == PitBear.BearState.WindUp ? "ЕСТЬ ФОРА — БЕГИ!" : "БРУНО РЯДОМ — БЕГИ!";
            cue.color = caught ? new Color(1, .47f, .3f) : targeted ? new Color(1, .77f, .3f) : new Color(1, .91f, .72f);
        }

        private void BeginShot()
        {
            Vector3 forward = local.transform.position - bear.transform.position; forward.y = 0;
            if (forward.sqrMagnitude < .01f) forward = bear.transform.forward;
            forward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 center = (local.transform.position + bear.transform.position) * .5f;
            Vector3 a = side * SideDistance + forward * ForwardOffset;
            Vector3 b = -side * SideDistance + forward * ForwardOffset;
            shotOffset = (center + a).sqrMagnitude < (center + b).sqrMagnitude ? a : b;
            shot = true;
            PositionShot();
            cameras?.Apply(CameraMode.TopDown, local.transform);
            Trace("Attack view; target=" + localId);
        }

        private void PositionShot()
        {
            if (attackView == null) return;
            Vector3 center = (local.transform.position + bear.transform.position) * .5f;
            Vector3 desired = center + shotOffset;
            Vector2 radial = Vector2.ClampMagnitude(new Vector2(desired.x, desired.z), CameraRadius);
            attackView.position = new Vector3(radial.x, bear.transform.position.y + CameraHeight, radial.y);
            attackView.rotation = Quaternion.LookRotation(center + Vector3.up * 1.1f - attackView.position);
        }

        private void EndShot(bool watching)
        {
            shot = false;
            Trace("Return from attack view; spectator=" + watching);
            if (!watching && cameras != null && local != null)
                cameras.Apply(CameraMode.ThirdPerson, local.transform);
        }

        public void ResetPresentation()
        {
            if (shot) EndShot(spectator != null && spectator.IsActive);
            if (inPit && cameras != null && local != null && (spectator == null || !spectator.IsActive))
                cameras.Apply(CameraMode.ThirdPerson, local.transform);
            if (inPit && shelfHud != null) shelfHud.SetActive(hudWasActive);
            inPit = false;
            if (cue != null) cue.enabled = false;
        }

        private void OnDisable() => ResetPresentation();

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void Trace(string message) => Debug.Log("[CircusView " + Time.time.ToString("F2") + "] " + message, this);
    }
}
