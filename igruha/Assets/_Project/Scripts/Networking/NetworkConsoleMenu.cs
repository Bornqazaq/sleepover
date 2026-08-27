using Unity.Netcode;
using UnityEngine;
using Igruha.Core.Hub;

namespace Igruha.Networking
{
    /// <summary>
    /// Сетевая половина экрана приставки: держит общее состояние меню и
    /// раздаёт его всем.
    ///
    /// Состояния всего два — включён ли экран и что на нём подсвечено, — и оба
    /// это <b>состояние</b>, а не событие: подключившийся посреди выбора обязан
    /// увидеть тот же экран, что и остальные. Поэтому NetworkVariable, а не RPC.
    ///
    /// Пишет только сервер. Клиенту сюда слать нечего: по правилам приставки
    /// выбирает хост, а хост и есть сервер. Значит и валидировать нечего —
    /// нет ни одного пути, которым клиент мог бы тронуть это состояние.
    ///
    /// Живёт на объекте сессии, а не в сцене хаба: хаб перезагружается между
    /// мини-играми, а сессия — нет. Сам экран в новой сцене находит себя сам
    /// через <see cref="ConsoleMenu.Active"/>.
    /// </summary>
    public sealed class NetworkConsoleMenu : NetworkBehaviour, IConsoleMenuRelay
    {
        private readonly NetworkVariable<bool> menuOpen = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> cursor = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool HasAuthority => IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            menuOpen.OnValueChanged += OnMenuOpenChanged;
            cursor.OnValueChanged += OnCursorChanged;

            // Экран мог быть включён до того, как эта копия появилась:
            // применяем то, что уже лежит в состоянии.
            ConsoleMenu.Relay = this;
            ApplyToScreen();
        }

        public override void OnNetworkDespawn()
        {
            menuOpen.OnValueChanged -= OnMenuOpenChanged;
            cursor.OnValueChanged -= OnCursorChanged;

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

            screen.ApplyCursor(cursor.Value);
            screen.ApplyOpen(menuOpen.Value);
        }
    }
}
