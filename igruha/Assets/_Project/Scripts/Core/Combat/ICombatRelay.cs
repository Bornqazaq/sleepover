using UnityEngine;

namespace Igruha.Core.Combat
{
    /// <summary>
    /// Мостик к сети для стрельбы — по образцу <c>IPushRelay</c>,
    /// <c>IWorldEffectRelay</c> и <c>IInteractionRelay</c>. Реализует сетевой слой,
    /// Core знает о нём только через этот интерфейс.
    /// </summary>
    public interface ICombatRelay
    {
        /// <summary>
        /// Отправить серверу намерение выстрелить в указанном направлении.
        /// <c>true</c> — сетевой слой взял ответственность на себя,
        /// <c>false</c> — сети нет, стрелять локально.
        /// </summary>
        bool TryRelayFire(Vector3 direction);
    }
}
