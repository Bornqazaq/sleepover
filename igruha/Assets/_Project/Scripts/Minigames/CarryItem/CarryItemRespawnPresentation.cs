using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local presentation of the server's respawn deadline. Keeps the
    /// fallen body hidden and the camera on the game until the teleport arrives.</summary>
    [DefaultExecutionOrder(-20)]
    public sealed class CarryItemRespawnPresentation : MonoBehaviour
    {
        private sealed class AvatarView
        {
            public SessionPlayer Player;
            public PlayerController Avatar;
            public Renderer[] Renderers;
            public bool[] WasHidden;
            public bool Hidden;

            public void SetHidden(bool hidden)
            {
                if (Hidden == hidden) return;
                for (int i = 0; i < Renderers.Length; i++)
                {
                    var renderer = Renderers[i];
                    if (renderer == null) continue;
                    if (hidden) WasHidden[i] = renderer.forceRenderingOff;
                    renderer.forceRenderingOff = hidden || WasHidden[i];
                }
                Hidden = hidden;
            }
        }

        [SerializeField] private MinigameCameraController cameras;
        [SerializeField] private SpectatorCamera spectator;
        [SerializeField] private Transform arenaView;
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text secondsText;

        private CarryItemMinigame game;
        private SessionPlayer local;
        private PlayerInputReader localInput;
        private bool inputWasEnabled;
        private readonly List<AvatarView> avatars = new List<AvatarView>(8);
        private readonly List<SessionPlayer> targets = new List<SessionPlayer>(8);
        private int shownSeconds = -1;
        public bool IsWaiting { get; private set; }
        public int SecondsLeft => shownSeconds;
        public bool HasLiveTarget => IsWaiting && spectator != null && spectator.IsActive && spectator.Target != null;

        private void Awake()
        {
            game = GetComponent<CarryItemMinigame>();
            if (panel != null) panel.SetActive(false);
        }

        public void Bind(IReadOnlyList<SessionPlayer> players)
        {
            local = SessionScoreboard.Current?.LocalPlayer ?? (players.Count > 0 ? players[0] : null);
            if (local?.Avatar != null) local.Avatar.TryGetComponent(out localInput);
            for (int i = avatars.Count - 1; i >= 0; i--)
            {
                var view = avatars[i];
                bool found = false;
                for (int j = 0; j < players.Count; j++)
                    if (players[j] == view.Player && players[j].Avatar == view.Avatar) { found = true; break; }
                if (found) continue;
                view.SetHidden(false);
                avatars.RemoveAt(i);
            }
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player.Avatar == null) continue;
                bool found = false;
                for (int j = 0; j < avatars.Count; j++)
                    if (avatars[j].Player == player) { found = true; break; }
                if (found) continue;
                var renderers = player.Avatar.GetComponentsInChildren<Renderer>(true);
                avatars.Add(new AvatarView { Player = player, Avatar = player.Avatar,
                    Renderers = renderers, WasHidden = new bool[renderers.Length] });
            }
        }

        private void Update()
        {
            if (game == null || !game.GameplayActive)
            {
                ResetPresentation();
                return;
            }
            targets.Clear();
            bool waiting = false;
            double localDeadline = 0;
            for (int i = 0; i < avatars.Count; i++)
            {
                var view = avatars[i];
                double deadline = 0;
                bool present = view.Avatar != null && game.TryGetRespawnDeadline(view.Player.Id, out deadline);
                if (!present) { view.SetHidden(false); continue; }
                // The clear-deadline packet and the owner's teleport can arrive
                // in either order. Never show the old body or return its camera.
                bool hidden = deadline > 0 || (view.Hidden && view.Avatar.Position.y < game.VoidLevel);
                view.SetHidden(hidden);
                if (view.Player == local) { waiting = hidden; localDeadline = deadline; }
                else if (!hidden && view.Avatar.Position.y >= game.VoidLevel && view.Avatar.gameObject.activeInHierarchy)
                    targets.Add(view.Player);
            }
            if (!waiting)
            {
                EndWaiting();
                return;
            }
            if (!IsWaiting && localInput != null) inputWasEnabled = localInput.enabled;
            IsWaiting = true;
            if (panel != null && !panel.activeSelf) panel.SetActive(true);
            if (targets.Count > 0 && spectator != null)
            {
                if (spectator.IsActive && !targets.Contains(spectator.Target)) spectator.Deactivate();
                if (!spectator.IsActive) spectator.Activate(targets);
            }
            else
            {
                spectator?.Deactivate();
                if (cameras != null && arenaView != null && cameras.CurrentTarget != arenaView)
                    cameras.Apply(CameraMode.ThirdPerson, arenaView);
            }
            if (localInput != null) localInput.enabled = false;
            int seconds = localDeadline > 0 ? Mathf.Max(0, Mathf.CeilToInt((float)(localDeadline - NetworkClock.Now))) : 0;
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds;
                if (secondsText != null)
                {
                    if (seconds > 0) secondsText.SetText("{0} С", seconds);
                    else secondsText.text = "ВОЗВРАЩАЕМСЯ…";
                }
            }
        }

        private void EndWaiting()
        {
            if (!IsWaiting) return;
            IsWaiting = false;
            shownSeconds = -1;
            if (panel != null) panel.SetActive(false);
            spectator?.Deactivate();
            if (localInput != null) localInput.enabled = inputWasEnabled;
            if (cameras != null && local?.Avatar != null)
                cameras.Apply(CameraMode.ThirdPerson, local.Avatar.CameraTarget);
        }

        public void ResetPresentation()
        {
            EndWaiting();
            for (int i = 0; i < avatars.Count; i++) avatars[i].SetHidden(false);
            targets.Clear();
        }

        private void OnDisable() => ResetPresentation();
    }
}
