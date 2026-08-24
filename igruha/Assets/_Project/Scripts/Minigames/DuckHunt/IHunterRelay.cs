using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>
    /// Куда Охотник отдаёт свои намерения, когда решает не эта машина: ход
    /// лифта, выстрел, перезарядка. Реализует сетевая половина игры.
    ///
    /// Шов тот же, что у <see cref="Igruha.Core.Player.IPushRelay"/> и
    /// <see cref="Igruha.Core.Combat.ICombatRelay"/>: роль остаётся обычным
    /// MonoBehaviour и про NGO ничего не знает, а сеть подставляется снаружи.
    /// Живёт в папке игры, а не в Core, потому что и платформу по оси ввода,
    /// и hitscan-ружьё во всём проекте держит ровно одна роль.
    ///
    /// Все три метода возвращают одно и то же: <c>true</c> — сетевой слой взял
    /// ответственность на себя и делать здесь ничего нельзя, <c>false</c> —
    /// решает эта машина, выполняем на месте.
    /// </summary>
    public interface IHunterRelay
    {
        /// <summary>Желаемая ось хода лифта, −1…1.</summary>
        bool TryRelayElevatorAxis(float axis);

        /// <summary>
        /// Намерение выстрелить: откуда смотрит игрок и куда. Направление
        /// уходит чистым — конус разброса накладывает сервер.
        /// </summary>
        bool TryRelayFire(Vector3 origin, Vector3 direction);

        /// <summary>Намерение перезарядиться вручную.</summary>
        bool TryRelayReload();
    }
}
