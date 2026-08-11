using Igruha.Core.Player;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Участник катки. Данные, которые позже мигрируют в сетевое состояние
    /// (NetworkVariable/NetworkList), поэтому — отдельная структура,
    /// а не поля разбросанные по MonoBehaviour'ам.
    /// </summary>
    public sealed class SessionPlayer
    {
        public int Id { get; }
        public string DisplayName { get; }
        public PlayerController Avatar { get; set; }
        public int Score { get; set; }

        public SessionPlayer(int id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }
}
