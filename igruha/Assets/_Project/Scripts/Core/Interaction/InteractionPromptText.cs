namespace Igruha.Core.Interaction
{
    /// <summary>
    /// Общая форма подсказок. IInteractable возвращает готовую строку вместе
    /// с клавишей; HUD её не дополняет. Динамические названия кэшируются у
    /// источника, чтобы опрос подсказки каждый кадр не создавал строки.
    /// </summary>
    public static class InteractionPromptText
    {
        public const string Hold = "Зажми E — ";
        public const string Release = "Отпусти E — ";
        public const string Click = "ЛКМ — ";
    }
}
