using UnityEngine;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Shows the suspension and counterweight using the existing platform position.</summary>
    public sealed class DuckHuntLiftRig : MonoBehaviour
    {
        [SerializeField] private Transform platform;
        [SerializeField] private Transform counterweight;
        [SerializeField] private Transform[] suspensionCables;
        [SerializeField] private Transform[] returnCables;
        [SerializeField] private Transform[] pulleys;
        [SerializeField] private float minHeight;
        [SerializeField] private float maxHeight;
        private const float HeadHeight = 38f;
        private const float PulleyRadius = .52f;

        public void Configure(Transform deck, Transform weight, Transform[] suspension, Transform[] returns,
            Transform[] wheels, float min, float max)
        {
            platform = deck;
            counterweight = weight;
            suspensionCables = suspension;
            returnCables = returns;
            pulleys = wheels;
            minHeight = min;
            maxHeight = max;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (platform == null || counterweight == null) return;
            float travel = Mathf.Clamp(platform.position.y, minHeight, maxHeight) - minHeight;
            counterweight.position = new Vector3(platform.position.x, maxHeight + 1f - travel, -18.12f);
            for (int i = 0; i < 2; i++)
            {
                float x = platform.position.x + (i == 0 ? -1.1f : 1.1f);
                Cable(suspensionCables[i], new Vector3(x, platform.position.y + 2.6f, -14.08f),
                    new Vector3(x, HeadHeight, -14.08f));
                Cable(returnCables[i], new Vector3(x, counterweight.position.y + .9f, -18.12f),
                    new Vector3(x, HeadHeight, -18.12f));
            }
            float angle = travel / PulleyRadius * Mathf.Rad2Deg;
            for (int i = 0; i < pulleys.Length; i++)
                pulleys[i].localRotation = Quaternion.Euler(angle, 0, 0);
        }

        private static void Cable(Transform cable, Vector3 from, Vector3 to)
        {
            Vector3 span = to - from;
            cable.position = (from + to) * .5f;
            cable.rotation = Quaternion.LookRotation(span.normalized);
            cable.localScale = new Vector3(.035f, .035f, span.magnitude);
        }
    }
}
