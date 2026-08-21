using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Пружина: всех персонажей в зоне подбрасывает (гарантированный кувырок).
    /// Зона — trigger-коллайдер на этом же объекте.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpringTrap : TrapBase
    {
        [Tooltip("Импульс подбрасывания")]
        [SerializeField] private float launchForce = 16f;
        [Tooltip("Наклон запуска от вертикали в сторону forward пружины, 0 — строго вверх")]
        [Range(0f, 1f)]
        [SerializeField] private float forwardTilt = 0.25f;

        private readonly List<PlayerController> occupants = new List<PlayerController>(8);

        /// <summary>
        /// Задать силу подброса извне. Нужно играм, где настраивается высота
        /// полёта, а не импульс: высоту приходится пересчитывать в импульс через
        /// гравитацию и массу тела, и делать это в инспекторе руками — верный
        /// способ разъехаться со спекой при первой же правке физики.
        /// </summary>
        public void SetLaunchForce(float force) => launchForce = Mathf.Max(0f, force);

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && !occupants.Contains(player))
            {
                occupants.Add(player);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                occupants.Remove(player);
            }
        }

        protected override void OnActivated()
        {
            Vector3 direction = (Vector3.up + transform.forward * forwardTilt).normalized;
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                if (occupants[i] == null)
                {
                    occupants.RemoveAt(i);
                    continue;
                }

                occupants[i].ApplyWorldImpulse(direction * launchForce);
            }
        }
    }
}
