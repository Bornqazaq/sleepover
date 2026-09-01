using TMPro;
using UnityEditor;
using UnityEngine;
using Igruha.Core.UI;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Строки интерфейса раунда, которые обязаны существовать в каждой сцене
    /// мини-игры: состояние роли и обратный отсчёт.
    ///
    /// <b>Зачем отдельный помощник.</b> Пустая ссылка на текст в
    /// <see cref="RoundHud"/> ломала игру уже трижды — «Верю / не верю»
    /// (25.08 и 28.08), «Рейс на память» (31.08) и «Переноска предмета»
    /// (01.09). Ошибка одна и та же и тихая: <c>ShowStatus</c> с пустой
    /// ссылкой молча выходит, поэтому в игре просто ничего не появляется,
    /// а в консоли ни строчки. Каждый билдер лечил её своей копией одного
    /// метода — здесь копия одна.
    ///
    /// Числа выведены прогонами, а не вкусом: <c>TimerText</c> стоит на −30
    /// при высоте 80, то есть занимает полосу до −110, и первая версия строки
    /// на −52 легла ровно на цифры таймера.
    /// </summary>
    public static class MinigameHudLines
    {
        private const float StatusY = -118f;
        private const float StatusFontSize = 24f;
        private const float CountdownY = -260f;
        private const float CountdownFontSize = 140f;

        /// <summary>
        /// Строка состояния роли. Ничего не делает, если ссылка уже стоит:
        /// у игры может быть своя, расставленная руками.
        /// </summary>
        public static void EnsureStatusLine(RoundHud hud) =>
            EnsureLine(hud, "statusText", "StatusLine", StatusY, StatusFontSize,
                       new Vector2(1100f, 68f), Color.white,
                       "строка состояния роли");

        /// <summary>
        /// Обратный отсчёт перед стартом. Без него игрок несколько секунд
        /// стоит без управления и без единого объяснения — на плейтесте это
        /// читается как зависший клиент, а не как «сейчас начнём».
        /// </summary>
        public static void EnsureCountdownLine(RoundHud hud) =>
            EnsureLine(hud, "countdownText", "CountdownLine", CountdownY, CountdownFontSize,
                       new Vector2(600f, 200f), Color.white,
                       "обратный отсчёт");

        private static void EnsureLine(RoundHud hud, string property, string objectName,
                                       float y, float fontSize, Vector2 size, Color color, string what)
        {
            if (hud == null)
            {
                Debug.LogWarning($"В сцене нет RoundHud — {what} вешать не на что");
                return;
            }

            var so = new SerializedObject(hud);
            SerializedProperty reference = so.FindProperty(property);
            if (reference == null || reference.objectReferenceValue != null)
            {
                return;
            }

            var canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError($"RoundHud не лежит под Canvas — {what} некуда положить", hud);
                return;
            }

            Transform existing = canvas.transform.Find(objectName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Top;
            text.raycastTarget = false;

            // Обводка, а не подложка: RoundHud гасит именно объект текста,
            // и отдельная панель осталась бы висеть пустой плашкой.
            text.outlineColor = new Color32(0, 0, 0, 210);
            text.outlineWidth = 0.2f;

            reference.objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"HUD получил {what}", text);
        }
    }
}
