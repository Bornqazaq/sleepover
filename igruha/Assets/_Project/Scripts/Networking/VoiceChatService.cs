using System.Collections.Generic;
using Unity.Collections;
using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Voice;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевой слой голосового чата: доставка кадров через тот же транспорт,
    /// которым ходит вся игра.
    ///
    /// Голос идёт именными сообщениями (<c>CustomMessagingManager</c>), а не
    /// RPC, и это не мелочь. RPC живёт на <c>NetworkObject</c>, то есть
    /// потребовал бы сетевого префаба в списке <c>NetworkConfig</c> — а
    /// расхождение этого списка между машинами даёт <c>NetworkConfig
    /// mismatch</c>, на котором в проекте уже терялись часы. Именное сообщение
    /// не требует ни префаба, ни объекта в сцене: голос включается сам в хабе
    /// и во всех мини-играх, включая чужую сцену напарника.
    ///
    /// Доставка ненадёжная и без гарантии порядка — единственно верная для
    /// голоса. Переспрашивать потерянный кадр бессмысленно: пока он доедет,
    /// его место в разговоре давно прошло, а очередь надёжной доставки к тому
    /// же задержала бы все следующие.
    ///
    /// Авторитет сервера соблюдён: клиент шлёт голос только серверу, никогда
    /// другому клиенту. Сервер проверяет размер пакета и частоту, решает, кому
    /// его раздать, и только он рассылает. Клиент не может ни заглушить
    /// другого, ни заговорить от чужого имени — имя говорящего в пакет
    /// проставляет сервер.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChatService : MonoBehaviour
    {
        private const string UpstreamMessage = "igruha.voice.up";
        private const string DownstreamMessage = "igruha.voice.down";

        /// <summary>Потолок пакетов в секунду с одной машины. Штатно их 25.</summary>
        private const int PacketsPerSecondLimit = 60;

        private readonly Dictionary<ulong, RateWindow> rates = new Dictionary<ulong, RateWindow>(8);
        private readonly List<ulong> targets = new List<ulong>(8);

        private byte[] receiveBuffer = new byte[VoiceFormat.EncodedFrameBytes];
        private VoiceChatRuntime runtime;
        private bool serverHandlerRegistered;
        private bool clientHandlerRegistered;
        private bool active;

        /// <summary>
        /// Голос поднимается сам, без объекта в сцене: сцены грузятся в режиме
        /// Single, и всё, что лежит в сцене, умирает на переходе к следующей
        /// мини-игре.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Стенду из восьми headless-процессов голос не нужен и микрофона у него нет.
            if (Application.isBatchMode) return;
            if (VoiceChatRuntime.Instance != null) return;

            var host = new GameObject("VoiceChat");
            DontDestroyOnLoad(host);
            host.AddComponent<VoiceChatRuntime>();
            host.AddComponent<VoiceChatHud>();
            host.AddComponent<VoiceChatService>();
        }

        private void Start()
        {
            runtime = GetComponent<VoiceChatRuntime>();
            runtime.FrameEncoded += OnFrameEncoded;

            // Виртуальные игроки Play Mode делят с редактором один микрофон:
            // пусть слушают, но не пишут, иначе стенд воет от эха.
            if (Application.isEditor && !CurrentPlayer.IsMainEditor) runtime.CaptureAllowed = false;
        }

        private void OnDestroy()
        {
            if (runtime != null) runtime.FrameEncoded -= OnFrameEncoded;
            Deactivate();
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool ready = manager != null && manager.IsListening && (manager.IsServer || manager.IsConnectedClient);

            if (ready && !active) Activate(manager);
            else if (!ready && active) Deactivate();
        }

        private void Activate(NetworkManager manager)
        {
            CustomMessagingManager messaging = manager.CustomMessagingManager;
            if (messaging == null) return;

            if (manager.IsServer && !serverHandlerRegistered)
            {
                messaging.RegisterNamedMessageHandler(UpstreamMessage, OnUpstream);
                serverHandlerRegistered = true;
            }

            if (manager.IsClient && !clientHandlerRegistered)
            {
                messaging.RegisterNamedMessageHandler(DownstreamMessage, OnDownstream);
                clientHandlerRegistered = true;
            }

            if (manager.IsServer) manager.OnClientDisconnectCallback += OnClientLeft;

            active = true;
            runtime.NetworkActive = true;
            Debug.Log("🔊 голосовой чат включён: " + (manager.IsServer ? "сервер раздаёт голос" : "клиент на приёме"));
        }

        private void Deactivate()
        {
            NetworkManager manager = NetworkManager.Singleton;
            CustomMessagingManager messaging = manager != null ? manager.CustomMessagingManager : null;

            if (messaging != null)
            {
                if (serverHandlerRegistered) messaging.UnregisterNamedMessageHandler(UpstreamMessage);
                if (clientHandlerRegistered) messaging.UnregisterNamedMessageHandler(DownstreamMessage);
            }

            if (manager != null) manager.OnClientDisconnectCallback -= OnClientLeft;

            serverHandlerRegistered = false;
            clientHandlerRegistered = false;
            rates.Clear();

            if (!active) return;
            active = false;
            if (runtime != null) runtime.NetworkActive = false;
        }

        private void OnClientLeft(ulong clientId)
        {
            rates.Remove(clientId);
            if (runtime != null) runtime.RemoveSpeaker(clientId);
        }

        /// <summary>Свой кадр: хост раздаёт его сам, клиент отправляет серверу.</summary>
        private void OnFrameEncoded(ushort sequence, byte[] data, int length)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (!active || manager == null || length != VoiceFormat.EncodedFrameBytes) return;

            if (manager.IsServer)
            {
                Relay(manager, manager.LocalClientId, sequence, data, 0, length);
                return;
            }

            if (!manager.IsConnectedClient) return;

            using var writer = new FastBufferWriter(VoiceFormat.UpstreamPacketBytes, Allocator.Temp);
            writer.WriteValueSafe(sequence);
            writer.WriteBytesSafe(data, length);
            manager.CustomMessagingManager.SendNamedMessage(
                UpstreamMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
        }

        /// <summary>Клиент прислал голос. Всё, что здесь делается, — проверка и раздача.</summary>
        private void OnUpstream(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return;

            if (reader.Length - reader.Position != VoiceFormat.UpstreamPacketBytes) return;
            if (!AllowPacket(senderClientId)) return;

            reader.ReadValueSafe(out ushort sequence);
            reader.ReadBytesSafe(ref receiveBuffer, VoiceFormat.EncodedFrameBytes);

            Relay(manager, senderClientId, sequence, receiveBuffer, 0, VoiceFormat.EncodedFrameBytes);
        }

        /// <summary>Сервер прислал чужой голос.</summary>
        private void OnDownstream(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            // Голос приходит только от сервера: чужой отправитель — чужая игра.
            if (senderClientId != NetworkManager.ServerClientId) return;
            if (reader.Length - reader.Position != VoiceFormat.DownstreamPacketBytes) return;

            reader.ReadValueSafe(out ulong speakerId);
            reader.ReadValueSafe(out ushort sequence);
            reader.ReadBytesSafe(ref receiveBuffer, VoiceFormat.EncodedFrameBytes);

            if (speakerId == manager.LocalClientId) return;
            runtime.ReceiveFrame(speakerId, sequence, receiveBuffer, 0, VoiceFormat.EncodedFrameBytes);
        }

        /// <summary>
        /// Раздать кадр всем, кроме самого говорящего: свой голос в наушниках
        /// с задержкой — верный способ сбиться с мысли на полуслове.
        /// </summary>
        private void Relay(NetworkManager manager, ulong speakerId, ushort sequence, byte[] data, int offset, int length)
        {
            targets.Clear();

            IReadOnlyList<ulong> clients = manager.ConnectedClientsIds;
            for (int i = 0; i < clients.Count; i++)
            {
                ulong id = clients[i];
                if (id == speakerId) continue;

                if (id == manager.LocalClientId)
                {
                    // Хост слушает без сети — пакет уже дома.
                    runtime.ReceiveFrame(speakerId, sequence, data, offset, length);
                    continue;
                }

                targets.Add(id);
            }

            if (targets.Count == 0) return;

            using var writer = new FastBufferWriter(VoiceFormat.DownstreamPacketBytes, Allocator.Temp);
            writer.WriteValueSafe(speakerId);
            writer.WriteValueSafe(sequence);
            writer.WriteBytesSafe(data, length, offset);
            manager.CustomMessagingManager.SendNamedMessage(DownstreamMessage, targets, writer, NetworkDelivery.Unreliable);
        }

        /// <summary>
        /// Не даём одной машине занять канал: штатно кадров 25 в секунду, и
        /// всё, что сверх потолка, отбрасывается, не доходя до остальных.
        /// </summary>
        private bool AllowPacket(ulong clientId)
        {
            float now = Time.unscaledTime;
            if (!rates.TryGetValue(clientId, out RateWindow window) || now - window.StartedAt >= 1f)
            {
                rates[clientId] = new RateWindow { StartedAt = now, Count = 1 };
                return true;
            }

            if (window.Count >= PacketsPerSecondLimit) return false;

            window.Count++;
            rates[clientId] = window;
            return true;
        }

        private struct RateWindow
        {
            public float StartedAt;
            public int Count;
        }
    }
}
