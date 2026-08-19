using System;
using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Площадка финиша на крыше: Утка вошла — место зафиксировано.
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

        private void OnTriggerEnter(Collider other)
        {
            DuckProgress duck = other.GetComponentInParent<DuckProgress>();
            if (duck != null && !duck.Retired)
            {
                DuckArrived?.Invoke(duck);
            }
        }
    }
}
