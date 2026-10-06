using Igruha.Core.Minigame;
using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>The visible load is always solid; its strike is active in practice and the scored round.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CarryCraneHazard : MonoBehaviour
    {
        private Collider[] contacts;
        private void Awake() => contacts = GetComponentsInChildren<Collider>();
        private void FixedUpdate()
        {
            bool active=MinigameControllerBase.Current is CarryItemMinigame game && game.Phase.IsGameplay() && !game.StartCountdownActive;
            foreach(var contact in contacts) contact.enabled = !contact.isTrigger || active;
        }
    }
}
