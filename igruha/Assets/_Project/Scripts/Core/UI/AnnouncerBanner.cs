using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Плашка диктора: короткие реплики по событиям раунда («Один готов.
    /// Дальше — математика»). Комментатор дворовых страстей, который держит
    /// драму там, где правила уже понятны.
    ///
    /// Общая для всех мини-игр: реплики приходят строкой на входе, а решение,
    /// что и когда сказать, остаётся у самой игры. Здесь только очередь,
    /// время показа и приоритет.
    ///
    /// <b>Очередь, а не перебивание:</b> в момент лавины событий (трое
    /// заразились подряд) реплики выстраиваются в ряд и показываются по
    /// очереди. Мигающая плашка, которую перебивают каждые полсекунды,
    /// не читается вовсе.
    ///
    /// Приоритет перебивает очередь: важная реплика («остался один») выходит
    /// вперёд болтовни. Равный приоритет очередь не меняет.
    ///
    /// Озвучка сюда добавляется позже одним местом (EPIC 22): голос ляжет
    /// рядом с текстом, и ни одной мини-игры это не коснётся.
    /// </summary>
    public sealed class AnnouncerBanner : MonoBehaviour
    {
        private const float DefaultSeconds = 3.5f;

        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text label;

        [Tooltip("Сколько секунд держится реплика по умолчанию")]
        [SerializeField] private float defaultSeconds = DefaultSeconds;

        [Tooltip("Время появления и исчезновения, сек")]
        [SerializeField] private float fadeSeconds = 0.18f;

        [Tooltip("Пауза между репликами, сек: без неё очередь читается как одна мигающая строка")]
        [SerializeField] private float gapSeconds = 0.15f;

        private readonly List<Line> queue = new List<Line>(8);
        private float remaining;
        private float gap;
        private bool showing;

        private readonly struct Line
        {
            public readonly string Text;
            public readonly float Seconds;
            public readonly int Priority;

            public Line(string text, float seconds, int priority)
            {
                Text = text;
                Seconds = seconds;
                Priority = priority;
            }
        }

        private void Awake()
        {
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
            }

            if (label == null)
            {
                label = GetComponentInChildren<TMP_Text>(true);
            }

            if (group != null)
            {
                group.alpha = 0f;
            }
        }

        /// <summary>Сказать реплику. Пустая строка игнорируется — молчание тоже реплика.</summary>
        public void Announce(string text) => Announce(text, defaultSeconds, 0);

        public void Announce(string text, float seconds) => Announce(text, seconds, 0);

        /// <summary>
        /// Реплика с приоритетом: чем больше число, тем раньше выйдет.
        /// Показанную реплику не перебивает — ждёт её конца, но встаёт
        /// в очереди перед всем, что ниже.
        /// </summary>
        public void Announce(string text, float seconds, int priority)
        {
            if (string.IsNullOrEmpty(text) || label == null)
            {
                return;
            }

            var line = new Line(text, seconds > 0f ? seconds : defaultSeconds, priority);

            int index = queue.Count;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].Priority < priority)
                {
                    index = i;
                    break;
                }
            }

            queue.Insert(index, line);
        }

        /// <summary>Убрать плашку и очередь: конец раунда, выход в хаб.</summary>
        public void Clear()
        {
            queue.Clear();
            remaining = 0f;
            gap = 0f;
            showing = false;

            if (group != null)
            {
                group.alpha = 0f;
            }
        }

        private void Update()
        {
            if (showing)
            {
                UpdateShown();
                return;
            }

            if (gap > 0f)
            {
                gap -= Time.deltaTime;
                FadeTowards(0f);
                return;
            }

            if (queue.Count == 0)
            {
                FadeTowards(0f);
                return;
            }

            Line next = queue[0];
            queue.RemoveAt(0);
            label.text = next.Text;
            remaining = next.Seconds;
            showing = true;
        }

        private void UpdateShown()
        {
            remaining -= Time.deltaTime;
            FadeTowards(1f);

            if (remaining > 0f)
            {
                return;
            }

            showing = false;
            gap = gapSeconds;
        }

        private void FadeTowards(float target)
        {
            if (group == null)
            {
                return;
            }

            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, step);
        }
    }
}
