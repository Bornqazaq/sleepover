using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Igruha.Minigames.Circus
{
    /// <summary>Local, render-only doubles. No player scripts, physics, events or network objects are copied.</summary>
    public sealed class CircusDuelPortraits : MonoBehaviour
    {
        private const int PortraitLayer = 2, TextureWidth = 1280, TextureHeight = 720;
        private const float SlowMotionSpeed = .035f, StageDistance = 10000f, StageSpacing = 30f;
        private const float MinimumHeight = 1.2f, MaximumHeight = 3f, PortraitFov = 30f;
        private const float NearPlane = .1f, FarPlane = 12f, LightRange = 9f;
        private const float PushSeconds = .19f, StartingDistance = 2.5f, HoldDistance = 1.65f;
        private const float AimHeightFraction = .64f, LensHeightFraction = .40f;
        private const float OrbitOffset = .27f, OrbitSpeed = .012f;
        private static readonly Vector3 KeyOffset = new Vector3(-2, 3, 3), RimOffset = new Vector3(2, 2.4f, -1);
        private static readonly Color KeyColor = new Color(1, .94f, .85f), WarmRim = new Color(1, .25f, .035f), CoolRim = new Color(.08f, .55f, 1);
        private const float KeyIntensity = 22f, RimIntensity = 34f;

        private sealed class Portrait
        {
            public GameObject Root;
            public Camera Camera;
            public RenderTexture Texture;
            public Vector3 Origin;
            public float Height, Bottom;
        }

        private readonly Portrait[] portraits = new Portrait[2];
        public bool IsActive => portraits[0] != null || portraits[1] != null;
        public Texture First => portraits[0]?.Texture;
        public Texture Second => portraits[1]?.Texture;

        public void Begin(CageStation first, CageStation second)
        {
            if (IsActive || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            portraits[0] = Create(first, 0);
            portraits[1] = Create(second, 1);
        }

        private Portrait Create(CageStation cage, int index)
        {
            if (cage == null || cage.Occupant == null) return null;
            var source = cage.Occupant.transform;
            var sourceAnimator = source.GetComponentInChildren<Animator>();
            var p = new Portrait { Root = new GameObject("DuelPortrait_" + index), Origin = new Vector3(StageDistance + index * StageSpacing, StageDistance, 0) };
            p.Root.transform.SetParent(transform, false);
            p.Root.transform.position = p.Origin;
            var map = new Dictionary<Transform, Transform>();
            Transform visual = CopyHierarchy(source, p.Root.transform, map);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = source.lossyScale;
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || skin.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var copy = map[skin.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh;
                copy.sharedMaterials = skin.sharedMaterials;
                copy.localBounds = skin.localBounds;
                var bones = skin.bones;
                for (int b = 0; b < bones.Length; b++) bones[b] = bones[b] != null && map.TryGetValue(bones[b], out var bone) ? bone : null;
                copy.bones = bones;
                if (skin.rootBone != null && map.TryGetValue(skin.rootBone, out var rootBone)) copy.rootBone = rootBone;
                copy.updateWhenOffscreen = true;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                for (int b = 0; skin.sharedMesh != null && b < skin.sharedMesh.blendShapeCount; b++) copy.SetBlendShapeWeight(b, skin.GetBlendShapeWeight(b));
            }
            foreach (var mesh in source.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mesh.enabled || mesh.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var filter = mesh.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                map[mesh.transform].gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = map[mesh.transform].gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = mesh.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (sourceAnimator != null && sourceAnimator.runtimeAnimatorController != null)
            {
                var animator = map[sourceAnimator.transform].gameObject.AddComponent<Animator>();
                animator.avatar = sourceAnimator.avatar;
                animator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var parameter in sourceAnimator.parameters)
                {
                    if (parameter.type == AnimatorControllerParameterType.Float) animator.SetFloat(parameter.nameHash, sourceAnimator.GetFloat(parameter.nameHash));
                    else if (parameter.type == AnimatorControllerParameterType.Int) animator.SetInteger(parameter.nameHash, sourceAnimator.GetInteger(parameter.nameHash));
                    else if (parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(parameter.nameHash, sourceAnimator.GetBool(parameter.nameHash));
                }
                for (int layer = 0; layer < sourceAnimator.layerCount; layer++)
                {
                    var state = sourceAnimator.GetCurrentAnimatorStateInfo(layer);
                    animator.SetLayerWeight(layer, sourceAnimator.GetLayerWeight(layer));
                    if (state.fullPathHash != 0) animator.Play(state.fullPathHash, layer, state.normalizedTime);
                }
                animator.Update(0);
                animator.speed = SlowMotionSpeed;
            }
            var renderers = visual.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(p.Origin + Vector3.up, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
                if (i == 0) bounds = renderers[i].bounds; else bounds.Encapsulate(renderers[i].bounds);
            p.Height = Mathf.Clamp(bounds.size.y, MinimumHeight, MaximumHeight);
            p.Bottom = bounds.min.y - p.Origin.y;
            var cameraObject = new GameObject("PortraitCamera"); cameraObject.transform.SetParent(p.Root.transform, false);
            p.Camera = cameraObject.AddComponent<Camera>();
            p.Camera.clearFlags = CameraClearFlags.SolidColor; p.Camera.backgroundColor = Color.clear;
            p.Camera.nearClipPlane = NearPlane; p.Camera.farClipPlane = FarPlane; p.Camera.fieldOfView = PortraitFov;
            p.Camera.cullingMask = 1 << PortraitLayer; p.Camera.allowHDR = false; p.Camera.allowMSAA = true;
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false; cameraData.renderShadows = false;
            p.Texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32) { name = "DuelPortraitRT_" + index, antiAliasing = 2 };
            p.Texture.Create(); p.Camera.targetTexture = p.Texture;
            Light(p, "Key", KeyOffset, KeyColor, KeyIntensity);
            Light(p, "Rim", RimOffset, index == 0 ? WarmRim : CoolRim, RimIntensity);
            return p;
        }

        private static Transform CopyHierarchy(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var copy = new GameObject(source.name).transform;
            copy.gameObject.layer = PortraitLayer;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            copy.gameObject.SetActive(source.gameObject.activeSelf);
            map.Add(source, copy);
            for (int i = 0; i < source.childCount; i++) CopyHierarchy(source.GetChild(i), copy, map);
            return copy;
        }

        private static void Light(Portrait p, string name, Vector3 offset, Color color, float intensity)
        {
            var go = new GameObject(name); go.transform.SetParent(p.Root.transform, false); go.transform.localPosition = offset;
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = LightRange;
            light.color = color; light.intensity = intensity; light.cullingMask = 1 << PortraitLayer; light.shadows = LightShadows.None;
        }

        public void Render(int cue, float age)
        {
            for (int i = 0; i < 2; i++)
            {
                var p = portraits[i]; if (p == null) continue;
                p.Camera.enabled = cue == 3 || cue == i + 1;
                float side = i == 0 ? -1 : 1;
                float push = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / PushSeconds), 4);
                float distance = p.Height * Mathf.Lerp(StartingDistance, HoldDistance, push);
                Vector3 aim = p.Origin + Vector3.up * (p.Bottom + p.Height * AimHeightFraction);
                Vector3 position = p.Origin + new Vector3(side * (OrbitOffset + age * OrbitSpeed), p.Bottom + p.Height * LensHeightFraction, distance);
                p.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(aim - position));
            }
        }

        public void Release()
        {
            for (int i = 0; i < portraits.Length; i++)
            {
                var p = portraits[i]; if (p == null) continue;
                p.Camera.enabled = false; p.Camera.targetTexture = null;
                p.Texture.Release(); Destroy(p.Texture); Destroy(p.Root); portraits[i] = null;
            }
        }
        private void OnDisable() => Release();
    }
}
