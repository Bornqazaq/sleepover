using UnityEngine.SceneManagement;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Оформление интерфейса одной строкой: общий HUD плюс панели самой игры.
    ///
    /// Зовётся в конце сборки арены каждой мини-игры. Причина та же, по которой
    /// кодом собирается всё остальное: пересборка обязана воспроизводить сцену
    /// целиком. Иначе первая же пересборка арены вернёт серые прямоугольники,
    /// и виноватым будет выглядеть оформление, а не порядок вызовов.
    /// </summary>
    internal static class UiSkinPass
    {
        internal static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            HudSkin.Build(scene);
            PanelSkin.Apply(scene);
        }
    }
}
