using System;
using System.Linq;
using Igruha.Minigames.BelieveOrNot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Original side-wall furniture, independent of the accepted shell and hero props.</summary>
    public static class BelieveClubFurnitureBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/BelieveOrNot.unity";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/BelieveOrNotConfig.asset";
        private const string FurnitureRoot = "_Furniture";
        private const float ShelfLightInset = 1.29f;
        private const float ShelfLightHalfSpan = 1.65f;
        private static readonly string[] SolidNames =
        {
            "East_BackBar", "East_BarCounter", "East_Clock",
            "West_Chesterfield", "West_Armchair_South", "West_Armchair_North"
        };

        [MenuItem("Igruha/Верю не верю/Собственный клуб — мебель по стенам")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before applying furniture.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open BelieveOrNot before applying furniture.");
            var arena = GameObject.Find("_Arena");
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            if (arena == null || config == null) throw new InvalidOperationException("Arena or config missing.");
            Build(arena.transform, config);
            BelievePrivateClubBuilder.Audit();
            Audit();
            EditorSceneManager.MarkSceneDirty(arena.scene);
            EditorSceneManager.SaveScene(arena.scene);
            AssetDatabase.SaveAssets();
        }

        internal static void Build(Transform arena, BelieveOrNotConfig config)
        {
            BelieveClubFurnitureAssets.Prepare();
            var old = arena.Find(FurnitureRoot);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(FurnitureRoot).transform;
            root.SetParent(arena, false);
            float side = config.HallWidth * .5f;
            Place(root, "East_BackBar", "BackBar", new Vector3(side - .64f, 0, 0), 270);
            Place(root, "East_BarCounter", "BarCounter", new Vector3(side - 1.63f, 0, 0), 270);
            for (int i = 0; i < 4; i++)
                Place(root, "East_Stool_" + i, "BarStool", new Vector3(config.SpectatorZoneRadius + .5f, 0, -2.1f + i * 1.4f), 270);
            Place(root, "East_Clock", "GrandfatherClock", new Vector3(side - .69f, 0, -4.2f), 270);
            Place(root, "West_Chesterfield", "Chesterfield", new Vector3(-side + 1.23f, 0, 1.1f), 90);
            Place(root, "West_Armchair_South", "ClubArmchair", new Vector3(-side + 2.03f, 0, -1.2f), 0);
            Place(root, "West_Armchair_North", "ClubArmchair", new Vector3(-side + 2.03f, 0, 3.4f), 180);
            Place(root, "West_CoffeeTable", "CoffeeTable", new Vector3(-side + 2.23f, 0, 1.1f), 90);
            Place(root, "West_Stag", "StagTrophy", new Vector3(-side + .44f, 2.15f, 1.1f), 90);
            PlaceCorners(root, side);
            PositionShelfLights(arena, config);
        }

        /// <summary>
        /// Углы зала. Зал 16.6 × 14.4 м, а мебель стояла одной линией вдоль
        /// восточной и западной стен: между баром и креслами оставались
        /// пустые метры, и клуб читался расставленной по периметру мебелью,
        /// а не местом, где живут. Референсы 02 и 03 набиты мебелью плотно.
        ///
        /// Что можно ставить, решает не вкус, а две границы. Предмет обязан
        /// лежать в клине East / West (|x| > |z|) и вне круга зрителей —
        /// это проверяет <see cref="Audit"/>. Сверх того восток закрыт при
        /// z больше +3.5, а запад при z меньше −3.5: там проходит край
        /// геройского кадра, и любой предмет встанет за лицом соперника.
        /// Поэтому группы разведены по диагонали: читальня на юго-востоке,
        /// пальма и пуф на северо-востоке, шкаф с вешалкой на северо-западе,
        /// столик с пуфом на юго-западе.
        /// </summary>
        private static void PlaceCorners(Transform root, float side)
        {
            Place(root, "East_Bookcase", "Bookcase", new Vector3(side - .42f, 0, -5.6f), 270);
            Place(root, "East_ReadingChair", "ClubArmchair", new Vector3(side - 2.3f, 0, -4.6f), 300);
            Place(root, "East_SideTable", "SideTable", new Vector3(side - 2.4f, 0, -5.85f), 0);
            Place(root, "East_Palm", "PottedPalm", new Vector3(side - 1.0f, 0, 3.2f), 250);
            Place(root, "East_Ottoman", "Ottoman", new Vector3(side - 2.4f, 0, 2.6f), 30);
            Place(root, "West_Bookcase", "Bookcase", new Vector3(-side + .42f, 0, 5.4f), 90);
            Place(root, "West_CoatStand", "CoatStand", new Vector3(-side + .78f, 0, 4.0f), 90);
            Place(root, "West_Palm", "PottedPalm", new Vector3(-side + 1.15f, 0, 6.2f), 70);
            Place(root, "West_SideTable", "SideTable", new Vector3(-side + 2.4f, 0, 2.4f), 0);
            Place(root, "West_Ottoman", "Ottoman", new Vector3(-side + 2.4f, 0, -2.6f), 200);
        }

        private static void Place(Transform root, string name, string model, Vector3 position, float yaw)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.Euler(0, yaw, 0);
            var go = BelieveClubFurnitureAssets.Model(holder, model);
            if (!SolidNames.Contains(name)) return;
            // A single local-space box enclosing the imported mesh; no mesh colliders.
            var bounds = new Bounds();
            bool first = true;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = holder.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            var collider = holder.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        internal static void PositionShelfLights(Transform arena, BelieveOrNotConfig config)
        {
            // Keep shelf accents attached to the cabinetry after either layer is rebuilt.
            var hall = arena.Find("_Hall");
            if (hall == null) return;
            var lights = hall.GetComponentsInChildren<Light>(true)
                .Where(l => l.name == "EastShelfGlow").OrderBy(l => l.transform.position.z).ToArray();
            if (lights.Length != 2) throw new InvalidOperationException("Expected two existing EastShelfGlow lights.");
            for (int i = 0; i < lights.Length; i++)
            {
                var p = arena.InverseTransformPoint(lights[i].transform.position);
                p.x = config.HallWidth * .5f - ShelfLightInset;
                p.z = i == 0 ? -ShelfLightHalfSpan : ShelfLightHalfSpan;
                lights[i].transform.position = arena.TransformPoint(p);
                lights[i].transform.rotation = Quaternion.LookRotation(arena.TransformPoint(
                    new Vector3(config.HallWidth * .5f - .69f, 1.7f, p.z)) - lights[i].transform.position);
            }
        }

        [MenuItem("Igruha/Верю не верю/Проверить мебель клуба")]
        public static void Audit()
        {
            var config = AssetDatabase.LoadAssetAtPath<BelieveOrNotConfig>(ConfigPath);
            var arena = GameObject.Find("_Arena");
            var root = arena == null ? null : arena.transform.Find(FurnitureRoot);
            if (root == null || config == null) throw new InvalidOperationException("Furniture or config missing.");
            if (root.parent != arena.transform || root.childCount != 22)
                throw new InvalidOperationException("Furniture must be a single complete group outside _Hall.");
            if (root.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.layer != 0))
                throw new InvalidOperationException("All furniture must use Default.");
            float nearest = float.MaxValue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                Vector3 c = b.center - arena.transform.position;
                float x = Mathf.Max(0, Mathf.Abs(c.x) - b.extents.x);
                float z = Mathf.Max(0, Mathf.Abs(c.z) - b.extents.z);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(x * x + z * z));
                if (r.sharedMaterials.Any(m => m == null || m.shader == null))
                    throw new InvalidOperationException("Missing furniture material: " + r.name);
                if (Mathf.Abs(c.x) <= Mathf.Abs(c.z))
                    throw new InvalidOperationException("Furniture must remain on East / West: " + r.name);
            }
            if (nearest < config.SpectatorZoneRadius)
                throw new InvalidOperationException("Furniture crosses spectator circle: " + nearest);
            foreach (Transform item in root)
            {
                var colliders = item.GetComponentsInChildren<Collider>(true);
                bool solid = SolidNames.Contains(item.name);
                if (colliders.Length != (solid ? 1 : 0) || (solid &&
                    (!(colliders[0] is BoxCollider) || colliders[0].transform != item ||
                     colliders[0].isTrigger || !colliders[0].enabled)))
                    throw new InvalidOperationException("Furniture collider policy violated: " + item.name);
            }
            foreach (string name in SolidNames)
                if (root.Find(name) == null) throw new InvalidOperationException("Missing solid furniture: " + name);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new InvalidOperationException("Missing furniture script: " + t.name);
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh == null) throw new InvalidOperationException("Missing furniture mesh: " + f.name);
            Debug.Log($"IGR-565: furniture valid; 22 items; 6 BoxColliders; all Default; nearest renderer {nearest:F2} m >= spectator radius {config.SpectatorZoneRadius:F2} m; missing refs 0.");
        }
    }
}
