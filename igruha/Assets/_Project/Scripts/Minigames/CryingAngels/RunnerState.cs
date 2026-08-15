using System;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Состояние Бегущего в раунде. Пока два: СВОБОДЕН и ЗАМОРОЖЕН
    /// (ОКАМЕНЕЛ добавляет 14.6 вместе со счётчиком).
    ///
    /// Заморозка блокирует только движение: камера остаётся управляемой,
    /// иначе замороженный слепнет и перестаёт понимать, что происходит,
    /// а вся соль роли — смотреть, как тебя обходят.
    ///
    /// Заморозка и нокдаун независимы. Замороженного можно толкнуть — он
    /// комично отлетает и падает, но остаётся замороженным, пока на нём луч.
    /// Управление вернётся, когда закончатся оба: этим занимается сам
    /// PlayerController, у него блокировка и нокдаун живут рядом.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class RunnerState : MonoBehaviour
    {
        public enum Phase
        {
            Free,
            Frozen
        }

        private PlayerController motor;

        /// <summary>Состояние сменилось — визуал и HUD цепляются сюда.</summary>
        public event Action<Phase> Changed;

        public Phase Current { get; private set; } = Phase.Free;

        public bool IsFree => Current == Phase.Free;

        private void Awake()
        {
            motor = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Единственная точка смены заморозки. Решение принимает контроллер
        /// раунда под авторитетом; в сетевой фазе сюда придёт реплицированное
        /// состояние, а тело метода останется прежним.
        /// </summary>
        public void SetFrozen(bool frozen)
        {
            Phase next = frozen ? Phase.Frozen : Phase.Free;
            if (Current == next)
            {
                return;
            }

            Current = next;
            motor.MovementLocked = frozen;
            Changed?.Invoke(Current);
        }

        /// <summary>Снять всё и вернуть в СВОБОДЕН: конец раунда, пересдача ролей.</summary>
        public void ResetState() => SetFrozen(false);
    }
}
