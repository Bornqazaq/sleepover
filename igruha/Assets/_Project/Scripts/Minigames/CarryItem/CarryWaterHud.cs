using Igruha.Core.Minigame;
using Igruha.Core.Session;
using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local presentation of replicated water, stock and clock. Never changes game state.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CarryWaterHud : MonoBehaviour
    {
        private static readonly Color Surface = new Color(.025f, .065f, .10f, .94f);
        private static readonly Color Muted = new Color(.65f, .78f, .84f);
        private static readonly Color Water = new Color(.13f, .81f, 1f);
        private static readonly Color Warning = new Color(1f, .73f, .26f);
        private const float CriticalSeconds = 10f;
        private const float LossNoticeSeconds = 1.2f;
        private const float RoundCardBottom = 64f, PracticeCardBottom = 300f;
        private CarryItemMinigame game;
        private RoundHud standardHud;
        private Canvas canvas;
        private TMP_FontAsset font;
        private RectTransform scoreRow, cartPanel, notice;
        private TMP_Text clock, clockCaption, waterAmount, waterCaption, status, advice, controls;
        private WaterLevelGraphic vessel;
        private TutorialPanel stateDot;
        private readonly TeamCard[] teams = new TeamCard[2];
        private int shownSeconds = -1, shownWater = -1, shownStatus = -1;
        private bool wasPractice, wasCountdown, wasHolding;
        private WaterCart observedCart;
        private float lossNoticeUntil;
        public float DisplayedWaterFraction => vessel != null ? vessel.Level : 0;
        public bool WaterVisible => cartPanel != null && cartPanel.gameObject.activeInHierarchy;

        private sealed class TeamCard
        {
            public TMP_Text Title, Amount, Stock;
            public RectTransform Fill;
            public TutorialPanel Accent;
            public int Water = -1, Carts = -1;
            public TeamSide Local = (TeamSide)byte.MaxValue;
            public bool Lost;
        }

        public void Bind(CarryItemMinigame owner, RoundHud hud, AnnouncerBanner banner)
        {
            game = owner; standardHud = hud;
            var source = hud != null ? hud.GetComponentInChildren<TMP_Text>(true) : null;
            font = source != null ? source.font : TMP_Settings.defaultFontAsset;
            // Older authored scenes contain two TeamProgress roots, only one of which is bound.
            // Retire both once, including inactive copies, so reloading training cannot revive them.
            foreach (var root in owner.gameObject.scene.GetRootGameObjects())
                foreach (var legacy in root.GetComponentsInChildren<TeamProgressBar>(true))
                    legacy.gameObject.SetActive(false);
            standardHud?.HideStatus(); standardHud?.SetTimerPlateVisible(false);
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            scoreRow = Rect("Water score", transform, new Vector2(.5f, 1), new Vector2(.5f, 1), 0, -24, 924, 108);
            teams[0] = BuildTeam(0, TeamSide.A);
            teams[1] = BuildTeam(612, TeamSide.B);
            var timer = Panel("Round clock", scoreRow, 338, 0, 248, 108, Surface, 24);
            clockCaption = Label("Clock caption", timer, "ДО КОНЦА РАУНДА", 12, 15, 224, 22, 16, Muted, true);
            clock = Label("Time", timer, "", 12, 31, 224, 70, 48, Color.white, true);

            cartPanel = Rect("Your water", transform, new Vector2(1, 0), new Vector2(1, 0), -32, RoundCardBottom, 404, 180);
            var card = cartPanel.gameObject.AddComponent<TutorialPanel>(); card.color = Surface; card.Radius = 24; card.raycastTarget = false;
            vessel = Rect("Water vessel", cartPanel, new Vector2(0, 1), new Vector2(0, 1), 18, -22, 82, 132).gameObject.AddComponent<WaterLevelGraphic>();
            vessel.raycastTarget = false;
            waterCaption = Label("Vessel caption", cartPanel, "В ТЕЛЕЖКЕ", 119, 15, 261, 22, 17, Muted);
            waterAmount = Label("Water amount", cartPanel, "", 117, 33, 264, 51, 34, Color.white);
            stateDot = Panel("State dot", cartPanel, 120, 94, 8, 8, Water, 4).GetComponent<TutorialPanel>();
            status = Label("Water state", cartPanel, "", 139, 83, 245, 31, 21, Water);
            advice = Label("Water advice", cartPanel, "", 119, 115, 268, 49, 18, Muted);
            advice.textWrappingMode = TextWrappingModes.Normal;
            controls = Label("Handle controls", cartPanel, "", 10, 189, 384, 30, 18, MinigameUiStyle.OnDark, true);

            if (banner != null)
            {
                notice = banner.transform as RectTransform;
                notice.anchorMin = notice.anchorMax = notice.pivot = new Vector2(.5f, 1);
                notice.anchoredPosition = new Vector2(0, -150); notice.sizeDelta = new Vector2(650, 54);
                var plate = notice.gameObject.AddComponent<TutorialPanel>();
                plate.color = Surface; plate.Radius = 18; plate.raycastTarget = false;
                var label = banner.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.fontSize = 23; label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 23;
                    label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                    label.rectTransform.offsetMin = new Vector2(18, 4); label.rectTransform.offsetMax = new Vector2(-18, -4);
                }
            }
            LateUpdate();
        }

        private TeamCard BuildTeam(float x, TeamSide side)
        {
            Color tint = TeamPalette.ColorOf(side);
            var root = Panel("Team " + side, scoreRow, x, 0, 312, 108, Surface, 22);
            var result = new TeamCard();
            result.Accent = Panel("Team accent", root, 16, 18, 5, 24, tint, 2).GetComponent<TutorialPanel>();
            result.Title = Label("Team name", root, "", 30, 15, 150, 26, 17, tint);
            result.Amount = Label("Delivered water", root, "", 162, 8, 134, 38, 24, Color.white);
            result.Amount.alignment = TextAlignmentOptions.Right;
            Panel("Tank track", root, 16, 51, 280, 14, new Color(.15f, .23f, .29f), 7);
            var clip = Rect("Tank clip", root, new Vector2(0, 1), new Vector2(0, 1), 16, -51, 280, 14);
            var mask = clip.gameObject.AddComponent<RectMask2D>(); mask.softness = Vector2Int.one;
            result.Fill = Panel("Delivered fill", clip, 0, 0, 280, 14, tint, 7);
            for (int tick = 1; tick < 4; tick++) Panel("Quarter", root, 16 + 70 * tick, 52, 2, 12, Surface, 0);
            Label("Delivered caption", root, "ДОСТАВЛЕНО", 16, 76, 120, 20, 13, Muted);
            result.Stock = Label("Cart stock", root, "", 144, 73, 152, 25, 15, Muted);
            result.Stock.alignment = TextAlignmentOptions.Right;
            return result;
        }

        private void LateUpdate()
        {
            if (game == null || canvas == null) return;
            bool active = game.GameplayActive;
            canvas.enabled = active;
            if (!active) return;
            bool practice = game.IsPractice, countdown = game.StartCountdownActive;
            scoreRow.gameObject.SetActive(!practice);
            var local = SessionScoreboard.Current?.LocalPlayer;
            TeamSide side = game.TeamOfPlayer(local?.Id ?? -1);
            UpdateTeam(teams[0], TeamSide.A, side, game.State.TeamA.Water);
            UpdateTeam(teams[1], TeamSide.B, side, game.State.TeamB.Water);
            game.TryGetRoundTime(out float remaining, out _);
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds; clock.text = $"{seconds / 60}:{seconds % 60:00}";
                clock.color = seconds <= CriticalSeconds && !countdown ? MinigameUiStyle.Urgent : Color.white;
            }
            if (countdown != wasCountdown) { clockCaption.text = countdown ? "ГОТОВЬТЕСЬ" : "ДО КОНЦА РАУНДА"; wasCountdown = countdown; }
            bool waiting = local != null && game.TryGetRespawnDeadline(local.Id, out double deadline) && deadline > 0;
            bool showWater = !countdown && !waiting && side != TeamSide.None;
            cartPanel.gameObject.SetActive(showWater);
            if (practice != wasPractice)
            {
                wasPractice = practice;
                cartPanel.anchoredPosition = new Vector2(-32, practice ? PracticeCardBottom : RoundCardBottom);
                if (notice != null) notice.anchoredPosition = new Vector2(0, practice ? -210 : -150);
            }
            if (!showWater) return;
            WaterCart cart = game.CartForPlayer(local.Id);
            if (cart != observedCart)
            {
                observedCart = cart; shownWater = -1; lossNoticeUntil = 0;
            }
            bool holding = cart != null && local.Avatar != null && cart.Carry.IsCarriedBy(local.Avatar);
            int water = cart != null && !cart.IsLost ? cart.Water : 0;
            int capacity = game.Config.CartCapacity;
            if (cart != null && !cart.IsLost && water > 0 &&
                (cart.Stability.IsSpilling || (shownWater > water && !cart.IsPouring && !cart.IsFilling)))
                lossNoticeUntil = Time.unscaledTime + LossNoticeSeconds;
            if (water != shownWater)
            {
                shownWater = water;
                waterAmount.SetText("{0}<size=55%><color=#A6C7D6> / {1}</color></size>", water, capacity);
            }
            vessel.SetLevel(capacity > 0 ? water / (float)capacity : 0);
            int state = StateOf(cart, side, holding);
            if (state != shownStatus || holding != wasHolding)
            {
                shownStatus = state; wasHolding = holding;
                PresentState(state, holding);
            }
        }

        private void UpdateTeam(TeamCard card, TeamSide side, TeamSide local, int water)
        {
            int capacity = game.Config.TankCapacity;
            if (water != card.Water)
            {
                card.Water = water; card.Amount.SetText("{0}<size=65%><color=#A6C7D6> / {1}</color></size>", water, capacity);
                card.Fill.sizeDelta = new Vector2(280 * Mathf.Clamp01(water / (float)Mathf.Max(1, capacity)), 14);
            }
            if (card.Local != local)
            {
                card.Local = local;
                card.Title.text = side == local ? "ВАША КОМАНДА" : side == TeamSide.A ? "КОМАНДА А" : "КОМАНДА Б";
            }
            var cart = game.CartOf(side);
            int stock = cart != null ? cart.RemainingCarts : 0;
            bool lost = cart != null && cart.IsLost && !cart.IsDepleted;
            if (stock == card.Carts && lost == card.Lost) return;
            card.Carts = stock; card.Lost = lost;
            card.Stock.text = lost ? "ЗАМЕНА ЕДЕТ…" : stock == 0 ? "ТЕЛЕЖЕК НЕТ" : stock == 1 ? "ТЕЛЕЖКИ  •  1/2" : "ТЕЛЕЖКИ  •  2/2";
            card.Stock.color = lost || stock == 0 ? Warning : Muted;
        }

        private int StateOf(WaterCart cart, TeamSide side, bool holding)
        {
            if (cart == null) return 0;
            if (cart.IsLost) return cart.IsDepleted ? 2 : 1;
            if (!holding && cart.ControlTeam != side) return 3;
            if (cart.Water > 0 && (cart.Stability.IsSpilling || Time.unscaledTime < lossNoticeUntil)) return 4;
            if (cart.IsPouring) return 5;
            if (cart.IsFilling) return 6;
            if (cart.Water == 0) return 7;
            if (holding && cart.Stability.State.Risk > .65f) return 8;
            if (holding && cart.Stability.NeedsHands) return 9;
            return 10;
        }

        private void PresentState(int state, bool holding)
        {
            Color tint = state == 4 ? MinigameUiStyle.Urgent : state is 1 or 2 or 3 or 8 ? Warning : Water;
            status.color = tint; stateDot.color = tint; vessel.Tint = tint;
            waterCaption.text = state == 3 ? "ВАШУ ТЕЛЕЖКУ УВЕЛИ" : "ВОДА В ТЕЛЕЖКЕ";
            status.text = state switch
            {
                0 => "ЖДЁМ ТЕЛЕЖКУ", 1 => "ЗАМЕНА В ПУТИ", 2 => "ТЕЛЕЖЕК НЕТ", 3 => "ВЕРНИТЕ ТЕЛЕЖКУ",
                4 => "ВОДА УХОДИТ!", 5 => "ДОСТАВЛЯЕМ", 6 => "НАПОЛНЯЕТСЯ", 7 => "БАК ПУСТ", 8 => "ОСТОРОЖНО, ВОЛНА",
                9 => "НУЖЕН ПОМОЩНИК", _ => "МОЖНО ВЕЗТИ"
            };
            advice.text = state switch
            {
                0 => "Скоро появится у крана", 1 => "Новая появится у крана", 2 => "Заберите свободную чужую",
                3 => "Сбейте хват соперника", 4 => "Сбавьте ход. Тяните вместе.", 5 => "Вода переходит в бак команды",
                6 => "Уезжайте, когда хватит воды", 7 => "Вернитесь к своему крану", 8 => "Поворачивайте плавнее",
                9 => "Вдвоём катить быстрее", _ => "Доставьте к насосу команды"
            };
            controls.text = state is 0 or 1 ? "" : holding ? "<b>WASD</b>  КАТИТЬ     <b>E</b>  ОТПУСТИТЬ" : "<b>E</b>  ВЗЯТЬСЯ ЗА РУЧКУ";
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h); return rect;
        }

        private static RectTransform Panel(string name, Transform parent, float x, float y, float w, float h, Color color, float radius)
        {
            var rect = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), x, -y, w, h);
            var image = rect.gameObject.AddComponent<TutorialPanel>(); image.color = color; image.Radius = radius; image.raycastTarget = false;
            return rect;
        }

        private TMP_Text Label(string name, Transform parent, string value, float x, float y, float w, float h, float size, Color tint, bool center = false)
        {
            var rect = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), x, -y, w, h);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = tint; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            return text;
        }
    }
}
