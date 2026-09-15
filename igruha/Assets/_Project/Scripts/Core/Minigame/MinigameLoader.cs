using Igruha.Core.Scenes;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Загрузка сцены мини-игры. В сетевой катке грузит только сервер через
    /// NGO SceneManager — клиенты подтягиваются сами. Без сети тот же путь,
    /// но обычным SceneManager, чтобы сцену можно было открыть из редактора.
    /// </summary>
    /// <remarks>
    /// Ключ загрузки один — имя сцены из Build Settings. Addressables здесь
    /// применить нельзя: NGO опознаёт сцены по индексу в списке сборки
    /// (<c>NetworkSceneManager.GenerateScenesInBuild</c>), и сцена без индекса
    /// отбивается на валидации ещё до отправки события. Addressables же
    /// вычищает из своих групп всё, что попало в список сборки, — два пути
    /// взаимно исключают друг друга, поэтому остался один.
    /// </remarks>
    public sealed class MinigameLoader : MonoBehaviour
    {
        public bool IsLoading { get; private set; }

        public void Load(MinigameDefinition definition) => TryLoad(definition);

        public bool TryLoad(MinigameDefinition definition)
        {
            if (IsLoading)
            {
                return false;
            }

            if (definition == null)
            {
                Debug.LogError($"{name}: у мини-игры нет определения — загружать нечего.", this);
                return false;
            }

            if (string.IsNullOrEmpty(definition.SceneName))
            {
                Debug.LogError($"{name}: у мини-игры '{definition.DisplayName}' не задано имя сцены.", this);
                return false;
            }

            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                return LoadNetworked(network, definition);
            }

            return LoadLocal(definition);
        }

        private bool LoadNetworked(NetworkManager network, MinigameDefinition definition)
        {
            if (!network.IsServer)
            {
                Debug.LogWarning($"{name}: клиент не грузит сцену — это делает хост", this);
                return false;
            }

            string sceneName = definition.SceneName;
            if (!BuildSceneCatalog.TryResolvePath(sceneName, out string scenePath))
            {
                Debug.LogError($"{name}: сцены '{sceneName}' нет в Build Settings — по сети она не загрузится.", this);
                return false;
            }

            IsLoading = true;
            SceneEventProgressStatus status = network.SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                IsLoading = false;
                Debug.LogError($"{name}: NGO не смог загрузить '{sceneName}': {status}", this);
                return false;
            }

            Debug.Log($"🗺️ HOST: гружу мини-игру '{sceneName}' — клиенты синхронизируются");
            return true;
        }

        private bool LoadLocal(MinigameDefinition definition)
        {
            string sceneName = definition.SceneName;
            if (!BuildSceneCatalog.TryResolvePath(sceneName, out _)) return false;

            IsLoading = true;
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                IsLoading = false;
                Debug.LogError($"{name}: сцены '{sceneName}' нет в Build Settings — загружать нечего.", this);
                return false;
            }

            operation.completed += OnSceneLoaded;
            return true;
        }

        private void OnSceneLoaded(AsyncOperation operation)
        {
            IsLoading = false;
        }
    }
}
