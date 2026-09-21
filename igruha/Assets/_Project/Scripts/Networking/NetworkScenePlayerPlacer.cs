using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Scenes;
using Igruha.Core.Session;
using Igruha.Core.Spawning;

namespace Igruha.Networking
{
    /// <summary>
    /// После загрузки сцены сервер ставит уже существующих игроков
    /// на SpawnPoint текущей арены. Круг из Connection Approval — только
    /// запасной вариант, пока сцены ещё нет (Boot).
    ///
    /// Расстановка идёт <b>до</b> старта мини-игры: по её концу открывается
    /// <see cref="ScenePlacementGate"/>, которого ждёт <c>MinigameBootstrap</c>.
    /// Раньше порядок был случайным и зависел от того, кто быстрее грузит
    /// сцену, — см. разбор в самом шлюзе.
    /// </summary>
    public sealed class NetworkScenePlayerPlacer : MonoBehaviour
    {
        [SerializeField] private SpawnRole defaultRole = SpawnRole.Default;

        private NetworkManager networkManager;

        private void Start()
        {
            networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                return;
            }

            // SceneManager существует только у поднятой сети. Если хост не
            // стартовал — занят порт, отказал транспорт, — здесь лежал
            // NullReferenceException поверх и без того непонятного экрана.
            if (networkManager.SceneManager == null)
            {
                networkManager = null;
                Debug.LogWarning("⚠️ NetworkScenePlayerPlacer: сеть не поднята — расставлять игроков нечем");
                return;
            }

            networkManager.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
            networkManager.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
            }

            networkManager.OnClientConnectedCallback -= OnClientConnected;
        }

        /// <summary>
        /// Сцену догрузили все. Порядок внутри обработчика и есть тот самый
        /// порядок, которого не хватало: сначала тела тем, у кого их нет,
        /// потом раскладка по точкам, и только потом открытый шлюз, по
        /// которому мини-игра начинает раздавать свои места.
        /// </summary>
        private void OnLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (networkManager != null && networkManager.IsServer)
            {
                EnsureBodiesForRound(sceneName);
                PlaceAllPlayers(sceneName);
            }

            ScenePlacementGate.Open();
        }

        private void OnClientConnected(ulong clientId)
        {
            // Опоздавший клиент не попадает в OnLoadEventCompleted — сцена уже загружена.
            PlacePlayer(clientId);
        }

        /// <summary>
        /// Выдать тело тем, кто зашёл в матч без него.
        ///
        /// Переподключившийся посреди катки персонажа не выбирает: экран
        /// выбора живёт в хабе, а «Полная игра» едет из мини-игры сразу в
        /// мини-игру, минуя хаб. Без этого человек оставался зрителем до конца
        /// всей катки — ровно то, что видели на прогоне вшестером.
        ///
        /// Только в сцене мини-игры: в хабе выбор персонажа делает человек
        /// сам, и выдавать ему случайного за две секунды до экрана выбора
        /// было бы хуже болезни.
        /// </summary>
        private void EnsureBodiesForRound(string sceneName)
        {
            if (MinigameControllerBase.Current == null)
            {
                return;
            }

            // Через общий реестр, а не GetComponent: выбор персонажа живёт на
            // сетевом объекте NetworkSessionManager, а размещатель — на самом
            // NetworkManager. Это разные объекты, и соседями они не бывают.
            var selection = CharacterSelection.Current as CharacterSelectionManager;
            if (selection == null)
            {
                return;
            }

            int given = selection.ServerGrantMissingBodies();
            if (given > 0)
            {
                Debug.Log($"🎭 [{sceneName}] выдано тел тем, кто зашёл без персонажа: {given}");
            }
        }

        private void PlaceAllPlayers(string sceneName)
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            if (FindFirstObjectByType<SpawnPointSet>() == null)
            {
                Debug.Log($"📍 [{sceneName}] точек спавна нет — игроки остаются где были");
                return;
            }

            int count = PlacementCount();
            for (int i = 0; i < count; i++)
            {
                PlaceAt(PlayerIdAt(i), i, count);
            }

            Debug.Log($"📍 [{sceneName}] игроки расставлены по SpawnPoint ({count})");
        }

        private void PlacePlayer(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            int count = PlacementCount();
            for (int i = 0; i < count; i++)
            {
                if (PlayerIdAt(i) == clientId)
                {
                    PlaceAt(clientId, i, count);
                    return;
                }
            }
        }

        /// <summary>
        /// Сколько мест раздаётся и в каком порядке.
        ///
        /// Порядок берётся у ростера сессии, а не у <c>ConnectedClientsIds</c>,
        /// намеренно: по ростеру раздают свои места и сами мини-игры
        /// (клетки в «Секундомере», полки в «Банках», команды в «Переноске»).
        /// Два разных порядка давали разъезд — человека ставили к той клетке,
        /// которую игра для него не включала, и он падал в яму мимо всего.
        /// </summary>
        private int PlacementCount()
        {
            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            return scoreboard != null ? scoreboard.Players.Count : networkManager.ConnectedClientsIds.Count;
        }

        private ulong PlayerIdAt(int index)
        {
            ISessionScoreboard scoreboard = SessionScoreboard.Current;
            if (scoreboard != null)
            {
                return (ulong)scoreboard.Players[index].Id;
            }

            return networkManager.ConnectedClientsIds[index];
        }

        private void PlaceAt(ulong clientId, int index, int count)
        {
            var spawnPoints = FindFirstObjectByType<SpawnPointSet>();
            if (spawnPoints == null)
            {
                return;
            }

            NetworkObject playerObject = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject == null)
            {
                return;
            }

            SpawnPoint point = spawnPoints.GetSpreadPoint(defaultRole, index, count);
            if (point == null)
            {
                return;
            }

            var relay = playerObject.GetComponent<IWorldEffectRelay>();
            if (relay != null)
            {
                relay.TryRelayTeleport(point.transform.position, point.transform.rotation);
            }

            if (playerObject.TryGetComponent(out PlayerRespawner respawner))
            {
                respawner.SetRespawnPoint(point.transform);
            }
        }
    }
}
