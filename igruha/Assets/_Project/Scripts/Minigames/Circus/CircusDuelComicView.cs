using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.Circus
{
    public sealed class CircusDuelComicView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RawImage picture;
        [SerializeField] private TMP_Text firstName, secondName, versus;
        [SerializeField] private CircusDuelPortraits portraits;
        private const float RevealSeconds = .065f, NameDelay = .22f, NameSlideSeconds = .33f;
        private const float NameEnterFade = 5f, NameScalePunch = .09f, NameRotation = 3f;
        private const float SoloNameWidth = .74f, SplitNameWidth = .42f, NameHeight = .18f;
        private const float SoloNameOffset = .12f, SplitNameOffset = .255f, NameBaseline = -.31f;
        private const float SoloNameSize = 112f, SplitNameSize = 88f;
        private const float VersusPunchSeconds = .22f, VersusFadeSeconds = .07f, VersusScalePunch = .65f;
        private Material material;
        private RectTransform rect;
        private bool prepared;
        private static readonly int SecondTex = Shader.PropertyToID("_SecondTex");
        private static readonly int CueId = Shader.PropertyToID("_Cue"), AgeId = Shader.PropertyToID("_Age");
        private static readonly int ElapsedId = Shader.PropertyToID("_Elapsed"), AspectId = Shader.PropertyToID("_Aspect");
        public bool IsVisible => group != null && group.alpha > .01f;

        private void Awake()
        {
            rect = (RectTransform)transform;
            if (picture != null && picture.material != null) { material = new Material(picture.material); picture.material = material; }
            if (group != null) group.alpha = 0;
        }

        public void Render(CageStation first, CageStation second, string a, string b, int cue, float elapsed)
        {
            if (cue >= 4 || cue < 0) { Hide(); return; }
            if (!prepared)
            {
                prepared = true;
                firstName.text = string.IsNullOrWhiteSpace(a) ? "ИГРОК" : a;
                secondName.text = string.IsNullOrWhiteSpace(b) ? "ИГРОК" : b;
                portraits.Begin(first, second);
                picture.texture = portraits.First;
                material.SetTexture(SecondTex, portraits.Second);
            }
            if (cue == 0) { group.alpha = 0; return; }
            float start = cue == 1 ? CircusDuelState.BlackoutSeconds : cue == 2 ? CircusDuelState.SecondSpotAt : CircusDuelState.BothSpotsAt;
            float age = Mathf.Max(0, elapsed - start);
            float fade = 1f - Mathf.SmoothStep(0, 1, (elapsed - CircusDuelState.ReturnAt) / (CircusDuelState.IntroSeconds - CircusDuelState.ReturnAt));
            // Keep the graphic continuous across cuts; only the first reveal fades over the arena.
            group.alpha = (cue == 1 ? Mathf.Clamp01(age / RevealSeconds) : 1f) * fade;
            portraits.Render(cue, age);
            material.SetFloat(CueId, cue); material.SetFloat(AgeId, age); material.SetFloat(ElapsedId, elapsed);
            material.SetFloat(AspectId, rect.rect.width / Mathf.Max(1, rect.rect.height));
            bool split = cue == 3;
            Name(firstName, cue == 1 || split, false, split, age);
            Name(secondName, cue == 2 || split, true, split, age);
            versus.gameObject.SetActive(split);
            if (split)
            {
                float hit = Mathf.Clamp01(age / VersusPunchSeconds);
                versus.rectTransform.localScale = Vector3.one * (1f + VersusScalePunch * Mathf.Pow(1-hit, 3));
                versus.alpha = Mathf.Clamp01(age / VersusFadeSeconds);
            }
        }

        private void Name(TMP_Text text, bool visible, bool second, bool split, float age)
        {
            text.gameObject.SetActive(visible); if (!visible) return;
            float progress = Mathf.Clamp01((age - NameDelay) / NameSlideSeconds);
            float settle = 1f + 2.5f * Mathf.Pow(progress-1, 3) + 1.5f * Mathf.Pow(progress-1, 2);
            float width = rect.rect.width, height = rect.rect.height;
            float sign = second ? 1 : -1;
            float x = split ? sign * width * SplitNameOffset : sign * width * SoloNameOffset;
            text.rectTransform.sizeDelta = new Vector2(width * (split ? SplitNameWidth : SoloNameWidth), height * NameHeight);
            text.rectTransform.anchoredPosition = new Vector2(x + sign * width * (1-settle), height * NameBaseline);
            text.rectTransform.localRotation = Quaternion.Euler(0,0,second ? NameRotation : -NameRotation);
            text.rectTransform.localScale = Vector3.one * (1 + NameScalePunch * (1-progress));
            text.fontSizeMax = split ? SplitNameSize : SoloNameSize;
            text.alpha = Mathf.Clamp01(progress * NameEnterFade);
        }

        public void Hide()
        {
            if (group != null) group.alpha = 0;
            if (!prepared) return;
            portraits.Release(); prepared = false;
            if (picture != null) picture.texture = null;
            if (material != null) material.SetTexture(SecondTex, null);
        }
        private void OnDisable() => Hide();
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
