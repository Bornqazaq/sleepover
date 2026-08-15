using UnityEngine;

namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Мостик к сети для взаимодействия — по образцу <c>IPushRelay</c>
    /// и <c>IWorldEffectRelay</c>. Реализует сетевой слой, Core знает о нём
    /// только через этот интерфейс и остаётся без ссылки на Igruha.Networking.
    /// </summary>
    public interface IInteractionRelay
    {
        /// <summary>Вправе ли эта машина решать исход взаимодействия.</summary>
        bool HasAuthority { get; }

        /// <summary>
        /// Отправить намерение «взаимодействую с этим» серверу.
        /// <c>true</c> — сетевой слой взял ответственность на себя (отправил
        /// намерение либо сознательно проглотил его на чужой копии),
        /// <c>false</c> — сети нет, вызывающему нужно выполнить действие локально.
        /// </summary>
        bool TryRelayInteract(GameObject target);
    }
}
