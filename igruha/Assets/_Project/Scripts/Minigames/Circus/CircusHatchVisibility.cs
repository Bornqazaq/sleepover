using System;
using System.Collections.Generic;
using Igruha.Core.Arena;
using Igruha.Core.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.Circus
{
    /// <summary>The local pit view cuts away open leaves near its lens or
    /// between the lens and runner. Physics and other cameras stay intact.</summary>
    public sealed class CircusHatchVisibility : IDisposable
    {
        private sealed class Leaf
        {
            internal HingedFloorHatch Hatch;
            internal Renderer[] Renderers;
            internal bool[] WasHidden;
            internal bool Hidden;
        }
        private readonly List<Leaf> leaves = new List<Leaf>(16);
        private readonly List<Camera> stack = new List<Camera>(4);
        private readonly PlayerController player;
        private readonly Camera output;
        private bool active;
        public int HiddenLeafCount { get; private set; }

        public CircusHatchVisibility(PlayerController player, Camera output, Transform arena)
        {
            this.player = player; this.output = output;
            if (player == null || arena == null) return;
            foreach (var hatch in arena.GetComponentsInChildren<HingedFloorHatch>(true))
                foreach (string side in new[] { "Floor/DoorLeft", "Floor/DoorRight" })
                {
                    Transform door = hatch.transform.Find(side);
                    if (door == null) continue;
                    var renderers = door.GetComponentsInChildren<Renderer>(true);
                    leaves.Add(new Leaf { Hatch = hatch, Renderers = renderers, WasHidden = new bool[renderers.Length] });
                }
        }

        public void SetActive(bool value)
        {
            if (active == value) return;
            active = value;
            if (value)
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
                RenderPipelineManager.endContextRendering += EndContext;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                RenderPipelineManager.endContextRendering -= EndContext;
                stack.Clear(); Restore();
            }
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        { stack.Add(camera); Apply(); }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            int last = stack.Count - 1;
            if (last >= 0 && stack[last] == camera) stack.RemoveAt(last); else stack.Clear();
            Apply();
        }
        private void EndContext(ScriptableRenderContext context, List<Camera> cameras)
        { stack.Clear(); Restore(); }

        private void Apply()
        {
            Restore();
            if (!active || player == null || output == null || stack.Count == 0 || stack[stack.Count - 1] != output) return;
            Vector3 from = output.transform.position, to = player.transform.position + Vector3.up;
            Vector3 segment = to - from;
            var ray = new Ray(from, segment.normalized);
            foreach (var leaf in leaves)
            {
                if (leaf.Hatch == null || !leaf.Hatch.DoorsOpen || player.transform.position.y >= leaf.Hatch.transform.position.y - .35f) continue;
                bool obstructing = false;
                foreach (var renderer in leaf.Renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds bounds = renderer.bounds;
                    bounds.Expand(.8f); // Preserve a readable silhouette around the runner.
                    // A foreground leaf can cover an approaching bear above the
                    // runner without crossing the ray to the runner's torso.
                    if (bounds.SqrDistance(from) < 9f || (bounds.IntersectRay(ray, out float distance) && distance < segment.magnitude))
                    { obstructing = true; break; }
                }
                if (!obstructing) continue;
                for (int i = 0; i < leaf.Renderers.Length; i++)
                {
                    var renderer = leaf.Renderers[i];
                    if (renderer == null) continue;
                    leaf.WasHidden[i] = renderer.forceRenderingOff;
                    renderer.forceRenderingOff = true;
                }
                leaf.Hidden = true; HiddenLeafCount++;
            }
        }

        private void Restore()
        {
            foreach (var leaf in leaves)
            {
                if (!leaf.Hidden) continue;
                for (int i = 0; i < leaf.Renderers.Length; i++)
                    if (leaf.Renderers[i] != null) leaf.Renderers[i].forceRenderingOff = leaf.WasHidden[i];
                leaf.Hidden = false;
            }
            HiddenLeafCount = 0;
        }

        public void Dispose() => SetActive(false);
    }
}
