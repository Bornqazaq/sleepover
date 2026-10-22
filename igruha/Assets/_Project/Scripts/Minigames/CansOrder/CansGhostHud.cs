using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CansOrder
{
    public sealed class CansGhostHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(.035f, .055f, .105f, .94f);
        private static readonly Color Mint = new Color(.58f, 1f, .88f);
        private GameObject canvasRoot, flight, victim, solved;
        private TMP_Text charges, targetLabel, action, victimText, solvedCount, solvedNext;
        private Image dot;
        private CanvasGroup victimAlpha;
        private CanvasGroup solvedAlpha;
        private float victimUntil, rejectedUntil, solvedUntil;
        public bool ShowingSolved => solved != null && solved.activeSelf;
        private int lastTarget = int.MinValue, lastCharges = -1, lastWait = -1, lastMode = -1;

        public void Build(TMP_FontAsset font)
        {
            if (canvasRoot != null) return;
            canvasRoot = new GameObject("Ghost HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 140;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var root = (RectTransform)canvasRoot.transform;
            flight = new GameObject("Flight", typeof(RectTransform)); flight.transform.SetParent(root, false);
            var f = (RectTransform)flight.transform; f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one; f.offsetMin = f.offsetMax = Vector2.zero;
            RectTransform header = Panel(f, "Ghost badge", new Vector2(.5f, 1), new Vector2(0, -76), new Vector2(530, 86));
            Text(header, font, "ТЫ СОБРАЛ — ТЫ ПРИЗРАК", new Vector2(0, 15), new Vector2(500, 34), 25, Mint);
            Text(header, font, "Твой результат сохранён", new Vector2(0, -21), new Vector2(480, 25), 17, Color.white);
            RectTransform controls = Panel(f, "Flight controls", new Vector2(.5f, 0), new Vector2(0, 88), new Vector2(960, 116));
            charges = Text(controls, font, "", new Vector2(-344, 20), new Vector2(220, 36), 26, Mint);
            action = Text(controls, font, "", new Vector2(125, 20), new Vector2(650, 36), 23, Color.white);
            Text(controls, font, "WASD — полёт   •   Мышь — направление   •   ЛКМ — пакость",
                new Vector2(0, -29), new Vector2(920, 28), 18, new Color(.7f, .78f, .86f));
            targetLabel = Text(f, font, "", new Vector2(0, -62), new Vector2(700, 34), 23, Mint);
            var dotRect = Rect(f, "Aim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(8, 8));
            dot = dotRect.gameObject.AddComponent<Image>(); dot.raycastTarget = false; dot.color = Mint; dotRect.localRotation = Quaternion.Euler(0, 0, 45);
            var v = Panel(root, "Someone swapped your cans", new Vector2(.5f, 1), new Vector2(0, -166), new Vector2(650, 76));
            victim = v.gameObject; victimAlpha = victim.AddComponent<CanvasGroup>();
            victimText = Text(v, font, "БУ! ПРИЗРАК ПОМЕНЯЛ ДВЕ БАНКИ", Vector2.zero, new Vector2(620, 60), 25, Mint);
            var success = Panel(root, "All cans solved", new Vector2(.5f, .62f), Vector2.zero, new Vector2(740, 204));
            solved = success.gameObject; solvedAlpha = solved.AddComponent<CanvasGroup>();
            solvedCount = Text(success, font, "", new Vector2(0, 55), new Vector2(690, 52), 42, Mint);
            Text(success, font, "ВСЕ БАНКИ СОБРАНЫ!", new Vector2(0, 2), new Vector2(690, 42), 32, Color.white);
            solvedNext = Text(success, font, "", new Vector2(0, -58), new Vector2(710, 44), 20, new Color(.78f, .9f, .88f));
            HideAll();
        }

        public void RenderGhost(bool active, CansGhostState state, int target, string name, bool available, bool pending, int circle = -1)
        {
            if (flight == null) return;
            if (flight.activeSelf != active) flight.SetActive(active);
            if (!active) return;
            if (lastCharges != state.Charges) { lastCharges = state.Charges; charges.SetText("ПАКОСТИ  {0}/2", lastCharges); }
            int wait = Mathf.Max(0, Mathf.CeilToInt((float)(state.ReadyAt - NetworkClock.Now)));
            bool nextTurn = state.Used == 1 && state.First.Circle == circle;
            int mode = Time.unscaledTime < rejectedUntil ? 5 : pending ? 4 : state.Charges == 0 ? 3 :
                wait > 0 ? 2 : nextTurn ? 6 : available ? 1 : 0;
            if (lastTarget != target)
            { lastTarget = target; targetLabel.text = target < 0 ? "" : name; }
            if (mode != lastMode || wait != lastWait)
            {
                lastMode = mode; lastWait = wait;
                action.text = mode == 6 ? "Следующая пакость — в следующем ходе" :
                    mode == 5 ? "Не сейчас — выбери другую полку" : mode == 4 ? "Ловим момент…" :
                    mode == 3 ? "Пакости закончились — летай и наблюдай" : mode == 2 ? "Следующая пакость через " + wait + " с" :
                    mode == 1 ? "ЛКМ — перепутать две банки" : "Подлети к полке во время расстановки";
            }
            dot.color = available ? Mint : new Color(.7f, .75f, .8f, .65f);
        }

        public void ShowVictim()
        {
            if (victim == null) return;
            victimUntil = Time.unscaledTime + 2.4f; victim.SetActive(true); victimAlpha.alpha = 1;
        }
        public void ShowSolved(int count, bool willBecomeGhost)
        {
            solvedCount.SetText("{0} / {0}", count);
            solvedNext.text = willBecomeGhost ? "Результат сохранён. Сейчас станешь призраком…"
                : "Результат сохранён. Ждём итогов раунда.";
            solvedUntil = Time.unscaledTime + CansGhostRules.SolvedSeconds;
            solvedAlpha.alpha = 1f; solved.SetActive(true);
        }
        public void Rejected() => rejectedUntil = Time.unscaledTime + 1.6f;
        private void Update()
        {
            if (ShowingSolved)
            {
                float remaining = solvedUntil - Time.unscaledTime;
                solvedAlpha.alpha = Mathf.Clamp01(remaining / .2f);
                if (remaining <= 0) solved.SetActive(false);
            }
            if (victim == null || !victim.activeSelf) return;
            float left = victimUntil - Time.unscaledTime;
            victimAlpha.alpha = Mathf.Clamp01(left / .35f);
            if (left <= 0) victim.SetActive(false);
        }
        public void HideAll()
        {
            if (solved != null) solved.SetActive(false);
            if (flight != null) flight.SetActive(false);
            if (victim != null) victim.SetActive(false);
            lastTarget = int.MinValue; lastCharges = lastWait = lastMode = -1;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = anchor; r.anchoredPosition = position; r.sizeDelta = size; return r;
        }
        private static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var r = Rect(parent, name, anchor, position, size); var image = r.gameObject.AddComponent<Image>();
            image.color = Ink; image.raycastTarget = false;
            var line = Rect(r, "Mint accent", new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(size.x, 3)).gameObject.AddComponent<Image>();
            line.color = Mint; line.raycastTarget = false; return r;
        }
        private static TMP_Text Text(Transform parent, TMP_FontAsset font, string value, Vector2 position, Vector2 size, float pointSize, Color color)
        {
            var r = Rect(parent, "Text", new Vector2(.5f, .5f), position, size);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>(); t.font = font; t.text = value;
            t.fontSize = pointSize; t.color = color; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            return t;
        }
        private void OnDestroy() { if (canvasRoot != null) Destroy(canvasRoot); }
    }
}
