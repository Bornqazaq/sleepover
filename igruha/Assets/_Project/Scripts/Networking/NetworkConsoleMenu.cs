using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Hub;

namespace Igruha.Networking
{
    /// <summary>
    /// The host publishes TV power, page and library cursor as persistent room state.
    /// Late joiners see the current page. Personal profile requests use CharacterSelectionManager.
    /// </summary>
    public sealed class NetworkConsoleMenu : NetworkBehaviour, IConsoleMenuRelay
    {
        private readonly NetworkVariable<bool> menuOpen = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> cursor = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> page = new NetworkVariable<int>(0);
        private readonly NetworkVariable<Unity.Collections.FixedString128Bytes> seriesSummary = new NetworkVariable<Unity.Collections.FixedString128Bytes>();
        public string SeriesSummary => seriesSummary.Value.ToString();
        public bool HasAuthority => IsServer;
        public void SetPage(ConsolePage value) { if (IsServer) page.Value = (int)value; }
        private void Update()
        {
            if (IsSpawned && IsServer && seriesSummary.Value.ToString() != Igruha.Core.Minigame.PartySeries.Summary)
                seriesSummary.Value = new Unity.Collections.FixedString128Bytes(Igruha.Core.Minigame.PartySeries.Summary);
        }
        private void OnPageChanged(int before, int after) => ApplyToScreen();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            menuOpen.OnValueChanged += OnMenuOpenChanged;
            cursor.OnValueChanged += OnCursorChanged;
            page.OnValueChanged += OnPageChanged;

            // Экран мог быть включён до того, как эта копия появилась:
            // применяем то, что уже лежит в состоянии.
            ConsoleMenu.Relay = this;
            ApplyToScreen();
        }

        public override void OnNetworkDespawn()
        {
            menuOpen.OnValueChanged -= OnMenuOpenChanged;
            cursor.OnValueChanged -= OnCursorChanged;
            page.OnValueChanged -= OnPageChanged;

            if (ReferenceEquals(ConsoleMenu.Relay, this))
            {
                ConsoleMenu.Relay = null;
            }

            base.OnNetworkDespawn();
        }

        public void SetMenuOpen(bool open)
        {
            if (!IsServer)
            {
                return;
            }

            menuOpen.Value = open;
        }

        public void SetCursor(int index)
        {
            if (!IsServer)
            {
                return;
            }

            cursor.Value = index;
        }

        public void SyncScreen() => ApplyToScreen();

        private void OnMenuOpenChanged(bool previous, bool current) => ApplyToScreen();

        private void OnCursorChanged(int previous, int current) => ApplyToScreen();

        /// <summary>
        /// Применить общее состояние к экрану этой машины. Экрана может не
        /// быть вовсе — идёт мини-игра, а не хаб; тогда применять нечего и это
        /// нормальный ход событий, а не ошибка.
        ///
        /// Подсветку ставим раньше включения: иначе первый кадр экран покажет
        /// выбор с прошлого раза.
        /// </summary>
        private void ApplyToScreen()
        {
            ConsoleMenu screen = ConsoleMenu.Active;
            if (screen == null)
            {
                return;
            }

            screen.ApplyPage((ConsolePage)page.Value);
            screen.ApplyCursor(cursor.Value);
            screen.ApplyOpen(menuOpen.Value);
        }
    }
}
