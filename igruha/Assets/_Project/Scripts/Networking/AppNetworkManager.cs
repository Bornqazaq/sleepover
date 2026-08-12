using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Networking;

/// <summary>
/// Точка входа сети в сцене Boot: поднимает хост или клиент и,
/// если это хост, загружает игровую сцену через NGO SceneManager
/// (клиенты синхронизируются автоматически).
/// </summary>
public class AppNetworkManager : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "Sandbox";

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("❌ NetworkManager.Singleton is NULL!");
            return;
        }

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // Режим определяется аргументом запуска: билд с --client подключается
        // к хосту, всё остальное поднимается как хост
        bool isClientMode = System.Array.Exists(System.Environment.GetCommandLineArgs(),
            element => element.Equals("--client"));

        if (isClientMode)
        {
            NetworkManager.Singleton.StartClient();
            Debug.Log("🟢 Started as CLIENT - Connecting to Host");
            return;
        }

        // Сцену грузим только после того, как сервер реально поднялся:
        // до этого SceneManager ещё не готов принимать запросы
        NetworkManager.Singleton.OnServerStarted += LoadGameplayScene;
        NetworkManager.Singleton.StartHost();
        Debug.Log("🟢 Started as HOST - NetworkManager ready (with Connection Approval)");
    }

    private void LoadGameplayScene()
    {
        NetworkManager.Singleton.OnServerStarted -= LoadGameplayScene;

        var status = NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"❌ Не удалось загрузить сцену '{gameplaySceneName}': {status}");
            return;
        }

        Debug.Log($"🗺️ HOST: загружаю игровую сцену '{gameplaySceneName}' — клиенты синхронизируются автоматически");
    }
}
