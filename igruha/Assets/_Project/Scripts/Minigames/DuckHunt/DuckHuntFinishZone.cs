using System;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Крыша башни: утка вошла — финиш зафиксирован.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class DuckHuntFinishZone : MonoBehaviour
    {
        public event Action<DuckProgress> DuckArrived;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            DuckProgress duck = other.GetComponentInParent<DuckProgress>();
            if (duck != null && !duck.Finished)
            {
                DuckArrived?.Invoke(duck);
            }
        }
    }
}
