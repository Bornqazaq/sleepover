using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>The load keeps rotating visibly in practice; contacts only count during gameplay.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CarryCraneHazard : MonoBehaviour
    {
        private Collider contact;
        private void Awake() => contact = GetComponent<Collider>();
        private void Update() => contact.enabled = MinigameControllerBase.Current is CarryItemMinigame game && game.Phase == MinigamePhase.Round && !game.StartCountdownActive;
    }
}
