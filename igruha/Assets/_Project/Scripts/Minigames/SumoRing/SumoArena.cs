using System;
using UnityEngine;

namespace Igruha.Minigames.SumoRing
{
    public sealed class SumoArena : MonoBehaviour
    {
        [SerializeField] private SumoMinigame game;
        [SerializeField] private SumoRingSegment[] segments;
        [SerializeField] private GameObject[] boundaries;
        private int nextWarning, nextCollapse;
        public event Action<int> Warning;
        public event Action<int> Collapse;
        public void ResetArena()
        {
            nextWarning = nextCollapse = 0;
            foreach (var segment in segments) segment.ResetSegment();
            for (int i = 0; i < boundaries.Length; i++) boundaries[i].SetActive(i == 0);
        }
        private void FixedUpdate()
        {
            if (!game.Running) return;
            foreach (var segment in segments) segment.TickPhysics(game.Config, game.Elapsed);
        }
        private void Update()
        {
            if (!game.Running) return;
            var config = game.Config; double elapsed = game.Elapsed;
            foreach (var segment in segments) segment.TickVisual(config, elapsed);
            while (nextWarning < config.RingCount && elapsed >= config.CollapseAt(nextWarning) - config.Warning)
            {
                int ring = nextWarning++;
                Warning?.Invoke(ring);
            }
            while (nextCollapse < config.RingCount && elapsed >= config.CollapseAt(nextCollapse))
            {
                boundaries[nextCollapse].SetActive(false);
                boundaries[nextCollapse + 1].SetActive(true);
                int ring = nextCollapse++;
                Collapse?.Invoke(ring);
            }
        }
    }
}
