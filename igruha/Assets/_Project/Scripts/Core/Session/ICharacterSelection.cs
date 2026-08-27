using System;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Выбор персонажа в сетевой катке — со стороны интерфейса.
    ///
    /// Core не знает про сетевой слой: сборки разведены, и ссылка идёт только
    /// в одну сторону. Поэтому экран выбора и загрузчик хаба разговаривают с
    /// сервером через этот интерфейс, а реализация живёт в Networking и
    /// регистрирует себя сама — тем же приёмом, что и табло катки
    /// (<see cref="SessionScoreboard"/>).
    ///
    /// Решает всё равно сервер: <see cref="Choose"/> отправляет намерение, а не
    /// применяет его. Занятость персонажа клиент тоже не считает сам — он
    /// читает то, что прислал сервер.
    /// </summary>
    public interface ICharacterSelection
    {
        /// <summary>Персонажа уже забрал кто-то другой.</summary>
        bool IsTaken(int characterIndex);

        /// <summary>
        /// У этого участника персонаж закреплён. Тем, у кого нет, тела не
        /// дают: они подключились посреди матча и досматривают его со стороны.
        /// </summary>
        bool HasCharacter(int playerId);

        /// <summary>Эта машина свой выбор уже сделала — сама или по истечении срока.</summary>
        bool HasChosen { get; }

        /// <summary>
        /// Сколько секунд осталось на выбор. Считается локально и только ради
        /// надписи: срок стережёт сервер, и расхождение в доли секунды на
        /// исход не влияет.
        /// </summary>
        float SecondsLeft { get; }

        /// <summary>Отправить серверу намерение взять персонажа.</summary>
        void Choose(int characterIndex);

        /// <summary>Сказать серверу, что экран показан и отсчёт можно начинать.</summary>
        void ReportReady();

        /// <summary>Занятые изменились либо выбор за эту машину уже сделан.</summary>
        event Action Changed;
    }

    /// <summary>Точка доступа к выбору персонажа текущей сетевой катки.</summary>
    public static class CharacterSelection
    {
        public static ICharacterSelection Current { get; private set; }

        public static void Register(ICharacterSelection selection) => Current = selection;

        public static void Unregister(ICharacterSelection selection)
        {
            if (Current == selection)
            {
                Current = null;
            }
        }
    }
}
