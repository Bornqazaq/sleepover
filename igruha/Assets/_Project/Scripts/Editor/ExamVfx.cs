using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты «Экзамена» — подфаза 4.4. Ставит системы частиц на арену
    /// и перецепляет на них runtime-компонент <see cref="ExamEffects"/>.
    ///
    /// <b>Эффект ставится рядом с объектом, а не внутрь его подвижных частей.</b>
    /// Пыль шва живёт на платформе, а не на створке: створка уезжает вниз
    /// на 110°, и вложенный в неё партикл уехал бы вместе с ней ровно в тот
    /// момент, ради которого его и ставили.
    ///
    /// <b>Партиклы пака переводятся в ручной запуск.</b> Все FX Synty приходят
    /// с <c>playOnAwake</c> и зацикливанием: оставь как есть — и пыль будет
    /// висеть над платформами весь матч.
    ///
    /// <b>Размер частицы считается от дистанции кадра, а не копируется.</b>
    /// «Верю / не верю» убавляла пыль пака с 0.15 до 0.025 — там камера стоит
    /// в полутора метрах от стола, и родная частица была хлопьями в ладонь.
    /// Здесь камера зала в пятнадцати метрах от платформ, и на 0.05 м пыль
    /// вышла невидимой: рендер приёмки показал десяток белых точек вместо
    /// облака. Взято 0.22 — крупнее родной, потому что и сцена крупнее.
    /// </summary>
    internal static class ExamVfx
    {
        private const string DustPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_Dust_Small_01.prefab";
        private const string GlowPath = "Assets/Synty/PolygonParticleFX/Prefabs/FX_GlowSpot_02.prefab";

        /// <summary>
        /// Размер частицы пыли. Подобран под дистанцию камеры зала — пятнадцать
        /// метров до платформ, — а не по чужому числу из другой игры.
        /// </summary>
        private const float DustSize = 0.22f;

        private static readonly List<string> Notes = new List<string>(4);
        private static int systems;
        private static int capacity;

        /// <summary>
        /// Поставить эффекты и связать их с платформами. Вызывается из
        /// пересборки арены: всё, что не воспроизводится ею, теряется при
        /// первом же слиянии веток.
        /// </summary>
        internal static void Build(GameObject arena, ExamConfig config)
        {
            if (arena == null || config == null)
            {
                return;
            }

            Notes.Clear();
            systems = 0;
            capacity = 0;

            Transform root = Group(arena.transform, "Effects");

            var component = arena.GetComponent<ExamEffects>();
            if (component == null)
            {
                component = arena.AddComponent<ExamEffects>();
            }

            var so = new SerializedObject(component);
            WireSide(so, "sideA", arena, root, ExamSide.A, config);
            WireSide(so, "sideB", arena, root, ExamSide.B, config);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);

            BuildPit(root, config);
        }

        /// <summary>
        /// Эффекты одной платформы. Три системы на сторону — ровно столько же,
        /// сколько у соседней: разница в числе или яркости эффектов у А и Б
        /// была бы такой же подсказкой, как разница в реквизите.
        /// </summary>
        private static void WireSide(SerializedObject so, string field, GameObject arena, Transform root,
            ExamSide side, ExamConfig config)
        {
            string platformName = side == ExamSide.A ? "Platform_A" : "Platform_B";
            Transform platform = arena.transform.Find(platformName);
            if (platform == null)
            {
                Note($"на арене нет платформы {platformName}");
                return;
            }

            Transform group = Group(root, side == ExamSide.A ? "SideA" : "SideB");
            Vector3 centre = platform.position;

            // Шов: узкая полоса ровно по линии, где расходятся половины.
            ParticleSystem seam = Dust(group, "SeamDust", centre + new Vector3(0f, 0.05f, 0f),
                new Vector3(0.35f, 0.05f, config.PlatformDepth * 0.95f),
                new Vector3(0f, 0.9f, 0f), 95, 1.1f, 1.6f);

            // Труха по периметру проёма, сыплющаяся вниз. Скорость направлена
            // в яму: это то, что делает провал провалом, а не исчезновением.
            ParticleSystem rim = Dust(group, "RimDust", centre + new Vector3(0f, -0.15f, 0f),
                new Vector3(config.PlatformWidth * 0.98f, 0.1f, config.PlatformDepth * 0.98f),
                new Vector3(0f, -2.2f, 0f), 80, 1.4f, 2.2f);

            // Хлопок при закрытии: короче и выше, чем труха.
            ParticleSystem close = Dust(group, "CloseDust", centre + new Vector3(0f, 0.06f, 0f),
                new Vector3(config.PlatformWidth * 0.98f, 0.05f, config.PlatformDepth * 0.98f),
                new Vector3(0f, 0.6f, 0f), 65, 0.8f, 1.2f);

            var bands = new List<Renderer>(4);
            Transform bandGroup = platform.Find("HatchBand");
            if (bandGroup != null)
            {
                foreach (Renderer band in bandGroup.GetComponentsInChildren<Renderer>(true))
                {
                    bands.Add(band);
                }
            }
            else
            {
                Note($"у {platformName} нет рамки люка — кромке нечем вспыхивать");
            }

            SerializedProperty entry = so.FindProperty(field);
            if (entry == null)
            {
                Note($"у ExamEffects нет поля {field}");
                return;
            }

            entry.FindPropertyRelative("Platform").objectReferenceValue = platform.GetComponent<ExamAnswerPlatform>();
            entry.FindPropertyRelative("SeamDust").objectReferenceValue = seam;
            entry.FindPropertyRelative("RimDust").objectReferenceValue = rim;
            entry.FindPropertyRelative("CloseDust").objectReferenceValue = close;

            SerializedProperty bandList = entry.FindPropertyRelative("EdgeBands");
            bandList.arraySize = bands.Count;
            for (int i = 0; i < bands.Count; i++)
            {
                bandList.GetArrayElementAtIndex(i).objectReferenceValue = bands[i];
            }
        }

        /// <summary>
        /// Яма: слабое свечение дна и медленная взвесь.
        ///
        /// Свет здесь несёт смысл, а не украшает. Спека требует, чтобы внизу
        /// читалась темнота, а не дыра в мире: у совсем чёрного проёма нет
        /// глубины, и провалившийся не падает, а пропадает. Тусклое дно
        /// возвращает провалу дно.
        ///
        /// Обе системы зациклены и играют всегда: у ямы нет события, она
        /// просто есть.
        /// </summary>
        private static void BuildPit(Transform root, ExamConfig config)
        {
            Transform group = Group(root, "Pit");
            float platformsZ = config.HallDepth * 0.5f - config.PodiumDepth - 2.16f - config.PlatformDepth * 0.5f;
            float width = config.PlatformWidth * 2f + config.PlatformGap;

            ParticleSystem glow = Spawn(group, "PitGlow", GlowPath,
                new Vector3(0f, -config.PitDepth + 0.6f, platformsZ));
            if (glow != null)
            {
                var main = glow.main;
                main.playOnAwake = true;
                main.loop = true;
                main.startSize = 3.2f;
                main.startLifetime = 4f;
                main.startSpeed = 0.12f;
                main.startColor = new Color(0.58f, 0.64f, 0.80f, 0.34f);
                main.maxParticles = 16;

                var emission = glow.emission;
                emission.rateOverTime = 3f;
                emission.SetBursts(new ParticleSystem.Burst[0]);

                var shape = glow.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(width * 0.8f, 0.2f, config.PlatformDepth * 0.8f);

                capacity += main.maxParticles;
                systems++;
            }

            // Слабый источник на дне. Без него яма — не темнота, а чёрная
            // заливка: у стенок нет градиента, и глаз не читает глубину.
            // Сама по себе лампа ничего не освещает наверху — её радиус
            // кончается под полом зала.
            var lampGo = new GameObject("PitLight");
            lampGo.transform.SetParent(group, false);
            lampGo.transform.position = new Vector3(0f, -config.PitDepth + 2.4f, platformsZ);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = config.PitDepth * 1.3f;
            lamp.intensity = 2.8f;
            lamp.color = new Color(0.62f, 0.70f, 0.88f);
            lamp.shadows = LightShadows.None;

            ParticleSystem haze = Dust(group, "PitHaze",
                new Vector3(0f, -config.PitDepth * 0.55f, platformsZ),
                new Vector3(width * 0.9f, config.PitDepth * 0.7f, config.PlatformDepth * 0.9f),
                new Vector3(0f, 0.15f, 0f), 0, 6f, 8f);
            if (haze == null)
            {
                return;
            }

            var hazeMain = haze.main;
            hazeMain.playOnAwake = true;
            hazeMain.loop = true;
            hazeMain.startSize = 0.6f;
            hazeMain.startColor = new Color(0.2f, 0.21f, 0.25f, 0.25f);
            hazeMain.maxParticles = 24;

            var hazeEmission = haze.emission;
            hazeEmission.rateOverTime = 4f;
            hazeEmission.SetBursts(new ParticleSystem.Burst[0]);
        }

        /// <summary>
        /// Пылевая система с ручным запуском: пак приходит зацикленным
        /// и играющим с первого кадра, а нам нужен один залп по событию.
        /// </summary>
        private static ParticleSystem Dust(Transform parent, string name, Vector3 position, Vector3 box,
            Vector3 velocity, int burst, float duration, float lifetime)
        {
            ParticleSystem effect = Spawn(parent, name, DustPath, position);
            if (effect == null)
            {
                return null;
            }

            var main = effect.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = lifetime;
            main.startSize = DustSize;
            main.startSpeed = 0f;
            main.startColor = new Color(0.78f, 0.74f, 0.68f, 0.75f);
            main.gravityModifier = 0.08f;
            main.maxParticles = Mathf.Max(burst * 2, 32);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = effect.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(burst > 0
                ? new[] { new ParticleSystem.Burst(0f, (short)burst) }
                : new ParticleSystem.Burst[0]);

            var shape = effect.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;

            var velocityModule = effect.velocityOverLifetime;
            velocityModule.enabled = true;
            velocityModule.space = ParticleSystemSimulationSpace.Local;
            velocityModule.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            velocityModule.y = new ParticleSystem.MinMaxCurve(velocity.y * 0.6f, velocity.y);
            velocityModule.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            capacity += main.maxParticles;
            systems++;
            return effect;
        }

        private static ParticleSystem Spawn(Transform parent, string name, string path, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Note($"эффект не найден: {path}");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = position;
            go.transform.localRotation = Quaternion.identity;

            // Коллайдеры срезаются и здесь. Эффект, ловящий броски и толчки, —
            // это не эффект, а невидимая преграда посреди арены.
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, true);
            }

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            SetLayer(go, LayerMask.NameToLayer("Default"));

            var effect = go.GetComponentInChildren<ParticleSystem>(true);
            if (effect == null)
            {
                Note($"у {name} нет системы частиц");
            }

            return effect;
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            return group;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayer(go.transform.GetChild(i).gameObject, layer);
            }
        }

        private static void Note(string text)
        {
            if (!Notes.Contains(text))
            {
                Notes.Add(text);
            }
        }

        /// <summary>Сколько систем поставлено и какой у них суммарный потолок частиц.</summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("✨ «Экзамен», эффекты 4.4 — ")
                .Append(systems).Append(" систем частиц, потолок ").Append(capacity).Append(" частиц");
            report.Append("\n   новых сетевых событий 0: эффекты смотрят на створки, которые игра уже раскрыла у всех");

            foreach (string note in Notes)
            {
                report.Append("\n⚠️ ").Append(note);
            }

            report.Append("\nЗамечаний эффектов: ").Append(Notes.Count);
            return report.ToString();
        }
    }
}
