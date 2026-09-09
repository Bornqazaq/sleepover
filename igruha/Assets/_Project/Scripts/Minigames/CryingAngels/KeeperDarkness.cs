using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Кромешная тьма для машины Водящего: луна, окна, ambient, отражения и
    /// туман гаснут, светит один фонарь. Бегущие на своих машинах видят зал
    /// при луне — решение геймдизайнера: ловцу страшно и тесно, бегущим видно,
    /// куда бежать.
    ///
    /// Это не пост-обработка: экспозиция глушила бы и луч. Гасятся сами
    /// источники, и луч остаётся единственным. Живёт на корне галереи, список
    /// источников заполняет билдер арта. На машине Водящего других игровых
    /// камер нет, поэтому глобальные RenderSettings трогать безопасно;
    /// при выключении или выгрузке сцены значения возвращаются.
    /// </summary>
    public sealed class KeeperDarkness : MonoBehaviour
    {
        [Tooltip("Источники лунного света зала: гаснут у Водящего")]
        [SerializeField] private Light[] moonLights;
        [Tooltip("Лунный визуал без света: лучи из окон, туман по полу — прячутся у Водящего")]
        [SerializeField] private Renderer[] moonVisuals;

        private bool pitchBlack;
        private Color sky, equator, ground, fog;
        private float reflection;

        /// <summary>Включить тьму у локального Водящего или вернуть лунный зал.</summary>
        public void SetPitchBlack(bool enabled)
        {
            if (enabled == pitchBlack)
            {
                return;
            }

            if (enabled)
            {
                sky = RenderSettings.ambientSkyColor;
                equator = RenderSettings.ambientEquatorColor;
                ground = RenderSettings.ambientGroundColor;
                fog = RenderSettings.fogColor;
                reflection = RenderSettings.reflectionIntensity;
                RenderSettings.ambientSkyColor = Color.black;
                RenderSettings.ambientEquatorColor = Color.black;
                RenderSettings.ambientGroundColor = Color.black;
                RenderSettings.fogColor = Color.black;
                RenderSettings.reflectionIntensity = 0f;
            }
            else
            {
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.fogColor = fog;
                RenderSettings.reflectionIntensity = reflection;
            }

            pitchBlack = enabled;
            Toggle(!enabled);
        }

        private void Toggle(bool moonlit)
        {
            if (moonLights != null)
            {
                foreach (Light light in moonLights)
                {
                    if (light != null)
                    {
                        light.enabled = moonlit;
                    }
                }
            }

            if (moonVisuals != null)
            {
                foreach (Renderer renderer in moonVisuals)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = moonlit;
                    }
                }
            }
        }

        private void OnDisable()
        {
            SetPitchBlack(false);
        }
    }
}
