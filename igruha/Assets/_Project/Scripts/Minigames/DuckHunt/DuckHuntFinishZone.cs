using System;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Приподнятая площадка финиша пятого этажа: Утка вошла — место зафиксировано.
    ///
    /// Зона только сообщает о входе. Засчитывает прибытие мини-игра: порядок
    /// финиша решает места, а значит считаться он обязан в одном месте и под
    /// авторитетом сервера, а не в триггере, который срабатывает на каждой машине.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class DuckHuntFinishZone : MonoBehaviour
    {
        /// <summary>Утка вошла в зону финиша.</summary>
        public event Action<DuckProgress> DuckArrived;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => CheckArrival(other);

        private void OnTriggerStay(Collider other) => CheckArrival(other);

        private void CheckArrival(Collider other)
        {
            DuckProgress duck = other.GetComponentInParent<DuckProgress>();
            // A capsule jumping UNDER the raised finish must not win by touching
            // the trigger with its head. Stay handles legitimate landings after entry.
            if (duck != null && !duck.Retired && duck.transform.position.y >= transform.position.y - .15f)
            {
                DuckArrived?.Invoke(duck);
            }
        }
    }
}
