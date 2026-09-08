using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;
using Entry = Igruha.EditorTools.DressKit.Entry;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Общие руки построителей павильона «Дырки в стене»: плита, предмет пака,
    /// посадка на пол, габарит по рендерерам и правила декорации.
    ///
    /// <b>Вынесены сюда, а не оставлены у павильона, потому что построителей
    /// стало четыре.</b> Павильон (<see cref="HoleInWallEnvironment"/>), зал
    /// (<see cref="HoleInWallStands"/>), оформление шоу
    /// (<see cref="HoleInWallShow"/>) и декор (<see cref="HoleInWallDecor"/>)
    /// ставят предметы по одним и тем же правилам — без коллайдера, на
    /// <c>Default</c>, без отброса теней, перекрашенными в палитру игры.
    /// Разъехавшаяся копия любого из этих правил ломается молча: коллайдер
    /// в декоре ловит сметённого игрока, чужой слой цепляет деокклюдер камеры,
    /// а собственный материал пака возвращает ссылку на <c>Assets/Synty/**</c>,
    /// ради снятия которой ветка не вливалась шесть подфаз.
    ///
    /// Разбор, почему каждое правило именно такое, — в шапке
    /// <see cref="HoleInWallEnvironment"/>; здесь только исполнение.
    /// </summary>
    internal static class HoleInWallProps
    {
        /// <summary>Верх бортика бассейна: по нему выложен пол студии.</summary>
        internal static float RimTopY(HoleInWallConfig config) =>
            config.PoolBottomY + config.PoolDepth + HoleInWallArenaBuilder.PoolRimHeight;

        /// <summary>
        /// Разброс в пределах ±<paramref name="amount"/>. Своим генератором,
        /// а не <see cref="Random"/> движка: посев декора задан числом, и зал
        /// обязан стоять одинаково у обоих разработчиков и в раздатке.
        /// </summary>
        internal static float Jitter(System.Random rng, float amount) =>
            (float)(rng.NextDouble() * 2.0 - 1.0) * amount;

        internal static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>
        /// Плита окружения: без коллайдера, на <c>Default</c> и без отброса
        /// теней. Все три свойства обязательны, и ни одно из них не косметика —
        /// разбор в шапке файла.
        /// </summary>
        internal static GameObject Slab(Transform parent, string slabName, Vector3 size, Vector3 centre,
            Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = slabName;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            MarkAsScenery(go);
            return go;
        }

        /// <summary>Плита окружения, одетая моделью пака через общий <see cref="DressKit"/>.</summary>
        internal static void DressedSlab(Transform parent, string slabName, Vector3 size, Vector3 centre,
            Tone paint, Entry entry, System.Random rng)
        {
            GameObject box = Slab(parent, slabName, size, centre, HoleInWallPaletteAssets.Get(paint));
            GameObject dress = DressKit.Apply(box, new[] { entry }, rng, HoleInWallPaletteAssets.Get(paint));
            if (dress != null)
            {
                MarkAsScenery(dress);
            }
        }

        /// <summary>Предмет, подвешенный за свою точку крепления: софит на ферме.</summary>
        internal static GameObject HangProp(Transform parent, string propName, string prefabPath, Vector3 position,
            float yaw, Tone paint)
        {
            GameObject go = SpawnProp(parent, propName, prefabPath, yaw, paint);
            if (go != null)
            {
                go.transform.position = position;
            }

            return go;
        }

        /// <summary>
        /// Предмет, поставленный на пол: нижняя грань его габарита садится
        /// ровно на заданную высоту, центр — в заданную точку по горизонтали.
        /// Замером, а не отступом на глаз: у моделей паков опорная точка стоит
        /// то в центре, то в основании, и предмет, поставленный по опорной
        /// точке, у половины моделей повисает в воздухе.
        /// </summary>
        internal static GameObject SeatProp(Transform parent, string propName, string prefabPath, Vector3 ground,
            float yaw, Tone paint)
        {
            GameObject go = SpawnProp(parent, propName, prefabPath, yaw, paint);
            if (go == null || !TryWorldBounds(go, out Bounds bounds))
            {
                return go;
            }

            Vector3 shift = new Vector3(ground.x - bounds.center.x, ground.y - bounds.min.y, ground.z - bounds.center.z);
            go.transform.position += shift;
            return go;
        }

        /// <summary>
        /// Поставить модель пака и перекрасить её тоном палитры.
        ///
        /// Перекраска здесь не украшательство: трибуна приезжает из «Карнавала»
        /// в ярмарочной раскраске, штатив из «Магазинов» — в своей, и в тёмной
        /// студии каждый такой предмет кричал бы громче арены. Правило то же,
        /// что у дресса на 4.2: цвет в кадре назначает палитра, а не атлас
        /// пака, из которого предмет приехал.
        /// </summary>
        internal static GameObject SpawnProp(Transform parent, string propName, string prefabPath, float yaw,
            Tone paint)
        {
            if (!DressKit.TryLoad(prefabPath, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = propName;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            MarkAsScenery(go);
            DressKit.Repaint(go, HoleInWallPaletteAssets.Get(paint));
            return go;
        }

        /// <summary>Габарит предмета по его рендерерам, в мировых координатах.</summary>
        internal static bool TryWorldBounds(GameObject go, out Bounds bounds)
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

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры, увести на
        /// <c>Default</c>, погасить отброс теней и включить пакетную отрисовку.
        /// </summary>
        internal static void MarkAsScenery(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            int layer = LayerMask.NameToLayer("Default");
            SetLayer(go, layer);

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        internal static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }
    }
}
