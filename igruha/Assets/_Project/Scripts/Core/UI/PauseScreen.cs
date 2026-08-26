using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Netcode;
using Igruha.Core.Hub;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Пауза по Esc. Из функционала — «Продолжить» и «Выход»: это заглушка на
    /// время разработки, настоящее меню с настройками живёт в EPIC 6 и придёт
    /// вместе с главным меню.
    ///
    /// <b>Выход означает выход из игры целиком</b>, а не возврат в хаб: хаб —
    /// это уже игра, возвращать в него из паузы некуда. Перед закрытием
    /// отключаемся от сессии, чтобы остальные увидели уход честно, а не
    /// зависший силуэт до таймаута.
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

        /// <summary>Пауза этой сцены. Нужна тем, кто тоже слушает Esc.</summary>
        public static PauseScreen Current { get; private set; }

        /// <summary>Пауза открыта.</summary>
        public bool IsPaused { get; private set; }

        private bool cursorWasLocked;

        /// <summary>
        /// Что было включено до паузы. Возвращаем ровно это, а не всю карту:
        /// половина действий в ней никем не читается, и включать их «заодно»
        /// значит менять состояние ввода мимо тех, кто им владеет.
        /// </summary>
        private readonly List<InputAction> suppressedActions = new List<InputAction>(8);

        private void Awake()
        {
            Current = this;

            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(Resume);
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(Exit);
            }
        }

        private void OnDestroy()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(Resume);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(Exit);
            }

            // Уходя со сцены, время обязаны вернуть: иначе следующая сцена
            // грузится в остановленном мире и выглядит зависшей.
            if (IsPaused)
            {
                Time.timeScale = 1f;
                SetGameplayInputEnabled(true);
            }

            if (Current == this)
            {
                Current = null;
            }
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
                Resume();
                return;
            }

            // Той же клавишей выключают телевизор. Пока идёт выбор игры, Esc
            // принадлежит экрану приставки: иначе одно нажатие и погасило бы
            // экран, и открыло паузу поверх.
            if (ConsoleMenu.Active != null && ConsoleMenu.Active.IsOpen)
            {
                return;
            }

            Pause();
        }

        private void Pause()
        {
            IsPaused = true;

            if (panel != null)
            {
                panel.SetActive(true);
            }

            if (!IsNetworkSession)
            {
                Time.timeScale = 0f;
            }

            SetGameplayInputEnabled(false);

            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Resume()
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;

            if (panel != null)
            {
                panel.SetActive(false);
            }

            Time.timeScale = 1f;
            SetGameplayInputEnabled(true);

            if (cursorWasLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// Выход из игры целиком.
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

            if (controls == null)
            {
                return;
            }

            InputActionMap map = controls.FindActionMap(gameplayMapName, false);
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
