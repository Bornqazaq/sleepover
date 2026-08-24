using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Netcode;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Пауза по Esc. Из функционала — только «Продолжить»: это заглушка на
    /// время разработки, а настоящее меню паузы с настройками и выходом
    /// живёт в EPIC 6 и появится вместе с главным меню.
    ///
    /// <b>Время останавливается только вне сети.</b> В сетевой катке
    /// <c>Time.timeScale = 0</c> остановил бы только одну машину: сервер
    /// продолжает считать раунд, таймер идёт, Утки бегут, — и вернувшийся
    /// игрок оказывается в другой игре. Поэтому по сети пауза остаётся
    /// экраном поверх, а мир под ним живёт.
    ///
    /// Курсор освобождается на время паузы: без этого мышь заперта ригом
    /// первого лица, и по кнопке нечем щёлкнуть.
    ///
    /// <b>Игровой ввод на паузе выключается целиком, картой действий.</b>
    /// Одной остановки времени мало: по сети время не останавливается вовсе,
    /// и под открытым меню персонаж продолжал бегать по WASD, а мышь, которой
    /// целятся в кнопку, крутила камеру. Глушить по одному потребителю нельзя —
    /// ввод читают и ридер игрока, и оба камерных рига, и каждый следующий
    /// про паузу забудет. Карта — единственное общее место, ниже которого
    /// читать уже нечего.
    /// </summary>
    public sealed class PauseScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [Tooltip("Кнопка «Продолжить». Больше на экране паузы ничего нет намеренно")]
        [SerializeField] private Button resumeButton;
        [Tooltip("Ассет управления. Не назначен — ввод на паузе останется живым, и персонаж продолжит бегать под меню")]
        [SerializeField] private InputActionAsset controls;
        [Tooltip("Карта игровых действий, которую гасит пауза. Карта интерфейса остаётся включённой — ей щёлкают по кнопке")]
        [SerializeField] private string gameplayMapName = "Player";

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
            if (panel != null)
            {
                panel.SetActive(false);
            }

            if (resumeButton != null)
            {
                resumeButton.onClick.AddListener(Resume);
            }
        }

        private void OnDestroy()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(Resume);
            }

            // Уходя со сцены, время обязаны вернуть: иначе следующая сцена
            // грузится в остановленном мире и выглядит зависшей.
            if (IsPaused)
            {
                Time.timeScale = 1f;
                SetGameplayInputEnabled(true);
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
