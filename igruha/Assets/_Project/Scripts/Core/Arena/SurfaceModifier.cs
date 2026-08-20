using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Core.Arena
{
    /// <summary>
    /// Зона поверхности, меняющей управление: лёд, грязь, масло. Внутри неё
    /// персонаж разгоняется и тормозит с заданными множителями, максимальная
    /// скорость при этом не меняется — скользит, но не разгоняется быстрее.
    ///
    /// Скольжение делается множителями, а не физматериалом поверхности:
    /// <see cref="PlayerController"/> задаёт скорость напрямую через linearVelocity,
    /// и коэффициент трения пола управляемый разгон не задаёт.
    ///
    /// <b>Но трение его съедает.</b> Заданная скорость — это не кинематика:
    /// следом идёт шаг физики, и контактное трение гасит часть прироста.
    /// При обычном разгоне прирост больше потерь и разница не видна, а на
    /// льду прирост за такт падает ниже того, что снимает трение, и персонаж
    /// не трогается с места вообще — замерено 20.08 на ледяных этажах Duck Hunt.
    /// Поэтому капсула персонажа несёт физматериал без трения
    /// (<c>Settings/Physics/CharacterFrictionless</c>) — без него этот компонент
    /// не работает. Лежачего от скольжения держит демпфирование нокдауна, а не трение.
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
