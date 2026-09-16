using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.Circus;
using Igruha.Core.CameraSystems;
using Igruha.Core.Minigame;
using Unity.Cinemachine;
using TMPro;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Updates only the bear and pit lights; preserves the dressed arenas.</summary>
    internal static class CircusBearPolishBuilder
    {
        [MenuItem("Igruha/Цирк/Гибкость и попадание Бруно — обе сцены")]
        internal static void ApplyBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and leave Play Mode first.");
            string opened = EditorSceneManager.GetActiveScene().path;
            CircusNightAssets.ImportBear();
            try
            {
                foreach (string name in new[] { "Stopwatch", "CansOrder" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Minigames/" + name + ".unity");
                    CircusBeast.RedressInScene();
                    foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    {
                        if (light.name == "Bruno_WarmKey" || light.name == "Bruno_Rim")
                        {
                            light.shadowBias = .015f;
                            light.shadowNormalBias = .07f;
                            light.shadowStrength = .9f;
                            light.shadowResolution = LightShadowResolution.High;
                        }
                        if (light.name == "PitReadability") light.intensity = 48f;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if (!string.IsNullOrEmpty(opened)) EditorSceneManager.OpenScene(opened); }
            AssetDatabase.SaveAssets();
        }

        internal static void Dress(PitBear bear, Animator animator)
        {
            Transform[] bones = animator.GetComponentsInChildren<Transform>();
            Func<string, Transform> bone = name => bones.First(t => t.name == name);
            var motion = animator.GetComponent<CircusBearMotion>();
            if (motion == null) motion = animator.gameObject.AddComponent<CircusBearMotion>();
            var motionData = new SerializedObject(motion);
            motionData.FindProperty("bear").objectReferenceValue = bear;
            foreach (string name in new[] { "Neck", "Head", "Chest", "Lumbar" })
                motionData.FindProperty(name.ToLowerInvariant()).objectReferenceValue = bone(name);
            motionData.ApplyModifiedPropertiesWithoutUndo();

            var old = bear.transform.Find("BearFeedback");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("BearFeedback").transform;
            root.SetParent(bear.transform, false);
            var voice = root.gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.spatialBlend = 1; voice.minDistance = 4; voice.maxDistance = 27;
            voice.rolloffMode = AudioRolloffMode.Linear;
            var impact = Dust(root, "ClawContact", .55f, .16f, 2.4f);
            var foot = Dust(root, "PawDust", .55f, .15f, .55f);
            var trailObject = new GameObject("ClawSwipe");
            trailObject.transform.SetParent(bone("ForeToes.R"), false);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = .12f; trail.minVertexDistance = .05f; trail.widthMultiplier = .13f;
            trail.widthCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
            trail.startColor = new Color(1, .88f, .59f, .36f); trail.endColor = new Color(1, .85f, .6f, 0);
            trail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CircusNightAssets.Materials + "/CN_EventDust.mat");
            trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false; trail.emitting = false;

            var feedback = bear.GetComponent<CircusBearFeedback>();
            if (feedback == null) feedback = bear.gameObject.AddComponent<CircusBearFeedback>();
            var data = new SerializedObject(feedback);
            data.FindProperty("voice").objectReferenceValue = voice;
            data.FindProperty("impactDust").objectReferenceValue = impact;
            data.FindProperty("footDust").objectReferenceValue = foot;
            data.FindProperty("clawTrail").objectReferenceValue = trail;
            foreach (string name in new[] { "growl", "swipe", "impact", "step" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Minigames/Circus/Bruno_" + name + ".wav");
                if (clip == null) throw new InvalidOperationException("Missing authored bear cue: " + name);
                data.FindProperty(name).objectReferenceValue = clip;
            }
            var paws = data.FindProperty("paws"); paws.arraySize = 4;
            string[] names = { "ForePaw.L", "ForePaw.R", "HindPaw.L", "HindPaw.R" };
            for (int i = 0; i < names.Length; i++) paws.GetArrayElementAtIndex(i).objectReferenceValue = bone(names[i]);
            data.ApplyModifiedPropertiesWithoutUndo();
            DressPresentation(bear);
        }

        private static void DressPresentation(PitBear bear)
        {
            var game = Object.FindFirstObjectByType<MinigameControllerBase>();
            var cameras = Object.FindFirstObjectByType<MinigameCameraController>();
            var spectator = Object.FindFirstObjectByType<SpectatorCamera>();
            var cameraData = new SerializedObject(cameras);
            var view = cameraData.FindProperty("topDownRig").objectReferenceValue as CinemachineCamera;
            if (view != null && view.name != "CircusAttackView")
                throw new InvalidOperationException("The optional minigame camera slot already has another owner.");
            if (view == null)
            {
                var go = new GameObject("CircusAttackView"); go.transform.SetParent(cameras.transform, false);
                view = go.AddComponent<CinemachineCamera>();
            }
            var lens = view.Lens; lens.FieldOfView = 55; lens.NearClipPlane = .1f; lens.FarClipPlane = 100; view.Lens = lens;
            cameraData.FindProperty("topDownRig").objectReferenceValue = view;
            cameraData.ApplyModifiedPropertiesWithoutUndo();
            view.gameObject.SetActive(false);

            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.renderMode == RenderMode.ScreenSpaceOverlay);
            var existing = canvas.transform.Find("CircusAttackCue");
            var cueObject = existing != null ? existing.gameObject : new GameObject("CircusAttackCue", typeof(RectTransform));
            cueObject.transform.SetParent(canvas.transform, false);
            var cue = cueObject.GetComponent<TextMeshProUGUI>();
            if (cue == null) cue = cueObject.AddComponent<TextMeshProUGUI>();
            cue.font = canvas.GetComponentsInChildren<TMP_Text>(true).First(t => t != cue && t.font != null).font;
            cue.fontSize = 28; cue.fontStyle = FontStyles.Bold; cue.alignment = TextAlignmentOptions.Center;
            cue.raycastTarget = false; cue.enabled = false;
            var rect = cue.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 100); rect.sizeDelta = new Vector2(760, 58);

            var presentation = game.GetComponent<CircusAttackPresentation>();
            if (presentation == null) presentation = game.gameObject.AddComponent<CircusAttackPresentation>();
            var data = new SerializedObject(presentation);
            data.FindProperty("cameras").objectReferenceValue = cameras;
            data.FindProperty("spectator").objectReferenceValue = spectator;
            data.FindProperty("attackView").objectReferenceValue = view.transform;
            data.FindProperty("bear").objectReferenceValue = bear;
            data.FindProperty("cue").objectReferenceValue = cue;
            var shelf = Object.FindFirstObjectByType<Igruha.Minigames.CansOrder.CansOrderLocalHud>();
            data.FindProperty("shelfHud").objectReferenceValue = shelf != null ? shelf.gameObject : null;
            data.ApplyModifiedPropertiesWithoutUndo();
            var rules = new SerializedObject(game);
            rules.FindProperty("attackPresentation").objectReferenceValue = presentation;
            rules.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem Dust(Transform parent, string name, float lifetime, float size, float speed)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .6f, lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size * 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .4f, speed);
            main.startColor = new Color(.85f, .66f, .38f, .65f);
            main.gravityModifier = .3f; main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.rateOverTime = 0;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .18f;
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.75f, .08f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CircusNightAssets.Materials + "/CN_EventDust.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return particles;
        }
    }
}
