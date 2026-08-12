using UnityEngine;
using Unity.Netcode;

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
            if (nm.NetworkConfig.Prefabs == null || nm.NetworkConfig.Prefabs.Count == 0)
            {
                Debug.LogWarning("⚠️  NetworkSetupHelper: No network prefabs registered in DefaultNetworkPrefabs");
            }
            else
            {
                Debug.Log($"✅ NetworkSetupHelper: Found {nm.NetworkConfig.Prefabs.Count} registered network prefabs");
            }

            // Проверяем что Player prefab имеет NetworkObject и NetworkTransform
            if (nm.PlayerPrefab != null)
            {
                var playerNetObj = nm.PlayerPrefab.GetComponent<NetworkObject>();
                var playerNetTransform = nm.PlayerPrefab.GetComponent<NetworkTransform>();

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
