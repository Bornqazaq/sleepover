using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Voice;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Пауза в мини-играх: сама поднимается в любой сцене, где своего меню нет.
    ///
    /// Заводится потому, что <see cref="PauseScreen"/> лежал ровно в двух
    /// сценах из тринадцати — в хабе и в «Тире». В остальных одиннадцати Esc
    /// не делал ничего: выйти из раунда посреди игры было нечем, и человек,
    /// которому надо отойти, закрывал игру целиком через Alt+F4, роняя свой
    /// персонаж на арену до таймаута.
    ///
    /// Поднимается из кода, а не кладётся в сцены, по той же причине, что и
    /// голосовой чат: сцены грузятся в режиме Single, мини-игр тринадцать,
    /// одна из них чужая, и каждый такой объект пришлось бы мерджить при
    /// каждом слиянии веток. Здесь — один файл и ни одной тронутой сцены.
    ///
    /// Рисуется через IMGUI по той же причине: холст с префабом означает и
    /// шрифты, и порядок отрисовки поверх чужого интерфейса, и тринадцать
    /// сцен снова. Меню паузы открыто секунды, ему хватает.
    ///
    /// <b>Сцене со своим меню паузы мы уступаем Esc, но не громкость.</b>
    /// В хабе стоит оформленный <see cref="PauseMenuView"/> — два обработчика
    /// Esc открыли бы два меню. Поэтому свой <see cref="PauseScreen"/> там
    /// выключается. Ползунки голоса и музыки при этом остаются: иначе в хабе
    /// нечем убавить подложку, а именно хаб человек открывает первым.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PauseScreen))]
    public sealed class PauseOverlay : MonoBehaviour
    {
        private static readonly Color Wash = new Color(0.01f, 0.03f, 0.03f, 0.72f);
        private static readonly Color Card = new Color(0.05f, 0.11f, 0.11f, 0.97f);
        private static readonly Color Gold = new Color(0.94f, 0.73f, 0.46f, 1f);
        private static readonly Color Ink = new Color(0.96f, 0.93f, 0.85f, 1f);
        private static readonly Color Muted = new Color(0.65f, 0.72f, 0.68f, 1f);

        private PauseScreen pause;
        private GUIStyle titleStyle;
        private GUIStyle rowStyle;
        private GUIStyle buttonStyle;

        /// <summary>Громкость трогали — на закрытии паузы её надо сохранить на диск.</summary>
        private bool volumeTouched;

        private bool wasPaused;

        /// <summary>
        /// Сцена принесла своё меню: Esc обрабатывает она, мы рисуем только
        /// ползунки рядом.
        /// </summary>
        private bool sceneOwnsPause;

        /// <summary>
        /// Поднять паузу на объекте, переживающем смену сцен. Момент тот же,
        /// что у голоса: после загрузки первой сцены, до первого Esc.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Стенду из восьми headless-процессов меню паузы ни к чему.
            if (Application.isBatchMode) return;

            var host = new GameObject("PauseOverlay");
            DontDestroyOnLoad(host);
            host.AddComponent<PauseScreen>();
            host.AddComponent<PauseOverlay>();
        }

        private void Awake()
        {
            pause = GetComponent<PauseScreen>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            YieldToSceneOwnPause();
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => YieldToSceneOwnPause();

        /// <summary>
        /// Настройки голоса и музыки пишутся на диск один раз, на закрытии
        /// паузы: перетаскивание ползунка меняет значение десятки раз за
        /// секунду, и запись на каждый кадр подвесила бы игру.
        /// </summary>
        private void Update()
        {
            bool paused = AnyPauseOpen();
            if (wasPaused && !paused && volumeTouched)
            {
                VoiceSettings.Flush();
                MusicSettings.Flush();
                volumeTouched = false;
            }

            wasPaused = paused;
        }

        /// <summary>
        /// Уступить Esc сцене со своим меню, но остаться рисовать громкость.
        /// </summary>
        private void YieldToSceneOwnPause()
        {
            PauseScreen[] all = FindObjectsByType<PauseScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            sceneOwnsPause = false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != pause) sceneOwnsPause = true;
            }

            // Esc и кнопки — у сцены. Мы сами остаёмся включёнными, чтобы
            // ползунки музыки и голоса были видны и в хабе.
            pause.enabled = !sceneOwnsPause;
            enabled = true;
        }

        private void OnGUI()
        {
            if (!AnyPauseOpen()) return;

            EnsureStyles();

            if (sceneOwnsPause) DrawVolumeCard();
            else DrawFullMenu();
        }

        /// <summary>Открыта ли пауза — своя или сцены.</summary>
        private bool AnyPauseOpen()
        {
            if (pause != null && pause.enabled && pause.IsPaused) return true;
            PauseScreen current = PauseScreen.Current;
            return current != null && current.IsPaused;
        }

        /// <summary>
        /// Полное меню для сцен без своей паузы: продолжить, выйти, громкость.
        /// </summary>
        private void DrawFullMenu()
        {
            float width = Mathf.Max(Screen.width * 0.3f, 420f);
            float row = Mathf.Max(Screen.height * 0.052f, 34f);
            float pad = width * 0.06f;

            bool inRound = MinigameControllerBase.Current != null && MinigameControllerBase.Current.CanLeaveRound;
            VoiceChatRuntime voice = VoiceChatRuntime.Instance;

            float lines = 2.2f + 1.25f + 1f;
            if (voice != null) lines += 2.8f;
            if (MusicPlayer.Instance != null) lines += 1.7f;
            if (inRound) lines += 1.15f;

            float height = pad * 2f + row * lines;
            var card = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            Fill(new Rect(0f, 0f, Screen.width, Screen.height), Wash);
            Fill(card, Card);
            Fill(new Rect(card.x, card.y, 4f, card.height), Gold);

            float inner = card.width - pad * 2f;
            float x = card.x + pad;
            float y = card.y + pad;

            Label(new Rect(x, y, inner, row), "Пауза", Ink, titleStyle);
            y += row * 1.2f;

            Label(new Rect(x, y, inner, row),
                inRound ? "Раунд идёт — остальные продолжают играть" : "Игра ждёт",
                Muted, rowStyle);
            y += row;

            if (GUI.Button(new Rect(x, y, inner, row), "Продолжить  ·  Esc", buttonStyle)) pause.Resume();
            y += row * 1.25f;

            y = DrawVoiceVolume(x, y, inner, row, voice);
            y = DrawMusicVolume(x, y, inner, row);

            if (inRound)
            {
                if (GUI.Button(new Rect(x, y, inner, row), "Выйти из раунда  ·  смотреть за игрой", buttonStyle))
                {
                    pause.LeaveRound();
                    return;
                }

                y += row * 1.15f;
            }

            if (GUI.Button(new Rect(x, y, inner, row), "Выйти из игры", buttonStyle)) pause.QuitGame();
        }

        /// <summary>
        /// Карточка громкости рядом с оформленным меню хаба — справа снизу,
        /// чтобы не перекрывать кнопки «Продолжить / Выйти».
        /// </summary>
        private void DrawVolumeCard()
        {
            float width = Mathf.Max(Screen.width * 0.22f, 320f);
            float row = Mathf.Max(Screen.height * 0.04f, 28f);
            float pad = width * 0.07f;

            VoiceChatRuntime voice = VoiceChatRuntime.Instance;
            float lines = 1.4f;
            if (voice != null) lines += 1.9f;
            if (MusicPlayer.Instance != null) lines += 1.7f;

            float height = pad * 2f + row * lines;
            var card = new Rect(Screen.width - width - 28f, Screen.height - height - 28f, width, height);

            Fill(card, Card);
            Fill(new Rect(card.x, card.y, 4f, card.height), Gold);

            float inner = card.width - pad * 2f;
            float x = card.x + pad;
            float y = card.y + pad;

            Label(new Rect(x, y, inner, row), "Звук", Ink, titleStyle);
            y += row * 1.15f;

            y = DrawMusicVolume(x, y, inner, row);
            DrawVoiceVolume(x, y, inner, row, voice);
        }

        /// <summary>
        /// Ползунок громкости чужих голосов. Голос поднимается сам и в редких
        /// случаях (batch-прогон, сцена без сети) его может не быть — тогда
        /// строки просто нет.
        /// </summary>
        private float DrawVoiceVolume(float x, float y, float inner, float row, VoiceChatRuntime voice)
        {
            if (voice == null) return y;

            Label(new Rect(x, y, inner, row * 0.8f),
                $"Громкость голоса: {VoiceSettings.Volume * 100f:0}%", Ink, rowStyle);
            y += row * 0.8f;

            float volume = GUI.HorizontalSlider(new Rect(x, y + row * 0.2f, inner, row * 0.5f),
                VoiceSettings.Volume, 0f, 1f);
            if (!Mathf.Approximately(volume, VoiceSettings.Volume))
            {
                voice.SetVolume(volume);
                volumeTouched = true;
            }
            y += row * 0.9f;

            // Подсказка клавиш — только в полном меню: в хабе карточка узкая.
            if (!sceneOwnsPause)
            {
                Label(new Rect(x, y, inner, row * 0.8f), VoiceKeys.Hint, Muted, rowStyle);
                return y + row * 1.1f;
            }

            return y;
        }

        /// <summary>
        /// Ползунок музыки. Стоит рядом с голосом: первое, что делает человек,
        /// которому подложка мешает разговаривать, — ищет, где её убавить.
        /// </summary>
        private float DrawMusicVolume(float x, float y, float inner, float row)
        {
            MusicPlayer music = MusicPlayer.Instance;
            if (music == null) return y;

            Label(new Rect(x, y, inner, row * 0.8f),
                $"Громкость музыки: {MusicSettings.Volume * 100f:0}%", Ink, rowStyle);
            y += row * 0.8f;

            float volume = GUI.HorizontalSlider(new Rect(x, y + row * 0.2f, inner, row * 0.5f),
                MusicSettings.Volume, 0f, 1f);
            if (!Mathf.Approximately(volume, MusicSettings.Volume))
            {
                music.SetVolume(volume);
                volumeTouched = true;
            }

            return y + row * 0.9f;
        }

        private void EnsureStyles()
        {
            int size = Mathf.RoundToInt(Mathf.Max(Screen.height * 0.021f, 14f));

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

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
            }

            rowStyle.fontSize = size;
            buttonStyle.fontSize = size;
            titleStyle.fontSize = Mathf.RoundToInt(size * 1.8f);
        }

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
    }
}
