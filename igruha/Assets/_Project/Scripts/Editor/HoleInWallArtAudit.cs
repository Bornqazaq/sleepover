using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Замеры арта «Дырки в стене»: та самая таблица, которую геймдизайнер
    /// смотрит вместо того, чтобы открывать редактор.
    ///
    /// Существует потому, что глазами эти вещи не ловятся, а ломаются молча.
    /// На Duck Hunt ровно их и не хватило: лёд лежал над пропастями, стог
    /// стоял поперёк этажа, декор висел в воздухе — всё это числа, и все они
    /// считаются за секунду. Считать их руками через консоль каждый раз —
    /// значит считать их по-разному в каждой подфазе и не заметить разъезда.
    ///
    /// Чего здесь <b>нет</b>: замечаний пересборки. Их печатает сама пересборка
    /// арены — в сцене от ненайденной модели не остаётся следа, кроме серой
    /// коробки, а серая коробка это законный вид на машине без паков.
    /// </summary>
    internal static class HoleInWallArtAudit
    {
        /// <summary>Имя, которым <see cref="DressKit"/> называет корень надетой модели.</summary>
        private const string DressHolder = "Dress";

        /// <summary>Приставка в имени подиума трибуны: он опора, а не предмет на ней.</summary>
        private const string DeckPrefix = "Deck";

        /// <summary>Приставка в имени плит пола студии.</summary>
        private const string FloorPrefix = "Floor_";

        private const string ArenaRoot = "_Arena";
        private const string StudioRoot = "_Studio";
        private const string EffectsRoot = "_Effects";
        private const string LightingRoot = "_Lighting";
        private const string LightingGroup = "HoleInWallStudio";

        private const string SyntyRoot = "Assets/Synty/";

        /// <summary>
        /// Допуск на сравнение габаритов, м. Сантиметр: меньше — это уже
        /// погрешность самих замеров, больше — начинает пропускать реальный
        /// свес.
        /// </summary>
        private const float Tolerance = 0.01f;

        /// <summary>Допуск на «стоит на полу», м: два сантиметра под ногой не видно.</summary>
        private const float SeatTolerance = 0.02f;

        /// <summary>
        /// Насколько запретная зона поджата внутрь от габарита арены, м.
        /// Кромка бассейна — законное место для неоновой нитки и панелей,
        /// и зона обязана начинаться за ней, а не на ней.
        /// </summary>
        private const float ZoneInset = 0.6f;

        /// <summary>Запас над верхом стены, выше которого декор уже не мешает вырезу, м.</summary>
        private const float ZoneHeadroom = 0.6f;

        [MenuItem("Igruha/Дырка в стене/Замеры арта")]
        public static void Run()
        {
            HoleInWallConfig config = FindConfig();
            if (config == null)
            {
                Debug.LogError("Не найден HoleInWallConfig — мерить нечего");
                return;
            }

            var roots = new List<GameObject>(16);
            SceneManager.GetActiveScene().GetRootGameObjects(roots);

            var report = new StringBuilder(1024);
            report.Append("📏 «Дырка в стене», замеры арта");

            MeasureDress(roots, report);
            MeasureScenery(config, report);
            MeasureEffects(config, report);
            MeasureColliders(roots, config, report);
            MeasureMeshes(roots, report);

            Debug.Log(report.ToString());
        }

        // ========== ДРЕСС ==========

        /// <summary>
        /// Модели, надетые на коробки блокаута: коллайдеры, посадка в коробку
        /// и вылезание за её габарит.
        ///
        /// «В воздухе» здесь значит не «не касается пола», а «не лежит в своей
        /// коробке по высоте». Настил садится к верхней грани коробки, тумба
        /// набирается копиями снизу вверх — общего пола у них нет, а вот
        /// выехать из коробки не имеет права ни один: коробка и есть то, что
        /// выверено фазами 2–3.
        /// </summary>
        private static void MeasureDress(List<GameObject> roots, StringBuilder report)
        {
            int holders = 0;
            int colliders = 0;
            int airborne = 0;
            int overhang = 0;
            var offenders = new List<string>(4);

            foreach (GameObject root in roots)
            {
                foreach (Transform holder in root.GetComponentsInChildren<Transform>(true))
                {
                    if (holder.name != DressHolder || holder.parent == null)
                    {
                        continue;
                    }

                    holders++;
                    colliders += holder.GetComponentsInChildren<Collider>(true).Length;

                    if (!TryBounds(holder.gameObject, out Bounds model))
                    {
                        continue;
                    }

                    Bounds box = BoxBounds(holder.parent);

                    if (model.min.y < box.min.y - Tolerance || model.max.y > box.max.y + Tolerance)
                    {
                        airborne++;
                        Remember(offenders, holder, "не лежит в коробке по высоте");
                    }

                    if (model.min.x < box.min.x - Tolerance || model.max.x > box.max.x + Tolerance ||
                        model.min.z < box.min.z - Tolerance || model.max.z > box.max.z + Tolerance)
                    {
                        overhang++;
                        Remember(offenders, holder, "вылезает за коробку по горизонтали");
                    }
                }
            }

            report.Append("\n— одетых коробок: ").Append(holders);
            report.Append("\n— коллайдеров в дрессе: ").Append(colliders).Append(" (норма 0)");
            report.Append("\n— в воздухе: ").Append(airborne).Append(" (норма 0)");
            report.Append("\n— вылезает за коробку: ").Append(overhang).Append(" (норма 0)");
            Append(report, offenders);
        }

        // ========== ОКРУЖЕНИЕ ==========

        /// <summary>
        /// Павильон: коллайдеры, посадка предметов на пол студии и обе
        /// запретные зоны.
        ///
        /// Зоны — главное здесь. Пятно арены обязано остаться пустым до высоты
        /// «верх стены плюс запас»: всё, что попадёт туда, перекроет вырез,
        /// а вырез в этой игре и есть игра. Полоса перед камерой — то же
        /// самое с другой стороны: там стоит камера игрока, и предмет в ней
        /// закрывает кадр целиком.
        /// </summary>
        private static void MeasureScenery(HoleInWallConfig config, StringBuilder report)
        {
            var groups = new List<Transform>(2);
            AddIfFound(groups, ArenaRoot, StudioRoot);
            AddIfFound(groups, LightingRoot, LightingGroup);

            if (groups.Count == 0)
            {
                report.Append("\n— окружения в сцене нет: павильон не построен");
                return;
            }

            Bounds arenaZone = ArenaZone(config);
            Bounds cameraZone = CameraZone(config);

            int pieces = 0;
            int colliders = 0;
            int airborne = 0;
            int inArenaZone = 0;
            int inCameraZone = 0;
            var offenders = new List<string>(4);

            foreach (Transform group in groups)
            {
                colliders += group.GetComponentsInChildren<Collider>(true).Length;

                foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>(true))
                {
                    pieces++;
                    Bounds bounds = renderer.bounds;

                    if (arenaZone.Intersects(bounds))
                    {
                        inArenaZone++;
                        Remember(offenders, renderer.transform, "в запретной зоне арены");
                    }

                    if (cameraZone.Intersects(bounds))
                    {
                        inCameraZone++;
                        Remember(offenders, renderer.transform, "в полосе перед камерой");
                    }
                }

                // Стоящий на площадке предмет проверяется отдельно: подвешенное
                // к ферме на полу стоять не обязано, а трибуна и телекамера —
                // обязаны, и парящая трибуна это ровно та поломка Duck Hunt,
                // из-за которой замеры и появились.
                //
                // Опора не одна: пол студии и подиум трибуны, — поэтому предмет
                // сверяется не с числом, а с тем, что реально лежит под ним.
                Transform stands = group.Find("Stands");
                if (stands == null)
                {
                    continue;
                }

                List<Bounds> supports = CollectSupports(group, stands);

                foreach (Transform prop in stands)
                {
                    if (prop.name.StartsWith(DeckPrefix) || !TryBounds(prop.gameObject, out Bounds bounds))
                    {
                        continue;
                    }

                    float support;
                    if (!TrySupport(bounds, supports, out support))
                    {
                        airborne++;
                        Remember(offenders, prop, $"под ним нет опоры, низ на {bounds.min.y:F2}");
                        continue;
                    }

                    if (bounds.min.y - support > SeatTolerance)
                    {
                        airborne++;
                        Remember(offenders, prop, $"низ на {bounds.min.y:F2} при опоре {support:F2}");
                    }
                }
            }

            report.Append("\n— предметов окружения: ").Append(pieces);
            report.Append("\n— коллайдеров в окружении: ").Append(colliders).Append(" (норма 0)");
            report.Append("\n— окружения в воздухе: ").Append(airborne).Append(" (норма 0)");
            report.Append("\n— в запретной зоне арены: ").Append(inArenaZone).Append(" (норма 0)");
            report.Append("\n— в полосе перед камерой: ").Append(inCameraZone).Append(" (норма 0)");
            Append(report, offenders);
        }

        /// <summary>
        /// Пятно арены до высоты, выше которой декор вырезу уже не мешает.
        /// Поджато внутрь на <see cref="ZoneInset"/>: кромка бассейна — законное
        /// место неоновой нитки, и зона не имеет права начинаться на ней.
        /// </summary>
        private static Bounds ArenaZone(HoleInWallConfig config)
        {
            float top = config.PlatformSurfaceY + config.WallHeight + ZoneHeadroom;
            float bottom = config.PoolBottomY;
            float centreZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;

            return new Bounds(
                new Vector3(0f, (top + bottom) * 0.5f, centreZ),
                new Vector3(config.ArenaWidth - ZoneInset * 2f, top - bottom, config.ArenaDepth - ZoneInset * 2f));
        }

        /// <summary>
        /// Полоса перед камерой: за ближним бортом бассейна, на глубину отхода
        /// камеры и выше пола студии. Ниже — сам пол студии, ему там место.
        /// </summary>
        private static Bounds CameraZone(HoleInWallConfig config)
        {
            float bottom = config.PoolBottomY + config.PoolDepth + HoleInWallArenaBuilder.PoolRimHeight + 0.1f;
            float top = config.PlatformSurfaceY + config.WallHeight + ZoneHeadroom;
            float far = config.ArenaNearZ;
            float near = far - config.CameraClearance;

            return new Bounds(
                new Vector3(0f, (top + bottom) * 0.5f, (far + near) * 0.5f),
                new Vector3(config.ArenaWidth - ZoneInset * 2f, top - bottom, far - near));
        }

        // ========== ЭФФЕКТЫ ==========

        /// <summary>
        /// Эффекты подфазы 4.4. Мерится не «красиво ли», а четыре вещи,
        /// которые ломаются молча и которых глазами не видно.
        ///
        /// <b>Коллайдеры и свет.</b> Первые ловили бы сметённого игрока и лучи
        /// деокклюдера камеры; второй съел бы один из четырёх дополнительных
        /// источников арены — предел URP-ассета, — и одна из дорожек молча
        /// потеряла бы свой цвет.
        ///
        /// <b>Автостарт.</b> Партиклы пака приходят зацикленными и с
        /// автостартом. Оставленный автостарт означает всплеск посреди сухой
        /// воды и удар в пустоту — причём с первого же кадра и навсегда.
        /// Норма здесь единица, а не ноль: туман над водой обязан идти всегда,
        /// он воздух студии, а не событие.
        ///
        /// <b>Верх постоянного эффекта.</b> Правило брифа — между игроком и
        /// стеной не должно быть ничего выше пола платформы, иначе перекрыт
        /// вырез, а вырез здесь и есть игра. Одноразовых эффектов это не
        /// касается: вспышка в вырезе и есть его подсветка, и она гаснет за
        /// секунду. А вот туман идёт постоянно, и его потолок обязан лежать
        /// <b>под</b> полом платформы — не на глаз, а числом.
        /// </summary>
        private static void MeasureEffects(HoleInWallConfig config, StringBuilder report)
        {
            GameObject arena = GameObject.Find(ArenaRoot);
            Transform effects = arena == null ? null : arena.transform.Find(EffectsRoot);

            if (effects == null)
            {
                report.Append("\n— эффектов в сцене нет: VFX не построены");
                return;
            }

            int emitters = 0;
            int systems = 0;
            int autoStart = 0;
            int looping = 0;
            float ceiling = float.NegativeInfinity;
            string ceilingName = null;

            foreach (ParticleSystem particles in effects.GetComponentsInChildren<ParticleSystem>(true))
            {
                systems++;

                // Эффект — это корневая система префаба пака; вложенные в неё
                // считаются системами, но не эффектами: у всплеска их три,
                // и три всплеска в отчёте были бы неправдой.
                if (particles.transform.parent == null ||
                    particles.transform.parent.GetComponent<ParticleSystem>() == null)
                {
                    emitters++;
                }

                ParticleSystem.MainModule main = particles.main;

                if (main.loop)
                {
                    looping++;
                }

                if (!main.playOnAwake)
                {
                    continue;
                }

                autoStart++;

                float top = TopReach(particles);
                if (top > ceiling)
                {
                    ceiling = top;
                    ceilingName = particles.name;
                }
            }

            report.Append("\n— эффектов: ").Append(emitters).Append(", систем в них: ").Append(systems);
            report.Append("\n— коллайдеров в эффектах: ")
                .Append(effects.GetComponentsInChildren<Collider>(true).Length).Append(" (норма 0)");
            report.Append("\n— источников света в эффектах: ")
                .Append(effects.GetComponentsInChildren<Light>(true).Length).Append(" (норма 0)");
            report.Append("\n— систем с автостартом: ").Append(autoStart).Append(" (норма 1 — только туман)");
            report.Append("\n— зацикленных систем: ").Append(looping).Append(" (туман и разряд на тросе)");

            if (ceilingName != null)
            {
                report.Append("\n— верх постоянного эффекта «").Append(ceilingName).Append("»: ")
                    .Append(ceiling.ToString("F2")).Append(" м при поле платформы ")
                    .Append(config.PlatformSurfaceY.ToString("F2")).Append(" (обязан быть ниже)");
            }
        }

        /// <summary>
        /// Докуда достаёт эффект по высоте: верх места рождения плюс всплывание
        /// за полную жизнь плюс половина самого крупного клока.
        ///
        /// Считается по настройкам, а не по <c>bounds</c> рендерера: в редакторе
        /// системы не идут, и границы у них пустые — замер показал бы ноль
        /// и молча одобрил бы туман по пояс.
        /// </summary>
        private static float TopReach(ParticleSystem particles)
        {
            ParticleSystem.MainModule main = particles.main;
            ParticleSystem.ShapeModule shape = particles.shape;

            float spawn = !shape.enabled ? 0f
                : shape.shapeType == ParticleSystemShapeType.Box ? shape.scale.y * 0.5f
                : shape.radius;

            float rise = Mathf.Max(0f, main.startSpeed.constantMax) * Mathf.Max(0f, main.startLifetime.constantMax);

            return particles.transform.position.y + spawn + rise + main.startSize.constantMax * 0.5f;
        }

        // ========== КОЛЛАЙДЕРЫ И ТРИГГЕРЫ ==========

        /// <summary>
        /// Коллайдеры сплошной геометрии и триггеры. Контрольное число:
        /// столько же, сколько было до арта. Арт — слой поверх блокаута,
        /// и любое расхождение здесь значит, что он сдвинул игру.
        ///
        /// Слои названы поимённо, а не «все»: камера видит препятствия только
        /// на <c>Ground</c>, <c>Cover</c> и <c>PlayerBarrier</c>
        /// (igruha/CLAUDE.md, 2a), и именно их число обязано быть неизменным.
        /// </summary>
        private static void MeasureColliders(List<GameObject> roots, HoleInWallConfig config, StringBuilder report)
        {
            int solid = 0;
            int triggers = 0;
            int groundLayer = LayerMask.NameToLayer("Ground");
            int coverLayer = LayerMask.NameToLayer("Cover");
            int barrierLayer = LayerMask.NameToLayer("PlayerBarrier");

            foreach (GameObject root in roots)
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger)
                    {
                        triggers++;
                        continue;
                    }

                    int layer = collider.gameObject.layer;
                    if (layer == groundLayer || layer == coverLayer || layer == barrierLayer)
                    {
                        solid++;
                    }
                }
            }

            report.Append("\n— коллайдеров на Ground/Cover/PlayerBarrier: ").Append(solid)
                .Append(" (столько же, сколько до арта)");
            report.Append("\n— триггеров: ").Append(triggers);
            report.Append("\n— дорожек в конфиге: ").Append(config.TrackCount);
        }

        // ========== МЕШИ ==========

        /// <summary>
        /// Видимые меши, треугольники и слоты, оставшиеся с материалами пака.
        ///
        /// Последнее — проверка палитры 4.2: бриф запрещает узоры, а модели
        /// Synty приходят с атласом, где на борту тумбы зебра. Ноль означает,
        /// что перекраска прошла по всем подмешам, а не по первому.
        /// </summary>
        private static void MeasureMeshes(List<GameObject> roots, StringBuilder report)
        {
            int meshes = 0;
            long triangles = 0;
            int packSlots = 0;

            foreach (GameObject root in roots)
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null)
                    {
                        continue;
                    }

                    meshes++;
                    triangles += mesh.triangles.Length / 3;

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            continue;
                        }

                        string path = AssetDatabase.GetAssetPath(material);
                        if (!string.IsNullOrEmpty(path) && path.StartsWith(SyntyRoot))
                        {
                            packSlots++;
                        }
                    }
                }
            }

            report.Append("\n— видимых мешей: ").Append(meshes);
            report.Append("\n— треугольников: ").Append(triangles);
            report.Append("\n— слотов с материалами пака: ").Append(packSlots).Append(" (норма 0)");
        }

        // ========== ОБЩЕЕ ==========

        /// <summary>
        /// Габарит коробки блокаута. Считается из её масштаба, а не из
        /// рендерера: рендерер коробки погашен дрессом, а у погашенного
        /// <c>bounds</c> Unity не обновляет.
        /// </summary>
        private static Bounds BoxBounds(Transform box)
        {
            Vector3 size = box.lossyScale;
            return new Bounds(box.position,
                new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }

        /// <summary>
        /// Опоры площадки: плиты пола студии и подиумы трибуны. Всё, что стоит
        /// на площадке, обязано опираться на одну из них.
        /// </summary>
        private static List<Bounds> CollectSupports(Transform studio, Transform stands)
        {
            var supports = new List<Bounds>(8);

            Transform shell = studio.Find("Shell");
            if (shell != null)
            {
                foreach (Transform slab in shell)
                {
                    if (slab.name.StartsWith(FloorPrefix) && TryBounds(slab.gameObject, out Bounds floor))
                    {
                        supports.Add(floor);
                    }
                }
            }

            foreach (Transform slab in stands)
            {
                if (slab.name.StartsWith(DeckPrefix) && TryBounds(slab.gameObject, out Bounds deck))
                {
                    supports.Add(deck);
                }
            }

            return supports;
        }

        /// <summary>Верх самой высокой опоры под предметом. Ложь — под ним пусто.</summary>
        private static bool TrySupport(Bounds prop, List<Bounds> supports, out float top)
        {
            top = float.NegativeInfinity;

            for (int i = 0; i < supports.Count; i++)
            {
                Bounds support = supports[i];
                if (prop.center.x < support.min.x || prop.center.x > support.max.x ||
                    prop.center.z < support.min.z || prop.center.z > support.max.z)
                {
                    continue;
                }

                if (support.max.y > prop.min.y + SeatTolerance)
                {
                    continue;
                }

                top = Mathf.Max(top, support.max.y);
            }

            return !float.IsNegativeInfinity(top);
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static void AddIfFound(List<Transform> groups, string rootName, string childName)
        {
            GameObject root = GameObject.Find(rootName);
            Transform child = root == null ? null : root.transform.Find(childName);
            if (child != null)
            {
                groups.Add(child);
            }
        }

        /// <summary>Запомнить нарушителя, но не весь список: длинный лог никто не читает.</summary>
        private static void Remember(List<string> offenders, Transform target, string reason)
        {
            if (offenders.Count < 8)
            {
                offenders.Add($"{Path(target)} — {reason}");
            }
        }

        private static void Append(StringBuilder report, List<string> offenders)
        {
            for (int i = 0; i < offenders.Count; i++)
            {
                report.Append("\n    ⚠ ").Append(offenders[i]);
            }
        }

        private static string Path(Transform target)
        {
            var path = new StringBuilder(target.name);
            Transform parent = target.parent;
            while (parent != null)
            {
                path.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return path.ToString();
        }

        private static HoleInWallConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:HoleInWallConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
