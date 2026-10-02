using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>The load keeps rotating visibly in practice; contacts only count during gameplay.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CarryCraneHazard : MonoBehaviour
    {
        private Collider[] contacts;
        private void Awake() => contacts = GetComponentsInChildren<Collider>();
        private void FixedUpdate()
        {
            bool active=MinigameControllerBase.Current is CarryItemMinigame game && game.Phase == MinigamePhase.Round && !game.StartCountdownActive;
            foreach(var contact in contacts)contact.enabled=active;
        }
    }
}
