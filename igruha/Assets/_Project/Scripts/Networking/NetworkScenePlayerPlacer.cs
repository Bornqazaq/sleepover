using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Player;
using Igruha.Core.Spawning;

namespace Igruha.Networking
{
    /// <summary>
    /// После загрузки сцены сервер ставит уже существующих игроков
    /// на SpawnPoint текущей арены. Круг из Connection Approval — только
    /// запасной вариант, пока сцены ещё нет (Boot).
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

        private void OnLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            PlaceAllPlayers(sceneName);
        }

        private void OnClientConnected(ulong clientId)
        {
            // Опоздавший клиент не попадает в OnLoadEventCompleted — сцена уже загружена.
            PlacePlayer(clientId);
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

            var ids = networkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                PlacePlayer(ids[i]);
            }

            Debug.Log($"📍 [{sceneName}] игроки расставлены по SpawnPoint ({ids.Count})");
        }

        private void PlacePlayer(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            var spawnPoints = FindFirstObjectByType<SpawnPointSet>();
            if (spawnPoints == null)
            {
                return;
            }

            int index = IndexOf(clientId);
            if (index < 0)
            {
                return;
            }

            NetworkObject playerObject = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject == null)
            {
                return;
            }

            SpawnPoint point = spawnPoints.GetSpreadPoint(defaultRole, index, networkManager.ConnectedClientsIds.Count);
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

        private int IndexOf(ulong clientId)
        {
            var ids = networkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == clientId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
