using UnityEngine.InputSystem;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Клавиши голосового чата.
    ///
    /// Читаются напрямую с клавиатуры, а не через <c>InputSystem_Actions</c>,
    /// и это сделано намеренно. Ассет привязок заморожен (раздел 0 в
    /// <c>igruha/CLAUDE.md</c>): каждая новая привязка в нём уже однажды
    /// всплыла случайным приседом посреди боя. Голосу хватает трёх клавиш,
    /// свободных во всей игре, и так же — прямым чтением клавиатуры — в
    /// проекте уже работают пауза и панель быстрых фраз.
    ///
    /// Раскладка: <b>M</b> — микрофон вкл/выкл, <b>V</b> — говорить, пока
    /// зажата, <b>F4</b> — настройки голоса.
    /// </summary>
    public static class VoiceKeys
    {
        /// <summary>Нажата ли в этом кадре клавиша выключения микрофона.</summary>
        public static bool MuteToggled => Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame;

        /// <summary>Зажата ли клавиша рации.</summary>
        public static bool PushToTalkHeld => Keyboard.current != null && Keyboard.current.vKey.isPressed;

        /// <summary>Нажата ли в этом кадре клавиша панели настроек.</summary>
        public static bool SettingsToggled => Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame;

        /// <summary>Подсказка для экрана — одной строкой.</summary>
        public const string Hint = "M — микрофон, V — говорить, F4 — громкость и настройки";
    }
}
