using Igruha.Minigames.Circus;
using TMPro;
using UnityEngine;
using static Igruha.Minigames.Circus.CircusUiLayout;

namespace Igruha.Minigames.Stopwatch
{
    /// <summary>Input feedback without a clock, progress animation or periodic cue.</summary>
    public sealed class StopwatchButtonHud : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform key;
        [SerializeField] private CircusUiGraphic keyFill, accent, stateIcon;
        [SerializeField] private TMP_Text title, note, caption, keyLabel;
        private static readonly Color Brass = new Color(1f, .76f, .40f);
        private static readonly Color Held = new Color(1f, .43f, .28f);
        private int previous = -1;

        public void Construct(Transform canvas, TMP_FontAsset font)
        {
            var card = Card(canvas, "StopwatchButtonHint", 0, 86, 556, 104, new Color(.10f,.075f,.085f,.97f));
            card.anchorMin = card.anchorMax = new Vector2(.5f, 0);
            panel = card.gameObject;
            accent = Icon(card,"Accent",CircusUiGraphic.Sign.Panel,-271,0,3,68,Brass);
            var keyRoot = Card(card,"Key_E",-221,0,64,64,new Color(.23f,.16f,.13f));
            key = keyRoot;
            keyFill = keyRoot.Find("Surface").GetComponent<CircusUiGraphic>();
            keyLabel = Text(keyRoot,"Letter",font,"E",0,1,60,40,31,Brass);
            keyLabel.fontStyle = FontStyles.Bold;
            caption = Text(card,"Caption",font,"ТВОЯ КНОПКА",34,31,416,18,10,Brass,TextAlignmentOptions.MidlineLeft);
            caption.characterSpacing = 2;
            title = Text(card,"Title",font,"Зажми E и считай про себя",34,7,416,30,22,Cream,TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            note = Text(card,"Note",font,"Отпусти, когда отмеришь нужное время",34,-24,416,24,14,Muted,TextAlignmentOptions.MidlineLeft);
            stateIcon = Icon(keyRoot,"Accepted",CircusUiGraphic.Sign.Check,0,0,28,28,Mint);
            panel.SetActive(false);
        }

        public void Render(CageButton button, bool practice)
        {
            bool visible = button != null && button.WindowOpen && !practice;
            Show(panel, visible);
            if (!visible) { previous = -1; return; }
            int state = (int)button.State;
            if (state == previous) return;
            previous = state;
            bool running = button.State == CageButton.ButtonState.Running;
            bool stopped = button.Completed;
            Color color = stopped ? Mint : running ? Held : Brass;
            accent.color = color; caption.color = color; keyLabel.color = color;
            keyFill.color = running ? new Color(.34f,.13f,.10f) : new Color(.23f,.16f,.13f);
            key.anchoredPosition = new Vector2(-221, running ? -3 : 0);
            Show(keyLabel.gameObject, !stopped); Show(stateIcon.gameObject, stopped);
            Set(caption, stopped ? "ОТВЕТ ЗАПИСАН" : running ? "КНОПКА НАЖАТА" : "ТВОЯ КНОПКА");
            Set(title, stopped ? "Готово — ждём остальных" : running ? "Держи E · считай про себя" : "Зажми E и считай про себя");
            Set(note, stopped ? "Результат появится на общем табло" : running ? "Отпусти E, чтобы зафиксировать ответ" : "Отпусти, когда отмеришь нужное время");
        }

        public void Hide() { Show(panel, false); previous = -1; }
        private void OnDisable() => Hide();
    }
}
