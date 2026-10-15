using System;
using Igruha.Core.Audio;
using Igruha.Core.CameraSystems;
using Igruha.Core.Hub.Activities;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Igruha.EditorTools.HubArkanoidBuilder;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Adds the adjacent cabinet once; existing scene network identities remain intact.</summary>
    public static class HubStarshipBuilder
    {
        private const string RootName = "_HubStarships";
        private static readonly Color Cyan = new Color(.3f, .92f, 1f), Gold = new Color(1f, .76f, .3f);
        private static readonly Color Ink = new Color(.018f, .025f, .075f);
        [MenuItem("Igruha/Хаб/Собрать кораблики")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            if (GameObject.Find(RootName) != null) throw new InvalidOperationException("Cabinet already exists. Preserve its NetworkObject identity.");
            Transform corner = GameObject.Find("_HubOriginal/ArcadeCorner")?.transform;
            if (corner == null || corner.childCount != 3) throw new InvalidOperationException("Expected three cabinets.");
            Transform cabinet = corner.GetChild(2), root = new GameObject(RootName).transform;
            root.SetPositionAndRotation(cabinet.position, cabinet.rotation);
            foreach (Renderer r in cabinet.GetComponentsInChildren<Renderer>()) if (r.name == "HO_Glow") r.enabled = false;
            PrepareFont();
            Transform stand = Child(root, "Stand"); stand.localPosition = new Vector3(0, .025f, 1);
            stand.localRotation = Quaternion.Euler(0, 180, 0);
            Transform seat = Child(root, "StarshipStation"); seat.SetPositionAndRotation(stand.position, stand.rotation);
            var zone = seat.gameObject.AddComponent<BoxCollider>(); zone.isTrigger = true;
            zone.center = Vector3.up * .9f; zone.size = new Vector3(.9f, 1.8f, .9f);
            seat.gameObject.AddComponent<NetworkObject>(); var station = seat.gameObject.AddComponent<StarshipStation>();
            Set(station, "standPoint", stand); Set(station, "activityName", "кораблики");
            Set(station, "takeRadius", 2f); Set(station, "leaveRadius", 2.2f);
            var view = root.gameObject.AddComponent<ArcadeScreenCamera>(); Set(view, "station", station); Set(station, "localCamera", view);
            var screen = new GameObject("StarshipScreen", typeof(RectTransform), typeof(Canvas));
            screen.transform.SetParent(root, false); screen.transform.localPosition = new Vector3(0, 1.5f, .164f);
            screen.transform.localRotation = Quaternion.Euler(9, 180, 0); screen.transform.localScale = Vector3.one * .001f;
            var rect = (RectTransform)screen.transform; rect.sizeDelta = new Vector2(620, 450);
            screen.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Rect(rect, "Glass", Vector2.zero, rect.sizeDelta, Ink);
            Label(rect, "Score", new Vector2(-190, 195), new Vector2(230, 30), 20, "СЧЁТ 00000", Cyan, out var score);
            Label(rect, "Wave", new Vector2(20, 195), new Vector2(180, 30), 19, "ВОЛНА 1", Color.white, out var wave);
            Label(rect, "Lives", new Vector2(215, 195), new Vector2(170, 30), 20, "ЖИЗНИ 3", Gold, out var lives);
            Rect(rect, "HeaderRule", new Vector2(0, 173), new Vector2(570, 2), Cyan);
            var field = ChildRect(rect, "Field"); field.sizeDelta = new Vector2(540, 336);
            var stars = new RectTransform[32];
            for (int i = 0; i < stars.Length; i++) stars[i] = Rect(field, "Star" + i, Vector2.zero,
                Vector2.one * (i % 3 == 0 ? 2 : 1), new Color(.3f, .55f, .8f, .45f));
            var enemies = new RectTransform[StarshipRules.EnemyCount];
            var patterns = new[] {new[]{"0010100","0111110","1101011","1111111","0100010"},
                new[]{"0011100","0111110","1101011","0111110","1010101"},
                new[]{"1000001","0100010","0111110","1101011","1111111"}};
            var colors = new[] {new Color(1,.45f,.65f), Gold, new Color(.5f,1,.65f)};
            for (int i = 0; i < enemies.Length; i++) enemies[i] = PixelShape(field, "Enemy" + i, patterns[i / StarshipRules.Columns], 4.3f, colors[i / StarshipRules.Columns]);
            var ship = PixelShape(field, "Ship", new[]{"0001000","0001000","0011100","1011101","1111111","1101011"}, 4.3f, Cyan);
            var shots = new RectTransform[StarshipRules.ProjectileCount]; var bombs = new RectTransform[StarshipRules.ProjectileCount];
            for (int i = 0; i < shots.Length; i++)
            {
                shots[i] = Rect(field, "Laser" + i, Vector2.zero, new Vector2(3, 12), Cyan);
                bombs[i] = Rect(field, "EnemyLaser" + i, Vector2.zero, new Vector2(5, 10), Gold);
            }
            var panel = Rect(rect, "StartPanel", new Vector2(0, -20), new Vector2(450, 174), new Color(.018f,.025f,.07f,.97f));
            Rect(panel, "Accent", new Vector2(0, 85), new Vector2(450, 3), Cyan);
            Label(panel, "Title", new Vector2(0,45), new Vector2(440,45), 30, "КОРАБЛИКИ", Color.white, out var title);
            var play = Rect(panel, "PlayButton", new Vector2(0,-5), new Vector2(275,45), Cyan);
            Label(play, "ButtonLabel", Vector2.zero, new Vector2(270,40), 23, "ЗАЖМИ E", Ink, out var button);
            Label(panel, "Hint", new Vector2(0,-55), new Vector2(445,32), 17, "ЗАЩИТИ СВОЮ ГАЛАКТИКУ", Gold, out var hint);
            Label(rect, "Controls", new Vector2(0,-196), new Vector2(600,35), 16,
                "МЫШЬ — КОРАБЛЬ  ·  ДЕРЖИ ЛКМ — ОГОНЬ  ·  E — ВЫХОД", new Color(.65f,.8f,.9f), out _);
            var presentation = screen.AddComponent<StarshipPresentation>();
            Set(presentation, "station", station); Set(presentation, "ship", ship); Set(presentation, "score", score);
            Set(presentation, "wave", wave); Set(presentation, "lives", lives); Set(presentation, "panel", panel.gameObject);
            Set(presentation, "title", title); Set(presentation, "button", button); Set(presentation, "hint", hint);
            SetArray(presentation, "enemies", enemies); SetArray(presentation, "shots", shots);
            SetArray(presentation, "bombs", bombs); SetArray(presentation, "stars", stars);
            var rig = Child(root, "StarshipView").gameObject.AddComponent<CinemachineCamera>();
            Vector3 eye = screen.transform.position - screen.transform.forward * .78f;
            rig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(screen.transform.position - eye, screen.transform.up));
            rig.Priority.Value = -100; rig.Priority.Enabled = true;
            var lens = rig.Lens; lens.FieldOfView = 38; lens.NearClipPlane = .03f; lens.FarClipPlane = 100; rig.Lens = lens; rig.enabled = false;
            Set(view, "screenRig", rig); Set(view, "brain", Object.FindFirstObjectByType<CinemachineBrain>());
            Set(view, "cameraController", Object.FindFirstObjectByType<MinigameCameraController>());
            var sound = screen.AddComponent<MinigameAudioPlayer>();
            Set(sound, "library", AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/Hub/Arkanoid/SfxLibrary.asset"));
            Set(sound, "maxDistance", 10f); Set(presentation, "audioPlayer", sound);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Starships: adjacent cabinet ready, 18 invaders, 3 lives, shared observer screen.");
        }
        private static RectTransform PixelShape(Transform parent, string name, string[] rows, float pixel, Color color)
        {
            var root = ChildRect(parent, name); root.sizeDelta = new Vector2(rows[0].Length, rows.Length) * pixel;
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (rows[y][x] != '1') continue;
                    int start = x; while (x + 1 < rows[y].Length && rows[y][x + 1] == '1') x++;
                    Rect(root, "Pixels", new Vector2((start + x) * .5f - (rows[y].Length - 1) * .5f, (rows.Length - 1) * .5f - y) * pixel,
                        new Vector2(x - start + 1, 1) * pixel, color);
                }
            return root;
        }
        private static void SetArray(Object target, string name, RectTransform[] values)
        {
            var so = new SerializedObject(target); var array = so.FindProperty(name); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
