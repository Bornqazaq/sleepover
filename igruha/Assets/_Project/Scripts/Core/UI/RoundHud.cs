using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Igruha.Core.Minigame;
using Igruha.Core.CameraSystems;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Общий HUD мини-игры: таймер раунда + панель результатов.
    /// Текст таймера обновляется только при смене секунды (без аллокаций в кадре).
    /// </summary>
    public sealed class RoundHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text timerText;

        [Tooltip("Плашка таймера матча. Если задана — прячется на итогах раунда. " +
                 "Пусто — таймер остаётся на экране, как было")]
        [SerializeField] private GameObject timerPlate;
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TMP_Text resultsText;
        [Tooltip("Панель наблюдателя: за кем сейчас смотрит выбывший")]
        [SerializeField] private GameObject spectatorPanel;
        [SerializeField] private TMP_Text spectatorText;
        [Tooltip("Крупная строка стартового отсчёта. Не назначена — отсчёт просто не показывается")]
        [SerializeField] private TMP_Text countdownText;
        [Tooltip("Строка состояния роли: обойма стрелка, число жизней, текущая цель. Не назначена — строка просто не показывается")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("Кнопка «ещё раз» на экране результатов. Не назначена — кнопки просто нет, остальные сцены править не надо")]
        [SerializeField] private Button restartButton;
        [SerializeField] private ThirdPersonCameraRig resultsCamera;

        [Header("Итоги строками")]
        [Tooltip("Готовые строки мест. Пусто — итоги показываются одним текстом, как раньше")]
        [SerializeField] private ResultRow[] resultRows = Array.Empty<ResultRow>();
        [Tooltip("Заголовок панели: «Итоги раунда» или «Итоги катки». Пусто — берётся текст «Title» внутри панели")]
        [SerializeField] private TMP_Text resultsTitle;

        [Header("Оформление")]
        [Tooltip("Появление цифры отсчёта. Не назначено — цифра просто меняется")]
        [SerializeField] private UiPop countdownPop;

        [Tooltip("С какой секунды таймер краснеет")]
        [SerializeField] private float criticalSeconds = 10f;

        /// <summary>
        /// Игрок попросил переиграть. Кнопка — только намерение: перезапускать
        /// раунд HUD не вправе, это решение правил и в сетевой катке решение
        /// сервера.
        /// </summary>
        public event Action RestartRequested;

        private readonly PanelCursor resultsCursor = new PanelCursor();
        private RoundTimer timer;
        private int lastShownSeconds = -1;
        private int lastShownCountdown = -1;

        /// <summary>Кнопка нажимаема: у клиента сетевой катки переигрывать не в его власти.</summary>
        private bool restartAllowed = true;

        /// <summary>Колонки хвоста строки итогов, в процентах ширины поля имени.</summary>
        private const int PointsColumn = 56;
        private const int TotalColumn = 74;
        private const string RoundTitle = "Итоги раунда";
        private const string SeriesTitle = "Итоги катки";

        private static readonly string AccentHex = ColorUtility.ToHtmlStringRGB(UiSkin.Accent);
        private static readonly string MutedHex = ColorUtility.ToHtmlStringRGB(UiSkin.TextSecondary);
        private static readonly string GoldHex = ColorUtility.ToHtmlStringRGB(UiSkin.Gold);

        private void Awake()
        {
            // Панель собрана билдером с заголовком, но ссылки на него у HUD не
            // было. Ищем один раз по имени, чтобы не пересобирать десять сцен.
            if (resultsTitle == null && resultsPanel != null)
            {
                resultsTitle = FindTitle(resultsPanel.transform);
            }

            if (restartButton != null)
            {
                // Некоторые сохранённые HUD собраны как декоративные Image.
                // Кнопке обязательно нужна принимающая указатель поверхность.
                if (restartButton.targetGraphic != null)
                    restartButton.targetGraphic.raycastTarget = true;
                restartButton.onClick.AddListener(RaiseRestart);
                restartButton.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(RaiseRestart);
            }
        }

        private void OnDisable()
        {
            resultsCursor.Restore();
            resultsCamera?.SetLookSuspended(false);
        }

        private void RaiseRestart()
        {
            if (restartAllowed) RestartRequested?.Invoke();
        }

        /// <summary>
        /// Разрешить или запретить кнопку до показа результатов. Мёртвая кнопка
        /// хуже отсутствующей: игрок жмёт и не понимает, почему ничего нет.
        /// </summary>
        public void SetRestartAvailable(bool available)
        {
            restartAllowed = available;
        }

        public void Bind(RoundTimer roundTimer)
        {
            resultsCursor.Restore();
            resultsCamera?.SetLookSuspended(false);
            timer = roundTimer;
            SetTimerPlateVisible(true);
            if (resultsPanel != null)
            {
                resultsPanel.SetActive(false);
            }

            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(false);
            }

            HideSpectatorTarget();
            HideCountdown();
            HideStatus();
        }

        /// <summary>
        /// Стартовый отсчёт перед активной фазой. Зовётся каждый кадр, а текст
        /// пересобирается только на смене секунды — как и таймер раунда, чтобы
        /// не аллоцировать строку в каждом кадре.
        /// </summary>
        public void ShowCountdown(float remaining)
        {
            if (countdownText == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == lastShownCountdown)
            {
                return;
            }

            lastShownCountdown = seconds;
            countdownText.text = seconds > 0 ? seconds.ToString() : string.Empty;
            countdownText.gameObject.SetActive(seconds > 0);

            // Толчок на каждой цифре: отсчёт, меняющийся без движения,
            // не читается как отсчёт — глаз просто не ловит смену знака.
            if (seconds > 0)
            {
                countdownPop?.Play();
            }
        }

        public void HideCountdown()
        {
            lastShownCountdown = -1;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Строка состояния роли: обойма стрелка, жизни, текущая цель. Звать
        /// только на смене значения — строка собирается заново каждый вызов,
        /// и в кадровом цикле это была бы аллокация на ровном месте.
        /// </summary>
        public void ShowStatus(string status)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = status;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(status));
        }

        public void HideStatus()
        {
            if (statusText != null)
            {
                statusText.gameObject.SetActive(false);
            }
        }

        /// <summary>Подписать, за кем смотрит наблюдатель. Зовётся только на смене цели.</summary>
        public void ShowSpectatorTarget(string playerName)
        {
            if (spectatorPanel == null || spectatorText == null)
            {
                return;
            }

            spectatorText.text = $"Смотрим за: {playerName}";
            spectatorPanel.SetActive(true);
        }

        public void HideSpectatorTarget()
        {
            if (spectatorPanel != null)
            {
                spectatorPanel.SetActive(false);
            }
        }

        private void SetTimerPlateVisible(bool visible)
        {
            if (timerPlate != null && timerPlate.activeSelf != visible)
            {
                timerPlate.SetActive(visible);
            }
        }

        private void Update()
        {
            if (timer == null || timerText == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(timer.Remaining);
            if (seconds == lastShownSeconds)
            {
                return;
            }

            lastShownSeconds = seconds;
            int minutes = seconds / 60;
            timerText.text = $"{minutes}:{seconds % 60:00}";
            timerText.color = seconds <= criticalSeconds ? UiSkin.Danger : UiSkin.TextPrimary;
        }

        /// <summary>Итоги раунда: место, имя, очки за раунд и сумма катки.</summary>
        public void ShowResults(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            if (resultsPanel == null)
            {
                return;
            }

            SetTitle(RoundTitle);

            if (resultRows.Length > 0)
            {
                FillResultRows(results, players);
            }
            else if (resultsText != null)
            {
                resultsText.text = BuildResultsText(results, players);
            }

            OpenResultsPanel(restartAllowed);
        }

        /// <summary>
        /// Таблица катки после последней игры серии: место по сумме, имя,
        /// сумма очков, отметка чемпиона. Кнопки «ещё раз» здесь нет —
        /// серия окончена, дальше хаб.
        /// </summary>
        public void ShowFinalStandings(SessionStandings standings, IReadOnlyList<SessionPlayer> players,
                                       IReadOnlyList<int> champions)
        {
            if (resultsPanel == null || standings == null)
            {
                return;
            }

            SetTitle(SeriesTitleFor(standings));

            if (resultRows.Length > 0)
            {
                FillStandingRows(standings, players, champions);
            }
            else if (resultsText != null)
            {
                resultsText.text = BuildStandingsText(standings, players, champions);
            }

            OpenResultsPanel(false);
        }

        private void OpenResultsPanel(bool restartVisible)
        {
            // Матчевый таймер на итогах продолжал идти под затемнением и тянул
            // взгляд с мест на себя: раунд уже кончился, а секунды всё бегут.
            SetTimerPlateVisible(false);
            resultsPanel.SetActive(true);
            resultsCursor.Release();
            resultsCamera?.SetLookSuspended(true);

            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(restartVisible);
            }
        }

        private void SetTitle(string text)
        {
            if (resultsTitle != null && resultsTitle.text != text)
            {
                resultsTitle.text = text;
            }
        }

        private static string SeriesTitleFor(SessionStandings standings)
        {
            string games = $"{standings.RoundsPlayed} {PartySeries.GamesWord(standings.RoundsPlayed)}";
            return standings.IsTie
                ? $"{SeriesTitle} · {games} · ничья за корону"
                : $"{SeriesTitle} · {games}";
        }

        private static TMP_Text FindTitle(Transform panel)
        {
            TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == "Title")
                {
                    return texts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Разложить итоги по готовым строкам: кружок места, номер, имя,
        /// очки. Строк ровно столько, на сколько собрана карточка (восемь —
        /// потолок лобби), лишние прячутся.
        /// </summary>
        private void FillResultRows(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;
            int row = 0;

            for (int place = 1; place <= entries.Count && row < resultRows.Length; place++)
            {
                for (int i = 0; i < entries.Count && row < resultRows.Length; i++)
                {
                    if (entries[i].Place != place)
                    {
                        continue;
                    }

                    resultRows[row]?.Set(place, FindName(players, entries[i].PlayerId),
                        results.Awarded ? RoundDetail(entries[i], results.CountsTowardSession) : null);
                    row++;
                }
            }

            for (; row < resultRows.Length; row++)
            {
                resultRows[row]?.Clear();
            }
        }

        private void FillStandingRows(SessionStandings standings, IReadOnlyList<SessionPlayer> players,
                                      IReadOnlyList<int> champions)
        {
            IReadOnlyList<SessionStandings.Entry> entries = standings.Entries;
            int row = 0;

            for (int i = 0; i < entries.Count && row < resultRows.Length; i++)
            {
                bool champion = IsChampion(standings, champions, entries[i].PlayerId);
                resultRows[row]?.Set(entries[i].Place, FindName(players, entries[i].PlayerId),
                    StandingDetail(entries[i], champion));
                row++;
            }

            for (; row < resultRows.Length; row++)
            {
                resultRows[row]?.Clear();
            }
        }

        /// <summary>
        /// Чемпион — тот, кого зафиксировал сервер. Пока список не доехал
        /// (клиент подключился впритык), опираемся на таблицу.
        /// </summary>
        private static bool IsChampion(SessionStandings standings, IReadOnlyList<int> champions, int playerId)
        {
            if (champions != null && champions.Count > 0)
            {
                for (int i = 0; i < champions.Count; i++)
                {
                    if (champions[i] == playerId)
                    {
                        return true;
                    }
                }

                return false;
            }

            return standings.IsLeader(playerId);
        }

        /// <summary>
        /// Хвост строки раунда: «+7» акцентом и «всего 12» приглушённо.
        /// В одиночной игре суммы катки нет — только очки этой игры.
        /// </summary>
        private static string RoundDetail(MinigameResults.PlayerResult entry, bool countsTowardSession)
        {
            string points = $"<pos={PointsColumn}%><color=#{AccentHex}>+{entry.Points} очк.</color>";
            return countsTowardSession
                ? $"{points}<pos={TotalColumn}%><color=#{MutedHex}>всего {entry.Total}</color>"
                : points;
        }

        /// <summary>Хвост строки катки: отметка чемпиона и сумма очков.</summary>
        private static string StandingDetail(SessionStandings.Entry entry, bool champion)
        {
            string crown = champion ? $"<pos={PointsColumn}%><color=#{GoldHex}>ЧЕМПИОН</color>" : string.Empty;
            return $"{crown}<pos={TotalColumn}%><color=#{MutedHex}>{entry.Score} очк.</color>";
        }

        /// <summary>Запасной вид итогов — одним текстом, для сцен без готовых строк.</summary>
        private static string BuildResultsText(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            var sb = new StringBuilder("Результаты раунда:\n");
            IReadOnlyList<MinigameResults.PlayerResult> entries = results.Entries;
            for (int place = 1; place <= entries.Count; place++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Place != place)
                    {
                        continue;
                    }

                    sb.Append($"{place} место — {FindName(players, entries[i].PlayerId)}");
                    if (results.Awarded)
                    {
                        sb.Append($"   +{entries[i].Points} очк.");
                        if (results.CountsTowardSession)
                        {
                            sb.Append($"   (всего {entries[i].Total})");
                        }
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string BuildStandingsText(SessionStandings standings, IReadOnlyList<SessionPlayer> players,
                                                 IReadOnlyList<int> champions)
        {
            var sb = new StringBuilder(SeriesTitleFor(standings)).Append(":\n");
            IReadOnlyList<SessionStandings.Entry> entries = standings.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append($"{entries[i].Place} место — {FindName(players, entries[i].PlayerId)}   {entries[i].Score} очк.");
                if (IsChampion(standings, champions, entries[i].PlayerId))
                {
                    sb.Append("   ЧЕМПИОН");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string FindName(IReadOnlyList<SessionPlayer> players, int playerId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Id == playerId)
                {
                    return players[i].DisplayName;
                }
            }

            return $"Игрок {playerId + 1}";
        }
    }
}
