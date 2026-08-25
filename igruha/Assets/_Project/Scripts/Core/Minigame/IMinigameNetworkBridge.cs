namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Шов между правилами мини-игры и сетью. Реализация живёт в
    /// Igruha.Networking (NetworkBehaviour на том же объекте, что контроллер),
    /// поэтому правила игры остаются обычным MonoBehaviour и работают в
    /// одиночной сцене, где моста просто нет.
    /// </summary>
    public interface IMinigameNetworkBridge
    {
        /// <summary>
        /// Сервер сетевой катки. До спавна авторитет у локальной машины —
        /// иначе сцена, открытая напрямую из редактора, не запустилась бы.
        /// </summary>
        bool HasAuthority { get; }

        /// <summary>
        /// Идёт сетевая катка. Отвечает сразу (не ждёт спавна), потому что
        /// точке входа сцены надо решить, спавнить ли игроков локально,
        /// ещё до того как приедет сетевое состояние.
        /// </summary>
        bool IsNetworkSession { get; }

        void PublishPhase(MinigamePhase phase);

        void PublishResults(MinigameResults results);

        /// <summary>
        /// Отправить серверу намерение выйти из раунда. Кто именно вышел,
        /// сервер берёт из отправителя — верить номеру из сообщения нельзя,
        /// иначе одним нажатием можно было бы выбить чужого.
        /// </summary>
        void RequestLeaveRound();
    }

    /// <summary>
    /// Обратная сторона шва: мост отдаёт контроллеру пришедшее из сети
    /// состояние и читает у него то, что нужно разослать.
    /// </summary>
    public interface IMinigameNetworkTarget
    {
        MinigamePhase Phase { get; }

        void ApplyPhase(MinigamePhase phase);

        void ApplyResults(MinigameResults results);

        /// <summary>Состояние таймера раунда для репликации (false — таймера нет).</summary>
        bool TryGetRoundTime(out float remaining, out float duration);

        /// <summary>Показать время, присланное сервером.</summary>
        void ApplyRoundTime(float remaining, float duration);

        /// <summary>
        /// Участник сам вышел из раунда, оставшись в катке. Приходит только на
        /// сервер и разбирается теми же правилами, что и уход по дисконнекту.
        /// </summary>
        void ApplyLeaveRound(int playerId);
    }
}
