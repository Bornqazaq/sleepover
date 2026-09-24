using Igruha.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.Mosquitoes
{
    public sealed class MosquitoesPresentation : MonoBehaviour
    {
        [SerializeField] private MosquitoesConfig config;
        [SerializeField] private Light bedsideLight, windowLight, fillLight;
        [SerializeField] private ScreenVignette vignette;
        [SerializeField] private TMP_Text status, instructions;
        [SerializeField] private MosquitoesAudio audioPlayer;
        [SerializeField] private ParticleSystem biteFlash, deathPuff;
        [SerializeField] private Renderer[] lampGlow;
        private MaterialPropertyBlock lampProperties;
        private AmbientMode ambientMode;
        private Color ambient, ambientSky, ambientEquator, ambientGround;
        private float ambientIntensity, reflection, lampIntensity, windowIntensity, fillIntensity;
        private bool active, giant, warned;
        private GiantPhase lastPhase = (GiantPhase)255;
        public void Begin()
        {
            if (active) return; active = true; warned = false; lastPhase = (GiantPhase)255;
            ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight;
            ambientSky = RenderSettings.ambientSkyColor; ambientEquator = RenderSettings.ambientEquatorColor; ambientGround = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity; reflection = RenderSettings.reflectionIntensity;
            lampIntensity = bedsideLight != null ? bedsideLight.intensity : 0;
            windowIntensity = windowLight != null ? windowLight.intensity : 0;
            fillIntensity = fillLight != null ? fillLight.intensity : 0;
            if (status != null) status.gameObject.SetActive(true);
            if (instructions != null) instructions.gameObject.SetActive(true);
            audioPlayer?.Begin();
        }
        public void SetRole(bool isGiant, string text)
        {
            giant = isGiant;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = giant ? Color.black : new Color(.30f, .37f, .55f);
            RenderSettings.ambientIntensity = giant ? 0 : 1;
            RenderSettings.reflectionIntensity = giant ? 0 : .4f;
            if (windowLight != null) windowLight.intensity = giant ? config.WindowIntensity : .7f;
            if (fillLight != null) fillLight.intensity = giant ? 0 : .2f;
            if (instructions != null) instructions.text = text;
        }
        public void Paint(GiantSleepState state, float countdown, int living, bool practice)
        {
            if (!active) return;
            if (instructions != null) instructions.gameObject.SetActive(!practice);
            if (lastPhase != state.Phase)
            {
                if (lastPhase != (GiantPhase)255 && (state.Phase == GiantPhase.LyingDown || state.Phase == GiantPhase.Waking))
                    audioPlayer?.Effect(3, bedsideLight.transform.position);
                lastPhase = state.Phase;
            }
            if (bedsideLight != null) bedsideLight.enabled = state.LampOn;
            SetLampGlow(state.LampOn);
            vignette?.SetAmount(giant && state.Phase == GiantPhase.Sleeping ? config.SleepVignette : 0);
            if (!warned && state.Sleep >= config.SleepTarget - 10) { warned = true; audioPlayer?.LastTenSeconds(); }
        }
        public void SetStatus(string text) { if (status != null) status.text = text; }
        private void SetLampGlow(bool on)
        {
            if (lampGlow == null) return;
            if (lampProperties == null) lampProperties = new MaterialPropertyBlock();
            lampProperties.SetColor("_EmissionColor", on ? new Color(.5f, .24f, .06f) : Color.black);
            foreach (var renderer in lampGlow) if (renderer != null) renderer.SetPropertyBlock(lampProperties);
        }
        public void Effect(byte kind, Vector3 position, bool visible)
        {
            audioPlayer?.Effect(kind, position);
            ParticleSystem particle = kind == 0 ? biteFlash : kind == 2 ? deathPuff : null;
            if (particle != null && visible) { particle.transform.position = position; particle.Play(); }
        }
        public void End()
        {
            if (!active) return; active = false;
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
            RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator; RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity; RenderSettings.reflectionIntensity = reflection;
            if (bedsideLight != null) { bedsideLight.enabled = true; bedsideLight.intensity = lampIntensity; }
            SetLampGlow(true);
            if (windowLight != null) windowLight.intensity = windowIntensity;
            if (fillLight != null) fillLight.intensity = fillIntensity;
            vignette?.SetImmediate(0);
            if (status != null) status.gameObject.SetActive(false);
            if (instructions != null) instructions.gameObject.SetActive(false);
            audioPlayer?.End();
            if (biteFlash != null) biteFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (deathPuff != null) deathPuff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void OnDisable() => End();
    }
}
