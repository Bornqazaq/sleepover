using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Зона, опускающая потолок скорости всем, кто в ней находится: песок,
    /// вода по колено, грязь. Внутри бегут медленнее — и догоняющий тоже,
    /// поэтому это выбор, а не ловушка.
    ///
    /// Отличается от <see cref="SurfaceModifier"/> тем, что меняет предел, а не
    /// разгон: скользкий лёд оставляет максимальную скорость, песок её срезает.
    /// Вместе их применять можно — они управляют разными величинами.
    ///
    /// По сети не синхронизируется: потолок зависит только от того, где стоит
    /// персонаж, значит на всех машинах совпадает сам собой. Проверка авторитета
    /// здесь сломала бы предсказание движения у владельца.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpeedZone : MonoBehaviour
    {
        [Tooltip("Доля от обычной максимальной скорости внутри зоны")]
        [Range(0.1f, 1f)]
        [SerializeField] private float speedMultiplier = 0.7f;

        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = Mathf.Clamp(value, 0.1f, 1f);
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.Config == null)
            {
                return;
            }

            player.ApplySpeedCap(this, player.Config.MaxSpeed * speedMultiplier);
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            player?.ClearSpeedCap(this);
        }
    }
}
