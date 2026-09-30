using System;
using Unity.Netcode;
using Igruha.Core.Session;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>
    /// Почему изменился уровень воды в тележке. Нужна звуку, VFX и отладке —
    /// само изменение от причины не зависит. Единственная причина со знаком
    /// «плюс» — <see cref="Filled"/>; единственная «потеря», идущая в счёт, —
    /// <see cref="Poured"/>.
    /// </summary>
    public enum WaterLossReason
    {
        /// <summary>Удар: брошенный предмет или ловушка.</summary>
        Hit,
        /// <summary>Крен за порогом дольше задержки — плещет через борт.</summary>
        Tilt,
        /// <summary>Толчок с разгона: доля остатка расплёскивается.</summary>
        Shove,
        /// <summary>Таран: досталось жертве.</summary>
        RamVictim,
        /// <summary>Таран: досталось атакующему.</summary>
        RamAttacker,
        /// <summary>Перелито в бак — идёт в счёт.</summary>
        Poured,
        /// <summary>Улетела в пропасть: весь остаток.</summary>
        Void,
        /// <summary>Набрано под краном. Единственная причина, при которой воды становится больше.</summary>
        Filled
    }

    /// <summary>
    /// Счёт одной команды. Отдельной структурой, потому что целиком уезжает
    /// в <c>NetworkVariable</c> внутри <see cref="CarryItemState"/>, а
    /// разрозненные поля MonoBehaviour пришлось бы синхронизировать по одному.
    /// </summary>
    public struct CarryItemTeamState : INetworkSerializable, IEquatable<CarryItemTeamState>
    {
        /// <summary>Воды в баке, единиц. Это и есть счёт.</summary>
        public int Water;

        /// <summary>
        /// Момент последней долитой порции на общих часах. Решает тайбрейк при
        /// равном ненулевом счёте: раньше донёс — выиграл.
        /// </summary>
        public double LastDeliveryTime;

        /// <summary>Сколько ходок команда закрыла. Нужно на приёмке: за раунд у команды выходит три-четыре.</summary>
        public int Deliveries;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Water);
            serializer.SerializeValue(ref LastDeliveryTime);
            serializer.SerializeValue(ref Deliveries);
        }

        public bool Equals(CarryItemTeamState other) =>
            Water == other.Water &&
            Deliveries == other.Deliveries &&
            LastDeliveryTime.Equals(other.LastDeliveryTime);
    }

    /// <summary>
    /// Состояние раунда одной структурой (спека 10). Едет в
    /// <c>NetworkVariable</c> целиком, поэтому правила её читают и пишут
    /// через контроллер, а не держат свои копии по углам.
    ///
    /// Уровень воды в баке — это счёт: разъедется он, разъедется исход раунда.
    /// Поэтому пишет структуру только сервер, а клиент её отображает.
    /// </summary>
    public struct CarryItemState : INetworkSerializable, IEquatable<CarryItemState>
    {
        public CarryItemTeamState TeamA;
        public CarryItemTeamState TeamB;

        /// <summary>Счёт этой команды.</summary>
        public readonly CarryItemTeamState Of(TeamSide side) =>
            side == TeamSide.A ? TeamA : TeamB;

        /// <summary>Долить команде воды и отметить момент. Одна точка записи счёта.</summary>
        public void Deliver(TeamSide side, int amount, int capacity, double time, bool finishedTrip)
        {
            if (side == TeamSide.A)
            {
                Apply(ref TeamA, amount, capacity, time, finishedTrip);
                return;
            }

            if (side == TeamSide.B)
            {
                Apply(ref TeamB, amount, capacity, time, finishedTrip);
            }
        }

        private static void Apply(ref CarryItemTeamState team, int amount, int capacity, double time,
            bool finishedTrip)
        {
            if (amount > 0)
            {
                // Излишек сверх вместимости просто не влезает, и раунд от этого
                // не кончается: догнать соперника ещё можно.
                team.Water = System.Math.Min(capacity, team.Water + amount);
                team.LastDeliveryTime = time;
            }

            if (finishedTrip)
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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            TeamA.NetworkSerialize(serializer);
            TeamB.NetworkSerialize(serializer);
        }

        public bool Equals(CarryItemState other) => TeamA.Equals(other.TeamA) && TeamB.Equals(other.TeamB);
    }
}
