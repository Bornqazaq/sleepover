using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Зона поверхности, меняющей управление: лёд, грязь, масло. Внутри неё
    /// персонаж разгоняется и тормозит с заданными множителями, максимальная
    /// скорость при этом не меняется — скользит, но не разгоняется быстрее.
    ///
    /// Физматериалом это не делается: <see cref="PlayerController"/> задаёт
    /// скорость напрямую через linearVelocity, и трение коллайдера на
    /// горизонтальное движение не влияет вообще.
    ///
    /// По сети не синхронизируется. Множитель зависит только от того, где
    /// стоит персонаж, значит на всех машинах он одинаков сам собой —
    /// проверка авторитета здесь была бы лишней и сломала бы предсказание
    /// движения у владельца.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SurfaceModifier : MonoBehaviour
    {
        [Tooltip("Множитель разгона. Меньше единицы — персонаж набирает скорость дольше обычного")]
        [SerializeField] private float accelerationMultiplier = 0.3f;
        [Tooltip("Множитель торможения. Он и даёт скольжение: чем меньше, тем дольше персонаж едет по инерции после отпущенного ввода")]
        [SerializeField] private float decelerationMultiplier = 0.2f;

        private void Reset()
        {
            // Зона обязана быть триггером: сплошной коллайдер поверх пола
            // просто не пустил бы игрока внутрь.
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            player?.ApplySurface(this, accelerationMultiplier, decelerationMultiplier);
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            player?.ClearSurface(this);
        }
    }
}
