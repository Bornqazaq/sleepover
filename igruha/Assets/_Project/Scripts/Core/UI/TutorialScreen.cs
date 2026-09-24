using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using Igruha.Core.Minigame;
using Igruha.Core.Session;

namespace Igruha.Core.UI
{
    /// <summary>
    /// Общий экран правил. Кнопка сообщает готовность, а Hide вызывает только смена фазы.
    /// Старые сериализованные ссылки сохраняются для совместимости сборщиков сцен;
    /// их панель заменяет единый TutorialView.
    /// </summary>
    public sealed class TutorialScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text objectiveText;

        [Header("Карточка")]
        [Tooltip("Плашка категории: «Все против всех», «Команды», «Один против всех»")]
        [SerializeField] private TMP_Text categoryText;
        [Tooltip("Готовые строки управления. Лишние прячутся, нехватка молча отбрасывает хвост подсказок")]
        [SerializeField] private TutorialHintRow[] hintRows = Array.Empty<TutorialHintRow>();
        [Tooltip("Старая ссылка сборщика сцены; автостарт отключён")]
        [SerializeField] private Image timerFill;
        [Tooltip("Появление карточки")]
        [SerializeField] private UiPop cardPop;

        [Tooltip("Сама карточка: по ней пересчитывается компоновка перед подгонкой кегля")]
        [SerializeField] private RectTransform card;


        private Action onClosed;
        private Action<bool> setReading;
        private Action restartPractice;
        private bool expanded;
        private bool practiceComplete;
        private TutorialView view;
        private float inputEnabledAt;
        private const float InputGuardSeconds = 0.3f;
        private bool oldCursorVisible;
        private CursorLockMode oldCursorLock;
        private bool visible;
        private bool pointerHeld;
        private bool wasPaused;
        private static TutorialScreen activeScreen;

        public bool IsVisible => visible;
        public bool RulesExpanded => expanded;
        // Читается ридером непосредственно из состояния клавиш: клик по UI
        // не должен стать ударом из-за порядка Update двух компонентов.
        public static bool PointerInputActive => activeScreen != null && activeScreen.visible &&
            (activeScreen.expanded || activeScreen.practiceComplete || PointerKeyHeld);

        private static bool PointerKeyHeld => Keyboard.current != null &&
            (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);

        public void Show(MinigameDefinition definition, Action readyCallback, Action<bool> reading = null, Action restart = null)
        {
            onClosed = readyCallback;
            setReading = reading;
            restartPractice = restart;
            practiceComplete = false;
            if (panel != null) panel.SetActive(false);
            if (view == null)
            {
                view = new GameObject("UnifiedTutorial").AddComponent<TutorialView>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(view.gameObject, gameObject.scene);
                view.Build(definition != null && definition.TutorialFont != null
                    ? definition.TutorialFont : titleText != null ? titleText.font : TMP_Settings.defaultFontAsset,
                    RequestReady, TogglePractice, RequestRestart);
            }
            if (!visible)
            {
                oldCursorVisible = Cursor.visible;
                oldCursorLock = Cursor.lockState;
            }
            visible = true;
            activeScreen = this;
            pointerHeld = PointerKeyHeld;
            wasPaused = false;
            inputEnabledAt = Time.unscaledTime + InputGuardSeconds;
            view.Show(definition);
            SetExpanded(false);
        }

        public void SetReadiness(IReadOnlyList<SessionPlayer> players,
            IReadOnlyList<TutorialParticipant> participants, int localId)
        {
            view?.SetReadiness(players, participants, localId);
        }

        public void RequestReady()
        {
            if (visible && Time.unscaledTime >= inputEnabledAt &&
                (PauseScreen.Current == null || !PauseScreen.Current.IsPaused)) onClosed?.Invoke();
        }

        private void Update()
        {
            if (!visible) return;
            if (PauseScreen.Current != null && PauseScreen.Current.IsPaused)
            {
                wasPaused = true;
                return;
            }
            if (pointerHeld != PointerKeyHeld || wasPaused)
            {
                pointerHeld = PointerKeyHeld;
                wasPaused = false;
                RefreshCursor();
            }
            if (Time.unscaledTime < inputEnabledAt) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) TogglePractice();
            if (keyboard.f2Key.wasPressedThisFrame) RequestReady();
        }

        public void TogglePractice()
        {
            if (!visible) return;
            SetExpanded(!expanded);
        }

        private void SetExpanded(bool value)
        {
            expanded = value;
            view.SetExpanded(value);
            RefreshCursor();
        }

        private void RefreshCursor()
        {
            bool release = expanded || practiceComplete || pointerHeld;
            setReading?.Invoke(release);
            Cursor.lockState = release ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = release;
        }

        public void SetPracticeComplete()
        {
            practiceComplete = true;
            SetExpanded(false);
            view.SetPracticeComplete();
        }

        private void RequestRestart()
        {
            if (visible && practiceComplete) restartPractice?.Invoke();
        }

        public void Hide()
        {
            onClosed = null;
            setReading = null;
            restartPractice = null;
            if (panel != null) panel.SetActive(false);
            if (view != null) view.gameObject.SetActive(false);
            if (!visible) return;
            visible = false;
            if (activeScreen == this) activeScreen = null;
            Cursor.lockState = oldCursorLock;
            Cursor.visible = oldCursorVisible;
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (view != null)
            {
                if (Application.isPlaying) Destroy(view.gameObject);
                else DestroyImmediate(view.gameObject);
            }
        }
    }
}
