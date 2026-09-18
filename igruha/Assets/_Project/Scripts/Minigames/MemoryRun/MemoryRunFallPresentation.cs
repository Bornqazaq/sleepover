using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;

namespace Igruha.Minigames.MemoryRun
{
    [DisallowMultipleComponent]
    public sealed class MemoryRunFallPresentation : MonoBehaviour
    {
        [SerializeField] private AnimationClip fallingClip;
        private readonly Dictionary<int, MemoryRunFallPose> poses = new Dictionary<int, MemoryRunFallPose>();
        public void Bind(int id, PlayerController player, float gateZ)
        {
            if (player == null || poses.ContainsKey(id)) return;
            var pose = player.gameObject.AddComponent<MemoryRunFallPose>();
            pose.Initialize(player, fallingClip, gateZ);
            poses.Add(id, pose);
        }
        public void Fail(int id, Vector3 impulse)
        {
            if (poses.TryGetValue(id, out var pose) && pose != null) pose.BeginFailure(impulse);
        }
        private void OnDestroy()
        {
            foreach (var pose in poses.Values) if (pose != null) Destroy(pose);
            poses.Clear();
        }
    }
}
