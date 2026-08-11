using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Загрузка сцены мини-игры по требованию через Addressables (CLAUDE.md 2).
    /// Единая точка: позже сюда добавится сетевая синхронная загрузка у всех клиентов.
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

            if (definition == null || string.IsNullOrEmpty(definition.SceneAddress))
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
