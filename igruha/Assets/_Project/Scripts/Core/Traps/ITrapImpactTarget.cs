using UnityEngine;

namespace Igruha.Core.Traps
{
    /// <summary>
    /// Объект, который ловушка может ударить, не зная, что это такое.
    ///
    /// Нужен потому, что ловушки живут в Core, а всё интересное, по чему они
    /// бьют, — в мини-играх: бутыль с водой, ящик с уликами, тележка с
    /// реквизитом. Ловушка сообщает факт удара и его направление, а что это
    /// значит — двадцать единиц воды или разлетевшиеся бумаги — решает сама
    /// цель.
    ///
    /// Игроки сюда не попадают: у них своя точка входа
    /// <c>PlayerController.ApplyWorldImpulse</c>, и подменять её не надо.
    /// </summary>
    public interface ITrapImpactTarget
    {
        /// <summary>
        /// По объекту прилетело. <paramref name="direction"/> — куда толкнуло,
        /// нормализован; <paramref name="force"/> — импульс удара.
        /// </summary>
        void TakeTrapImpact(Vector3 direction, float force);
    }
}
