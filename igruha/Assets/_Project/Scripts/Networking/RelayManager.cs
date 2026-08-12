using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;

#if UNITY_SERVICES_RELAY && UNITY_SERVICES_LOBBY

using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;
using UnityTransport = Unity.Netcode.Transports.UTP.UnityTransport;

namespace Igruha.Networking
{
    /// <summary>
    /// Управляет Relay (облачное соединение) для мультиплеера.
    ///
    /// IGR-55: Создание матча + получение join-кода
    /// IGR-56: Присоединение по коду
    ///
    /// Relay позволяет игрокам соединяться через публичный облачный IP,
    /// без необходимости знать прямой IP друг друга.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        private string relayJoinCode;
        private RelayServerData relayServerData;

        /// <summary>
        /// Хост: создать Relay матч и получить join-код.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync()
        {
            try
            {
                Debug.Log("🔄 Relay: Allocating host session...");

                // Создать Relay allocation для хоста
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers: 8);

                // Получить публичный join-код
                relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                Debug.Log($"✅ Relay: Allocated with join code: {relayJoinCode}");

                // Настроить UnityTransport для использования Relay
                SetupTransport(allocation, isHost: true);

                return relayJoinCode;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"❌ Relay: Failed to create allocation — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Клиент: присоединиться к матчу по join-коду.
        /// </summary>
        public async Task<bool> JoinClientWithRelayAsync(string joinCode)
        {
            try
            {
                Debug.Log($"🔄 Relay: Joining with code '{joinCode}'...");

                // Получить allocation из join-кода
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                Debug.Log($"✅ Relay: Joined allocation {joinAllocation.AllocationId}");

                // Настроить UnityTransport для использования Relay
                SetupTransport(joinAllocation, isHost: false);

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"❌ Relay: Failed to join allocation — {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Внутренний метод: настроить UnityTransport с данными Relay.
        /// </summary>
        private void SetupTransport(Allocation allocation, bool isHost)
        {
            SetupTransportInternal(allocation.RelayServer, allocation.AllocationIdBytes, allocation.AccessToken, isHost);
        }

        private void SetupTransport(JoinAllocation joinAllocation, bool isHost)
        {
            SetupTransportInternal(joinAllocation.RelayServer, joinAllocation.AllocationIdBytes, joinAllocation.AccessToken, isHost);
        }

        private void SetupTransportInternal(RelayServer relayServer, byte[] allocationIdBytes, byte[] accessToken, bool isHost)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("NetworkManager not found!");
                return;
            }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("UnityTransport not found!");
                return;
            }

            // Настроить данные Relay
            relayServerData = new RelayServerData(relayServer, "udp");
            transport.SetRelayServerData(relayServerData);

            if (isHost)
            {
                Debug.Log($"🌐 Transport: Configured for HOST (Relay)");
            }
            else
            {
                Debug.Log($"🌐 Transport: Configured for CLIENT (Relay)");
            }
        }

        /// <summary>
        /// Получить текущий join-код (для хоста).
        /// </summary>
        public string GetJoinCode() => relayJoinCode;

        /// <summary>
        /// Проверить что Relay интегрирован и готов.
        /// </summary>
        public static bool IsRelayAvailable()
        {
#if UNITY_SERVICES_RELAY
            return true;
#else
            return false;
#endif
        }
    }
}

#else

// Fallback если Relay пакеты не установлены
namespace Igruha.Networking
{
    public class RelayManager : MonoBehaviour
    {
        public async System.Threading.Tasks.Task<string> StartHostWithRelayAsync()
        {
            Debug.LogError("❌ Relay: com.unity.services.relay not installed!");
            return null;
        }

        public async System.Threading.Tasks.Task<bool> JoinClientWithRelayAsync(string joinCode)
        {
            Debug.LogError("❌ Relay: com.unity.services.relay not installed!");
            return false;
        }

        public static bool IsRelayAvailable() => false;
    }
}

#endif
