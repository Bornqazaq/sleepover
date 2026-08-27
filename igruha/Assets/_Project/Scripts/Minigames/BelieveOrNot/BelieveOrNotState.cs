namespace Igruha.Minigames.BelieveOrNot
{
    /// <summary>Место коробки на столе. Привязано к стулу, а не к самой коробке.</summary>
    public enum BoxSlot : byte
    {
        None = 0,

        /// <summary>Ближняя к тому, кто сидит на первом месте.</summary>
        Seat0 = 1,

        /// <summary>Ближняя к тому, кто сидит на втором месте.</summary>
        Seat1 = 2
    }

    /// <summary>Что лежит в коробке.</summary>
    public enum BelieveCard : byte
    {
        Unknown = 0,

        /// <summary>Галочка — победа.</summary>
        Win = 1,

        /// <summary>Крест — поражение.</summary>
        Lose = 2
    }

    /// <summary>Что решил Решающий.</summary>
    public enum Decision : byte
    {
        /// <summary>Ещё не решил.</summary>
        None = 0,

        /// <summary>Коробки остаются как есть. Это же засчитывается по истечении таймера.</summary>
        Keep = 1,

        /// <summary>Коробки меняются местами.</summary>
        Swap = 2
    }

    /// <summary>
    /// Команда участника. <see cref="None"/> — лобби из 2–3, где команд нет
    /// и счёт только личный (спека 6.3).
    /// </summary>
    public enum TeamId : byte
    {
        None = 0,
        A = 1,
        B = 2
    }

    /// <summary>
    /// Стадии одного кона. Значение — то самое <c>byte</c>, которым оперирует
    /// <see cref="Igruha.Core.Minigame.MinigameStageState"/>: Core не знает,
    /// что за стадия, он знает только момент перехода.
    ///
    /// Ноль занят под <c>MinigameStageState.NoStage</c> и здесь не используется.
    /// </summary>
    public static class BelieveStage
    {
        /// <summary>Рассадка: перенос за стол, блокировки, камера.</summary>
        public const byte Seating = 1;

        /// <summary>Показ карточки Знающему — и только ему.</summary>
        public const byte Peek = 2;

        /// <summary>Уговоры. Закрывается досрочно решением Решающего.</summary>
        public const byte Persuasion = 3;

        /// <summary>Обмен коробок (если меняли) и одновременное раскрытие крышек.</summary>
        public const byte Reveal = 4;

        /// <summary>Пауза на реакцию: звук, гэг, обновление счёта.</summary>
        public const byte Reaction = 5;
    }

    /// <summary>
    /// Состояние матча одной структурой. Собрано так намеренно: в фазе 3 это
    /// ровно то, что уедет в <c>NetworkVariable</c> целиком, а не россыпь полей
    /// MonoBehaviour, которую пришлось бы синхронизировать по одному.
    ///
    /// Содержимого коробок здесь нет и быть не должно — оно живёт только
    /// в серверном поле контроллера и не покидает сервер до раскрытия
    /// (спека 10.3).
    /// </summary>
    public struct BelieveMatchState
    {
        /// <summary>Номер текущего кона, с единицы.</summary>
        public int RoundNumber;

        /// <summary>
        /// Всего конов в матче. Считается один раз на старте и больше
        /// не меняется, даже если кто-то вышел: от числа конов зависит
        /// ротация, а она обязана быть предсказуемой (спека 10.4).
        /// </summary>
        public int TotalRounds;

        /// <summary>
        /// Кто занимает первое место за столом. Само по себе место ничего
        /// не значит, но по нему клиент понимает, чья коробка где стоит
        /// и куда ставить камеру, — а значит, оно обязано быть одинаковым
        /// на всех машинах.
        /// </summary>
        public int Seat0PlayerId;

        /// <summary>Кто занимает второе место за столом.</summary>
        public int Seat1PlayerId;

        /// <summary>Кто сейчас Знающий. Всегда один из двух сидящих.</summary>
        public int KnowerPlayerId;

        /// <summary>Кто сейчас Решающий. Второй из двух сидящих.</summary>
        public int DeciderPlayerId;

        /// <summary>Сколько конов выиграла команда A. При 2–3 игроках не используется.</summary>
        public int TeamAWins;

        /// <summary>Сколько конов выиграла команда B. При 2–3 игроках не используется.</summary>
        public int TeamBWins;

        /// <summary>Что решил Решающий в текущем коне.</summary>
        public Decision Decision;

        /// <summary>Кон разрешён: исход посчитан, крышки открыты.</summary>
        public bool Resolved;

        /// <summary>
        /// Кон отменён: Знающий ушёл до решения, очко не начисляется
        /// (спека 10.4).
        /// </summary>
        public bool Cancelled;
    }

    /// <summary>
    /// Строка участника. Все поля — простые типы: структура обязана остаться
    /// unmanaged, иначе её не положить в <c>NetworkList</c> в фазе 3.
    /// </summary>
    public struct BelieveEntry
    {
        public int PlayerId;

        /// <summary>Команда. <see cref="TeamId.None"/> при лобби из 2–3.</summary>
        public TeamId Team;

        /// <summary>Сколько конов выиграл лично: сидел за столом и остался с галочкой.</summary>
        public int RoundsWon;

        /// <summary>
        /// Из них — сколько выиграл в роли Решающего. Второй критерий
        /// тайбрейка: выиграть вслепую заметно труднее.
        /// </summary>
        public int DeciderWins;

        /// <summary>Сколько раз садился за стол. Третий критерий тайбрейка, и он «чем меньше, тем лучше».</summary>
        public int RoundsSeated;

        /// <summary>
        /// Когда одержана последняя личная победа, по общим часам. Четвёртый
        /// критерий тайбрейка. В отличие от «Экзамена» коллизий тут не бывает:
        /// кон выигрывает ровно один игрок, и метка у каждой победы своя.
        /// </summary>
        public double LastWonAt;

        /// <summary>Игрок ещё в матче. Выбывания в этой игре нет — это про дисконнект.</summary>
        public bool Present;
    }
}
