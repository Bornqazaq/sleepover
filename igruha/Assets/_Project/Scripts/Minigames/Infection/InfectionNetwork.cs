using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Igruha.Minigames.Infection
{
    /// <summary>
    /// Состояние одного игрока в сетевом виде. NetworkList умеет только
    /// unmanaged-типы, а <see cref="InfectionState"/> — компонент со ссылками
    /// на сцену, реплицировать его нечем.
    ///
    /// Едет только то, что видно на машине игрока: фаза заражения и два флага
    /// (Нулевой, грейс). Чистое время и счётчик личных заражений остаются на
    /// сервере — по ним он раскладывает места сам, клиенту они не нужны, и
    /// каждую десятую секунды их гонять не за чем.
    /// </summary>
    public struct InfectionNetState : INetworkSerializable, IEquatable<InfectionNetState>
    {
        public int PlayerId;
        public byte Phase;
        public byte Flags;

        public const byte FlagPatientZero = 1 << 0;
        public const byte FlagInGrace = 1 << 1;

        public bool PatientZero => (Flags & FlagPatientZero) != 0;
        public bool InGrace => (Flags & FlagInGrace) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Flags);
        }

        public bool Equals(InfectionNetState other) =>
            PlayerId == other.PlayerId && Phase == other.Phase && Flags == other.Flags;
    }

    /// <summary>
    /// Сетевая половина «Заражения»: вешается на тот же объект, что и
    /// <see cref="InfectionMinigame"/>. Правила остаются обычным MonoBehaviour
    /// и работают без этого компонента, когда сцену открывают напрямую —
    /// образец у <see cref="Igruha.Networking.NetworkMinigameBridge"/>, который
    /// рядом реплицирует фазу, таймер и итоговые места. Своего канала под них
    /// здесь нет намеренно.
    ///
    /// Что реплицируется здесь: фаза заражения каждого игрока (для краски,
    /// мигания и счётчика Чистых) и реплики диктора (иначе их слышал бы только
    /// хост). Исход — кто Нулевой, кто кого заразил, кому какое место — целиком
    /// решает сервер; клиенту едет результат, а не право его менять.
    /// </summary>
    public sealed class InfectionNetwork : NetworkBehaviour
    {
        private readonly NetworkList<InfectionNetState> states = new NetworkList<InfectionNetState>();

        private InfectionMinigame game;
        private bool statesDirty;

        /// <summary>Ушедшие, которых сервер ещё не разобрал: копим до Update, как у «Ангелов».</summary>
        private readonly List<ulong> pendingLeavers = new List<ulong>(4);

        /// <summary>Буфер состава для клиента — чтобы не аллоцировать на каждой дельте списка.</summary>
        private readonly List<int> rosterBuffer = new List<int>(8);

        /// <summary>Идёт сетевая катка и эта половина живая.</summary>
        public bool IsActive => IsSpawned;

        private void Awake()
        {
            game = GetComponent<InfectionMinigame>();
            if (game == null)
            {
                Debug.LogError($"{name}: InfectionNetwork не нашёл InfectionMinigame на своём объекте", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            states.OnListChanged += OnStatesChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            else
            {
                // Подключились в середине раунда — догоняем то, что уже решено.
                statesDirty = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            states.OnListChanged -= OnStatesChanged;
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        /// <summary>Reapply the received snapshot after local avatar binding or round reset.</summary>
        public void RefreshClientPresentation() => statesDirty = true;

        private void Update()
        {
            // NetworkList raises one callback per Add. Applying a partial roster
            // immediately removes avatars which are present in later deltas.
            // Consume the complete packet once, after local avatar binding.
            if (!IsServer)
            {
                if (statesDirty && states.Count > 0 && game != null && game.LocalRosterReady)
                {
                    statesDirty = false;
                    ApplyAllStates();
                }
                return;
            }
            if (!IsServer || pendingLeavers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < pendingLeavers.Count; i++)
            {
                game?.HandlePlayerDisconnected((int)pendingLeavers[i]);
            }

            pendingLeavers.Clear();
        }

        // ========== СОСТАВ ==========

        /// <summary>Синхронизировать состав: добавить новых, убрать ушедших. Только сервер.</summary>
        public void ServerSyncRoster(IReadOnlyList<int> playerIds)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            for (int i = states.Count - 1; i >= 0; i--)
            {
                if (!Contains(playerIds, states[i].PlayerId))
                {
                    states.RemoveAt(i);
                }
            }

            for (int i = 0; i < playerIds.Count; i++)
            {
                if (IndexOf(playerIds[i]) >= 0)
                {
                    continue;
                }

                states.Add(new InfectionNetState
                {
                    PlayerId = playerIds[i],
                    Phase = (byte)InfectionPhase.Clean,
                    Flags = 0
                });
            }
        }

        /// <summary>
        /// Записать состояние игрока, если оно изменилось. Сравнение обязательно:
        /// присваивание элемента NetworkList шлёт дельту без проверки, и запись
        /// тем же значением каждый такт превратилась бы в постоянный трафик.
        /// </summary>
        public void ServerSyncState(int playerId, InfectionPhase phase, bool patientZero, bool inGrace)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            int index = IndexOf(playerId);
            if (index < 0)
            {
                return;
            }

            byte flags = 0;
            if (patientZero) flags |= InfectionNetState.FlagPatientZero;
            if (inGrace) flags |= InfectionNetState.FlagInGrace;

            InfectionNetState state = states[index];
            if (state.Phase == (byte)phase && state.Flags == flags)
            {
                return;
            }

            state.Phase = (byte)phase;
            state.Flags = flags;
            states[index] = state;
        }

        private void OnStatesChanged(NetworkListEvent<InfectionNetState> evt)
        {
            if (IsServer)
            {
                return;
            }

            statesDirty = true;
        }

        private void ApplyAllStates()
        {
            if (game == null)
            {
                return;
            }

            rosterBuffer.Clear();
            for (int i = 0; i < states.Count; i++)
            {
                rosterBuffer.Add(states[i].PlayerId);
            }

            game.ApplyNetworkRoster(rosterBuffer);

            for (int i = 0; i < states.Count; i++)
            {
                InfectionNetState s = states[i];
                game.ApplyNetworkState(s.PlayerId, (InfectionPhase)s.Phase, s.PatientZero, s.InGrace);
            }
        }

        // ========== ДИКТОР ==========

        /// <summary>Разослать реплику диктора всем. Только сервер.</summary>
        public void ServerAnnounce(byte code, int playerId)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            AnnounceClientRpc(code, playerId);
        }

        [ClientRpc]
        private void AnnounceClientRpc(byte code, int playerId)
        {
            // Хост уже показал реплику локально (сервер зовёт свой ApplyAnnounce
            // напрямую), поэтому здесь — только удалённые клиенты.
            if (IsServer)
            {
                return;
            }

            game?.ApplyAnnounce(code, playerId);
        }

        // ========== ДИСКОННЕКТ ==========

        private void OnClientDisconnected(ulong clientId)
        {
            if (IsServer)
            {
                pendingLeavers.Add(clientId);
            }
        }

        // ========== ХЕЛПЕРЫ ==========

        private int IndexOf(int playerId)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].PlayerId == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Contains(IReadOnlyList<int> ids, int value)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
