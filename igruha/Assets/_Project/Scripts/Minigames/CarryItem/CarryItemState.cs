using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Почему из бутыли ушла вода. Нужна звуку, VFX и отладке — сама потеря от причины не зависит.</summary>
    public enum WaterLossReason
    {
        /// <summary>Удар: игрок, брошенный предмет, ловушка.</summary>
        Hit,
        /// <summary>Наклон за порогом дольше задержки.</summary>
        Tilt,
        /// <summary>Отпустили все — бутыль упала на землю.</summary>
        Drop,
        /// <summary>Совместный бросок: половина остатка.</summary>
        Throw,
        /// <summary>Таран: досталось жертве.</summary>
        RamVictim,
        /// <summary>Таран: досталось атакующему.</summary>
        RamAttacker,
        /// <summary>Перелито в бак — единственная «потеря», которая идёт в счёт.</summary>
        Poured,
        /// <summary>Улетела в пропасть: весь остаток.</summary>
        Void
    }

    /// <summary>
    /// Счёт одной команды. Отдельной структурой, потому что в фазе 3 она
    /// целиком уезжает в <c>NetworkVariable</c>, а разрозненные поля
    /// MonoBehaviour пришлось бы синхронизировать по одному.
    /// </summary>
    public struct CarryItemTeamState
    {
        /// <summary>Воды в баке, единиц. Это и есть счёт.</summary>
        public int Water;

        /// <summary>
        /// Момент последней долитой порции на общих часах. Решает тайбрейк при
        /// равном ненулевом счёте: раньше донёс — выиграл.
        /// </summary>
        public double LastDeliveryTime;

        /// <summary>Сколько ходок команда закрыла. Нужно на приёмке: за раунд их должно выходить шесть.</summary>
        public int Deliveries;
    }

    /// <summary>
    /// Состояние раунда одной структурой (спека 10). Мигрирует в
    /// <c>NetworkVariable</c> целиком, поэтому правила её читают и пишут
    /// через контроллер, а не держат свои копии по углам.
    /// </summary>
    public struct CarryItemState
    {
        public CarryItemTeamState TeamA;
        public CarryItemTeamState TeamB;

        /// <summary>Счёт этой команды.</summary>
        public readonly CarryItemTeamState Of(TeamSide side) =>
            side == TeamSide.A ? TeamA : TeamB;

        /// <summary>Долить команде воды и отметить момент. Одна точка записи счёта.</summary>
        public void Deliver(TeamSide side, int amount, int capacity, double time, bool finishedBottle)
        {
            if (side == TeamSide.A)
            {
                Apply(ref TeamA, amount, capacity, time, finishedBottle);
                return;
            }

            if (side == TeamSide.B)
            {
                Apply(ref TeamB, amount, capacity, time, finishedBottle);
            }
        }

        private static void Apply(ref CarryItemTeamState team, int amount, int capacity, double time,
            bool finishedBottle)
        {
            if (amount > 0)
            {
                // Излишек сверх вместимости просто не влезает, и раунд от этого
                // не кончается: догнать соперника ещё можно.
                team.Water = System.Math.Min(capacity, team.Water + amount);
                team.LastDeliveryTime = time;
            }

            if (finishedBottle)
            {
                team.Deliveries++;
            }
        }

        /// <summary>
        /// Кто победил. <see cref="TeamSide.None"/> — победителя нет: обе
        /// команды с нулём, и это не ничья, а общий провал (спека 6).
        ///
        /// Равный ненулевой счёт решается по времени последней порции: кто
        /// пришёл к своему счёту раньше, тот держал его дольше под давлением.
        /// </summary>
        public readonly TeamSide Winner()
        {
            if (TeamA.Water == 0 && TeamB.Water == 0)
            {
                return TeamSide.None;
            }

            if (TeamA.Water != TeamB.Water)
            {
                return TeamA.Water > TeamB.Water ? TeamSide.A : TeamSide.B;
            }

            return TeamA.LastDeliveryTime <= TeamB.LastDeliveryTime ? TeamSide.A : TeamSide.B;
        }
    }
}
