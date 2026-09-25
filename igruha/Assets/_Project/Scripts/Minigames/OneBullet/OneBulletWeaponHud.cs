using Igruha.Core.Minigame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.OneBullet
{
    /// <summary>Displays replicated round deadlines; never runs a separate gameplay timer.</summary>
    public sealed class OneBulletWeaponHud : MonoBehaviour
    {
        [SerializeField] private OneBulletMinigame game;
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private TMP_Text title, detail, counter, unit;
        [SerializeField] private Image progress, accent;
        private int shown = int.MinValue;
        private const float FadeSpeed = 6f, PulseSpeed = 5f;
        private static readonly Color Gold = new Color(1f, .76f, .36f);
        private static readonly Color Pale = new Color(1f, .92f, .72f);

        private void Update()
        {
            bool visible = game.GameplayActive && !game.LocalArmed;
            visibility.alpha = Mathf.MoveTowards(visibility.alpha, visible ? 1 : 0, Time.unscaledDeltaTime * FadeSpeed);
            if (!visible) { shown = int.MinValue; return; }
            var round = game.Round;
            float remaining = Mathf.Max(0, (float)(round.SpawnAt - NetworkClock.Now));
            int state = round.Holder >= 0 ? -2 : round.Pickup >= 0 ? -1 : Mathf.CeilToInt(remaining);
            bool waiting = state >= 0;
            float duration = round.PreviousPickup < 0 ? game.Config.FirstSpawnDelay : game.Config.RespawnDelay;
            progress.fillAmount = waiting ? 1 - Mathf.Clamp01(remaining / Mathf.Max(.01f, duration)) : 1;
            accent.color = waiting && remaining <= 3 ? Color.Lerp(Gold, Pale, .5f + .5f * Mathf.Sin(Time.unscaledTime * PulseSpeed)) : Gold;
            if (state == shown) return;
            shown = state;
            title.text = waiting ? "ДО РЕВОЛЬВЕРА" : state == -1 ? "РЕВОЛЬВЕР ПОЯВИЛСЯ" : "ОРУЖИЕ ПОДОБРАНО";
            detail.text = waiting ? "Приготовься. Один патрон — один шанс." : state == -1 ? "Ищи золотое свечение · подбери касанием" : "Слушай шаги. Используй укрытия.";
            if (waiting) counter.SetText("{0:00}", state);
            else counter.text = state == -1 ? "!" : "•";
            unit.text = waiting ? "СЕК" : state == -1 ? "ИЩИ" : "ТИШЕ";
        }
    }
}
