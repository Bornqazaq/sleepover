using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.Traps;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты «Переноски предмета» — подфаза 4.4. Пак — <b>POLYGON Particle FX</b>.
    ///
    /// Здесь только сборка: какие эффекты завести, где их положить и чем
    /// перекрасить. Когда они играют — дело <see cref="CarryItemEffects"/>,
    /// и он висит на событиях, которые игра уже поднимает. <c>Core/</c> на этой
    /// подфазе не тронут ни строкой, новых сообщений по сети нет.
    ///
    /// <b>Партиклы пака переводятся в ручной запуск.</b> У них у всех
    /// <c>playOnAwake</c> и почти у всех зацикливание: оставь как есть — и
    /// арена с первого кадра стоит в брызгах. Исключение ровно два, и оба
    /// постоянные по замыслу: струя прорванной трубы и пыль стройки.
    ///
    /// <b>Пул лежит на арене, а не на бутыли.</b> Бутыль исчезает в тот же
    /// кадр, в который сливается: вложенный в неё всплеск погас бы ровно в тот
    /// момент, ради которого его ставили.
    /// </summary>
    internal static class CarryItemVfx
    {
        private const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";

        /// <summary>Сколько копий держит каждый пул. Больше — на восьмерых эффекты обрывают сами себя.</summary>
        private const int PoolSize = 4;

        /// <summary>Куда прячется неигранный эффект: под пол, чтобы не мозолить глаза в редакторе.</summary>
        private static readonly Vector3 Parking = new Vector3(0f, -50f, 0f);

        internal static void Build(Transform arena, CarryItemConfig config, GameObject manager,
            BottleStack[] stacks, TrapBase cart, Transform pipe, Transform beam)
        {
            Transform group = ResetGroup(arena, "Effects");

            // Масштабы подобраны замером высоты разлёта, а не на глаз: эффекты
            // пака рассчитаны на открытое поле, и в родном размере всплеск
            // уходил на пять метров, а облако тачки — на восемь с половиной.
            // Над маршрутом это стена брызг вместо обратной связи.
            ParticleSystem[] splashes = Pool(group, "Splash", Fx + "FX_Impact_Water_01.prefab", 0.55f);
            ParticleSystem[] sprays = Pool(group, "Spray", Fx + "FX_Impact_Water_Ripple_01.prefab", 0.34f);
            ParticleSystem[] dusts = Pool(group, "Dust", Fx + "FX_Impact_Stone_01.prefab", 0.45f);
            ParticleSystem[] swooshes = Pool(group, "Swoosh", Fx + "FX_Slash_01.prefab", 1.1f);

            ParticleSystem cartBurst = Spawn(group, "CartBurst", Fx + "FX_Impact_Dirt_01.prefab", 0.55f);
            if (cartBurst != null && cart != null)
            {
                cartBurst.transform.position = cart.transform.position;
            }

            BuildPipeJet(group, config, pipe);
            BuildBeamTrail(group, beam);
            BuildHaze(group, config);

            Wire(manager, stacks, cart, splashes, sprays, dusts, swooshes, cartBurst);
        }

        /// <summary>
        /// Струя прорванной трубы. Единственный эффект игры, который идёт
        /// постоянно и по замыслу зациклен: труба течёт весь раунд.
        ///
        /// Ставится у излома и бьёт поперёк маршрута — туда же, куда толкает
        /// зона. До 4.4 объём струи рисовался мешевыми слоями, и они остаются
        /// подложкой: частицы показывают воду, слои — где именно толкает.
        /// </summary>
        private static void BuildPipeJet(Transform group, CarryItemConfig config, Transform pipe)
        {
            if (pipe == null)
            {
                return;
            }

            ParticleSystem jet = Spawn(group, "PipeJet", Fx + "FX_WaterDrip_01.prefab", 1.3f,
                keepLoop: true, keepAwake: true);
            if (jet == null)
            {
                return;
            }

            float edgeZ = config.NeckWidth * 0.5f - 1.1f;
            jet.transform.position = new Vector3(
                pipe.position.x, config.ToMeters(1f), config.ToMeters(edgeZ));

            // Смотрит поперёк прохода, в сторону осевой: вода идёт от излома
            // к середине горлышка, а не вдоль маршрута.
            jet.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        }

        /// <summary>
        /// Пыльный шлейф за балкой. Событие срабатывания у неё есть только
        /// косвенное — через потерю воды, — а телеграф ловушке нужен по спеке
        /// 8.5: шлейф идёт всё время и показывает, где балка сейчас.
        /// </summary>
        private static void BuildBeamTrail(Transform group, Transform beam)
        {
            if (beam == null)
            {
                return;
            }

            ParticleSystem trail = Spawn(group, "BeamTrail", Fx + "FX_Trail_Dust_01.prefab", 0.7f,
                keepLoop: true, keepAwake: true);
            if (trail == null)
            {
                return;
            }

            // Ребёнок самой балки: она крутится вокруг центра горлышка, и шлейф
            // обязан ехать вместе с ней. Поставь рядом — балка уйдёт одна.
            trail.transform.SetParent(beam, false);
            trail.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            trail.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Пыль стройки над перекрытием. Держится низко — выше 3 ШИ вдоль
        /// маршрута нельзя ничего, — и редкой: густая взвесь на низкополигональных
        /// частицах читается хлопьями, а не воздухом (урок «Дырки в стене», 3.43).
        /// </summary>
        private static void BuildHaze(Transform group, CarryItemConfig config)
        {
            var spots = new[]
            {
                new Vector3(-26f, 0.5f, 0f),
                new Vector3(3f, 0.5f, 0f),
                new Vector3(28f, 0.5f, 0f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                ParticleSystem haze = Spawn(group, $"Haze_{i + 1}", Fx + "FX_Dust_Small_01.prefab", 0.85f,
                    keepLoop: true, keepAwake: true);
                if (haze == null)
                {
                    continue;
                }

                haze.transform.position = new Vector3(
                    config.ToMeters(spots[i].x), config.ToMeters(spots[i].y), config.ToMeters(spots[i].z));

                ParticleSystem.MainModule main = haze.main;
                main.startColor = new Color(0.86f, 0.84f, 0.78f, 0.28f);

                // Пыль прижата к полу: в родном виде она поднималась на шесть
                // метров, то есть висела ровно между игроком и всем, на что он
                // смотрит. Медленнее и короче живёт — и остаётся взвесью под
                // ногами, а не туманом в кадре.
                main.startSpeed = 0.35f;
                main.startLifetime = 2.2f;
                main.gravityModifier = 0.04f;
            }
        }

        /// <summary>Пул одинаковых эффектов: играются по кругу, стоят под полом, пока не нужны.</summary>
        private static ParticleSystem[] Pool(Transform group, string kind, string prefabPath, float scale)
        {
            var pool = new ParticleSystem[PoolSize];
            Transform holder = ResetGroup(group, kind);

            for (int i = 0; i < PoolSize; i++)
            {
                pool[i] = Spawn(holder, $"{kind}_{i + 1}", prefabPath, scale);
                if (pool[i] != null)
                {
                    pool[i].transform.position = Parking;
                }
            }

            return pool;
        }

        /// <summary>
        /// Поставить эффект пака и перевести его в ручной запуск.
        ///
        /// Автостарт с зацикливанием — состояние по умолчанию у всех партиклов
        /// пака: без этой правки арена стоит в брызгах с первого кадра, а
        /// эффект события никогда не «начинается».
        /// </summary>
        private static ParticleSystem Spawn(Transform parent, string effectName, string prefabPath, float scale,
            bool keepLoop = false, bool keepAwake = false)
        {
            if (!DressKit.TryLoad(prefabPath, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = effectName;
            go.transform.localScale = Vector3.one * scale;

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.playOnAwake = keepAwake;
                main.loop = keepLoop;

                // Без этого масштаб корня не доходит до вложенных систем.
                // У эффектов пака scalingMode стоит Local: дочерняя система
                // берёт только свой трансформ и родителя не слушает вовсе —
                // уменьшенный вдвое эффект продолжал разлетаться на прежние
                // метры, и замер высоты не двигался ни на сантиметр, сколько
                // масштаб ни правь.
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            MarkAsEffect(go);
            return go.GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// Пометить эффект: без коллайдеров, без света, без теней, на
        /// <c>Default</c> и без статической пакетной отрисовки.
        ///
        /// Последнее не мелочь: у окружения статичность включена, а помеченный
        /// статичным партикл Unity вмораживает в общий меш вместе с его текущим
        /// положением — переставить его перед запуском уже нельзя, а весь пул
        /// на том и держится.
        /// </summary>
        private static void MarkAsEffect(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            var lights = go.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Object.DestroyImmediate(lights[i], true);
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            SetLayer(go, LayerMask.NameToLayer("Default"));
            GameObjectUtility.SetStaticEditorFlags(go, 0);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }

        private static void Wire(GameObject manager, BottleStack[] stacks, TrapBase cart,
            ParticleSystem[] splashes, ParticleSystem[] sprays, ParticleSystem[] dusts,
            ParticleSystem[] swooshes, ParticleSystem cartBurst)
        {
            if (manager == null)
            {
                return;
            }

            var effects = manager.GetComponent<CarryItemEffects>();
            if (effects == null)
            {
                effects = manager.AddComponent<CarryItemEffects>();
            }

            var so = new SerializedObject(effects);
            FillArray(so.FindProperty("stacks"), stacks);
            FillArray(so.FindProperty("splashes"), splashes);
            FillArray(so.FindProperty("sprays"), sprays);
            FillArray(so.FindProperty("dusts"), dusts);
            FillArray(so.FindProperty("swooshes"), swooshes);
            FillArray(so.FindProperty("traps"), cart != null ? new Object[] { cart } : new Object[0]);
            so.FindProperty("cartBurst").objectReferenceValue = cartBurst;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static Transform ResetGroup(Transform parent, string groupName)
        {
            Transform existing = parent.Find(groupName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(groupName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
