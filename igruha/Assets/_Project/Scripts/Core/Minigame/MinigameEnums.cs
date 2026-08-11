namespace Igruha.Core.Minigame
{
    /// <summary>Категория мини-игры по GDD 4.1 — для фильтрации пула по составу лобби.</summary>
    public enum MinigameCategory
    {
        FreeForAll,
        Team,
        Asymmetric
    }

    /// <summary>
    /// Режим камеры мини-игры (GDD 9.2). Сегодня реализован только ThirdPerson;
    /// TopDown и Fixed — точки расширения.
    /// </summary>
    public enum CameraMode
    {
        ThirdPerson,
        TopDown,
        Fixed
    }
}
