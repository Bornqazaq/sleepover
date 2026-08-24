using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Igruha.Core.Minigame;
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

        /// <summary>
        /// Игрок попросил переиграть. Кнопка — только намерение: перезапускать
        /// раунд HUD не вправе, это решение правил и в сетевой катке решение
        /// сервера.
        /// </summary>
        public event Action RestartRequested;

        private RoundTimer timer;
        private int lastShownSeconds = -1;
        private int lastShownCountdown = -1;

        /// <summary>Кнопка нажимаема: у клиента сетевой катки переигрывать не в его власти.</summary>
        private bool restartAllowed = true;

        private void Awake()
        {
            if (restartButton != null)
            {
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

        private void RaiseRestart() => RestartRequested?.Invoke();

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
            timer = roundTimer;
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
        }

        public void ShowResults(MinigameResults results, IReadOnlyList<SessionPlayer> players)
        {
            if (resultsPanel == null || resultsText == null)
            {
                return;
            }

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

                    string playerName = FindName(players, entries[i].PlayerId);
                    sb.AppendLine($"{place} место — {playerName}");
                }
            }

            resultsText.text = sb.ToString();
            resultsPanel.SetActive(true);

            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(restartAllowed);
            }
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
