using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Igruha.Core.Audio;
using Igruha.Core.Hub;
using Igruha.Core.Minigame;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Пауза по Esc. Представление необязательно: существующие сцены сохраняют
    /// прежнее меню, хаб использует PauseMenuView с подтверждением выхода.
    ///
    /// <b>«Выход» означает разное в зависимости от того, где нажали.</b>
    /// Идёт раунд мини-игры, из которого можно выйти, — выходим из раунда и
    /// остаёмся в катке наблюдателем: катка чужая, рвать её из-за одного
    /// игрока нельзя. В остальных случаях выход означает выход из игры
    /// целиком, а не возврат в хаб: хаб — это уже игра, возвращать в него из
    /// паузы некуда. Перед закрытием отключаемся от сессии, чтобы остальные
    /// увидели уход честно, а не зависший силуэт до таймаута.
    ///
    /// <b>Время останавливается только вне сети.</b> В сетевой катке
    /// <c>Time.timeScale = 0</c> остановил бы одну машину: сервер продолжает
    /// считать, и вернувшийся игрок оказался бы в другой игре. Поэтому по сети
    /// пауза — экран поверх, а мир под ним живёт.
    ///
    /// <b>Игровой ввод на паузе выключается целиком, картой действий.</b>
    /// Одной остановки времени мало: по сети время не останавливается вовсе, и
    /// под открытым меню персонаж продолжал бы бегать по WASD, а мышь, которой
    /// целятся в кнопку, крутила бы камеру. Глушить по одному потребителю
    /// нельзя — ввод читают и ридер игрока, и камерные риги, и каждый
    /// следующий про паузу забудет. Карта — единственное общее место.
    /// </summary>
    public sealed class PauseScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [Tooltip("Кнопка «Продолжить»")]
        [SerializeField] private Button resumeButton;
        [Tooltip("Кнопка «Выход»: закрывает игру. Пусто — кнопки на экране нет")]
        [SerializeField] private Button exitButton;
        [Tooltip("Ассет управления. Не назначен — ввод на паузе останется живым, и персонаж продолжит бегать под меню")]
        [SerializeField] private InputActionAsset controls;
        [Tooltip("Карта игровых действий, которую гасит пауза. Карта интерфейса остаётся включённой — ей щёлкают по кнопке")]
        [SerializeField] private string gameplayMapName = "Player";

        /// <summary>
        /// Включённые паузы. Их бывает две: своя у сцены и общая рантайм-пауза
        /// (<see cref="PauseOverlay"/>), которая на время такой сцены
        /// выключается. Список, а не поле, чтобы выключение одной возвращало
        /// главной вторую, а не обнуляло обеих.
        /// </summary>
        private static readonly List<PauseScreen> Enabled = new List<PauseScreen>(2);

        /// <summary>Пауза этой сцены. Нужна тем, кто тоже слушает Esc.</summary>
        public static PauseScreen Current => Enabled.Count > 0 ? Enabled[Enabled.Count - 1] : null;

        /// <summary>Пауза открыта.</summary>
        public bool IsPaused { get; private set; }

        [SerializeField] private PauseMenuView presentation;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private float previousTimeScale;
        private GameObject previousSelection;

        /// <summary>
        /// Что было включено до паузы. Возвращаем ровно это, а не всю карту:
        /// половина действий в ней никем не читается, и включать их «заодно»
        /// значит менять состояние ввода мимо тех, кто им владеет.
        /// </summary>
        private readonly List<InputAction> suppressedActions = new List<InputAction>(8);

        /// <summary>
        /// Ассет управления: свой, если назначен в сцене, иначе общий для
        /// проекта. Пауза, поднятая в рантайме (<see cref="PauseOverlay"/>),
        /// назначить его в инспекторе не может, а гасить ввод обязана так же:
        /// иначе под открытым меню персонаж продолжает бегать по WASD.
        /// </summary>
        private InputActionAsset Controls => controls != null ? controls : InputSystem.actions;

        private void Awake()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(ContinueOrCancel);
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(RequestExit);
            }
        }

        /// <summary>
        /// Пауза сцены объявляется включением, а не рождением. Это нужно
        /// рантайм-паузе: в хабе своё меню, и общая пауза на время хаба
        /// выключается — иначе Esc обработали бы обе сразу.
        /// </summary>
        private void OnEnable()
        {
            if (!Enabled.Contains(this))
            {
                Enabled.Add(this);
            }
        }

        private void OnDisable()
        {
            // Выключают нас обычно на переходе сцены. Уходить, оставив мир
            // на паузе с заглушенным вводом, нельзя.
            if (IsPaused)
            {
                Resume();
            }

            Enabled.Remove(this);
        }

        private void OnDestroy()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(ContinueOrCancel);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(RequestExit);
            }

            // Уходя со сцены, время обязаны вернуть: иначе следующая сцена
            // грузится в остановленном мире и выглядит зависшей. Штатно это
            // уже сделал OnDisable, но объект могут снести и активным.
            if (IsPaused)
            {
                Time.timeScale = previousTimeScale;
                SetGameplayInputEnabled(true);
                RestoreCursor();
            }

            Enabled.Remove(this);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (IsPaused)
            {
                ContinueOrCancel();
                return;
            }

            // Той же клавишей выключают телевизор. Пока идёт выбор игры, Esc
            // принадлежит экрану приставки: иначе одно нажатие и погасило бы
            // экран, и открыло паузу поверх.
            if (ConsoleMenu.Active != null && ConsoleMenu.Active.BlocksPause)
            {
                return;
            }

            Pause();
        }

        private void Pause()
        {
            if (IsPaused) return;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            IsPaused = true;

            // Пауза — единственное меню, открываемое не кнопкой, а клавишей,
            // поэтому свой щелчок она обязана сыграть сама: общий проход
            // озвучивает кнопки, а Esc кнопкой не является.
            UiAudio.Play(CoreSfx.UiMenuOpen);

            if (panel != null)
            {
                panel.SetActive(true);
            }

            if (!IsNetworkSession)
            {
                Time.timeScale = 0f;
            }

            SetGameplayInputEnabled(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (presentation != null)
                presentation.Show(IsNetworkSession, MinigameControllerBase.Current != null && MinigameControllerBase.Current.CanLeaveRound,
                    NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
            else if (resumeButton != null) resumeButton.Select();
        }

        public void Resume()
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            UiAudio.Play(CoreSfx.UiBack);

            if (panel != null)
            {
                panel.SetActive(false);
            }

            Time.timeScale = previousTimeScale;
            SetGameplayInputEnabled(true);

            RestoreCursor();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
        }

        private void RestoreCursor()
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private void ContinueOrCancel()
        {
            if (presentation != null && presentation.IsConfirmingExit) presentation.CancelExit();
            else Resume();
        }

        private void RequestExit()
        {
            if (presentation != null && !presentation.IsConfirmingExit) presentation.ConfirmExit();
            else Exit();
        }

        /// <summary>
        /// Выход из раунда, если из него можно выйти, иначе — из игры целиком.
        ///
        /// Сессию рвём до закрытия и намеренно не ждём ответа: приложение всё
        /// равно закрывается, а хосту с остальными игроками важно получить
        /// разрыв сразу, а не по таймауту.
        ///
        /// Паузу снимаем первой строкой — на случай, если выход почему-то не
        /// состоится (в редакторе это обычное дело): иначе сцена осталась бы с
        /// остановленным временем и заглушенным вводом.
        /// </summary>
        public void Exit()
        {
            // Идёт раунд, из которого предусмотрен выход, — уходим из него,
            // а не из катки: остальные продолжают играть, мы досматриваем
            // наблюдателем.
            if (LeaveRound())
            {
                return;
            }

            QuitGame();
        }

        /// <summary>
        /// Выйти из раунда, оставшись в катке наблюдателем. Возвращает ложь,
        /// если выходить неоткуда — раунда нет или он уже кончился.
        /// </summary>
        public bool LeaveRound()
        {
            MinigameControllerBase minigame = MinigameControllerBase.Current;
            if (minigame == null || !minigame.CanLeaveRound)
            {
                return false;
            }

            Resume();
            Debug.Log("Пауза: выход из раунда — остаюсь в катке наблюдателем");
            minigame.LeaveRound();
            return true;
        }

        /// <summary>Закрыть игру целиком, разорвав сессию.</summary>
        public void QuitGame()
        {
            Resume();

            NetworkManager network = NetworkManager.Singleton;
            if (network != null && (network.IsClient || network.IsServer))
            {
                Debug.Log($"Пауза: выход из игры ({(network.IsHost ? "хост" : "клиент")}) — рву сессию");
                network.Shutdown();
            }

            Debug.Log("Пауза: закрываю игру");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Выключить или вернуть игровые действия. Возвращаются ровно те,
        /// что были включены на момент паузы.
        /// </summary>
        private void SetGameplayInputEnabled(bool enabled)
        {
            if (enabled)
            {
                for (int i = 0; i < suppressedActions.Count; i++)
                {
                    suppressedActions[i].Enable();
                }

                suppressedActions.Clear();
                return;
            }

            suppressedActions.Clear();

            InputActionAsset asset = Controls;
            if (asset == null)
            {
                return;
            }

            InputActionMap map = asset.FindActionMap(gameplayMapName, false);
            if (map == null)
            {
                Debug.LogWarning($"{name}: в ассете управления нет карты «{gameplayMapName}» — " +
                                 "ввод на паузе останется живым", this);
                return;
            }

            foreach (InputAction action in map.actions)
            {
                if (!action.enabled)
                {
                    continue;
                }

                suppressedActions.Add(action);
                action.Disable();
            }
        }

        private static bool IsNetworkSession =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }
}
