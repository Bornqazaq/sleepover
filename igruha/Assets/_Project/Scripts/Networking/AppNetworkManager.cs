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
    [SerializeField] private string gameplaySceneName = "Hub";

    [Tooltip("Табло катки: сервер спавнит его один раз, счёт живёт между мини-играми")]
    [SerializeField] private NetworkObject sessionManagerPrefab;

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
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.StartHost();
        Debug.Log("🟢 Started as HOST - NetworkManager ready (with Connection Approval)");
    }

    private void HandleServerStarted()
    {
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;

        // Табло поднимаем до загрузки сцены: мини-игра ждёт готовый ростер
        SpawnSessionManager();
        LoadGameplayScene();
    }

    /// <summary>
    /// Табло живёт вне сцен: сцены грузятся в режиме Single, и объект,
    /// лежащий в сцене, потерял бы счёт при переходе к следующей мини-игре.
    /// </summary>
    private void SpawnSessionManager()
    {
        if (sessionManagerPrefab == null)
        {
            Debug.LogError("❌ AppNetworkManager: не назначен префаб табло катки (SessionManager) — очки начислять некому");
            return;
        }

        NetworkObject instance = Instantiate(sessionManagerPrefab);
        instance.DestroyWithScene = false;
        instance.Spawn();
        DontDestroyOnLoad(instance.gameObject);

        Debug.Log("🏆 HOST: табло катки заспавнено и переживёт смену сцен");
    }

    private void LoadGameplayScene()
    {
        var status = NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"❌ Не удалось загрузить сцену '{gameplaySceneName}': {status}");
            return;
        }

        Debug.Log($"🗺️ HOST: загружаю игровую сцену '{gameplaySceneName}' — клиенты синхронизируются автоматически");
    }
}
