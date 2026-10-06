using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoCombatHud : MonoBehaviour
    {
        private static readonly string[] Labels = { "ЛКМ  ТОЛЧОК     ЗАЖАТЬ ЛКМ  СИЛА     ПКМ  ЗАЩИТА", "ЗАЩИТА СПЕРЕДИ", "ЗАРЯД — ОТПУСТИ ЛКМ", "СИЛОВОЙ ГОТОВ — ОТПУСТИ ЛКМ", "КОНТРАТАКА!  ЛКМ", "ВОССТАНОВЛЕНИЕ", "РАВНОВЕСИЕ ПОТЕРЯНО", "ПАРИРОВАНИЕ!", "ГОТОВИТСЯ РЫВОК", "РЫВОК ПЛЕЧОМ" };
        private static readonly string[] GamepadLabels = { "RT/R2  ТОЛЧОК     УДЕРЖАТЬ  СИЛА     LT/L2  ЗАЩИТА", "ЗАЩИТА СПЕРЕДИ", "ЗАРЯД — ОТПУСТИ RT/R2", "СИЛОВОЙ ГОТОВ — ОТПУСТИ RT/R2", "КОНТРАТАКА!  RT/R2", "ВОССТАНОВЛЕНИЕ", "РАВНОВЕСИЕ ПОТЕРЯНО", "ПАРИРОВАНИЕ!", "ГОТОВИТСЯ РЫВОК", "РЫВОК ПЛЕЧОМ" };
        private const float GamepadHintThreshold = .25f;
        public bool UsesGamepad { get; private set; }
        public string LabelFor(int status) => (UsesGamepad ? GamepadLabels : Labels)[status];
        private SumoMinigame game;
        private SumoCombat combat;
        private Canvas canvas;
        private TextMeshProUGUI label, dashLabel;
        private Image fill;
        private int shown = -1, dashShown = -2;
        private void OnEnable() => InputSystem.onActionChange += OnActionChange;
        private void OnDisable() => InputSystem.onActionChange -= OnActionChange;
        private void OnActionChange(object value, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed || !(value is InputAction action) || action.activeControl == null) return;
            var control = action.activeControl;
            bool gamepad = control.device is Gamepad;
            if (!gamepad && !(control.device is Mouse) && !(control.device is Keyboard)) return;
            // Ignore releases and analogue drift; merely connecting a pad changes no hints.
            if (control.EvaluateMagnitude() < (gamepad ? GamepadHintThreshold : .01f) || UsesGamepad == gamepad) return;
            UsesGamepad = gamepad; shown = -1; dashShown = -2;
        }
        public void Bind(SumoMinigame owner, SumoCombat fight, TMP_FontAsset font)
        {
            game = owner; combat = fight;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var panel = Rect("Combat", transform, new Vector2(.5f, 0), new Vector2(820, 96), new Vector2(0, 103));
            var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(.025f, .07f, .075f, .9f); bg.raycastTarget = false;
            var text = Rect("State", panel, new Vector2(.5f, .5f), new Vector2(800, 40), new Vector2(0, 21));
            label = text.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font != null ? font : TMP_Settings.defaultFontAsset; label.fontSize = 23; label.alignment = TextAlignmentOptions.Center; label.color = new Color(1, .95f, .8f); label.raycastTarget = false;
            var hint = Rect("Dash", panel, new Vector2(.5f, .5f), new Vector2(800, 28), new Vector2(0, -14));
            dashLabel = hint.gameObject.AddComponent<TextMeshProUGUI>(); dashLabel.font = label.font; dashLabel.fontSize = 18;
            dashLabel.alignment = TextAlignmentOptions.Center; dashLabel.color = new Color(.9f, .8f, .6f); dashLabel.raycastTarget = false;
            var bar = Rect("Progress", panel, new Vector2(.5f, 0), new Vector2(780, 5), new Vector2(0, 10));
            fill = bar.gameObject.AddComponent<Image>(); fill.color = new Color(.35f, .9f, .83f); fill.raycastTarget = false;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor; r.sizeDelta = size; r.anchoredPosition = position; return r;
        }
        private void Update()
        {
            var local = combat.Local;
            bool visible = game.Phase == MinigamePhase.Round && combat.Active && local != null && !local.Participant.Dead;
            canvas.enabled = visible; if (!visible) return;
            // Input windows use estimated command arrival, not buffered presentation time.
            var s = local.VisualState; double now = local.VisualNow;
            int status = 0; float amount = 0;
            var color = new Color(.35f, .9f, .83f);
            if (s.HasCounter(now)) { status = 4; amount = (float)(s.CounterUntil - now) / local.Config.CounterWindow; color = new Color(1, .79f, .25f); }
            else if (s.Attack == SumoAttack.Dash && (s.Phase == SumoCombatPhase.Windup || s.Phase == SumoCombatPhase.Dash))
            { status = s.Phase == SumoCombatPhase.Windup ? 8 : 9; amount = Mathf.Clamp01((float)((s.Until - now) / (s.Until - s.Since))); color = new Color(1, .55f, .22f); }
            else if (s.Phase == SumoCombatPhase.Charge)
            { amount = Mathf.Clamp01((float)(now - s.Since) / local.Config.ChargeSeconds); status = amount >= 1 ? 3 : 2; color = new Color(1, .72f, .2f); }
            else if (s.Phase == SumoCombatPhase.Guard) { status = 1; amount = Mathf.Clamp01((float)(s.ParryUntil - now) / local.Config.ParryWindow); }
            else if (s.Phase == SumoCombatPhase.Stagger) { status = 6; amount = Mathf.Clamp01((float)((s.Until - now) / (s.Until - s.Since))); color = new Color(1, .43f, .3f); }
            else if (s.Phase == SumoCombatPhase.Recovery || s.Phase == SumoCombatPhase.Windup) { status = 5; amount = Mathf.Clamp01((float)((s.Until - now) / (s.Until - s.Since))); }
            if (shown != status) { shown = status; label.text = LabelFor(status); }
            int cooldown = s.HasCounter(now) ? -1 : Mathf.Max(0, Mathf.CeilToInt((float)(s.NextDashAt - now)));
            if (dashShown != cooldown)
            {
                dashShown = cooldown;
                if (cooldown < 0) dashLabel.text = "ПАРИРОВАНИЕ ОТКРЫЛО КОНТРАТАКУ";
                else if (cooldown > 0) dashLabel.SetText("РЫВОК ЧЕРЕЗ {0} с", cooldown);
                else dashLabel.text = UsesGamepad ? "LT/L2 + RT/R2  РЫВОК ПЛЕЧОМ" : "ПКМ + ЛКМ  РЫВОК ПЛЕЧОМ";
            }
            fill.rectTransform.sizeDelta = new Vector2(780 * Mathf.Clamp01(amount), 5); fill.color = color;
        }
    }
}
