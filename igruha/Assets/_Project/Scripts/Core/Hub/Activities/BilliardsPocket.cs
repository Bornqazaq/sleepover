using UnityEngine;

namespace Igruha.Core.Hub.Activities
{
    /// <summary>
    /// Луза: триггер на краю стола. Сама ничего не решает — только зовёт
    /// станцию. Исход (убрать шар, вернуть биток) считает авторитет.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BilliardsPocket : MonoBehaviour
    {
        private BilliardsStation station;

        public void Bind(BilliardsStation owner) => station = owner;

        private void OnTriggerEnter(Collider other)
        {
            if (station == null || other == null)
            {
                return;
            }

            BilliardsBall ball = other.GetComponent<BilliardsBall>();
            if (ball != null)
            {
                station.NotifyPocketed(ball);
            }
        }
    }
}
