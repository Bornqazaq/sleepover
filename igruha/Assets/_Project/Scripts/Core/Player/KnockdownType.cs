namespace Igruha.Core.Player
{
    /// <summary>
    /// Как персонаж падает от удара. Определяется направлением импульса
    /// относительно его взгляда и выбирает пару клипов «падение → подъём».
    /// </summary>
    public enum KnockdownType
    {
        /// <summary>Прилетело со спины: падение вперёд, подъём с живота.</summary>
        FallForward,

        /// <summary>Прилетело в лицо: отлёт назад, подъём со спины.</summary>
        FlyBack
    }
}
