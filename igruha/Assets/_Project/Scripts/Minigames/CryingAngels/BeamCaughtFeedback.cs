using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Экранный ответ Бегущему на луч. Замер по логике сервера, а картинка
    /// (конус луча, свет на полу) от третьего лица не говорит «это в тебя»:
    /// луч чуть в стороне, а игрока уже держит, и заморозка читается как
    /// залипание. Здесь момент попадания делается однозначным:
    /// вспышка в кадр в момент попадания, пульсирующая тёплая кромка и надпись,
    /// пока луч держит, короткий отклик на окаменение.
    ///
    /// Показывает только своего игрока и ничего не решает: состояние берёт из
    /// <see cref="RunnerState"/>, которое у клиента приезжает с сервера.
    /// Все элементы строятся кодом при старте — сцена хранит только сам
    /// компонент на канвасе рамки окаменения.
    /// </summary>
    public sealed class BeamCaughtFeedback : MonoBehaviour
    {
        private const float GlowClearRadius = 0.38f;

        [Header("Попадание")]
        [Tooltip("Цвет фонаря на экране: вспышка и кромка")]
        [SerializeField] private Color torchColor = new Color(1f, 0.92f, 0.74f, 1f);
        [Tooltip("Сила вспышки в момент попадания, 0..1")]
        [Range(0f, 1f)]
        [SerializeField] private float hitFlashAlpha = 0.75f;
        [Tooltip("За сколько секунд вспышка гаснет")]
        [SerializeField] private float hitFlashDuration = 0.4f;

        [Header("Пока держит луч")]
        [Tooltip("Кромка экрана: минимум и максимум пульсации, 0..1")]
        [SerializeField] private Vector2 glowAlphaRange = new Vector2(0.22f, 0.5f);
        [Tooltip("Частота пульсации кромки, Гц")]
        [SerializeField] private float glowPulseHz = 2.4f;
        [Tooltip("Надпись, пока луч держит игрока")]
        [SerializeField] private string caughtLabel = "ТЫ В ЛУЧЕ";
        [Tooltip("Подсказка под надписью")]
        [SerializeField] private string caughtHint = "замри — фонарь держит тебя";
        [Tooltip("Надпись в момент окаменения")]
        [SerializeField] private string petrifiedLabel = "ОКАМЕНЕЛ";
        [Tooltip("Цвет надписи окаменения")]
        [SerializeField] private Color stoneColor = new Color(0.72f, 0.7f, 0.66f, 1f);

        [Header("Надпись")]
        [Tooltip("Шрифт. Пусто — берётся шрифт TMP по умолчанию")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float labelFontSize = 92f;
        [SerializeField] private float hintFontSize = 34f;
        [Tooltip("С какого масштаба надпись «ударяет» в кадр")]
        [SerializeField] private float labelPunchScale = 1.7f;
        [Tooltip("За сколько секунд надпись садится в нормальный масштаб")]
        [SerializeField] private float labelPunchDuration = 0.16f;
        [Tooltip("За сколько секунд надпись уходит после освобождения")]
        [SerializeField] private float labelFadeOut = 0.22f;
        [Tooltip("Смещение надписи от центра экрана вверх, доля высоты")]
        [Range(0f, 0.5f)]
        [SerializeField] private float labelHeight = 0.22f;

        /// <summary>Луч только что поймал своего игрока — точка для звука.</summary>
        public event Action Caught;

        private RunnerState tracked;
        private Image flash;
        private Image glow;
        private TMP_Text label;
        private TMP_Text hint;
        private RectTransform labelRoot;
        private Texture2D glowTexture;

        private float hitTimer;
        private float punchTimer;
        private float fadeTimer;
        private float glowPhase;
        private bool held;

        private void Awake()
        {
            BuildElements();
            HideAll();
        }

        /// <summary>За кем следим. Ставится контроллером на раздаче ролей; null — всё прячем.</summary>
        public void Track(RunnerState runner)
        {
            if (tracked != null)
            {
                tracked.Changed -= OnPhaseChanged;
            }

            tracked = runner;
            HideAll();

            if (tracked != null)
            {
                tracked.Changed += OnPhaseChanged;
                // Игрок мог вернуться в раунд уже под лучом — тогда кромка нужна сразу, без вспышки.
                if (tracked.Current != RunnerState.Phase.Free)
                {
                    ShowHeld(tracked.Current, false);
                }
            }
        }

        private void OnDestroy()
        {
            if (tracked != null)
            {
                tracked.Changed -= OnPhaseChanged;
            }

            if (glowTexture != null)
            {
                Destroy(glowTexture);
            }
        }

        private void OnPhaseChanged(RunnerState.Phase phase)
        {
            switch (phase)
            {
                case RunnerState.Phase.Frozen:
                    ShowHeld(phase, true);
                    Caught?.Invoke();
                    break;
                case RunnerState.Phase.Petrified:
                    ShowHeld(phase, false);
                    break;
                default:
                    Release();
                    break;
            }
        }

        private void ShowHeld(RunnerState.Phase phase, bool punch)
        {
            bool frozen = phase == RunnerState.Phase.Frozen;
            held = true;
            fadeTimer = 0f;
            punchTimer = punch ? labelPunchDuration : 0f;
            hitTimer = punch ? hitFlashDuration : 0f;
            glowPhase = 0f;

            label.text = frozen ? caughtLabel : petrifiedLabel;
            label.color = frozen ? torchColor : stoneColor;
            hint.text = caughtHint;
            hint.enabled = frozen;
            label.enabled = true;
            labelRoot.localScale = Vector3.one * (punch ? labelPunchScale : 1f);
            SetLabelAlpha(1f);

            // Окаменение рисует серая рамка: тёплая кромка на нём врала бы, что фонарь ещё держит.
            glow.enabled = frozen;
        }

        private void Release()
        {
            held = false;
            hitTimer = 0f;
            glow.enabled = false;
            hint.enabled = false;
            fadeTimer = label.enabled ? labelFadeOut : 0f;
        }

        private void Update()
        {
            if (tracked == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            if (hitTimer > 0f)
            {
                hitTimer = Mathf.Max(0f, hitTimer - dt);
                float t = hitFlashDuration > 0f ? hitTimer / hitFlashDuration : 0f;
                SetFlash(hitFlashAlpha * t * t);
            }

            if (held)
            {
                if (punchTimer > 0f)
                {
                    punchTimer = Mathf.Max(0f, punchTimer - dt);
                    float t = labelPunchDuration > 0f ? 1f - punchTimer / labelPunchDuration : 1f;
                    float eased = 1f - (1f - t) * (1f - t);
                    labelRoot.localScale = Vector3.one * Mathf.Lerp(labelPunchScale, 1f, eased);
                }

                if (glow.enabled)
                {
                    glowPhase += dt * glowPulseHz * Mathf.PI * 2f;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(glowPhase);
                    SetGlow(Mathf.Lerp(glowAlphaRange.x, glowAlphaRange.y, pulse));
                }
            }
            else if (fadeTimer > 0f)
            {
                fadeTimer = Mathf.Max(0f, fadeTimer - dt);
                float t = labelFadeOut > 0f ? fadeTimer / labelFadeOut : 0f;
                SetLabelAlpha(t);
                if (fadeTimer <= 0f)
                {
                    label.enabled = false;
                }
            }
        }

        private void HideAll()
        {
            held = false;
            hitTimer = 0f;
            punchTimer = 0f;
            fadeTimer = 0f;
            SetFlash(0f);
            glow.enabled = false;
            label.enabled = false;
            hint.enabled = false;
        }

        private void SetFlash(float alpha)
        {
            Color c = torchColor;
            c.a = alpha;
            flash.color = c;
            flash.enabled = alpha > 0.001f;
        }

        private void SetGlow(float alpha)
        {
            Color c = torchColor;
            c.a = alpha;
            glow.color = c;
        }

        private void SetLabelAlpha(float alpha)
        {
            Color c = label.color;
            c.a = alpha;
            label.color = c;

            Color h = hint.color;
            h.a = alpha * 0.85f;
            hint.color = h;
        }

        /// <summary>
        /// Слои снизу вверх: кромка, вспышка, надпись. Всё на весь экран,
        /// без raycast — под ними живёт игровой HUD.
        /// </summary>
        private void BuildElements()
        {
            glow = CreateFullScreenImage("BeamGlow");
            glow.sprite = RadialVignetteSprite.Create("BeamGlow", GlowClearRadius, out glowTexture);

            flash = CreateFullScreenImage("BeamHitFlash");

            var rootGo = new GameObject("BeamLabel", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            labelRoot = rootGo.GetComponent<RectTransform>();
            labelRoot.anchorMin = new Vector2(0.5f, 0.5f + labelHeight);
            labelRoot.anchorMax = labelRoot.anchorMin;
            labelRoot.sizeDelta = Vector2.zero;

            label = CreateText("Caption", labelFontSize, 0f);
            label.fontStyle = FontStyles.Bold;
            hint = CreateText("Hint", hintFontSize, -labelFontSize * 0.85f);
            hint.color = new Color(1f, 1f, 1f, 0.85f);
        }

        private Image CreateFullScreenImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private TMP_Text CreateText(string name, float size, float yOffset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(labelRoot, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = rect.anchorMin;
            rect.anchoredPosition = new Vector2(0f, yOffset);
            rect.sizeDelta = new Vector2(1600f, size * 1.4f);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(0, 0, 0, 200);
            text.enabled = false;
            return text;
        }
    }
}
