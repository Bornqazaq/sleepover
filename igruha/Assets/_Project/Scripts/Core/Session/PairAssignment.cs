using System;
using System.Collections.Generic;

namespace Igruha.Core.Session
{
    /// <summary>
    /// Деление состава раунда на пары и — если состав нечётный — одиночку.
    ///
    /// Общее: парных игр в проекте будет несколько (Дырка в стене, Переноска
    /// предмета, Крокодил), а держать делёж внутри одной из них значит написать
    /// его трижды.
    ///
    /// <c>TeamAssignment</c> здесь не годится и не трогается: он делит лобби
    /// на две команды через одного, а тут нужны пары по двое.
    /// </summary>
    /// <remarks>
    /// <b>Делит сервер, детерминированно.</b> Входа два — состав раунда
    /// и серверный сид, — и оба одинаковы на всех машинах, поэтому результат
    /// не зависит от того, где его посчитали. Сид берётся <c>System.Random</c>,
    /// а не <c>UnityEngine.Random</c>: у второго состояние глобальное, и любой
    /// посторонний вызов посреди раунда сдвинул бы раскладку.
    ///
    /// <b>Двое — особый случай: пара не формируется.</b> Иначе единственная
    /// пара играла бы против пустоты, и место было бы одно на двоих.
    /// </remarks>
    public static class PairAssignment
    {
        /// <summary>Второго в паре нет — это одиночка.</summary>
        public const int NoPlayer = -1;

        /// <summary>
        /// Пара участников либо одиночка. Одна пара — одна дорожка: кто где
        /// стоит, решает уже мини-игра.
        /// </summary>
        public readonly struct Pair
        {
            public int FirstId { get; }

            /// <summary>Напарник либо <see cref="NoPlayer"/>.</summary>
            public int SecondId { get; }

            public Pair(int firstId, int secondId)
            {
                FirstId = firstId;
                SecondId = secondId;
            }

            /// <summary>Играет один: напарника не досталось.</summary>
            public bool IsSolo => SecondId == NoPlayer;

            /// <summary>Сколько человек в этой связке: один или двое.</summary>
            public int Size => IsSolo ? 1 : 2;

            /// <summary>Участник по месту внутри связки. Минус один — такого места нет.</summary>
            public int MemberAt(int index)
            {
                if (index == 0)
                {
                    return FirstId;
                }

                return index == 1 ? SecondId : NoPlayer;
            }

            /// <summary>Есть ли в связке этот участник.</summary>
            public bool Contains(int playerId) => FirstId == playerId || SecondId == playerId;
        }

        /// <summary>
        /// Рабочий буфер тасования. Статический, потому что делёж происходит
        /// раз в раунд и в одном потоке, а плодить список на каждый раунд
        /// незачем.
        /// </summary>
        private static readonly List<int> ShuffleBuffer = new List<int>(8);

        /// <summary>
        /// Сколько дорожек нужно такому составу.
        ///
        /// | Игроков | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
        /// | Дорожек | 2 | 2 | 2 | 3 | 3 | 4 | 4 |
        /// </summary>
        public static int TrackCount(int playerCount)
        {
            if (playerCount <= 0)
            {
                return 0;
            }

            // Двое играют одиночками, каждый на своей дорожке, — потому и два.
            return playerCount == 2 ? 2 : (playerCount + 1) / 2;
        }

        /// <summary>
        /// Разложить состав на пары. Заполняет переданный список, а не
        /// возвращает новый: делёж идёт раз в раунд, но плодить мусор незачем,
        /// а вызывающему список всё равно держать у себя.
        /// </summary>
        public static void Assign(IReadOnlyList<SessionPlayer> players, int seed, List<Pair> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();

            if (players == null || players.Count == 0)
            {
                return;
            }

            ShuffleBuffer.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                ShuffleBuffer.Add(players[i].Id);
            }

            Shuffle(ShuffleBuffer, seed);

            // Двое: пары нет вовсе, оба идут одиночками.
            if (ShuffleBuffer.Count == 2)
            {
                destination.Add(new Pair(ShuffleBuffer[0], NoPlayer));
                destination.Add(new Pair(ShuffleBuffer[1], NoPlayer));
                return;
            }

            int index = 0;
            while (index + 1 < ShuffleBuffer.Count)
            {
                destination.Add(new Pair(ShuffleBuffer[index], ShuffleBuffer[index + 1]));
                index += 2;
            }

            // Нечётный последний остаётся одиночкой.
            if (index < ShuffleBuffer.Count)
            {
                destination.Add(new Pair(ShuffleBuffer[index], NoPlayer));
            }
        }

        /// <summary>Тасование Фишера — Йетса от серверного сида: одинаковый вход даёт одинаковый выход.</summary>
        private static void Shuffle(List<int> ids, int seed)
        {
            var random = new Random(seed);
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }
        }
    }
}
