using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Igruha.Networking
{
    /// <summary>
    /// Что происходит при запуске — словами на экране игрока.
    ///
    /// Сцена Boot пустая: камера, свет и NetworkManager. Пока сеть не поднялась,
    /// человек видит ровный серый экран и больше ничего — и хост с занятым портом,
    /// и клиент, не нашедший хоста, выглядят для него одинаково «игра не грузится».
    /// Лог объясняет всё, но до лога на прогоне с людьми не доходит никто.
    ///
    /// Экран рисуется через IMGUI намеренно: он обязан работать в сцене, где
    /// нет ни холста, ни шрифтов, ни единого ассета UI, и обязан пережить любой
    /// сбой сети. Как только поднялась игровая сцена, экран гаснет сам.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootStatusScreen : MonoBehaviour
    {
        private const float TitleHeightFraction = 0.042f;
        private const float DetailsHeightFraction = 0.026f;
        private const float SideMarginFraction = 0.08f;

        private static readonly Color BackgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        private static readonly Color TitleColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color DetailsColor = new Color(0.68f, 0.71f, 0.76f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.38f, 1f);

        private string title = string.Empty;
        private string details = string.Empty;
        private bool isError;
        private bool isVisible = true;
        private string bootSceneName;

        private GUIStyle titleStyle;
        private GUIStyle detailsStyle;

        private void Awake()
        {
            bootSceneName = SceneManager.GetActiveScene().name;
        }

        /// <summary>Обычный ход дела: роль, адрес, ожидание.</summary>
        public void Show(string statusTitle, string statusDetails)
        {
            title = statusTitle;
            details = statusDetails;
            isError = false;
            isVisible = true;
        }

        /// <summary>
        /// Сбой, после которого игра сама не поедет. Экран остаётся до выхода:
        /// это единственное место, где человек узнает, что именно сломалось.
        /// </summary>
        public void ShowError(string errorTitle, string errorDetails)
        {
            title = errorTitle;
            details = errorDetails;
            isError = true;
            isVisible = true;
        }

        public void Hide()
        {
            isVisible = false;
        }

        /// <summary>
        /// Гаснуть по факту загрузки игровой сцены, а не по факту старта сети:
        /// между «сервер поднялся» и «комната появилась» проходит секунда, и
        /// в эту секунду серый экран возвращается.
        ///
        /// Игра запускается в полноэкранном окне, поэтому на экране ошибки
        /// нужен собственный выход: закрывать её человеку больше нечем.
        /// </summary>
        private void Update()
        {
            if (!isVisible)
            {
                return;
            }

            if (isError)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    Application.Quit();
                }

                return;
            }

            if (SceneManager.GetActiveScene().name != bootSceneName)
            {
                Hide();
            }
        }

        private void OnGUI()
        {
            if (!isVisible)
            {
                return;
            }

            EnsureStyles();

            Color previousColor = GUI.color;
            GUI.color = BackgroundColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            float margin = Screen.width * SideMarginFraction;
            float width = Screen.width - (margin * 2f);
            float titleHeight = Screen.height * 0.2f;
            float top = Screen.height * 0.32f;

            titleStyle.normal.textColor = isError ? ErrorColor : TitleColor;
            GUI.Label(new Rect(margin, top, width, titleHeight), title, titleStyle);
            GUI.Label(new Rect(margin, top + titleHeight, width, Screen.height * 0.4f), details, detailsStyle);
        }

        /// <summary>
        /// Стили строятся один раз и только внутри OnGUI: вне цикла IMGUI
        /// <c>GUI.skin</c> недоступен, а пересоздание стиля каждый кадр — мусор
        /// в куче на пустом месте.
        /// </summary>
        private void EnsureStyles()
        {
            int titleFontSize = Mathf.Max(18, Mathf.RoundToInt(Screen.height * TitleHeightFraction));
            int detailsFontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * DetailsHeightFraction));

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true,
                    fontStyle = FontStyle.Bold
                };
            }

            if (detailsStyle == null)
            {
                detailsStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true
                };
                detailsStyle.normal.textColor = DetailsColor;
            }

            titleStyle.fontSize = titleFontSize;
            detailsStyle.fontSize = detailsFontSize;
        }
    }
}
