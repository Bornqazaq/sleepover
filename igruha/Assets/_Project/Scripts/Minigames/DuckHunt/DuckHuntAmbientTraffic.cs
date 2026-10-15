using UnityEngine;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.DuckHunt
{
    /// <summary>Decorative traffic outside the arena. No physics or game state.</summary>
    public sealed class DuckHuntAmbientTraffic : MonoBehaviour
    {
        [SerializeField] private Vector3 start;
        [SerializeField] private Vector3 end;
        [SerializeField] private float metresPerSecond=8;
        [SerializeField] private float phase;

        public void Configure(Vector3 from,Vector3 to,float speed,float offset)
        {
            start=from;end=to;metresPerSecond=speed;phase=offset;
        }

        private void Update()
        {
            Vector3 direction=end-start;
            float distance=direction.magnitude;
            if(distance<.01f)return;
            double cycles=NetworkClock.Now*metresPerSecond/distance+phase;
            float progress=(float)(cycles-System.Math.Floor(cycles));
            transform.localPosition=Vector3.Lerp(start,end,progress);
            transform.localRotation=Quaternion.LookRotation(direction,Vector3.up);
        }
    }
}
