using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Igruha.Core.Scenes;
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
    [Tooltip("Сколько секунд клиент ждёт хоста, прежде чем сдаться. Считаем сроком, а не числом " +
             "попыток: на localhost закрытый порт отвечает отказом сразу, поэтому заходы сгорают " +
             "за секунды и любое их число ничего не гарантирует")]
    [SerializeField] private float connectionTimeout = 180f;

    [Tooltip("Пауза перед следующим заходом на подключение")]
    [SerializeField] private float retryDelay = 1f;

    private int failedAttempts;
    private float giveUpTime;
    private bool isConnected;
    private bool isSubscribedToClientEvents;

    /// <summary>Кадров в секунду у headless-инстанса: физике и сети хватает, ядро не жжётся.</summary>
    private const int HeadlessFrameRate = 60;

    /// <summary>
    /// Ограничить частоту кадров инстансу, который ничего не рисует.
    ///
    /// Без рисования Unity крутит цикл настолько быстро, насколько может, и
    /// один такой процесс съедает ядро целиком. На стенде из восьми процессов
    /// это значит, что машина меряет саму себя: клиенты отбирают время у хоста,
    /// таймеры плывут, и найденное «расхождение» оказывается перегрузкой
    /// стенда, а не багом игры.
    /// </summary>
    private void Awake()
    {
        if (Application.isBatchMode)
        {
            Application.targetFrameRate = HeadlessFrameRate;
            Debug.Log($"🖥️ headless-инстанс: частота кадров ограничена {HeadlessFrameRate}");
        }
    }

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
            ApplyEndpointArguments();
            Debug.Log($"🟢 Роль CLIENT ({reason}) — подключаюсь к {DescribeTarget()}");
            giveUpTime = Time.realtimeSinceStartup + connectionTimeout;
            SubscribeToClientEvents();
            StartClient();
            return;
        }

        ApplyEndpointArguments();

        // Сцену грузим только после того, как сервер реально поднялся:
        // до этого SceneManager ещё не готов принимать запросы
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
        NetworkManager.Singleton.StartHost();
        Debug.Log($"🟢 Роль HOST ({reason}) — слушаю {DescribeListen()} (с Connection Approval)");
        WarnIfListenAddressIsLocal();
    }

    /// <summary>
    /// Перекрыть адрес и порт транспорта аргументами запуска.
    ///
    /// Адрес хоста нельзя зашить в сцену: у каждой катки он свой — домашняя
    /// сеть, Tailscale, чужая квартира. Сцена задаёт значение по умолчанию,
    /// <c>--host</c> и <c>--port</c> его перекрывают, и один и тот же билд
    /// годится всем.
    ///
    /// Хосту адрес не меняем: он слушает на том, что стоит в сцене
    /// (<c>ServerListenAddress</c>), и это должен быть <c>0.0.0.0</c>, иначе
    /// снаружи к нему не подключиться.
    /// </summary>
    /// <summary>Адрес, на котором хост принимает подключения откуда угодно.</summary>
    private const string AnyListenAddress = "0.0.0.0";

    private void ApplyEndpointArguments()
    {
        UnityTransport transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            Debug.LogWarning("⚠️ Транспорт не UnityTransport — аргументы --host/--port пропущены");
            return;
        }

        if (NetworkLaunchArguments.TryGetHostAddress(out string address))
        {
            transport.ConnectionData.Address = address;
        }

        if (NetworkLaunchArguments.TryGetPort(out ushort port))
        {
            transport.ConnectionData.Port = port;
        }
    }

    /// <summary>Куда стучится клиент — одной строкой для лога.</summary>
    private string DescribeTarget()
    {
        UnityTransport transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        return transport != null
            ? $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port}"
            : "неизвестный транспорт";
    }

    /// <summary>
    /// Что реально слушает хост — одной строкой для лога.
    ///
    /// Печатать сюда <c>ConnectionData.Address</c> нельзя: это адрес, по
    /// которому к хосту стучатся клиенты, а сокет сервер биндит на
    /// <c>ServerListenAddress</c>, и они не совпадают. В логе билда стояло
    /// <c>127.0.0.1:7777</c>, пока сокет честно висел на <c>0.0.0.0:7777</c>
    /// (проверено netstat). На живом прогоне такая строка отправляет чинить
    /// то, что не сломано.
    /// </summary>
    private string DescribeListen()
    {
        UnityTransport transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            return "неизвестный транспорт";
        }

        string listenAddress = transport.ConnectionData.ServerListenAddress;
        return string.IsNullOrWhiteSpace(listenAddress)
            ? $"(адрес прослушивания не задан):{transport.ConnectionData.Port}"
            : $"{listenAddress}:{transport.ConnectionData.Port}";
    }

    /// <summary>
    /// Предупредить, если хост слушает петлю.
    ///
    /// С <c>127.0.0.1</c> хост поднимается штатно и локально работает, а
    /// снаружи к нему не подключается никто — и выясняется это только когда
    /// люди уже собрались на прогон. Лучше сказать об этом в первой же строке
    /// лога хоста, чем искать причину при десяти зрителях.
    /// </summary>
    private void WarnIfListenAddressIsLocal()
    {
        UnityTransport transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            return;
        }

        string listenAddress = transport.ConnectionData.ServerListenAddress;
        if (listenAddress == AnyListenAddress)
        {
            return;
        }

        Debug.LogWarning(
            $"⚠️ Хост слушает {listenAddress}, а не {AnyListenAddress} — снаружи к нему никто не подключится. " +
            "Поправить ServerListenAddress у UnityTransport в сцене Boot.");
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
        if (Time.realtimeSinceStartup >= giveUpTime)
        {
            Debug.LogError($"❌ CLIENT: хост не отозвался за {connectionTimeout:F0} сек ({failedAttempts} заходов) — сеть не запущена");
            UnsubscribeFromClientEvents();
            return;
        }

        // Шумит только каждый десятый заход: на localhost отказ приходит мгновенно,
        // и построчный лог за три минуты ожидания забил бы консоль.
        if (failedAttempts % 10 == 1)
        {
            float left = giveUpTime - Time.realtimeSinceStartup;
            Debug.Log($"⏳ CLIENT: хост ещё не поднялся (заход {failedAttempts}), жду ещё {left:F0} сек");
        }
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
        if (!BuildSceneCatalog.TryResolvePath(gameplaySceneName, out string scenePath))
        {
            Debug.LogError($"❌ Сцены '{gameplaySceneName}' нет в Build Settings — по сети она не загрузится");
            return;
        }

        var status = NetworkManager.Singleton.SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"❌ Не удалось загрузить сцену '{gameplaySceneName}': {status}");
            return;
        }

        Debug.Log($"🗺️ HOST: загружаю игровую сцену '{gameplaySceneName}' — клиенты синхронизируются автоматически");
    }
}
