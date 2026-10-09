using System.IO;
using Igruha.Minigames.OneBullet;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    public static class OneBulletStormArtBuilder
    {
        private const string Art = "Assets/_Project/Art/Minigames/OneBullet";
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            EditorSceneManager.OpenScene(OneBulletArenaBuilder.ScenePath);
            var game = Object.FindFirstObjectByType<OneBulletMinigame>(); var storm = game.GetComponent<OneBulletStorm>();
            var so = new SerializedObject(storm); var layout = (OneBulletStormLayout)so.FindProperty("layout").objectReferenceValue;
            var old = GameObject.Find("_OneBulletStormFX"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("_OneBulletStormFX"); var view = root.AddComponent<OneBulletStormPresentation>();
            Set(view, "game", game); Set(view, "storm", storm);
            var dustMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/Dust.mat");
            var warning = Material("StormWarning", "Igruha/OneBullet/StormVeil", new Color(.83f, .65f, .39f, .32f));
            var danger = Material("StormDanger", "Igruha/OneBullet/StormVeil", new Color(.77f, .54f, .28f, .85f));
            var arrowMaterial = Material("StormArrow", "Universal Render Pipeline/Unlit", new Color(1f, .77f, .36f));
            Set(view, "warningVeil", warning); Set(view, "dangerVeil", danger);
            var dustTemplate = Dust(root.transform, Vector3.zero, dustMaterial, 0);
            var dustPrefab = PrefabUtility.SaveAsPrefabAsset(dustTemplate.gameObject, "Assets/_Project/Prefabs/Minigames/OneBullet/StormDust.prefab");
            Set(view, "dustPrefab", dustPrefab.GetComponent<ParticleSystem>()); Object.DestroyImmediate(dustTemplate.gameObject);
            var arrows = new Transform[layout.NodeCount]; var veils = new Renderer[layout.Edges.Count];
            var arrowMesh = ArrowMesh();
            for (int i = 0; i < layout.NodeCount; i++)
            {
                var arrow = new GameObject("ExitArrow_" + i); arrow.transform.SetParent(root.transform, false);
                arrow.transform.position = layout.StandingPoint(i) + Vector3.up * .03f;
                arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                var renderer = arrow.AddComponent<MeshRenderer>(); renderer.sharedMaterial = arrowMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
                arrows[i] = arrow.transform; arrow.SetActive(false);
            }
            for (int i = 0; i < layout.Edges.Count; i++)
            {
                var e = layout.Edges[i]; Vector3 a = layout.Position(e.A), b = layout.Position(e.B);
                Vector3 midpoint = (a + b) * .5f;
                var veil = GameObject.CreatePrimitive(PrimitiveType.Quad); veil.name = "StormBoundary_" + e.A + "_" + e.B;
                Object.DestroyImmediate(veil.GetComponent<Collider>()); veil.transform.SetParent(root.transform, false);
                veil.transform.position = midpoint + Vector3.up * 2f;
                Vector3 direction = b - a; direction.y = 0;
                veil.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                veil.transform.localScale = new Vector3(2.16f, 4.2f, 1);
                veils[i] = veil.GetComponent<Renderer>(); veils[i].sharedMaterial = danger;
                veils[i].shadowCastingMode = ShadowCastingMode.Off; veils[i].enabled = false;
            }
            Refs(view, "arrows", arrows); Refs(view, "veils", veils);
            BuildHud(view);
            OneBulletSfx.Build();
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene); EditorSceneManager.SaveScene(game.gameObject.scene); AssetDatabase.SaveAssets();
            Debug.Log("OneBullet storm presentation and seven sound slots built.");
        }
        private static ParticleSystem Dust(Transform parent, Vector3 point, Material material, int index)
        {
            var ps = new GameObject("SandDrift_" + index).AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false); ps.transform.position = point + Vector3.up * 1.4f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.duration = 5; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.3f); main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            main.startColor = new Color(.77f, .58f, .33f, .3f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 18;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.8f, 2.3f, 1.8f);
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.2f, .65f); velocity.y = new ParticleSystem.MinMaxCurve(.05f, .15f); velocity.z = new ParticleSystem.MinMaxCurve(.1f, .3f);
            var emission = ps.emission; emission.rateOverTime = 3;
            var color = ps.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(.7f, .7f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }
        private static Mesh ArrowMesh()
        {
            Directory.CreateDirectory(Art + "/Meshes"); string path = Art + "/Meshes/StormExitArrow.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "Storm exit arrow" }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.vertices = new[] { new Vector3(-.12f,0,-.4f), new Vector3(.12f,0,-.4f), new Vector3(.12f,0,.04f),
                new Vector3(.35f,0,.04f),new Vector3(0,0,.45f),new Vector3(-.35f,0,.04f),new Vector3(-.12f,0,.04f) };
            mesh.triangles = new[] { 0,2,1,0,6,2,5,4,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
        private static Material Material(string name, string shader, Color color)
        {
            string path = Art + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color); EditorUtility.SetDirty(mat); return mat;
        }
        public static void BuildHud(OneBulletStormPresentation view)
        {
            var canvas = GameObject.Find("_UI").GetComponentInChildren<Canvas>().transform;
            var old = canvas.Find("StormHUD"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("StormHUD", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(canvas, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            var tint = Panel(root, "SandTint", Vector2.zero, Vector2.zero, Color.clear);
            tint.rectTransform.anchorMin = Vector2.zero; tint.rectTransform.anchorMax = Vector2.one; tint.rectTransform.offsetMin = tint.rectTransform.offsetMax = Vector2.zero;
            Set(view, "tint", tint);
            var can = Card(root, "Decoys", new Vector2(0, 0), new Vector2(28, 28), new Vector2(328, 76));
            Set(view, "canPanel", can); Set(view, "canCount", Label(can.transform, "CanCount", new Vector2(0, 12), new Vector2(306, 30), 22, "Q  ·  БРОСИТЬ БАНКУ   2"));
            Set(view, "canHint", Label(can.transform, "CanHint", new Vector2(0, -15), new Vector2(306, 22), 14, "Уже с собой · подбирать не нужно"));
            var warn = Card(root, "StormWarning", new Vector2(.5f, 1), new Vector2(0, -246), new Vector2(558, 84));
            Set(view, "warningPanel", warn);
            Set(view, "title", Label(warn.transform, "Title", new Vector2(-37, 16), new Vector2(442, 30), 23, "БУРЯ ПРИБЛИЖАЕТСЯ"));
            Set(view, "detail", Label(warn.transform, "Detail", new Vector2(-37, -12), new Vector2(442, 22), 15, "Уходи в чистый двор по стрелкам"));
            Set(view, "seconds", Label(warn.transform, "Seconds", new Vector2(234, 3), new Vector2(64, 46), 38, "20"));
            var fill = Panel(warn.transform, "Progress", new Vector2(0, -37), new Vector2(530, 3), new Color(1, .76f, .36f));
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            Set(view, "progress", fill);
            var route = Card(root, "EscapeRoute", new Vector2(.5f, 0), new Vector2(0, 153), new Vector2(460, 40));
            Set(view, "routePanel", route); Set(view, "route", Label(route.transform, "Direction", Vector2.zero, new Vector2(444, 36), 19, "К ЧИСТОМУ ДВОРУ"));
        }
        private static CanvasGroup Card(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var panel = Panel(parent, name, position, size, new Color(.055f, .069f, .080f, .94f));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = panel.rectTransform.pivot = anchor;
            var group = panel.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false; return group;
        }
        private static Image Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var p = new GameObject(name, typeof(RectTransform)).AddComponent<Image>(); p.transform.SetParent(parent, false);
            p.rectTransform.anchoredPosition = position; p.rectTransform.sizeDelta = size; p.color = color; p.raycastTarget = false; return p;
        }
        private static TMP_Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, string text)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); t.transform.SetParent(parent, false);
            t.rectTransform.anchoredPosition = position; t.rectTransform.sizeDelta = size; t.font = UiFonts.SansMedium;
            t.fontSize = fontSize; t.color = new Color(1, .86f, .61f); t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.text = text; return t;
        }
        private static void Set(Object target, string field, Object value) => OneBulletArenaBuilder.Set(target, field, value);
        private static void Refs(Object target, string field, Object[] objects)
        {
            var s = new SerializedObject(target); var p = s.FindProperty(field); p.arraySize = objects.Length;
            for (int i = 0; i < objects.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = objects[i]; s.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
