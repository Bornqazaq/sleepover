using Igruha.Core.Minigame;
using Unity.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoCombatHud : MonoBehaviour
    {
        private static readonly string[] Labels = { "ЛКМ  ТОЛЧОК     ЗАЖАТЬ ЛКМ  СИЛА     ПКМ  ЗАЩИТА", "ЗАЩИТА СПЕРЕДИ", "ЗАРЯД — ОТПУСТИ ЛКМ", "СИЛОВОЙ ГОТОВ — ОТПУСТИ ЛКМ", "КОНТРАТАКА!  ЛКМ", "ВОССТАНОВЛЕНИЕ", "РАВНОВЕСИЕ ПОТЕРЯНО", "ПАРИРОВАНИЕ!" };
        private SumoMinigame game;
        private SumoCombat combat;
        private Canvas canvas;
        private TextMeshProUGUI label;
        private Image fill;
        private NetworkManager network;
        private int shown = -1;
        public void Bind(SumoMinigame owner, SumoCombat fight, TMP_FontAsset font)
        {
            game = owner; combat = fight; network = NetworkManager.Singleton;
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var panel = Rect("Combat", transform, new Vector2(.5f, 0), new Vector2(820, 66), new Vector2(0, 103));
            var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(.025f, .07f, .075f, .9f); bg.raycastTarget = false;
            var text = Rect("State", panel, new Vector2(.5f, .5f), new Vector2(800, 40), new Vector2(0, 5));
            label = text.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font != null ? font : TMP_Settings.defaultFontAsset; label.fontSize = 23; label.alignment = TextAlignmentOptions.Center; label.color = new Color(1, .95f, .8f); label.raycastTarget = false;
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
            var s = local.State; double now = network != null && network.IsListening ? network.LocalTime.Time : NetworkClock.Now;
            int status = 0; float amount = 0;
            var color = new Color(.35f, .9f, .83f);
            if (s.HasCounter(now)) { status = 4; amount = (float)(s.CounterUntil - now) / local.Config.CounterWindow; color = new Color(1, .79f, .25f); }
            else if (s.Phase == SumoCombatPhase.Charge)
            { amount = Mathf.Clamp01((float)(now - s.Since) / local.Config.ChargeSeconds); status = amount >= 1 ? 3 : 2; color = new Color(1, .72f, .2f); }
            else if (s.Phase == SumoCombatPhase.Guard) { status = 1; amount = Mathf.Clamp01((float)(s.ParryUntil - now) / local.Config.ParryWindow); }
            else if (s.Phase == SumoCombatPhase.Stagger) { status = 6; amount = Mathf.Clamp01((float)((s.Until - now) / (s.Until - s.Since))); color = new Color(1, .43f, .3f); }
            else if (s.Phase == SumoCombatPhase.Recovery || s.Phase == SumoCombatPhase.Windup) { status = 5; amount = Mathf.Clamp01((float)((s.Until - now) / (s.Until - s.Since))); }
            if (shown != status) { shown = status; label.text = Labels[status]; }
            fill.rectTransform.sizeDelta = new Vector2(780 * Mathf.Clamp01(amount), 5); fill.color = color;
        }
    }
}
