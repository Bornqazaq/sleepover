using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Core.Scenes;

namespace Igruha.Networking
{
    public sealed partial class NetworkMinigameBridge
    {
        private NetworkList<int> sceneParticipants;
        private readonly List<int> sceneParticipantMirror = new List<int>(8);
        private readonly NetworkVariable<bool> placementComplete = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> sceneReady = new NetworkVariable<bool>();
        private readonly HashSet<ulong> readyClients = new HashSet<ulong>();
        private readonly HashSet<ulong> pendingDisconnects = new HashSet<ulong>();
        private float readyDeadline;
        private bool quitting;

        public bool PlacementComplete => IsSpawned && placementComplete.Value;
        public bool CanStart => IsSpawned && sceneReady.Value;
        public IReadOnlyList<int> ParticipantIds => sceneParticipantMirror;

        // Только после завершения NGO scene event, выдачи тел и общего телепорта.
        public void CompletePlacement(IReadOnlyList<int> participants)
        {
            if (!IsSpawned || !IsServer || placementComplete.Value) return;
            sceneParticipants.Clear();
            for (int i = 0; i < participants.Count; i++) sceneParticipants.Add(participants[i]);
            readyDeadline = Time.realtimeSinceStartup + 45f;
            placementComplete.Value = true;
            ScenePlacementGate.Open();
            Debug.Log($"[SceneReady] {gameObject.scene.name}: PLACED participants={participants.Count}");
        }

        private void OnSceneParticipantsChanged(NetworkListEvent<int> change) => RefreshSceneParticipants();

        private void RefreshSceneParticipants()
        {
            sceneParticipantMirror.Clear();
            for (int i = 0; i < sceneParticipants.Count; i++) sceneParticipantMirror.Add(sceneParticipants[i]);
        }

        public void ReportLocalReady()
        {
            if (PlacementComplete) SceneReadyServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SceneReadyServerRpc(ServerRpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (!placementComplete.Value || sceneReady.Value || !sceneParticipants.Contains((int)id)) return;
            if (readyClients.Add(id)) Debug.Log($"[SceneReady] {gameObject.scene.name}: ACK client={id}");
        }

        private void UpdateSceneReadiness()
        {
            // Disconnect callbacks run inside NGO shutdown too. Rules and RPCs
            // must only run on a later live frame, never inside those callbacks.
            if (pendingDisconnects.Count > 0)
            {
                var disconnected = new List<ulong>(pendingDisconnects);
                pendingDisconnects.Clear();
                foreach (ulong id in disconnected)
                {
                    if (NetworkManager.ConnectedClients.ContainsKey(id)) continue;
                    sceneParticipants.Remove((int)id);
                    readyClients.Remove(id);
                    tutorialTarget?.RemoveTutorialParticipant((int)id);
                }
            }
            if (!placementComplete.Value || sceneReady.Value) return;
            if (sceneParticipants.Count < 2)
            {
                AbortPreparation("Для начала мини-игры нужны хотя бы два загруженных игрока.");
                return;
            }
            bool allReady = true;
            for (int i = 0; i < sceneParticipants.Count; i++)
            {
                ulong id = (ulong)sceneParticipants[i];
                if (readyClients.Contains(id)) continue;
                allReady = false;
                if (Time.realtimeSinceStartup < readyDeadline) continue;
                if (id == NetworkManager.LocalClientId)
                {
                    AbortPreparation("Хост не смог подготовить персонажей мини-игры.");
                    return;
                }
                // Remove on the next live frame with the session roster.
                NetworkManager.DisconnectClient(id, "Не удалось загрузить персонажей. Подключитесь заново.");
            }
            if (!allReady) return;
            sceneReady.Value = true;
            Debug.Log($"[SceneReady] {gameObject.scene.name}: ALL_READY participants={sceneParticipants.Count}");
        }

        public void AbortPreparation(string reason)
        {
            Debug.LogWarning($"[SceneReady] {gameObject.scene.name}: preparation cancelled: {reason}");
            PartySeries.Reset();
            var handler = NetworkManager != null ? NetworkManager.GetComponent<DisconnectionHandler>() : null;
            if (handler != null) handler.ReturnToMainMenu(reason);
            else if (NetworkManager != null) NetworkManager.Shutdown();
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            pendingDisconnects.Clear();
        }
    }
}
