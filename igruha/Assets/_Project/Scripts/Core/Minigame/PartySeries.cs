using System;
using System.Collections.Generic;
using Igruha.Core.Scenes;
using Igruha.Core.Session;
using UnityEngine;

namespace Igruha.Core.Minigame
{
    /// <summary>
    /// Серия игр катки — очередь хоста. Переживает смену сцен, каждую сцену
    /// отдаёт один раз. Старт серии сбрасывает счёт и журнал табло; конец
    /// серии фиксирует чемпионов (<see cref="Complete"/>) — до показа
    /// таблицы катки, чтобы на ней корона уже стояла.
    /// </summary>
    public static class PartySeries
    {
        private static readonly Queue<MinigameDefinition> remaining = new Queue<MinigameDefinition>();
        public static bool Active { get; private set; }
        public static int Total { get; private set; }
        public static int Round { get; private set; }
        public static string Summary { get; private set; } = string.Empty;

        /// <summary>Последняя игра серии доиграна, чемпионы зафиксированы.</summary>
        public static bool Completed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            remaining.Clear(); Active = false; Completed = false; Total = Round = 0; Summary = string.Empty;
        }

        public static List<MinigameDefinition> BuildQueue(MinigameCatalog catalog, int count, System.Random random,
            Func<string, bool> available)
        {
            var list = new List<MinigameDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null) return list;
            foreach (var game in catalog.Games)
                if (game != null && !string.IsNullOrEmpty(game.SceneName) && count >= game.MinPlayers &&
                    count <= game.MaxPlayers && available(game.SceneName) && seen.Add(game.SceneName)) list.Add(game);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1); var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
            return list;
        }

        public static int AvailableCount(MinigameCatalog catalog, int count) =>
            BuildQueue(catalog, count, new System.Random(0), IsInBuild).Count;
        private static bool IsInBuild(string scene) => BuildSceneCatalog.TryResolvePath(scene, out _);

        public static bool Start(MinigameCatalog catalog, MinigameLoader loader)
        {
            var session = SessionScoreboard.Current;
            if (!CanStart(session, loader)) return false;
            return Begin(BuildQueue(catalog, session.Players.Count, new System.Random(), IsInBuild), session, loader);
        }

        /// <summary>
        /// Серия из заданных игр в заданном порядке — для стенда автопрогона
        /// (<c>--autostart A,B --series</c>). Тот же путь, что и «Полная
        /// игра», только без перемешивания: результат прогона должен быть
        /// воспроизводим.
        /// </summary>
        public static bool StartWith(IReadOnlyList<MinigameDefinition> games, MinigameLoader loader)
        {
            var session = SessionScoreboard.Current;
            if (games == null || !CanStart(session, loader)) return false;
            int count = session.Players.Count;
            var queue = new List<MinigameDefinition>(games.Count);
            foreach (var game in games)
                if (game != null && !string.IsNullOrEmpty(game.SceneName) && count >= game.MinPlayers &&
                    count <= game.MaxPlayers && IsInBuild(game.SceneName)) queue.Add(game);
            return Begin(queue, session, loader);
        }

        private static bool CanStart(ISessionScoreboard session, MinigameLoader loader)
        {
            if (Active || loader == null || session == null || !session.HasAuthority) return false;
            foreach (var player in session.Players) if (player.Avatar == null) return false;
            return true;
        }

        private static bool Begin(List<MinigameDefinition> queue, ISessionScoreboard session, MinigameLoader loader)
        {
            if (queue.Count == 0) return false;
            Reset(); foreach (var game in queue) remaining.Enqueue(game);
            Active = true; Total = remaining.Count;
            (session as ISessionScoreReset)?.ResetScores();
            return Advance(loader);
        }

        /// <summary>
        /// Есть ли в очереди игра под этот состав. Ложь при активной серии
        /// значит, что идущий сейчас раунд — последний: после него будет
        /// таблица катки, а не следующая игра.
        /// </summary>
        public static bool HasNext(int playerCount)
        {
            if (!Active) return false;
            foreach (var game in remaining)
                if (playerCount >= game.MinPlayers && playerCount <= game.MaxPlayers) return true;
            return false;
        }

        /// <summary>
        /// Последний раунд доигран и очки за него начислены: зафиксировать
        /// чемпионов в табло и строку итога. Зовёт контроллер последней игры
        /// у авторитета. Очередь при этом не закрывается — её закроет
        /// <see cref="Advance"/>, когда придёт время ехать в хаб.
        /// </summary>
        public static void Complete()
        {
            if (!Active || Completed) return;
            var session = SessionScoreboard.Current;
            if (session == null || !session.HasAuthority) return;
            Completed = true;
            session.CompleteSeries();
            Summary = BuildSummary(session);
            Debug.Log("PARTY SERIES COMPLETE: " + Summary);
        }

        /// <summary>
        /// Строка для экрана телевизора. Короткая нарочно: сетевой слой везёт
        /// её в FixedString128Bytes, а кириллица занимает по два байта.
        /// </summary>
        private static string BuildSummary(ISessionScoreboard session)
        {
            string head = $"Катка окончена · {Round} {GamesWord(Round)}";
            IReadOnlyList<int> champions = session.Champions;
            if (champions.Count == 1)
            {
                SessionPlayer champion = session.FindPlayer(champions[0]);
                if (champion != null) return $"{head} · Корона: {champion.DisplayName} ({champion.Score} очк.)";
            }
            else if (champions.Count > 1)
            {
                return $"{head} · Ничья за корону";
            }
            return head;
        }

        /// <summary>Склонение «игра / игры / игр» по числу.</summary>
        public static string GamesWord(int count)
        {
            int n = Math.Abs(count) % 100;
            int last = n % 10;
            if (n >= 11 && n <= 19) return "игр";
            if (last == 1) return "игра";
            if (last >= 2 && last <= 4) return "игры";
            return "игр";
        }

        public static bool Advance(MinigameLoader loader)
        {
            if (!Active || SessionScoreboard.Current?.HasAuthority != true) return false;
            int count = SessionScoreboard.Current.Players.Count;
            while (remaining.Count > 0)
            {
                var game = remaining.Dequeue();
                // A disconnect can make a later game unsuitable. Do not repeat earlier games to fill it.
                if (count < game.MinPlayers || count > game.MaxPlayers) continue;
                if (!loader.TryLoad(game))
                {
                    Summary = "Серия остановлена: не удалось загрузить следующую игру.";
                    Active = false; remaining.Clear(); return false;
                }
                Round++;
                Debug.Log($"PARTY SERIES {Round}/{Total}: {game.SceneName}");
                return true;
            }
            Active = false;
            if (!Completed)
            {
                // Сюда попадаем только если Complete не позвали: например,
                // серия закончилась до первого раунда. Чемпионов нет, итог общий.
                Summary = $"Серия завершена · сыграно {Round} из {Total}";
                Debug.Log("PARTY SERIES COMPLETE: " + Summary);
            }
            return false;
        }
    }
}
