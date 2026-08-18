using Unity.Netcode;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Общие часы матча. В сетевой катке это время сервера — только оно
    /// одинаково на всех машинах; вне сети обычное время сцены, чтобы сцена,
    /// открытая напрямую из редактора, работала без изменений.
    ///
    /// Нужны везде, где момент важнее длительности: конец стадии подраунда,
    /// метка нажатия кнопки, любой замер интервала. Считать это в каждом
    /// потребителе по-своему нельзя — разъедется.
    /// </summary>
    public static class NetworkClock
    {
        public static double Now
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                if (network != null && network.IsListening)
                {
                    return network.ServerTime.Time;
                }

                return Time.timeAsDouble;
            }
        }
    }
}
