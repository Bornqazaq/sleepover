using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace Igruha.Networking
{
    /// <summary>
    /// Helper скрипт для автоматической регистрации сетевых объектов.
    /// Этот скрипт должен быть на GameObject'е в Boot сцене и выполниться перед NetworkManager.Start().
    /// </summary>
    public class NetworkSetupHelper : MonoBehaviour
    {
        private void Awake()
        {
            if (!Application.isEditor)
                return;

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogWarning("❌ NetworkSetupHelper: NetworkManager.Singleton is NULL");
                return;
            }

            ValidateNetworkManagerSetup();
            ValidatePlayerPrefab();
        }

        private void ValidateNetworkManagerSetup()
        {
            var nm = NetworkManager.Singleton;

            // Проверяем что ConnectionApproval включен
            if (!nm.NetworkConfig.ConnectionApproval)
            {
                Debug.LogWarning("⚠️  NetworkSetupHelper: Connection Approval is disabled. Consider enabling it for server-authority.");
                nm.NetworkConfig.ConnectionApproval = true;
            }

            // Проверяем что EnableSceneManagement включен
            if (!nm.NetworkConfig.EnableSceneManagement)
            {
                Debug.LogError("❌ NetworkSetupHelper: EnableSceneManagement must be enabled!");
                nm.NetworkConfig.EnableSceneManagement = true;
            }

            // Проверяем TickRate
            if (nm.NetworkConfig.TickRate != 30)
            {
                Debug.LogWarning($"⚠️  NetworkSetupHelper: TickRate is {nm.NetworkConfig.TickRate}, recommended is 30");
            }

            Debug.Log("✅ NetworkSetupHelper: NetworkManager configuration validated");
        }

        private void ValidatePlayerPrefab()
        {
            var nm = NetworkManager.Singleton;

            // Проверяем что Player.prefab зарегистрирована в DefaultNetworkPrefabs
            int registeredPrefabs = nm.NetworkConfig.Prefabs == null ? 0 : nm.NetworkConfig.Prefabs.Prefabs.Count;
            if (registeredPrefabs == 0)
            {
                Debug.LogWarning("⚠️  NetworkSetupHelper: No network prefabs registered in DefaultNetworkPrefabs");
            }
            else
            {
                Debug.Log($"✅ NetworkSetupHelper: Found {registeredPrefabs} registered network prefabs");
            }

            // Проверяем что Player prefab имеет NetworkObject и NetworkTransform
            var playerPrefab = nm.NetworkConfig.PlayerPrefab;
            if (playerPrefab != null)
            {
                var playerNetObj = playerPrefab.GetComponent<NetworkObject>();
                var playerNetTransform = playerPrefab.GetComponent<NetworkTransform>();

                if (playerNetObj == null)
                    Debug.LogError("❌ NetworkSetupHelper: Player prefab is missing NetworkObject component!");
                else
                    Debug.Log("✅ NetworkSetupHelper: Player has NetworkObject");

                if (playerNetTransform == null)
                    Debug.LogWarning("⚠️  NetworkSetupHelper: Player prefab is missing NetworkTransform component");
                else
                    Debug.Log("✅ NetworkSetupHelper: Player has NetworkTransform");
            }
            else
            {
                Debug.LogWarning("⚠️  NetworkSetupHelper: NetworkManager.PlayerPrefab is not set (will auto-spawn from DefaultNetworkPrefabs)");
            }
        }
    }
}
