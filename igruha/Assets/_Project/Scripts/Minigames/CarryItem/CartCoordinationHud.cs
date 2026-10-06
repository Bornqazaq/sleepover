using Igruha.Core.Items;
using Igruha.Core.Minigame;
using Igruha.Core.Player;
using Igruha.Core.Session;
using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Read-only local view of the occupied handles. No steering, input bindings or camera changes.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class CartCoordinationHud : MonoBehaviour
    {
        public const int MaxMembers = 4;
        public const float CardWidth = 132f;
        public const float ResultWidth = 170f;
        private const float PanelHeight = 172f;
        private const float PracticeHeight = 370f;
        private const float RoundHeight = 134f;
        private static readonly string[] KeyLabels = { "W", "A", "S", "D" };
        private static readonly Vector2[] KeyPositions =
        {
            new Vector2(0, -30), new Vector2(-25, -56), new Vector2(0, -56), new Vector2(25, -56)
        };
        private static readonly string[] ResultLabels = { "СТОИМ", "КАТИМ ВМЕСТЕ", "РАСХОДИМСЯ", "НАВСТРЕЧУ", "КАЧАЕТ", "ВОДА УХОДИТ", "СТЫКИ • ТИШЕ" };

        public sealed class Member
        {
            public PlayerController Player;
            public CarryInputSample Input;
            public Vector2 Direction;
            public CartInputRelation Relation;
            public bool IsLocal;
            internal RectTransform Root;
            internal TMP_Text Name;
            internal TMP_Text[] Keys;
        }

        private readonly Member[] members = new Member[MaxMembers];
        private CarryItemMinigame game;
        private Camera viewCamera;
        private Canvas canvas;
        private RectTransform panel, result;
        private CartCoordinationGraphic graphic;
        private TMP_Text resultLabel;
        private int shownCount = -1, shownResult = -1;
        private bool wasPractice;
        public int MemberCount { get; private set; }
        public Vector2 MeanDirection { get; private set; }
        public Vector2 CartDirection { get; private set; }
        public float SpillRisk { get; private set; }
        public bool Spilling { get; private set; }
        public Vector2 SpillDirection { get; private set; }
        public bool Visible => canvas != null && canvas.enabled;
        public Member MemberAt(int index) => members[index];
        public float PanelWidth => CardWidth * MemberCount + ResultWidth + 24f;
        public float MemberX(int index) => -PanelWidth * 0.5f + 12f + CardWidth * (index + 0.5f);
        public float ResultX => PanelWidth * 0.5f - ResultWidth * 0.5f - 12f;

        public void Bind(CarryItemMinigame owner, TMP_FontAsset font)
        {
            game = owner;
            viewCamera = Camera.main;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25;
            canvas.enabled = false;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            panel = Rect("Coordination", transform, new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, RoundHeight));
            graphic = panel.gameObject.AddComponent<CartCoordinationGraphic>();
            graphic.Bind(this); graphic.raycastTarget = false;
            for (int i = 0; i < MaxMembers; i++)
            {
                var m = new Member(); members[i] = m;
                m.Root = Rect("Carrier " + i, panel, Vector2.one * 0.5f, new Vector2(CardWidth, PanelHeight), Vector2.zero);
                m.Name = Label("Name", m.Root, font, new Vector2(0, 61), new Vector2(112, 28), 20);
                m.Name.overflowMode = TextOverflowModes.Ellipsis; m.Name.richText = false;
                m.Keys = new TMP_Text[KeyLabels.Length];
                for (int k = 0; k < KeyLabels.Length; k++)
                {
                    m.Keys[k] = Label(KeyLabels[k], m.Root, font, KeyPositions[k], new Vector2(23, 23), 17);
                    m.Keys[k].text = KeyLabels[k];
                }
            }
            result = Rect("Result", panel, Vector2.one * 0.5f, new Vector2(ResultWidth, PanelHeight), Vector2.zero);
            resultLabel = Label("Direction", result, font, new Vector2(0, 61), new Vector2(166, 28), 20);
            Label("Release", result, font, new Vector2(0, -60), new Vector2(150, 23), 15).text = "E  ОТПУСТИТЬ";
        }

        private void LateUpdate()
        {
            var scoreboard = SessionScoreboard.Current;
            var local = scoreboard?.LocalPlayer;
            WaterCart cart = local != null ? game.CartForPlayer(local.Id) : null;
            bool visible = game.GameplayActive && !game.StartCountdownActive && local?.Avatar != null &&
                cart != null && !cart.IsLost && cart.Carry.IsCarriedBy(local.Avatar);
            canvas.enabled = visible;
            if (!visible)
            {
                MemberCount = 0;
                return;
            }
            if (viewCamera == null) viewCamera = Camera.main;
            Vector3 cameraForward = viewCamera != null ? viewCamera.transform.forward : Vector3.forward;
            Vector3 cameraRight = viewCamera != null ? viewCamera.transform.right : Vector3.right;
            MultiCarryObject carry = cart.Carry;
            Vector3 sum = Vector3.zero;
            MemberCount = 0;
            for (int slot = 0; slot < carry.HandleCount; slot++)
            {
                var player = carry.CarrierAt(slot);
                if (player == null) continue;
                var m = members[MemberCount++];
                bool isLocal = player == local.Avatar;
                if (m.Player != player || m.IsLocal != isLocal)
                {
                    m.Player = player; m.IsLocal = isLocal;
                    string name = "ИГРОК";
                    foreach (var entry in scoreboard.Players)
                        if (entry.Avatar == player) { name = entry.DisplayName; break; }
                    m.Name.text = isLocal ? "ТЫ" : name;
                    m.Name.color = isLocal ? CartCoordinationGraphic.Local : MinigameUiStyle.OnDark;
                }
                m.Input = carry.CarrierInputAt(slot);
                sum += m.Input.World;
                m.Direction = CartCoordinationMath.OnScreen(m.Input.World, cameraForward, cameraRight);
            }
            MeanDirection = CartCoordinationMath.OnScreen(sum / Mathf.Max(1, MemberCount), cameraForward, cameraRight);
            CartDirection = CartCoordinationMath.OnScreen(cart.transform.forward, cameraForward, cameraRight);
            SpillRisk = cart.Stability.State.Risk;
            Spilling = cart.Stability.IsSpilling;
            SpillDirection = CartCoordinationMath.OnScreen(cart.transform.TransformDirection(
                CartWaterSurface.Outward(cart.Stability.State.SpillSide)), cameraForward, cameraRight);
            bool opposing = false, diverging = false;
            for (int i = 0; i < MemberCount; i++)
            {
                var m = members[i];
                m.Relation = CartCoordinationMath.Relation(m.Input.World, sum - m.Input.World);
                opposing |= m.Relation == CartInputRelation.Opposing;
                diverging |= m.Relation == CartInputRelation.Diverging;
                for (int k = 0; k < KeyLabels.Length; k++)
                    m.Keys[k].color = KeyActive(m.Input.Move, k) ? MinigameUiStyle.Ink : MinigameUiStyle.MutedOnDark;
            }
            int state = opposing ? 3 : diverging ? 2 : MeanDirection.sqrMagnitude > 0.01f ? 1 : 0;
            if (SpillRisk > 0.65f) state = 4;
            if (cart.Stability.State.Cause == CartTiltCause.Road && SpillRisk > .45f) state = 6;
            if (Spilling) state = 5;
            if (state != shownResult) { shownResult = state; resultLabel.text = ResultLabels[state]; }
            resultLabel.color = Spilling ? MinigameUiStyle.Urgent : SpillRisk > 0.65f ? MinigameUiStyle.Accent : MinigameUiStyle.MutedOnDark;
            bool practice = game.Phase == MinigamePhase.Practice;
            if (MemberCount != shownCount || practice != wasPractice)
            {
                shownCount = MemberCount; wasPractice = practice;
                panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
                panel.anchoredPosition = new Vector2(0, practice ? PracticeHeight : RoundHeight);
                for (int i = 0; i < MaxMembers; i++)
                {
                    members[i].Root.gameObject.SetActive(i < MemberCount);
                    members[i].Root.anchoredPosition = new Vector2(MemberX(i), 0);
                }
                result.anchoredPosition = new Vector2(ResultX, 0);
            }
            graphic.SetVerticesDirty();
        }

        public static bool KeyActive(Vector2 move, int key) => key switch
        {
            0 => move.y > CartCoordinationMath.DeadZone,
            1 => move.x < -CartCoordinationMath.DeadZone,
            2 => move.y < -CartCoordinationMath.DeadZone,
            _ => move.x > CartCoordinationMath.DeadZone
        };

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, Vector2 position, Vector2 size, float fontSize)
        {
            var text = Rect(name, parent, Vector2.one * 0.5f, size, position).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset; text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center; text.color = MinigameUiStyle.MutedOnDark;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false; return text;
        }
    }
}
