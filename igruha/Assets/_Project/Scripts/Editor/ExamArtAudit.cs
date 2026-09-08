using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замеры арта «Экзамена» — числа приёмки каждой подфазы фазы 4.
    ///
    /// Существует потому, что ровно этих чисел не хватило Duck Hunt: лёд лежал
    /// над пропастями, стог стоял поперёк этажа, декор висел в воздухе — всё
    /// ловится замером до того, как геймдизайнер откроет сцену. Собран по
    /// образцу <c>CarryItemArtAudit</c>.
    ///
    /// Контрольные числа блокаута (бриф, 14.9): 23 коллайдера на <c>Ground</c>,
    /// 54 на <c>Default</c>. После любой подфазы они обязаны остаться теми же.
    ///
    /// Число на <c>Default</c> было 38 и выросло 08.09 вместе с ужатием зала:
    /// колонна парт в боковом нефе стала пятикоробочной вместо трёхкоробочной,
    /// а коробка парты — это четыре примитива со своими коллайдерами.
    /// 16 новых коллайдеров — ровно четыре новые коробки на четыре.
    /// <c>Ground</c> не изменился и измениться был не должен: порог над
    /// зазором площадок поставлен без коллайдера намеренно — опору там
    /// держит невидимый пол под ним.
    /// </summary>
    internal static class ExamArtAudit
    {
        private const string ArenaRoot = "_Arena";

        /// <summary>Допуск на «стоит на опоре» и «не вылезает». Два сантиметра — это шов, а не дефект.</summary>
        private const float Tolerance = 0.03f;

        private const int ExpectedGround = 23;
        private const int ExpectedDefault = 54;

        /// <summary>Как называются группы дресса: по ним он и отличается от блокаута.</summary>
        private static readonly string[] DressGroups =
        {
            "Dress", "Seam", "Hinges", "HatchBand", "Frame", "BoardTrim", "Bags"
        };

        /// <summary>
        /// Что не обязано стоять на полу, и почему.
        ///
        /// Список короткий намеренно: каждое имя здесь — это отказ от проверки,
        /// и он должен быть объясним. Всё остальное обязано иметь опору снизу.
        /// </summary>
        private static readonly Dictionary<string, string> Airborne = new Dictionary<string, string>
        {
            { "Deck_", "настил живёт в коробке створки" },
            { "SeamStrip", "шов лежит на кромке створки" },
            { "HingeBarrel", "ствол петли сидит на оси вращения" },
            { "HingeStrap", "накладка лежит на полотне створки" },
            { "Band_", "полоса рамки лежит на полу зала" },
            { "Model", "модель садится в коробку блокаута" },
            { "Stage", "подиум садится в коробку кафедры" },
            { "Locker_", "шкафчики садятся в коробку шкафа" },
            { "Rail_", "карниз и полка крепятся к доске" },
            { "Frame_", "рамка обнимает подвесную табличку" },
            { "SchoolBag", "рюкзак висит на перекладине" },
            { "Books", "стопка учебников лежит на столешнице парты" },
            { "Bookcase_Books", "учебники стоят на полках книжного шкафа" },
            { "Chart", "учебный плакат висит на стене" },
            { "Beam_", "балка перекрытия висит под потолком" },
            { "SideSconce", "бра крепится к пилястре" }
        };

        /// <summary>
        /// Коробки, дресс которых выходит за габарит намеренно, и насколько.
        ///
        /// Список объявляется здесь, а не прячется допуском: правило фазы —
        /// «меняешь форму, скажи вслух и покажи замер». Число рядом с именем
        /// и есть тот замер, и превысить его дресс не имеет права.
        /// </summary>
        private static readonly Dictionary<string, KeyValuePair<float, string>> DeclaredOverflow =
            new Dictionary<string, KeyValuePair<float, string>>
            {
                {
                    "Podium",
                    new KeyValuePair<float, string>(0.15f,
                        "карниз обнимает кромку возвышения снаружи; невидимая стенка кафедры " +
                        "останавливает Ученика на сантиметр раньше него")
                }
            };

        [MenuItem("Igruha/Экзамен/Замеры арта")]
        private static void Measure()
        {
            GameObject arena = GameObject.Find(ArenaRoot);
            if (arena == null)
            {
                Debug.LogError("Арена _Arena не найдена — сначала «Igruha/Экзамен/Построить арену»");
                return;
            }

            var report = new StringBuilder();
            report.Append("📐 «Экзамен», замеры арта");

            MeasureColliders(arena, report);
            MeasureOverflow(arena, report);
            MeasureSupport(arena, report);
            MeasureSymmetry(arena, report);
            MeasureMirror(arena, report);
            MeasureEnvironment(arena, report);
            MeasureEffects(arena, report);
            MeasureHatch(arena, report);
            MeasureMeshes(arena, report);

            Debug.Log(report.ToString(), arena);
        }

        /// <summary>
        /// Коллайдеры: в дрессе их обязано быть ноль, а по слоям — ровно
        /// столько же, сколько до арта. Коллайдер — договор с фазами 2–3.
        /// </summary>
        private static void MeasureColliders(GameObject arena, StringBuilder report)
        {
            int inDress = 0;
            foreach (Transform group in FindDressGroups(arena))
            {
                inDress += group.GetComponentsInChildren<Collider>(true).Length;
            }

            var byLayer = new Dictionary<string, int>();
            foreach (Collider collider in arena.GetComponentsInChildren<Collider>(true))
            {
                string layer = LayerMask.LayerToName(collider.gameObject.layer);
                byLayer.TryGetValue(layer, out int count);
                byLayer[layer] = count + 1;
            }

            byLayer.TryGetValue("Ground", out int ground);
            byLayer.TryGetValue("Default", out int standard);

            report.Append("\n\n— Коллайдеры");
            report.Append("\n  в дрессе: ").Append(inDress).Append(Mark(inDress == 0));
            report.Append("\n  на Ground: ").Append(ground).Append(" из ").Append(ExpectedGround)
                .Append(Mark(ground == ExpectedGround));
            report.Append("\n  на Default: ").Append(standard).Append(" из ").Append(ExpectedDefault)
                .Append(Mark(standard == ExpectedDefault));

            foreach (KeyValuePair<string, int> pair in byLayer)
            {
                if (pair.Key != "Ground" && pair.Key != "Default")
                {
                    report.Append("\n  на ").Append(pair.Key).Append(": ").Append(pair.Value);
                }
            }
        }

        /// <summary>
        /// Насколько дресс вылезает за коробку, в которую посажен.
        ///
        /// Ловится случай Duck Hunt: коробка укрытия 1.37 м, модель 5.32 м,
        /// и полтора метра её висели над пропастью. По высоте не меряем —
        /// предмет выше своей коробки правилами разрешён.
        /// </summary>
        private static void MeasureOverflow(GameObject arena, StringBuilder report)
        {
            int checkedBoxes = 0;
            int over = 0;
            float worst = 0f;
            string worstName = string.Empty;
            var declared = new List<string>(2);

            foreach (Transform group in FindDressGroups(arena))
            {
                if (group.name != "Dress")
                {
                    continue;
                }

                Transform box = group.parent;
                if (box == null || !TryBoxBounds(box, out Bounds boxBounds) ||
                    !TryWorldBounds(group.gameObject, out Bounds dressBounds))
                {
                    continue;
                }

                checkedBoxes++;
                float excess = Mathf.Max(
                    Mathf.Max(dressBounds.max.x - boxBounds.max.x, boxBounds.min.x - dressBounds.min.x),
                    Mathf.Max(dressBounds.max.z - boxBounds.max.z, boxBounds.min.z - dressBounds.min.z));

                float allowed = Tolerance;
                if (DeclaredOverflow.TryGetValue(box.name, out KeyValuePair<float, string> exception))
                {
                    allowed = exception.Key;
                    declared.Add($"{box.name}: {excess:F3} м из {exception.Key:F2} — {exception.Value}");
                }
                else if (excess > worst)
                {
                    worst = excess;
                    worstName = box.name;
                }

                if (excess > allowed)
                {
                    over++;
                }
            }

            report.Append("\n\n— Дресс в коробках");
            report.Append("\n  проверено коробок: ").Append(checkedBoxes);
            report.Append("\n  вылезают за габарит: ").Append(over).Append(Mark(over == 0));
            report.Append("\n  худший вылет: ").Append(worst.ToString("F3")).Append(" м");
            if (!string.IsNullOrEmpty(worstName))
            {
                report.Append(" (").Append(worstName).Append(')');
            }

            foreach (string line in declared)
            {
                report.Append("\n  объявлено: ").Append(line);
            }
        }

        /// <summary>
        /// Что висит в воздухе. Опорой считается любая геометрия арены, чей верх
        /// приходится под низ предмета и перекрывается с ним по плану.
        ///
        /// Коллайдеры для этого не годятся: у дресса их нет вовсе, а опорой
        /// служат как раз его же модели — полка держит мел, створка держит
        /// накладку петли.
        /// </summary>
        private static void MeasureSupport(GameObject arena, StringBuilder report)
        {
            var surfaces = new List<Bounds>(256);
            foreach (Renderer renderer in arena.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer)
                {
                    surfaces.Add(renderer.bounds);
                }
            }

            var floating = new List<string>(4);
            int checkedItems = 0;
            int exempt = 0;

            foreach (Transform group in FindDressGroups(arena))
            {
                foreach (Transform item in group)
                {
                    if (!TryWorldBounds(item.gameObject, out Bounds bounds))
                    {
                        continue;
                    }

                    if (IsAirborneByDesign(item.name))
                    {
                        exempt++;
                        continue;
                    }

                    checkedItems++;
                    if (!HasSupport(bounds, surfaces))
                    {
                        floating.Add($"{item.name} (низ {bounds.min.y:F2} м)");
                    }
                }
            }

            report.Append("\n\n— Опора под предметами");
            report.Append("\n  проверено: ").Append(checkedItems)
                .Append(", вынесено по списку: ").Append(exempt);
            report.Append("\n  висят в воздухе: ").Append(floating.Count).Append(Mark(floating.Count == 0));
            foreach (string item in floating)
            {
                report.Append("\n    ⚠️ ").Append(item);
            }
        }

        /// <summary>
        /// Платформы А и Б обязаны быть неотличимы ничем, кроме буквы и цвета:
        /// любая асимметрия — подсказка, на какую бежать, а вся игра построена
        /// на том, что подсказок нет. Проверяется числом, а не на глаз.
        /// </summary>
        private static void MeasureSymmetry(GameObject arena, StringBuilder report)
        {
            CountGeometry(arena.transform.Find("Platform_A"), out int meshesA, out int trianglesA);
            CountGeometry(arena.transform.Find("Platform_B"), out int meshesB, out int trianglesB);

            report.Append("\n\n— Симметрия А/Б");
            report.Append("\n  мешей: ").Append(meshesA).Append(" / ").Append(meshesB)
                .Append(Mark(meshesA == meshesB));
            report.Append("\n  треугольников: ").Append(trianglesA).Append(" / ").Append(trianglesB)
                .Append(Mark(trianglesA == trianglesB));
        }

        /// <summary>
        /// Зеркальность зала целиком. Любой предмет, стоящий у А и
        /// отсутствующий у Б, — это ориентир, по которому можно угадывать
        /// ответ, поэтому половины зала обязаны совпадать до треугольника.
        ///
        /// Считается <b>вся арена</b>, а не одно окружение, и это важно.
        /// Блокаут поставил шкаф слева, а вешалку справа; окружение добавило
        /// зеркальных двойников — вешалку слева и шкаф справа. Порознь каждая
        /// группа перекошена, вместе они сходятся, и осмысленно только целое.
        /// </summary>
        private static void MeasureMirror(GameObject arena, StringBuilder report)
        {
            report.Append("\n\n— Зеркальность зала");

            int leftMeshes = 0, rightMeshes = 0, leftTriangles = 0, rightTriangles = 0, onAxis = 0;
            foreach (MeshFilter filter in arena.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null)
                {
                    continue;
                }

                int triangles = filter.sharedMesh.triangles.Length / 3;
                float x = renderer.bounds.center.x;
                if (x < -0.05f)
                {
                    leftMeshes++;
                    leftTriangles += triangles;
                }
                else if (x > 0.05f)
                {
                    rightMeshes++;
                    rightTriangles += triangles;
                }
                else
                {
                    onAxis++;
                }
            }

            report.Append("\n  мешей слева / справа: ").Append(leftMeshes).Append(" / ").Append(rightMeshes)
                .Append(Mark(leftMeshes == rightMeshes));
            report.Append("\n  треугольников: ").Append(leftTriangles).Append(" / ").Append(rightTriangles)
                .Append(Mark(leftTriangles == rightTriangles));
            report.Append("\n  на оси зала: ").Append(onAxis);
        }

        /// <summary>
        /// Эффекты: коллайдеров ноль, число систем и потолок частиц записаны,
        /// а у А и Б их поровну — разница в числе или яркости эффектов была бы
        /// такой же подсказкой, как разница в реквизите.
        /// </summary>
        private static void MeasureEffects(GameObject arena, StringBuilder report)
        {
            Transform effects = arena.transform.Find("Effects");
            report.Append("\n\n— Эффекты");
            if (effects == null)
            {
                report.Append("\n  группы Effects нет  ⚠️");
                return;
            }

            int colliders = effects.GetComponentsInChildren<Collider>(true).Length;
            int systems = 0;
            int capacity = 0;
            foreach (ParticleSystem system in effects.GetComponentsInChildren<ParticleSystem>(true))
            {
                systems++;
                capacity += system.main.maxParticles;
            }

            int sideA = CountSystems(effects.Find("SideA"), out int capacityA);
            int sideB = CountSystems(effects.Find("SideB"), out int capacityB);
            int lights = effects.GetComponentsInChildren<Light>(true).Length;

            report.Append("\n  коллайдеров: ").Append(colliders).Append(Mark(colliders == 0));
            report.Append("\n  систем частиц: ").Append(systems).Append(", потолок частиц: ").Append(capacity);
            report.Append("\n  систем у А / Б: ").Append(sideA).Append(" / ").Append(sideB).Append(Mark(sideA == sideB));
            report.Append("\n  потолок у А / Б: ").Append(capacityA).Append(" / ").Append(capacityB)
                .Append(Mark(capacityA == capacityB));
            report.Append("\n  источников света в эффектах: ").Append(lights);
        }

        private static int CountSystems(Transform side, out int capacity)
        {
            capacity = 0;
            if (side == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ParticleSystem system in side.GetComponentsInChildren<ParticleSystem>(true))
            {
                count++;
                capacity += system.main.maxParticles;
            }

            return count;
        }

        /// <summary>
        /// Предметы окружения, которые обязаны стоять на полу. Висящее на
        /// стенах и потолке сюда не входит — оно на то и висит.
        ///
        /// Глобуса здесь нет намеренно: он стоит на плинте, и его низ по
        /// построению на метр выше пола. Проверка ловила его исправно —
        /// исправно и не по делу.
        /// </summary>
        private static readonly string[] FloorStanding =
        {
            "Pew_", "Bin", "BackDesk", "Plinth", "Shelf", "Paper_", "Plane_", "Pen_"
        };

        /// <summary>Плотность и цена окружения: коллайдеров ноль, теней ноль, предметы посчитаны.</summary>
        private static void MeasureEnvironment(GameObject arena, StringBuilder report)
        {
            Transform environment = arena.transform.Find("Environment");
            report.Append("\n\n— Окружение");
            if (environment == null)
            {
                report.Append("\n  группы Environment нет  ⚠️");
                return;
            }

            int colliders = environment.GetComponentsInChildren<Collider>(true).Length;
            int shadowCasters = 0;
            int meshes = 0;
            int triangles = 0;
            foreach (MeshRenderer renderer in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                meshes++;
                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    shadowCasters++;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
            }

            int lights = environment.GetComponentsInChildren<Light>(true).Length;

            // Что обязано стоять на полу — стоит ли. Ловится тот же класс, что
            // на Duck Hunt: декор, повисший в воздухе, читается забытым куском.
            int grounded = 0;
            int hovering = 0;
            foreach (Transform item in environment.GetComponentsInChildren<Transform>(true))
            {
                bool onFloor = false;
                for (int i = 0; i < FloorStanding.Length; i++)
                {
                    if (item.name.StartsWith(FloorStanding[i]))
                    {
                        onFloor = true;
                        break;
                    }
                }

                if (!onFloor || !TryWorldBounds(item.gameObject, out Bounds bounds))
                {
                    continue;
                }

                grounded++;
                if (Mathf.Abs(bounds.min.y) > Tolerance)
                {
                    hovering++;
                }
            }

            report.Append("\n  коллайдеров: ").Append(colliders).Append(Mark(colliders == 0));
            report.Append("\n  стоят на полу: ").Append(grounded - hovering).Append(" из ").Append(grounded)
                .Append(Mark(hovering == 0));
            report.Append("\n  отбрасывают тень: ").Append(shadowCasters).Append(Mark(shadowCasters == 0));
            report.Append("\n  предметов / треугольников: ").Append(meshes).Append(" / ").Append(triangles);
            report.Append("\n  источников света: ").Append(lights);
        }

        /// <summary>
        /// Створка обязана читаться створкой до первого раскрытия: настил, шов
        /// по центру, петли на оси. Здесь печатаются числа, по которым это
        /// видно без открытия сцены.
        /// </summary>
        private static void MeasureHatch(GameObject arena, StringBuilder report)
        {
            report.Append("\n\n— Створки");

            foreach (string platformName in new[] { "Platform_A", "Platform_B" })
            {
                Transform platform = arena.transform.Find(platformName);
                if (platform == null)
                {
                    continue;
                }

                int deck = 0;
                int hinges = 0;
                int seams = 0;
                float deckTop = float.NegativeInfinity;

                foreach (Transform child in platform.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.StartsWith("Deck_"))
                    {
                        deck++;
                        var renderer = child.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            deckTop = Mathf.Max(deckTop, renderer.bounds.max.y);
                        }
                    }
                    else if (child.name.StartsWith("HingeBarrel"))
                    {
                        hinges++;
                    }
                    else if (child.name == "SeamStrip")
                    {
                        seams++;
                    }
                }

                bool flush = Mathf.Abs(deckTop) <= Tolerance;
                report.Append("\n  ").Append(platformName)
                    .Append(": плит ").Append(deck)
                    .Append(", петель ").Append(hinges)
                    .Append(", полос шва ").Append(seams)
                    .Append(", верх настила ").Append(deckTop.ToString("F3")).Append(" м")
                    .Append(Mark(flush));
            }
        }

        private static void MeasureMeshes(GameObject arena, StringBuilder report)
        {
            int visible = 0;
            int triangles = 0;
            foreach (MeshFilter filter in arena.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null)
                {
                    continue;
                }

                visible++;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }

            report.Append("\n\n— Геометрия");
            report.Append("\n  видимых мешей: ").Append(visible);
            report.Append("\n  треугольников: ").Append(triangles);
        }

        // ────────────────────────────────────────────────────────────────

        private static IEnumerable<Transform> FindDressGroups(GameObject arena)
        {
            var found = new List<Transform>(32);
            foreach (Transform candidate in arena.GetComponentsInChildren<Transform>(true))
            {
                for (int i = 0; i < DressGroups.Length; i++)
                {
                    if (candidate.name == DressGroups[i])
                    {
                        found.Add(candidate);
                        break;
                    }
                }
            }

            return found;
        }

        private static bool IsAirborneByDesign(string name)
        {
            foreach (KeyValuePair<string, string> pair in Airborne)
            {
                if (name.StartsWith(pair.Key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSupport(Bounds item, List<Bounds> surfaces)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                Bounds surface = surfaces[i];
                if (surface.max.y > item.min.y + Tolerance || surface.max.y < item.min.y - 0.6f)
                {
                    continue;
                }

                bool overlapX = surface.max.x > item.min.x && surface.min.x < item.max.x;
                bool overlapZ = surface.max.z > item.min.z && surface.min.z < item.max.z;
                if (overlapX && overlapZ)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CountGeometry(Transform root, out int meshes, out int triangles)
        {
            meshes = 0;
            triangles = 0;
            if (root == null)
            {
                return;
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null)
                {
                    continue;
                }

                meshes++;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
        }

        /// <summary>
        /// Габарит коробки блокаута. Рендерер у неё погашен дрессом, поэтому
        /// меряем по коллайдеру, а без коллайдера — по погашенному рендереру.
        ///
        /// Отдельный случай — <b>группа коробок</b>: парта состоит из столешницы,
        /// двух ножек и скамьи, и у самой группы нет ни того, ни другого.
        /// Габарит группы — объединение её собственных частей, но <b>без</b>
        /// вложенного дресса: иначе проверка сравнивала бы дресс сам с собой
        /// и всегда сходилась. Без этого группа мерилась единичным кубом
        /// в точке начала координат, и шесть парт из шести числились
        /// «вылезающими» на пять сантиметров.
        /// </summary>
        private static bool TryBoxBounds(Transform box, out Bounds bounds)
        {
            var collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                bounds = collider.bounds;
                return true;
            }

            var renderer = box.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            bool has = false;
            bounds = new Bounds();
            foreach (Collider part in box.GetComponentsInChildren<Collider>(true))
            {
                if (IsInsideDress(part.transform, box))
                {
                    continue;
                }

                Encapsulate(ref bounds, ref has, part.bounds);
            }

            foreach (MeshRenderer part in box.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsInsideDress(part.transform, box))
                {
                    continue;
                }

                Encapsulate(ref bounds, ref has, part.bounds);
            }

            return has;
        }

        private static void Encapsulate(ref Bounds total, ref bool has, Bounds one)
        {
            if (!has)
            {
                total = one;
                has = true;
                return;
            }

            total.Encapsulate(one);
        }

        /// <summary>Лежит ли объект внутри группы дресса, не выходя за пределы коробки.</summary>
        private static bool IsInsideDress(Transform item, Transform box)
        {
            for (Transform step = item; step != null && step != box; step = step.parent)
            {
                for (int i = 0; i < DressGroups.Length; i++)
                {
                    if (step.name == DressGroups[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var used = new List<Renderer>(renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                if (renderer is MeshRenderer)
                {
                    used.Add(renderer);
                }
            }

            if (used.Count == 0)
            {
                return false;
            }

            bounds = used[0].bounds;
            for (int i = 1; i < used.Count; i++)
            {
                bounds.Encapsulate(used[i].bounds);
            }

            return true;
        }

        private static string Mark(bool ok)
        {
            return ok ? "  ✅" : "  ⚠️";
        }
    }
}
