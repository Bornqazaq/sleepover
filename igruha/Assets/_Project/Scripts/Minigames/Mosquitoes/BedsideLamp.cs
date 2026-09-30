using Igruha.Core.Interaction;
using Igruha.Core.Player;
using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    public sealed class BedsideLamp : MonoBehaviour, IInteractable
    {
        [SerializeField] private MosquitoesMinigame game;
        [SerializeField] private bool lampOnly;
        public string InteractionPrompt => lampOnly ? "E — лампа / проснуться" : "E — лечь / встать";
        public bool CanInteract(PlayerController player) => game != null && game.IsGiant(player) && game.ControlsAvailable &&
            (game.Sleep.Phase == GiantPhase.Awake || game.Sleep.Phase == GiantPhase.Sleeping);
        public void Interact(PlayerController player)
        {
            if (!CanInteract(player)) return;
            if (lampOnly) game.TryLamp(game.GiantId); else game.TryBed(game.GiantId);
        }
    }
}
