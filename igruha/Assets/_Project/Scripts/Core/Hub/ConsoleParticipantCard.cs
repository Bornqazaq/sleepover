using Igruha.Core.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.Hub
{
    public sealed class ConsoleParticipantCard : MonoBehaviour
    {
        public Image border, body;
        public TMP_Text playerName, badge, score, empty;
        public void Show(SessionPlayer player, bool own, Sprite portrait, int index, int slots)
        {
            bool compact = slots > 4;
            var rect = (RectTransform)transform;
            rect.anchoredPosition = new Vector2(48 + (index % 4) * 300, -(186 + (index / 4) * 151));
            rect.sizeDelta = new Vector2(284, compact ? 139 : 290);
            body.rectTransform.anchoredPosition = new Vector2(compact ? 14 : 38, compact ? -25 : -30);
            body.rectTransform.sizeDelta = new Vector2(compact ? 96 : 208, compact ? 114 : 232);
            body.sprite = portrait; body.enabled = portrait != null;
            playerName.rectTransform.anchoredPosition = new Vector2(compact ? 120 : 16, compact ? -42 : -247);
            playerName.rectTransform.sizeDelta = new Vector2(compact ? 150 : 252, 30);
            playerName.text = player?.DisplayName ?? string.Empty;
            badge.text = player == null ? $"МЕСТО {index + 1:00}" : own ? "ВАШ ПЕРСОНАЖ" : "В КОМНАТЕ";
            border.color = own ? new Color(.96f,.74f,.48f) : new Color(.25f,.37f,.38f);
            score.gameObject.SetActive(player != null && player.Score > 0);
            score.text = player != null ? $"{player.Score} ОЧК." : string.Empty;
            score.rectTransform.anchoredPosition = new Vector2(compact ? 120 : 198, compact ? -78 : -33);
            empty.gameObject.SetActive(player == null || player.Avatar == null);
            empty.text = player == null ? "+\n<size=17>ЖДЁМ ДРУЗЕЙ</size>" : "…\n<size=17>ВЫБИРАЕТ ОБЛИК</size>";
        }
    }
}
