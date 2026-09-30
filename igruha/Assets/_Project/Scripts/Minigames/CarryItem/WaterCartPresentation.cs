using UnityEngine;

namespace Igruha.Minigames.CarryItem
{
    /// <summary>Local visuals follow replicated position and load; no gameplay authority.</summary>
    [RequireComponent(typeof(WaterCart))]
    public sealed class WaterCartPresentation : MonoBehaviour
    {
        [SerializeField] private Transform tub;
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float wheelRadius = 0.22f;
        [SerializeField] private float loadedSag = 0.025f;
        [SerializeField] private LineRenderer pourStream;
        private const int PourSegments = 16;
        private const float PourArcHeight = 0.25f;
        private static readonly Vector3 PourLip = new Vector3(0f, 0.94f, -0.64f);
        private WaterTank receiver;
        private WaterCart cart;
        private Vector3 previousPosition;
        private Vector3 tubRest;
        private float angle;

        private void Awake()
        {
            cart = GetComponent<WaterCart>();
            previousPosition = transform.position;
            if (tub != null) tubRest = tub.localPosition;
        }

        private void LateUpdate()
        {
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            // Respawn teleports must not spin the wheels at impossible speeds.
            if (delta.sqrMagnitude < 1f)
                angle = Mathf.Repeat(angle + Vector3.Dot(delta, transform.forward) / wheelRadius * Mathf.Rad2Deg, 360f);
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (tub != null) tub.localPosition = tubRest + Vector3.down * (loadedSag * cart.Load);
            if (pourStream == null) return;
            if (receiver == null)
            {
                var game = Igruha.Core.Minigame.MinigameControllerBase.Current as CarryItemMinigame;
                if (game != null) receiver = game.TankOf(cart.Team);
            }
            pourStream.enabled = cart.IsPouring && receiver != null;
            if (!pourStream.enabled) return;
            Vector3 source = tub.TransformPoint(PourLip);
            Vector3 target = receiver.PourPoint;
            for (int i = 0; i < PourSegments; i++)
            {
                float t = i / (float)(PourSegments - 1);
                pourStream.SetPosition(i, Vector3.Lerp(source, target, t) + Vector3.up * (4f * t * (1f - t) * PourArcHeight));
            }
        }
    }
}
