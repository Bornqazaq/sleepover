using System.Collections.Generic;
using System.Text;
using Igruha.Core.Spawning;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Отвердить декор арены: надеть коллайдеры на модели, которые дресс
    /// поставил в зал голыми.
    ///
    /// Почему это понадобилось. Дресс любой мини-игры помечает модель
    /// декорацией (<c>Scenery</c> / <c>DressKit.StripColliders</c>) и срезает
    /// её коллайдеры целиком. Для моделей, посаженных внутрь коробки блокаута,
    /// это верно: столкновения держит коробка, а мешевый коллайдер поверх неё
    /// дал бы вторую поверхность другой формы. Но половина дресса ставится
    /// не в коробку, а прямо на пол — парты, стойка бара, трибуны, ящики,
    /// станки. У них коробки нет, и после среза они перестают существовать
    /// для физики: игрок проходит сквозь мебель насквозь.
    ///
    /// Инструмент проходит арену и выдаёт коллайдер каждой модели, которая
    /// одновременно: ничем не прикрыта, стоит в досягаемости игрока и не
    /// принадлежит механизму. Всё остальное остаётся декорацией.
    ///
    /// Слой отвердевших моделей — <c>Cover</c>, а не <c>Default</c>. Причина
    /// не в оформлении, а в двух масках. Проверка опоры игрока
    /// (<c>PlayerController.groundLayer</c>) слушает только <c>Ground</c>
    /// и <c>Cover</c>: ящик на <c>Default</c> держал бы игрока, но считался бы
    /// воздухом, и стоящий на ящике игрок висел бы в прыжковой анимации.
    /// Маска препятствий камеры устроена так же — <c>Default</c> для неё
    /// прозрачен, и камера ныряла бы сквозь шкаф.
    /// </summary>
    public static class PropColliders
    {
        /// <summary>
        /// Корни сцены, по которым проходит инструмент.
        ///
        /// Ловушки лежат отдельным корнем от арены — у «Переноски предмета»
        /// это <c>_Traps</c>, — и декор внутри них дресс раздевает так же.
        /// </summary>
        private static readonly string[] RootNames = { "_Arena", "_Traps" };

        /// <summary>Слой, на который уходит отвердевшая модель.</summary>
        private const string SolidLayerName = "Cover";

        /// <summary>
        /// Ниже этой высоты модель считается настилом, а не предметом, в метрах.
        ///
        /// Мусор, потёртости пола, швы плит и опилки лежат плашмя. Коллайдер
        /// на них не делает мир твёрже, зато превращает ровный пол в стиральную
        /// доску, на которой контроллер спотыкается на каждом шаге.
        /// </summary>
        private const float MinPropHeight = 0.25f;

        /// <summary>
        /// Тоньше этого модель считается плёнкой, в метрах.
        ///
        /// Холст портрета, полотнище афиши и наклейка вырезаны в ноль толщины.
        /// Коллайдер такой модели — это поверхность без объёма поверх стены,
        /// которая уже твёрдая: физике он не даёт ничего, а вырожденных
        /// коллайдеров в сцене прибавляет сотнями.
        /// </summary>
        private const float MinPropThickness = 0.01f;

        /// <summary>
        /// Ниже этого следа на полу модель считается мелочёвкой, в метрах.
        ///
        /// Сквозь мел, кружку и лампочку никто не «проходит» — их обходят
        /// глазами. Коллайдер им не нужен, а в сцене их тысячи.
        /// </summary>
        private const float MinPropFootprint = 0.30f;

        /// <summary>
        /// На какой высоте над полом под моделью она уже вне досягаемости, в метрах.
        ///
        /// Выше этого — подвес: балки, растяжки, софтбоксы, полотнища, люстры.
        /// Коллайдер там не мешал бы игроку, зато мешал бы камере: габаритная
        /// коробка поверх ажурной фермы — это невидимая плита, в которую
        /// камера утыкается на открытом месте.
        /// </summary>
        private const float ReachHeight = 2.6f;

        /// <summary>
        /// Сколько проходов делает инструмент, пока не перестанет находить новое.
        ///
        /// Проходов больше одного, потому что досягаемость меряется лучом вниз
        /// по уже твёрдому: отвердевшая стена первого ряда становится опорой
        /// для второго ряда над ней, отвердевший станок на дне ямы — для того,
        /// что стоит на нём. Один проход оставил бы стену твёрдой наполовину,
        /// а повторный запуск пункта меню каждый раз находил бы «ещё немного»
        /// и результат зависел бы от числа нажатий. Прогон до неподвижной
        /// точки убирает и то, и другое.
        /// </summary>
        private const int MaxPasses = 6;

        /// <summary>Радиус капсулы персонажа, в метрах. Из замороженного префаба игрока.</summary>
        private const float PlayerRadius = 0.36f;

        /// <summary>Высота капсулы персонажа, в метрах. Из замороженного префаба игрока.</summary>
        private const float PlayerHeight = 1.65f;

        /// <summary>Длина луча вниз в поисках пола под моделью, в метрах.</summary>
        private const float FloorProbeLength = 80f;

        /// <summary>Насколько луч начинается выше основания модели, в метрах.</summary>
        private const float FloorProbeLift = 0.05f;

        /// <summary>
        /// На какой высоте над полом модель ещё считается стоящей на нём, в метрах.
        ///
        /// Порог отделяет напольный софит от настенного бра и от гирлянды
        /// под куполом.
        /// </summary>
        private const float FixtureStandTolerance = 0.5f;

        /// <summary>
        /// До скольки треугольников модель одевается мешевым коллайдером.
        ///
        /// Меш держит форму — арку, лестницу, проём в заборе, — а габаритная
        /// коробка их заливает. Пак Synty низкополигонален, и в лимит попадает
        /// почти всё; что не попало, получает коробку по габаритам меша.
        /// </summary>
        private const int MeshColliderTriangleLimit = 3000;

        /// <summary>
        /// Слова, по которым модель остаётся декорацией при любом раскладе.
        ///
        /// Это то, что не предмет по своей природе: эффекты, вода, ткань,
        /// наклейки, надписи, дальний фон. Проверяется и имя самой модели,
        /// и имена её родителей до корня арены — дресс складывает такие вещи
        /// группами.
        /// </summary>
        private static readonly string[] NeverSolidNames =
        {
            "vfx", "fx", "effect", "particle", "smoke", "steam", "dust", "sawdust", "spark", "glow", "halo",
            "water", "pool", "splash", "foam", "decal", "wear", "seam", "litter", "trash", "shadow",
            "banner", "curtain", "canopy", "flag", "bunting", "garland", "pennant", "cloth",
            "silhouette", "label", "text", "hint", "marker", "arrow", "ghost", "preview",
            "template", "gizmo", "horizon", "sky", "backdrop", "cloud"
        };

        /// <summary>
        /// Слова, по которым модель твердеет, только если стоит на полу.
        ///
        /// Одно и то же слово называет и подвес, и предмет: «лампа» — это
        /// и лампочка гирлянды под куполом, и четырёхметровый софит, стоящий
        /// посреди манежа; «катушка провода» — это моток на полу цеха, а не
        /// провод. Подвешенное остаётся декорацией: коллайдер там нужен только
        /// камере, и нужен ей во вред. Стоящее на полу — обычный предмет,
        /// сквозь который игрок ходить не должен.
        /// </summary>
        private static readonly string[] FloorFixtureNames =
        {
            "light", "lamp", "sconce", "chandelier", "bulb", "spot", "softbox", "neon", "beam",
            "flood", "screen", "monitor", "rope", "cable", "wire", "chain", "rigging"
        };

        /// <summary>Почему модель осталась без коллайдера.</summary>
        private enum Skip
        {
            None,
            AlreadySolid,
            Mechanism,
            SoftName,
            TooSmall,
            OnSpawn,
            Overhead,
            Floating,
            Hidden
        }

        /// <summary>Решение по одной модели: считается до того, как в сцену попадёт хоть один коллайдер.</summary>
        private struct Verdict
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Skip Reason;
        }

        [MenuItem("Igruha/Арт/Физика декора: отчёт")]
        public static void ReportCurrentScene()
        {
            RunScene(true);
        }

        [MenuItem("Igruha/Арт/Физика декора: применить")]
        public static void ApplyCurrentScene()
        {
            RunScene(false);
        }

        /// <summary>
        /// Отвердить декор под корнем арены. Вызывается в конце сборки арены
        /// каждой мини-игры — после дресса, окружения и эффектов, иначе часть
        /// моделей ещё не поставлена.
        /// </summary>
        /// <returns>Строка отчёта для консоли.</returns>
        public static string Build(GameObject arenaRoot)
        {
            return RunToFixedPoint(arenaRoot, false);
        }

        /// <summary>Прогон до неподвижной точки: пока очередной проход что-то находит.</summary>
        private static string RunToFixedPoint(GameObject root, bool dryRun)
        {
            var sb = new StringBuilder();
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                string report = Run(root, dryRun, pass + 1, out int added);
                sb.Append(report);
                if (added == 0 || dryRun)
                {
                    break;
                }
            }

            return sb.ToString();
        }

        private static void RunScene(bool dryRun)
        {
            bool found = false;
            for (int i = 0; i < RootNames.Length; i++)
            {
                var root = GameObject.Find(RootNames[i]);
                if (root == null)
                {
                    continue;
                }

                found = true;
                RunToFixedPoint(root, dryRun);
                if (dryRun)
                {
                    continue;
                }

                EditorUtility.SetDirty(root);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            }

            if (!found)
            {
                Debug.LogWarning("⚠️ Ни одного корня арены в сцене не найдено — отвердевать нечего");
            }
        }

        private static string Run(GameObject arenaRoot, bool dryRun, int pass, out int added)
        {
            added = 0;
            if (arenaRoot == null)
            {
                return "физика декора: арена не найдена";
            }

            Physics.SyncTransforms();

            int solidLayer = LayerMask.NameToLayer(SolidLayerName);
            var renderers = arenaRoot.GetComponentsInChildren<MeshRenderer>(true);
            var verdicts = new List<Verdict>(renderers.Length);
            var skipped = new Dictionary<Skip, int>();
            var byGroup = new Dictionary<string, int>();

            // Два прохода. Досягаемость модели считается лучом вниз, и если
            // добавлять коллайдеры по ходу, луч начнёт цепляться за уже
            // отвердевшие соседние модели: результат прогона зависел бы
            // от порядка обхода иерархии.
            Bounds[] spawns = SpawnBoxes(arenaRoot.scene);
            for (int i = 0; i < renderers.Length; i++)
            {
                verdicts.Add(Judge(renderers[i], arenaRoot.transform, spawns));
            }

            for (int i = 0; i < verdicts.Count; i++)
            {
                Verdict verdict = verdicts[i];
                if (verdict.Reason != Skip.None)
                {
                    skipped.TryGetValue(verdict.Reason, out int had);
                    skipped[verdict.Reason] = had + 1;
                    continue;
                }

                added++;
                string group = GroupOf(verdict.Renderer.transform, arenaRoot.transform);
                byGroup.TryGetValue(group, out int count);
                byGroup[group] = count + 1;

                if (dryRun)
                {
                    continue;
                }

                Solidify(verdict, solidLayer);
            }

            return Report(arenaRoot, dryRun, pass, added, renderers.Length, skipped, byGroup);
        }

        /// <summary>Надеть коллайдер и увести модель на слой твёрдого.</summary>
        private static void Solidify(Verdict verdict, int solidLayer)
        {
            GameObject go = verdict.Renderer.gameObject;
            if (TriangleCount(verdict.Mesh) <= MeshColliderTriangleLimit)
            {
                var mesh = go.AddComponent<MeshCollider>();
                mesh.sharedMesh = verdict.Mesh;
                mesh.convex = false;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = verdict.Mesh.bounds.center;
                box.size = verdict.Mesh.bounds.size;
            }

            if (solidLayer >= 0)
            {
                go.layer = solidLayer;
            }
        }

        /// <summary>
        /// Треугольники меша без чтения самих индексов: <c>Mesh.triangles</c>
        /// поднял бы в память весь индексный буфер каждой модели арены.
        /// </summary>
        private static long TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                indices += (long)mesh.GetIndexCount(i);
            }

            return indices / 3;
        }

        /// <summary>
        /// Решить судьбу одной модели, ничего не меняя в сцене.
        /// </summary>
        private static Verdict Judge(MeshRenderer renderer, Transform arenaRoot, Bounds[] spawns)
        {
            var verdict = new Verdict { Renderer = renderer, Reason = Skip.None };

            var filter = renderer.GetComponent<MeshFilter>();
            verdict.Mesh = filter != null ? filter.sharedMesh : null;
            if (verdict.Mesh == null || !renderer.gameObject.activeInHierarchy)
            {
                verdict.Reason = Skip.Hidden;
                return verdict;
            }

            // Коллайдер где-то по ветке — модель уже держит блокаут (или она
            // сама триггер). Вторая поверхность поверх выверенной опаснее
            // дырки: приземление становится лотереей.
            for (Transform t = renderer.transform; t != null && t != arenaRoot; t = t.parent)
            {
                if (t.GetComponent<Collider>() != null)
                {
                    verdict.Reason = Skip.AlreadySolid;
                    return verdict;
                }
            }

            if (renderer.GetComponentInChildren<Collider>(true) != null)
            {
                verdict.Reason = Skip.AlreadySolid;
                return verdict;
            }

            // Сам корень арены в разбор не входит. На нём висит контроллер
            // мини-игры (ExamEffects, DuckHuntArena) — по правилу «ветка
            // механизма остаётся декорацией» он объявил бы механизмом всю
            // арену целиком, и прогон честно отвердевал бы ноль моделей.
            bool fixture = false;
            for (Transform t = renderer.transform; t != null && t != arenaRoot; t = t.parent)
            {
                if (IsMechanism(t))
                {
                    verdict.Reason = Skip.Mechanism;
                    return verdict;
                }

                if (Contains(NeverSolidNames, t.name))
                {
                    verdict.Reason = Skip.SoftName;
                    return verdict;
                }

                fixture |= Contains(FloorFixtureNames, t.name);
            }

            Bounds bounds = renderer.bounds;
            if (bounds.size.y < MinPropHeight ||
                Mathf.Max(bounds.size.x, bounds.size.z) < MinPropFootprint ||
                Mathf.Min(bounds.size.x, Mathf.Min(bounds.size.y, bounds.size.z)) < MinPropThickness)
            {
                verdict.Reason = Skip.TooSmall;
                return verdict;
            }

            for (int i = 0; i < spawns.Length; i++)
            {
                if (bounds.Intersects(spawns[i]))
                {
                    verdict.Reason = Skip.OnSpawn;
                    return verdict;
                }
            }

            if (!Floor(bounds, out float floorY))
            {
                verdict.Reason = Skip.Floating;
                return verdict;
            }

            float lift = bounds.min.y - floorY;
            if (lift > ReachHeight)
            {
                verdict.Reason = Skip.Overhead;
                return verdict;
            }

            verdict.Reason = fixture && lift > FixtureStandTolerance ? Skip.SoftName : Skip.None;
            return verdict;
        }

        /// <summary>
        /// Габариты персонажа на каждой точке спавна.
        ///
        /// Модель, накрывающую точку спавна, отвердевать нельзя: раунд начнётся
        /// с того, что игрок окажется внутри парты, и физика будет его оттуда
        /// выталкивать. В «Экзамене» так стояли шесть из девяти спавнов,
        /// в Duck Hunt — один. Пока дресс ставит мебель поверх спавнов, такая
        /// мебель остаётся декорацией, и об этом пишется предупреждение:
        /// чинить это по-настоящему нужно в раскладке, а не здесь.
        /// </summary>
        private static Bounds[] SpawnBoxes(UnityEngine.SceneManagement.Scene scene)
        {
            var boxes = new List<Bounds>();
            var points = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].gameObject.scene != scene)
                {
                    continue;
                }

                Vector3 centre = points[i].transform.position + Vector3.up * (PlayerHeight * 0.5f);
                boxes.Add(new Bounds(centre, new Vector3(PlayerRadius * 2f, PlayerHeight, PlayerRadius * 2f)));
            }

            return boxes.ToArray();
        }

        /// <summary>
        /// Найти пол под моделью.
        ///
        /// Луч бьёт из основания габарита вниз по уже существующим коллайдерам
        /// — то есть по блокауту. Пола под моделью нет вовсе — это фон
        /// за пределами арены (горизонт, силуэты города), трогать его незачем.
        /// Насколько высоко модель над найденным полом, решают вызывающие:
        /// выше роста — подвес, чуть выше пола — напольный предмет.
        /// </summary>
        private static bool Floor(Bounds bounds, out float floorY)
        {
            floorY = 0f;
            Vector3 from = new Vector3(bounds.center.x, bounds.min.y + FloorProbeLift, bounds.center.z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, FloorProbeLength,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            floorY = hit.point.y;
            return true;
        }

        /// <summary>
        /// Механизм ли это — то, чем управляет игра.
        ///
        /// Створки платформы «Экзамена» собирают опору игрока из всех
        /// коллайдеров в своих детях, клетка шатра ездит, тележка падает.
        /// Коллайдер, подложенный в такую ветку со стороны, ломает механику
        /// молча — поэтому вся ветка остаётся декорацией.
        /// </summary>
        private static bool IsMechanism(Transform t)
        {
            if (t.GetComponent<Rigidbody>() != null ||
                t.GetComponent<Animator>() != null ||
                t.GetComponent<Animation>() != null)
            {
                return true;
            }

            var components = t.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                string ns = components[i].GetType().Namespace;
                if (ns != null && ns.StartsWith("Igruha.", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Совпадает ли имя объекта со словом из списка.
        ///
        /// Сравнение идёт по словам имени, а не по подстроке. Подстрокой
        /// «катушка провода» (<c>Wirespool</c>) попадала в слово «pool»
        /// и оставалась проходимой как лужа. Имя режется на слова по
        /// разделителям и по смене регистра (<c>FloorWear</c> → «floor»,
        /// «wear»), слово засчитывается по началу — чтобы «softboxes»
        /// совпало с «softbox», а «bulbs» с «bulb».
        /// </summary>
        private static bool Contains(string[] parts, string name)
        {
            int start = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                bool boundary = i == name.Length || !char.IsLetterOrDigit(name[i]) ||
                                (i > start && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]));
                if (!boundary)
                {
                    continue;
                }

                if (i > start && MatchesWord(parts, name.Substring(start, i - start).ToLowerInvariant()))
                {
                    return true;
                }

                start = i == name.Length || char.IsLetterOrDigit(name[i]) ? i : i + 1;
            }

            return false;
        }

        private static bool MatchesWord(string[] parts, string word)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (word.StartsWith(parts[i], System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Имя группы дресса — первый уровень под корнем арены.</summary>
        private static string GroupOf(Transform t, Transform arenaRoot)
        {
            Transform group = t;
            while (group.parent != null && group.parent != arenaRoot)
            {
                group = group.parent;
            }

            return group == arenaRoot ? arenaRoot.name : group.name;
        }

        private static string Report(GameObject arenaRoot, bool dryRun, int pass, int added, int total,
            Dictionary<Skip, int> skipped, Dictionary<string, int> byGroup)
        {
            var sb = new StringBuilder();
            sb.AppendLine(dryRun
                ? $"🧱 Физика декора (отчёт, ничего не менялось): {arenaRoot.scene.name}/{arenaRoot.name}"
                : $"🧱 Физика декора: {arenaRoot.scene.name}/{arenaRoot.name}, проход {pass}");
            sb.AppendLine($"   моделей на арене: {total}, отвердело: {added}");

            foreach (KeyValuePair<Skip, int> pair in skipped)
            {
                sb.AppendLine($"   пропущено «{Explain(pair.Key)}»: {pair.Value}");
            }

            if (skipped.TryGetValue(Skip.OnSpawn, out int onSpawn) && onSpawn > 0)
            {
                Debug.LogWarning($"⚠️ {arenaRoot.scene.name}: {onSpawn} моделей стоят на точках спавна и оставлены " +
                                 "проходимыми — иначе игрок начнёт раунд внутри мебели. Раскладку стоит поправить.",
                    arenaRoot);
            }

            var groups = new List<string>(byGroup.Keys);
            groups.Sort();
            foreach (string group in groups)
            {
                sb.AppendLine($"   + {group}: {byGroup[group]}");
            }

            string text = sb.ToString();
            Debug.Log(text, arenaRoot);
            return text;
        }

        private static string Explain(Skip reason)
        {
            switch (reason)
            {
                case Skip.AlreadySolid: return "уже твёрдое";
                case Skip.Mechanism: return "механизм";
                case Skip.SoftName: return "не предмет";
                case Skip.TooSmall: return "мелочь и настил";
                case Skip.OnSpawn: return "стоит на точке спавна";
                case Skip.Overhead: return "подвес";
                case Skip.Floating: return "фон без пола";
                case Skip.Hidden: return "без меша или выключено";
                default: return reason.ToString();
            }
        }
    }
}
