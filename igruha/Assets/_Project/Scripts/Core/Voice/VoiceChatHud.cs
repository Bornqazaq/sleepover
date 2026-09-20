using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Session;

namespace Igruha.Core.Voice
{
    /// <summary>
    /// Что видит игрок: свой микрофон, кто говорит сейчас, и панель настроек
    /// по F4.
    ///
    /// Рисуется через IMGUI по той же причине, что и экран запуска: голос
    /// обязан работать в тринадцати сценах, включая чужую сцену напарника, а
    /// холст с префабом пришлось бы класть в каждую и мерджить при каждом
    /// слиянии веток. Кода тут ровно на индикатор, зато ни одна сцена не
    /// тронута и конфликтовать в git нечему.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VoiceChatRuntime))]
    public sealed class VoiceChatHud : MonoBehaviour
    {
        private const float PanelWidthFraction = 0.2f;
        private const float MarginFraction = 0.018f;
        private const float RowHeightFraction = 0.028f;

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.72f);
        private static readonly Color TextColor = new Color(0.9f, 0.92f, 0.95f, 1f);
        private static readonly Color DimColor = new Color(0.62f, 0.66f, 0.72f, 1f);
        private static readonly Color LiveColor = new Color(0.45f, 0.86f, 0.5f, 1f);
        private static readonly Color MutedColor = new Color(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color MeterColor = new Color(0.36f, 0.68f, 0.95f, 1f);

        private VoiceChatRuntime runtime;
        private GUIStyle rowStyle;
        private GUIStyle titleStyle;

        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorTaken;

        private void Awake()
        {
            runtime = GetComponent<VoiceChatRuntime>();
        }

        private void Update()
        {
            // Настройки открываются посреди игры, где курсор спрятан камерой.
            if (runtime.SettingsOpen && !cursorTaken) TakeCursor();
            else if (!runtime.SettingsOpen && cursorTaken) ReleaseCursor();
        }

        private void OnDisable()
        {
            if (cursorTaken) ReleaseCursor();
        }

        private void OnGUI()
        {
            if (!runtime.NetworkActive) return;

            EnsureStyles();

            float margin = Screen.height * MarginFraction;
            float width = Mathf.Max(Screen.width * PanelWidthFraction, 240f);
            float rowHeight = Mathf.Max(Screen.height * RowHeightFraction, 18f);

            IReadOnlyList<VoiceSpeaker> speakers = runtime.Speakers;
            int talking = 0;
            for (int i = 0; i < speakers.Count; i++)
            {
                if (speakers[i].IsSpeaking) talking++;
            }

            float height = rowHeight * (2 + talking) + margin;
            var panel = new Rect(margin, Screen.height - height - margin, width, height);

            Fill(panel, PanelColor);

            float y = panel.y + margin * 0.5f;
            DrawSelfRow(new Rect(panel.x + margin * 0.5f, y, panel.width - margin, rowHeight));
            y += rowHeight;

            for (int i = 0; i < speakers.Count; i++)
            {
                VoiceSpeaker speaker = speakers[i];
                if (!speaker.IsSpeaking) continue;

                DrawSpeakerRow(new Rect(panel.x + margin * 0.5f, y, panel.width - margin, rowHeight), speaker);
                y += rowHeight;
            }

            GUI.color = DimColor;
            GUI.Label(new Rect(panel.x + margin * 0.5f, y, panel.width - margin, rowHeight), VoiceKeys.Hint, rowStyle);
            GUI.color = Color.white;

            if (runtime.SettingsOpen) DrawSettings();
        }

        private void DrawSelfRow(Rect rect)
        {
            bool live = runtime.Transmitting;
            bool muted = !runtime.MicrophoneEnabled;
            bool broken = !runtime.CaptureReady;

            string label;
            Color color;

            if (broken)
            {
                label = "Микрофона нет — F4";
                color = MutedColor;
            }
            else if (muted)
            {
                label = "Микрофон выключен — M";
                color = MutedColor;
            }
            else if (live)
            {
                label = "Вы говорите";
                color = LiveColor;
            }
            else
            {
                label = VoiceSettings.Mode == VoiceInputMode.PushToTalk ? "Говорить — зажмите V" : "Микрофон включён";
                color = DimColor;
            }

            DrawRow(rect, label, color, broken || muted ? 0f : runtime.InputLevel);
        }

        private void DrawSpeakerRow(Rect rect, VoiceSpeaker speaker)
        {
            DrawRow(rect, NameOf(speaker.ClientId), TextColor, speaker.Level);
        }

        /// <summary>
        /// Строка «кто — насколько громко». Полоска уровня тут не украшение:
        /// без неё нельзя понять, слышат ли тебя вообще, и настройка порога
        /// превращается в гадание.
        /// </summary>
        private void DrawRow(Rect rect, string label, Color color, float level)
        {
            float meterWidth = rect.width * 0.28f;
            var textRect = new Rect(rect.x, rect.y, rect.width - meterWidth - 8f, rect.height);
            var meterRect = new Rect(rect.xMax - meterWidth, rect.y + rect.height * 0.35f, meterWidth, rect.height * 0.3f);

            GUI.color = color;
            GUI.Label(textRect, label, rowStyle);
            GUI.color = Color.white;

            Fill(meterRect, new Color(1f, 1f, 1f, 0.12f));

            // Шкала логарифмическая: речь живёт в нижней десятой доле линейной
            // громкости, и на линейной полоске её попросту не видно.
            float normalized = Mathf.Clamp01(Mathf.Sqrt(Mathf.Clamp01(level * 6f)));
            if (normalized > 0.001f)
            {
                Fill(new Rect(meterRect.x, meterRect.y, meterRect.width * normalized, meterRect.height), MeterColor);
            }
        }

        private void DrawSettings()
        {
            float width = Mathf.Max(Screen.width * 0.34f, 420f);
            float height = Mathf.Max(Screen.height * 0.62f, 420f);
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            Fill(panel, new Color(0.08f, 0.09f, 0.12f, 0.97f));

            float pad = width * 0.05f;
            float row = Mathf.Max(Screen.height * 0.034f, 22f);
            float y = panel.y + pad;
            float inner = panel.width - pad * 2f;

            Label(new Rect(panel.x + pad, y, inner, row), "Голосовой чат", TextColor, titleStyle);
            y += row * 1.2f;

            // Устройство
            Label(new Rect(panel.x + pad, y, inner, row), "Микрофон", TextColor, rowStyle);
            y += row * 0.9f;

            string[] devices = Microphone.devices;
            string current = string.IsNullOrEmpty(runtime.CaptureDevice) ? "не выбран" : runtime.CaptureDevice;
            Label(new Rect(panel.x + pad, y, inner, row),
                runtime.CaptureReady ? $"{current} · {runtime.CaptureRate} Гц" : runtime.CaptureError,
                runtime.CaptureReady ? DimColor : MutedColor, rowStyle);
            y += row;

            if (devices != null && devices.Length > 1)
            {
                if (GUI.Button(new Rect(panel.x + pad, y, inner * 0.48f, row), "Следующий микрофон"))
                {
                    runtime.SetDevice(NextDevice(devices, runtime.CaptureDevice));
                }
            }

            if (GUI.Button(new Rect(panel.x + pad + inner * 0.52f, y, inner * 0.48f, row), "Искать заново"))
            {
                runtime.RestartCapture();
            }

            y += row * 1.3f;

            // Режим
            bool pushToTalk = VoiceSettings.Mode == VoiceInputMode.PushToTalk;
            if (GUI.Button(new Rect(panel.x + pad, y, inner, row),
                    pushToTalk ? "Режим: рация (V) — переключить на открытый" : "Режим: открытый микрофон — переключить на рацию"))
            {
                runtime.SetMode(pushToTalk ? VoiceInputMode.Open : VoiceInputMode.PushToTalk);
            }

            y += row * 1.3f;

            // Чувствительность
            Label(new Rect(panel.x + pad, y, inner, row),
                $"Чувствительность: {VoiceSettings.Threshold:0.000} (ниже — ловит тише)", TextColor, rowStyle);
            y += row * 0.85f;
            float threshold = GUI.HorizontalSlider(new Rect(panel.x + pad, y + row * 0.25f, inner, row * 0.5f),
                VoiceSettings.Threshold, VoiceSettings.MinThreshold, VoiceSettings.MaxThreshold);
            if (!Mathf.Approximately(threshold, VoiceSettings.Threshold)) runtime.SetThreshold(threshold);
            y += row;

            // Уровень прямо сейчас — по нему и выставляют порог
            var level = new Rect(panel.x + pad, y, inner, row * 0.3f);
            Fill(level, new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(level.x, level.y, level.width * Mathf.Clamp01(Mathf.Sqrt(Mathf.Clamp01(runtime.InputLevel * 6f))), level.height),
                runtime.Transmitting ? LiveColor : MeterColor);

            float markerAt = level.x + level.width * Mathf.Clamp01(Mathf.Sqrt(Mathf.Clamp01(VoiceSettings.Threshold * 6f)));
            Fill(new Rect(markerAt, level.y - 2f, 2f, level.height + 4f), MutedColor);
            y += row;

            // Усиление
            Label(new Rect(panel.x + pad, y, inner, row), $"Усиление микрофона: {VoiceSettings.Gain:0.0}×", TextColor, rowStyle);
            y += row * 0.85f;
            float gain = GUI.HorizontalSlider(new Rect(panel.x + pad, y + row * 0.25f, inner, row * 0.5f),
                VoiceSettings.Gain, VoiceSettings.MinGain, VoiceSettings.MaxGain);
            if (!Mathf.Approximately(gain, VoiceSettings.Gain)) runtime.SetGain(gain);
            y += row * 1.1f;

            // Громкость чужих голосов
            Label(new Rect(panel.x + pad, y, inner, row), $"Громкость собеседников: {VoiceSettings.Volume * 100f:0}%", TextColor, rowStyle);
            y += row * 0.85f;
            float volume = GUI.HorizontalSlider(new Rect(panel.x + pad, y + row * 0.25f, inner, row * 0.5f),
                VoiceSettings.Volume, 0f, 1f);
            if (!Mathf.Approximately(volume, VoiceSettings.Volume)) runtime.SetVolume(volume);
            y += row * 1.2f;

            Label(new Rect(panel.x + pad, y, inner, row * 2f),
                "Играйте в наушниках: с колонками ваш микрофон ловит голоса\nостальных и возвращает их эхом.", DimColor, rowStyle);

            if (GUI.Button(new Rect(panel.x + pad, panel.yMax - pad - row, inner, row), "Закрыть (F4)"))
            {
                runtime.SettingsOpen = false;
                VoiceSettings.Flush();
            }
        }

        private static string NextDevice(string[] devices, string current)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i] != current) continue;
                return devices[(i + 1) % devices.Length];
            }

            return devices[0];
        }

        /// <summary>Имя берём из табло катки — там оно уже синхронизировано сервером.</summary>
        private static string NameOf(ulong clientId)
        {
            SessionPlayer player = SessionScoreboard.Current?.FindPlayer((int)clientId);
            return player != null && !string.IsNullOrEmpty(player.DisplayName) ? player.DisplayName : $"Игрок {clientId}";
        }

        private void EnsureStyles()
        {
            int rowSize = Mathf.RoundToInt(Mathf.Max(Screen.height * 0.019f, 13f));
            if (rowStyle == null)
            {
                rowStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
                rowStyle.normal.textColor = Color.white;
            }

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = Color.white;
            }

            rowStyle.fontSize = rowSize;
            titleStyle.fontSize = Mathf.RoundToInt(rowSize * 1.5f);
        }

        /// <summary>Подпись своим цветом, не пачкая цвет кнопок и ползунков.</summary>
        private static void Label(Rect rect, string text, Color color, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void TakeCursor()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorTaken = true;
        }

        private void ReleaseCursor()
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorTaken = false;
        }
    }
}
