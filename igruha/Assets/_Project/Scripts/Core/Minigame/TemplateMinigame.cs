using Igruha.Core.Minigame;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// «Пустая» мини-игра шаблонной сцены: ничего уникального, места — по
    /// порядку регистрации. Нужна, чтобы MinigameTemplate запускался и игрался:
    /// спавн → обучалка → таймер → результаты.
    /// </summary>
    public sealed class TemplateMinigame : MinigameControllerBase
    {
        protected override void CollectResults(MinigameResults results)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                results.Add(Players[i].Id, i + 1);
            }
        }
    }
}
