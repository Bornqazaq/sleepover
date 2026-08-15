using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Igruha.Networking;

/// <summary>
/// Точка входа сети в сцене Boot: поднимает хост или клиент и,
/// если это хост, загружает игровую сцену через NGO SceneManager
/// (клиенты синхронизируются автоматически).
///
/// Роль определяет <see cref="NetworkRoleResolver"/>: аргумент запуска для
/// билдов, тег Multiplayer Play Mode или признак виртуального игрока.
/// </summary>
public class AppNetworkManager : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "Hub";

    [Tooltip("Табло катки: сервер спавнит его один раз, счёт живёт между мини-играми")]
    [SerializeField] private NetworkObject sessionManagerPrefab;

    [Header("Подключение клиента")]
    [Tooltip("Сколько раз клиент заходит на подключение после того, как NGO признал попытку неудачной. " +
             "Одна попытка — это уже MaxConnectAttempts транспорта, то есть десятки секунд ожидания")]
    [SerializeField] private int connectionAttempts = 3;

    [Tooltip("Пауза перед следующим заходом на подключение")]
    [SerializeField] private float retryDelay = 1f;

    private int failedAttempts;
    private bool isConnected;
    private bool isSubscribedToClientEvents;

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

        NetworkStartRole role = NetworkRoleResolver.Resolve(out string reason);

        if (role == NetworkStartRole.Client)
        {
            Debug.Log($"🟢 Роль CLIENT ({reason}) — подключаюсь к хосту");
            SubscribeToClientEvents();
            StartClient();
            return;
        }

        // Сцену грузим только после того, как сервер реально поднялся:
        // до этого SceneManager ещё не готов принимать запросы
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.StartHost();
        Debug.Log($"🟢 Роль HOST ({reason}) — NetworkManager поднят (с Connection Approval)");
    }

    private void OnDestroy()
    {
        UnsubscribeFromClientEvents();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        }
    }

    // ========== КЛИЕНТ ==========

    /// <summary>
    /// Одна попытка подключения — это не одна посылка: транспорт внутри себя
    /// повторяет запрос <c>MaxConnectAttempts</c> раз с интервалом
    /// <c>ConnectTimeoutMS</c>, то есть сам ждёт хоста десятки секунд.
    /// Обрывать это своим таймером нельзя: обрыв уже одобренного подключения
    /// оставляет на сервере запись о клиенте и его персонажа в сцене.
    /// </summary>
    private void StartClient()
    {
        if (!NetworkManager.Singleton.StartClient())
        {
            Debug.LogError("❌ CLIENT: NetworkManager отказался стартовать — проверь транспорт в сцене Boot");
        }
    }

    private void SubscribeToClientEvents()
    {
        if (isSubscribedToClientEvents)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        isSubscribedToClientEvents = true;
    }

    private void UnsubscribeFromClientEvents()
    {
        if (!isSubscribedToClientEvents || NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        isSubscribedToClientEvents = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        isConnected = true;
        Debug.Log($"✅ CLIENT: подключился к хосту (заходов: {failedAttempts + 1})");
        UnsubscribeFromClientEvents();
    }

    /// <summary>
    /// На клиенте этот колбэк приходит и когда подключиться не удалось,
    /// и когда соединение разорвалось после успешного входа. Второй случай —
    /// не наше дело, его разбирает <see cref="DisconnectionHandler"/>.
    /// </summary>
    private void HandleClientDisconnected(ulong clientId)
    {
        if (isConnected)
        {
            return;
        }

        string reason = NetworkManager.Singleton.DisconnectReason;
        if (!string.IsNullOrEmpty(reason))
        {
            // Сервер ответил и отказал — повторять бессмысленно, причина не пройдёт и в следующий раз
            Debug.LogError($"❌ CLIENT: хост отклонил подключение — «{reason}»");
            UnsubscribeFromClientEvents();
            return;
        }

        failedAttempts++;
        if (failedAttempts >= connectionAttempts)
        {
            Debug.LogError($"❌ CLIENT: хост не отозвался за {connectionAttempts} заходов — сеть не запущена");
            UnsubscribeFromClientEvents();
            return;
        }

        Debug.LogWarning($"⏳ CLIENT: хост не отозвался (заход {failedAttempts} из {connectionAttempts}), пробую ещё раз");
        StartCoroutine(RestartClientAfterShutdown());
    }

    private IEnumerator RestartClientAfterShutdown()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        while (networkManager.ShutdownInProgress)
        {
            yield return null;
        }

        yield return new WaitForSecondsRealtime(retryDelay);

        if (!isConnected)
        {
            StartClient();
        }
    }

    // ========== ХОСТ ==========

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
