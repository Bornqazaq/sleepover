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
        [Tooltip("Полоса, показывающая, сколько осталось до автостарта")]
        [SerializeField] private Image timerFill;
        [Tooltip("Появление карточки")]
        [SerializeField] private UiPop cardPop;

        [Tooltip("Сама карточка: по ней пересчитывается компоновка перед подгонкой кегля")]
        [SerializeField] private RectTransform card;


        private Action onClosed;
        private Action<bool> setPracticeControls;
        private bool expanded;
        private bool practiceComplete;
        private TutorialArenaPreview arenaPreview;
        private TutorialView view;
        private float inputEnabledAt;
        private const float InputGuardSeconds = 0.3f;
        private bool oldCursorVisible;
        private CursorLockMode oldCursorLock;
        private bool visible;

        public bool IsVisible => visible;

        public void Show(MinigameDefinition definition, Action readyCallback, Action<bool> practiceControls = null)
        {
            onClosed = readyCallback;
            setPracticeControls = practiceControls;
            practiceComplete = false;
            if (panel != null) panel.SetActive(false);
            if (view == null)
            {
                view = new GameObject("UnifiedTutorial").AddComponent<TutorialView>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(view.gameObject, gameObject.scene);
                view.Build(definition != null && definition.TutorialFont != null
                    ? definition.TutorialFont : titleText != null ? titleText.font : TMP_Settings.defaultFontAsset,
                    RequestReady, TogglePractice);
                arenaPreview = view.gameObject.AddComponent<TutorialArenaPreview>();
                arenaPreview.Bind(view.ArenaPreview);
            }
            if (!visible)
            {
                oldCursorVisible = Cursor.visible;
                oldCursorLock = Cursor.lockState;
            }
            visible = true;
            inputEnabledAt = Time.unscaledTime + InputGuardSeconds;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            view.Show(definition);
            SetExpanded(true);
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
            if (!visible || Time.unscaledTime < inputEnabledAt ||
                (PauseScreen.Current != null && PauseScreen.Current.IsPaused)) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) TogglePractice();
            if (keyboard.f2Key.wasPressedThisFrame) RequestReady();
        }

        public void TogglePractice()
        {
            if (!visible || practiceComplete) return;
            SetExpanded(!expanded);
        }

        private void SetExpanded(bool value)
        {
            expanded = value;
            view.SetExpanded(value);
            arenaPreview.SetVisible(value);
            setPracticeControls?.Invoke(!value);
            Cursor.lockState = value ? CursorLockMode.None : oldCursorLock;
            Cursor.visible = value || oldCursorVisible;
        }

        public void SetPracticeComplete()
        {
            practiceComplete = true;
            SetExpanded(true);
            view.SetPracticeComplete();
        }

        public void Hide()
        {
            onClosed = null;
            setPracticeControls = null;
            if (arenaPreview != null) arenaPreview.SetVisible(false);
            if (panel != null) panel.SetActive(false);
            if (view != null) view.gameObject.SetActive(false);
            if (!visible) return;
            visible = false;
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
