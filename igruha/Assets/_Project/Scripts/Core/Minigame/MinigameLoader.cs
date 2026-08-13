using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Загрузка сцены мини-игры. В сетевой катке грузит только сервер через
    /// NGO SceneManager — клиенты подтягиваются сами. Без сети остаётся
    /// Addressables, чтобы сцену можно было открыть напрямую из редактора.
    /// </summary>
    public sealed class MinigameLoader : MonoBehaviour
    {
        public bool IsLoading { get; private set; }

        public void Load(MinigameDefinition definition)
        {
            if (IsLoading)
            {
                return;
            }

            if (definition == null)
            {
                Debug.LogError($"{name}: у мини-игры нет определения — загружать нечего.", this);
                return;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                LoadNetworked(network, definition);
                return;
            }

            LoadLocal(definition);
        }

        private void LoadNetworked(NetworkManager network, MinigameDefinition definition)
        {
            if (!network.IsServer)
            {
                Debug.LogWarning($"{name}: клиент не грузит сцену — это делает хост", this);
                return;
            }

            string sceneName = definition.SceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"{name}: у мини-игры не задано имя сцены для сети.", this);
                return;
            }

            IsLoading = true;
            SceneEventProgressStatus status = network.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                IsLoading = false;
                Debug.LogError($"{name}: NGO не смог загрузить '{sceneName}': {status}", this);
                return;
            }

            Debug.Log($"🗺️ HOST: гружу мини-игру '{sceneName}' — клиенты синхронизируются");
            IsLoading = false;
        }

        private void LoadLocal(MinigameDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.SceneAddress))
            {
                Debug.LogError($"{name}: у мини-игры не задан Scene Address — загружать нечего.", this);
                return;
            }

            IsLoading = true;
            AsyncOperationHandle<SceneInstance> handle =
                Addressables.LoadSceneAsync(definition.SceneAddress, LoadSceneMode.Single);
            handle.Completed += OnSceneLoaded;
        }

        private void OnSceneLoaded(AsyncOperationHandle<SceneInstance> handle)
        {
            IsLoading = false;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"{name}: не удалось загрузить сцену мини-игры: {handle.OperationException}", this);
            }
        }
    }
}
