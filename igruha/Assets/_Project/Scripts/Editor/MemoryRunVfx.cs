using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты взрыва «Рейса на память» — подфаза 4.4.
    ///
    /// Собирает пул облаков и образец копоти, кладёт их в группу
    /// <c>_Effects</c> на арене и вешает на менеджер
    /// <see cref="MemoryRunEffects"/> со ссылками. Всё кодом: эффект, собранный
    /// мышью, не переживает первое же слияние веток.
    ///
    /// <b>Огня в этом взрыве нет ни одной частицы.</b> Не потому, что не нашли:
    /// в паке лежат и <c>FX_Fire_Explosion_01</c>, и <c>FX_Grenade_Explosive_01</c>.
    /// Спека (раздел 7) просит круглое пухлое облако и смешную смерть, а не
    /// страшную, и любой огненный шар превращает нелепое падение в поражающий
    /// элемент. Оранжевый цвет облаку даёт перекраска дыма, а не пламя.
    ///
    /// 🔴 <b>И ни одной частицы, которая осела бы на плите.</b> Всё, что здесь
    /// собрано, расширяется и гаснет в воздухе: ни decal, ни падающих обломков,
    /// ни следа. Маршрут не расчищается по ходу игры.
    /// </summary>
    internal static class MemoryRunVfx
    {
        private const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";

        /// <summary>
        /// Сколько облаков держим наготове.
        ///
        /// Четырёх заведомо хватает: ходят строго по одному, и два взрыва
        /// подряд возможны только через полный круг очереди. Пул нужен не
        /// против одновременности, а против того, чтобы не создавать объект
        /// в момент взрыва.
        /// </summary>
        private const int BlastPoolSize = 4;

        /// <summary>Оранжево-жёлтое облако — цвет из арт-брифа, но светлее: оно светится на тёмном.</summary>
        private static readonly Color CloudColor = new Color32(0xFF, 0xA8, 0x3C, 0xFF);

        /// <summary>Тёмный хвост, который остаётся в воздухе на секунду после облака.</summary>
        private static readonly Color TailColor = new Color32(0x3A, 0x33, 0x2E, 0xFF);

        /// <summary>Копоть на лице.</summary>
        private static readonly Color SootColor = new Color32(0x24, 0x20, 0x1E, 0xFF);

        private static int built;
        private static readonly StringBuilder notes = new StringBuilder();

        internal static void Build(Transform arena, GameObject manager)
        {
            built = 0;
            notes.Clear();
            blastMaterial = null;
            sootMaterial = null;

            var root = new GameObject("_Effects");
            root.transform.SetParent(arena, false);
            root.layer = LayerMask.NameToLayer("Default");

            var pool = new ParticleSystem[BlastPoolSize];
            for (int i = 0; i < BlastPoolSize; i++)
            {
                pool[i] = BuildBlast(root.transform, i);
            }

            GameObject soot = BuildSoot(root.transform);

            WireComponent(manager, pool, soot);
        }

        /// <summary>
        /// Одно облако: оранжевый ком, мультяшное кольцо и тёмный хвост.
        ///
        /// Кольцо здесь не украшение — оно единственное, что читается с сорока
        /// метров мгновенно. Ком дыма на таком расстоянии выглядит пятном,
        /// а расходящееся кольцо ловится глазом даже боковым зрением, и зритель
        /// в стартовой зоне успевает повернуться на взрыв.
        /// </summary>
        private static ParticleSystem BuildBlast(Transform parent, int index)
        {
            // ⚠️ Корень — <b>перенастроенный эффект пака</b>, а не система,
            // созданная кодом. Причина ровно та же, по которой блокаут не
            // красится встроенным материалом: у системы, добавленной через
            // AddComponent, материал не из URP, и облако выходит фиолетовым —
            // в редакторе, в кадре и в сборке. Проверено на кадре 4.4.
            //
            // Взять готовый эффект и переписать ему поведение дешевле и
            // надёжнее, чем собирать свой и искать ему материал: у пака
            // и текстура дыма правильная, и шейдер рабочий.
            if (!DressKit.TryLoad(Fx + "FX_Smoke_White_Small_01.prefab", out GameObject prefab))
            {
                notes.Append("\n— основа облака не найдена: FX_Smoke_White_Small_01");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = $"MineBlast_{index}";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = LayerMask.NameToLayer("Default");

            ParticleSystem cloud = go.GetComponent<ParticleSystem>();
            if (cloud == null)
            {
                notes.Append("\n— у FX_Smoke_White_Small_01 нет системы на корне");
                return null;
            }

            ConfigureCloud(cloud);

            AttachPackEffect(go.transform, Fx + "FX_Cartoony_Rings_01.prefab", "Ring", CloudColor,
                2.6f, 2.0f, 0.7f, 3);
            AttachPackEffect(go.transform, Fx + "FX_Smoke_Black_Small_01.prefab", "Tail", TailColor,
                1.0f, 1.7f, 1.5f, 7);

            built++;
            return cloud;
        }

        /// <summary>
        /// Ком дыма, перекрашенный в оранжевый.
        ///
        /// Настройки переписываются целиком, а не подправляются: у эффекта пака
        /// зацикливание, автостарт и своя гравитация. Гравитация здесь особенно
        /// важна — она поднимает частицу, а не роняет: <b>падающая частица
        /// садится на плиту, а на плиту нельзя ничего.</b>
        /// </summary>
        private static void ConfigureCloud(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 1.1f;
            main.startSpeed = 2.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.05f, 1.9f);
            main.startColor = CloudColor;
            main.gravityModifier = -0.05f;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.55f;

            // Ком раздувается и тает: округлая мультяшная форма держится
            // кривой размера, а не текстурой.
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.5f));

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color32(0xFF, 0xD9, 0x6B, 0xFF), 0f),
                    new GradientColorKey(CloudColor, 0.35f),
                    new GradientColorKey(new Color32(0x8A, 0x5E, 0x3A, 0xFF), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(0.35f);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = BlastMaterial(renderer.sharedMaterial);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material blastMaterial;

        /// <summary>
        /// Материал облака: текстура дыма из пака на аддитивном шейдере.
        ///
        /// Родной материал пака сюда не годится, и это видно только на кадре.
        /// Он идёт с <c>_TintColor</c> в половину яркости и половину альфы —
        /// расчёт на дневную сцену. В тёмном цеху оранжевое облако через такой
        /// тинт превращалось в пару бурых клякс: взрыв, которого не видно
        /// с соседней плиты, не говоря о зоне ожидания.
        ///
        /// Аддитивный шейдер выбран не ради «красивее»: облако обязано
        /// читаться с сорока метров на самом тёмном фоне игры — над чёрным
        /// провалом. Светящийся ком там виден, тонированный дым — нет.
        /// Огня при этом по-прежнему нет ни одной частицы: светится дым,
        /// а не пламя.
        /// </summary>
        private static Material BlastMaterial(Material source)
        {
            if (blastMaterial != null)
            {
                return blastMaterial;
            }

            const string path = "Assets/_Project/Materials/MemoryRun/MR_Blast.mat";
            blastMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (blastMaterial == null)
            {
                Shader shader = Shader.Find("Legacy Shaders/Particles/Additive (Soft)");
                if (shader == null)
                {
                    notes.Append("\n— шейдер частиц не найден, облако осталось на материале пака");
                    return source;
                }

                blastMaterial = new Material(shader) { name = "MR_Blast" };
                AssetDatabase.CreateAsset(blastMaterial, path);
            }

            // ⚠️ Текстуру приходится брать <b>не у дыма</b>. Дымовые материалы
            // пака идут вовсе без карты: их клубы — низкополигональные меши,
            // а не билборды, и в тёмном цеху они читаются бурыми кляксами.
            // Круглое пятно лежит в материале колец — оттуда его и берём,
            // а облако переводим в билборд. Это и есть «круглое пухлое
            // облако» из спеки, буквально.
            const string roundMat = "Assets/Synty/PolygonParticleFX/Materials/FX_Cutout_Round_01_VertexLit.mat";
            var round = AssetDatabase.LoadAssetAtPath<Material>(roundMat);
            Texture puff = round != null && round.HasProperty("_MainTex") ? round.GetTexture("_MainTex") : null;
            if (puff == null && source != null && source.HasProperty("_MainTex"))
            {
                puff = source.GetTexture("_MainTex");
            }

            if (puff != null)
            {
                blastMaterial.SetTexture("_MainTex", puff);
            }
            else
            {
                notes.Append("\n— круглая текстура частицы не найдена, облако будет квадратным");
            }

            EditorUtility.SetDirty(blastMaterial);
            return blastMaterial;
        }

        /// <summary>
        /// Подвесить эффект пака ребёнком и перевести его в ручной запуск.
        ///
        /// Автостарт с зацикливанием — общая беда эффектов пака: оставленные
        /// как есть, они играли бы вечно и с первого кадра сцены.
        /// </summary>
        private static void AttachPackEffect(Transform parent, string path, string name, Color tint, float scale,
            float size, float life, int burst)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                notes.Append("\n— эффект не найден: ").Append(path);
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            go.layer = LayerMask.NameToLayer("Default");

            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.loop = false;
                main.playOnAwake = false;
                main.startColor = tint;
                main.maxParticles = Mathf.Min(main.maxParticles, 60);

                // ⚠️ Размер и время жизни переписываются обязательно. У дыма
                // пака частица идёт <b>восемь метров</b> и живёт три секунды —
                // расчёт на горящий танк в чистом поле. В цеху такой хвост
                // накрывал собой и облако, и половину маршрута: на кадре 4.4
                // от взрыва оставался чёрный столб, а оранжевого кома не было
                // видно вовсе.
                main.startSize = size;
                main.startLifetime = life;
                main.duration = Mathf.Max(0.3f, life * 0.5f);

                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, true);
            }

            built++;
        }

        /// <summary>
        /// Образец копоти: тёмное пятно на голове и тонкий дымок над ней.
        ///
        /// Пятно сделано приплюснутой сферой вокруг головы, а не наклейкой на
        /// лицо, и это решение о надёжности. Ось «вперёд» у кости головы своя
        /// у каждого рига, и наклейка, посаженная по ней, у части персонажей
        /// уехала бы на затылок. Сфера читается копотью с любого ракурса.
        ///
        /// Дымок нужен для дистанции: пятно на лице с сорока метров не видно
        /// вовсе, а поднимающаяся ниточка дыма над головой — видна.
        /// </summary>
        private static GameObject BuildSoot(Transform parent)
        {
            var go = new GameObject("SootTemplate");
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("Default");

            var smudge = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            smudge.name = "Smudge";
            Object.DestroyImmediate(smudge.GetComponent<Collider>());
            smudge.transform.SetParent(go.transform, false);
            smudge.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            smudge.transform.localScale = new Vector3(0.30f, 0.26f, 0.30f);
            smudge.layer = LayerMask.NameToLayer("Default");

            var renderer = smudge.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SootMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Дымок — тоже эффект пака: система, созданная кодом, пришла бы
            // без URP-материала и висела бы над головой фиолетовым кубом.
            if (!DressKit.TryLoad(Fx + "FX_Smoke_Black_Small_01.prefab", out GameObject wispPrefab))
            {
                notes.Append("\n— дымок копоти не найден: FX_Smoke_Black_Small_01");
                go.SetActive(false);
                built++;
                return go;
            }

            var wisp = (GameObject)PrefabUtility.InstantiatePrefab(wispPrefab, go.transform);
            wisp.name = "Wisp";
            wisp.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            wisp.transform.localRotation = Quaternion.identity;
            wisp.transform.localScale = Vector3.one;
            wisp.layer = LayerMask.NameToLayer("Default");

            ParticleSystem system = wisp.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 1.2f;
            main.startSpeed = 0.5f;
            main.startSize = 0.22f;
            main.startColor = TailColor;
            main.gravityModifier = 0f;
            main.maxParticles = 12;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 6f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(TailColor, 0f), new GradientColorKey(TailColor, 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var wispRenderer = system.GetComponent<ParticleSystemRenderer>();
            wispRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wispRenderer.receiveShadows = false;

            // Образец лежит выключенным: включается только его копия на голове.
            go.SetActive(false);
            built++;
            return go;
        }

        private static Material sootMaterial;

        private static Material SootMaterial()
        {
            if (sootMaterial != null)
            {
                return sootMaterial;
            }

            const string path = "Assets/_Project/Materials/MemoryRun/MR_Soot.mat";
            sootMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sootMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Debug.LogError("Шейдер URP Lit не найден — копоть будет фиолетовой в сборке");
                    return null;
                }

                sootMaterial = new Material(shader) { name = "MR_Soot" };
                AssetDatabase.CreateAsset(sootMaterial, path);
            }

            Igruha.Minigames.HoleInWall.HoleInWallMaterials.ConfigureTransparent(
                sootMaterial, new Color(SootColor.r, SootColor.g, SootColor.b, 0.62f), 0.1f);
            EditorUtility.SetDirty(sootMaterial);
            return sootMaterial;
        }

        /// <summary>
        /// Повесить компонент на менеджер и вписать ссылки. Через
        /// <see cref="SerializedObject"/>, а не публичными полями: поля
        /// приватные и остаются такими — правило проекта.
        /// </summary>
        private static void WireComponent(GameObject manager, ParticleSystem[] pool, GameObject soot)
        {
            if (manager == null)
            {
                notes.Append("\n— MinigameManager не найден, эффекты не привязаны");
                return;
            }

            var effects = manager.GetComponent<MemoryRunEffects>();
            if (effects == null)
            {
                effects = manager.AddComponent<MemoryRunEffects>();
            }

            var so = new SerializedObject(effects);
            so.FindProperty("game").objectReferenceValue = manager.GetComponent<MemoryRunMinigame>();
            so.FindProperty("sootTemplate").objectReferenceValue = soot;

            SerializedProperty blasts = so.FindProperty("blasts");
            blasts.arraySize = pool.Length;
            for (int i = 0; i < pool.Length; i++)
            {
                blasts.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Отчёт подфазы: что собрано и чего не хватило.</summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("💥 «Рейс на память», эффекты 4.4");
            report.Append("\n— облаков в пуле:         ").Append(BlastPoolSize);
            report.Append("\n— собрано объектов:       ").Append(built);
            report.Append("\n— огня в эффекте:         нет ни одной частицы ✔");
            report.Append("\n— следов на плите:        нет, эффект живёт вне иерархии плиты ✔");

            if (notes.Length == 0)
            {
                report.Append("\n— замечаний:              нет ✔");
            }
            else
            {
                report.Append("\n— замечания:").Append(notes);
            }

            return report.ToString();
        }
    }
}
