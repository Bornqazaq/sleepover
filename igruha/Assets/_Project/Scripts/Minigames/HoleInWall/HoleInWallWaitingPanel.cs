using UnityEngine;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>A decorative shutter at the launch gate, driven by the existing wall state.</summary>
    public sealed class HoleInWallWaitingPanel : MonoBehaviour
    {
        [SerializeField] private SweepingWall wall;
        [SerializeField] private GameObject artwork;

        private void LateUpdate()
        {
            bool visible = wall != null && !wall.Running;
            if (artwork != null && artwork.activeSelf != visible)
                artwork.SetActive(visible);
        }
    }
}
