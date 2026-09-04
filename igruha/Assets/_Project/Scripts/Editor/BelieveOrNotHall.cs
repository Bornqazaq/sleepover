using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Зал «Верю / не верю» — подфаза 4.3: всё, что стоит за кругом света.
    /// Бархат по стенам, барная стойка, мягкая мебель, картины и тёплые
    /// точки света, на которых зал читается силуэтами.
    ///
    /// <b>Восток и запад — да, север и юг — нет.</b> Сидящие смотрят вдоль
    /// оси Z, значит фоном геройского кадра работают стены North и South:
    /// всё, что на них поставить, встанет ровно за лицом соперника. Поэтому
    /// там только бархат, а бар, диваны и картины — на West и East. Это не
    /// вкус, это единственное требование брифа к планировке зала (14.7).
    ///
    /// <b>Ни одного коллайдера.</b> Вокруг стола весь кон бегают и дерутся
    /// шестеро; коллайдер на диване или на бутылке ловил бы их телами, а
    /// толчок в декорацию — это уже не декорация. Всё окружение уходит на
    /// <c>Default</c>: слоя нет в маске деоклюдера, и камера сквозь него
    /// проходит, вместо того чтобы нырять зрителю в затылок.
    ///
    /// <b>Свободная зона зрителей неприкосновенна.</b> Спека даёт радиус
    /// 10 ШП (7.2 м), внутри которого пусто и ровно. Каждый предмет проверяется
    /// по расстоянию до центра стола, и пересборка печатает ближайший:
    /// «не залезло» обязано быть числом.
    /// </summary>
    internal static class BelieveOrNotHall
    {
        private const string Casino = "Assets/Synty/PolygonCasino/Prefabs/";
        private const string Nightclubs = "Assets/Synty/PolygonNightclubs/Prefabs/";

        private const string CurtainPath = Casino + "Buildings/SM_Bld_Curtain_Closed_01.prefab";
        private const string BarPath = Nightclubs + "Props/Modular/SM_Prop_Bar_01.prefab";
        private const string MinibarPath = Casino + "Props/SM_Prop_Minibar_01.prefab";
        private const string StoolPath = Casino + "Props/SM_Prop_Bar_Stool_01.prefab";
        private const string BottlePath = Casino + "Props/SM_Prop_Bar_Bottle_02.prefab";
        private const string GlassPath = Casino + "Props/SM_Prop_Bar_Glass_02.prefab";
        private const string BucketPath = Casino + "Props/SM_Prop_Champagne_Bucket_02.prefab";
        private const string SofaPath = Casino + "Props/SM_Prop_Sofa_01.prefab";
        private const string CouchPath = Casino + "Props/SM_Prop_Couch_Suite_01.prefab";
        private const string BenchPath = Casino + "Props/SM_Prop_Bench_Pew_01.prefab";
        private const string LowTablePath = Casino + "Props/SM_Prop_Table_Suite_01.prefab";
        private const string WallArtPath = Casino + "Props/SM_Prop_Wall_Art_02.prefab";
        private const string WallArtAltPath = Casino + "Props/SM_Prop_Wall_Art_01.prefab";

        private const string HallRoot = "_Hall";

        /// <summary>Сколько полотнищ шторы приходится на одну стену.</summary>
        private const int CurtainsPerWall = 6;

        /// <summary>Полотнищ с каждого края восточной и западной стен; середина отдана бару и диванам.</summary>
        private const int CurtainsAtEnds = 2;

        /// <summary>Отступ предмета от плоскости стены, м: вплотную модель проваливается в стену углами.</summary>
        private const float WallGap = 0.06f;

        private static readonly List<string> notes = new List<string>(8);
        private static float nearest;
        private static int props;

        /// <summary>
        /// Построить зал. <paramref name="wallFace"/> — расстояние от центра
        /// до внутренней плоскости стены.
        /// </summary>
        internal static void Build(Transform arena, BelieveOrNotConfig config, float wallFace)
        {
            if (arena == null || config == null)
            {
                return;
            }

            notes.Clear();
            nearest = float.MaxValue;
            props = 0;

            var root = new GameObject(HallRoot);
            root.transform.SetParent(arena, false);
            Transform hall = root.transform;

            Curtains(hall, config, wallFace);
            Bar(hall, wallFace);
            Lounge(hall, wallFace);
            Lights(hall);

            notes.Add($"предметов окружения {props}, ближайший к центру {nearest:F2} м " +
                      $"при свободной зоне {config.SpectatorZoneRadius:F2} м");
        }

        /// <summary>Замеры зала для отчёта пересборки.</summary>
        internal static IReadOnlyList<string> Notes
        {
            get { return notes; }
        }

        // ========== БАРХАТ ==========

        /// <summary>
        /// Шторы по стенам. Север и юг закрыты целиком, у востока и запада
        /// занавешены только края: середина отдана бару и диванам.
        ///
        /// Полотнище пака — 5.00 × 5.94 м, и оно подгоняется под стену
        /// по обеим осям: шесть штук ровно закрывают 24.48 м, а по высоте
        /// ткань садится с потолка на пол. Растяжение выходит 0.816 против
        /// 0.848 — разницы в три процента на складках не видно.
        /// </summary>
        private static void Curtains(Transform hall, BelieveOrNotConfig config, float wallFace)
        {
            float span = config.HallWidth / CurtainsPerWall;
            var scale = new Vector3(span / 5.00f, config.CeilingHeight / 5.94f, config.CeilingHeight / 5.94f);
            var group = Group(hall, "Curtains");

            for (int i = 0; i < CurtainsPerWall; i++)
            {
                float offset = -config.HallWidth * 0.5f + span * (i + 0.5f);
                bool atEnd = i < CurtainsAtEnds || i >= CurtainsPerWall - CurtainsAtEnds;

                Prop(group, "Curtain_N", CurtainPath, new Vector3(offset, 0f, wallFace - WallGap), 180f, scale);
                Prop(group, "Curtain_S", CurtainPath, new Vector3(offset, 0f, -wallFace + WallGap), 0f, scale);

                if (!atEnd)
                {
                    continue;
                }

                Prop(group, "Curtain_E", CurtainPath, new Vector3(wallFace - WallGap, 0f, offset), 270f, scale);
                Prop(group, "Curtain_W", CurtainPath, new Vector3(-wallFace + WallGap, 0f, offset), 90f, scale);
            }

            notes.Add($"шторы: {CurtainsPerWall * 2 + CurtainsAtEnds * 4} полотнищ, " +
                      $"полотнище {span:F2} × {config.CeilingHeight:F2} м");
        }

        // ========== БАР ==========

        /// <summary>
        /// Барная зона у восточной стены: стойка из трёх модулей, две тумбы
        /// за ней, табуреты, бутылки и ведро со льдом.
        ///
        /// Стойка стоит фронтом в зал и на два с лишним метра дальше границы
        /// свободной зоны: зритель, отбежавший от стола, упирается не в неё,
        /// а в пустой пол.
        /// </summary>
        private static void Bar(Transform hall, float wallFace)
        {
            Transform group = Group(hall, "Bar");
            const float counterX = 10.6f;
            const float counterWidth = 2.53f;
            const float counterTop = 1.06f;

            for (int i = -1; i <= 1; i++)
            {
                Prop(group, "BarCounter", BarPath, new Vector3(counterX, 0f, i * counterWidth), 90f);
            }

            Prop(group, "BackBar", MinibarPath, new Vector3(wallFace - 1.05f, 0f, 2.2f), 270f);
            Prop(group, "BackBar", MinibarPath, new Vector3(wallFace - 1.05f, 0f, -2.2f), 270f);

            for (int i = -1; i <= 1; i++)
            {
                Prop(group, "Stool", StoolPath, new Vector3(counterX - 1.1f, 0f, i * 2.2f), 90f);
            }

            // Мелочь на стойке. Она ниже кромки и видна только силуэтом
            // на просвет, но именно от неё стойка перестаёт быть бруском.
            Prop(group, "Bottle", BottlePath, new Vector3(counterX + 0.15f, counterTop, -1.9f), 20f);
            Prop(group, "Bottle", BottlePath, new Vector3(counterX + 0.15f, counterTop, -1.6f), 200f);
            Prop(group, "Glass", GlassPath, new Vector3(counterX - 0.1f, counterTop, 0.4f), 0f);
            Prop(group, "Glass", GlassPath, new Vector3(counterX - 0.1f, counterTop, 0.7f), 120f);
            Prop(group, "Bucket", BucketPath, new Vector3(counterX, counterTop, 2.1f), 0f);

            Prop(group, "WallArt", WallArtPath, new Vector3(wallFace - WallGap, 0.3f, 4.9f), 270f);
            Prop(group, "WallArt", WallArtPath, new Vector3(wallFace - WallGap, 0.3f, -4.9f), 270f);
        }

        // ========== МЯГКАЯ ЗОНА ==========

        /// <summary>
        /// Западная стена: диваны, банкетки, низкие столики и картины
        /// в тяжёлых рамах — место, где зритель «дома».
        /// </summary>
        private static void Lounge(Transform hall, float wallFace)
        {
            Transform group = Group(hall, "Lounge");

            Prop(group, "Sofa", SofaPath, new Vector3(-10.6f, 0f, -2.4f), 90f);
            Prop(group, "Couch", CouchPath, new Vector3(-10.4f, 0f, 2.6f), 90f);
            Prop(group, "Bench", BenchPath, new Vector3(-10.9f, 0f, -7.4f), 90f);
            Prop(group, "Bench", BenchPath, new Vector3(-10.9f, 0f, 7.4f), 90f);

            Prop(group, "LowTable", LowTablePath, new Vector3(-8.9f, 0f, -2.4f), 0f);
            Prop(group, "LowTable", LowTablePath, new Vector3(-8.9f, 0f, 2.6f), 0f);

            // На стене — золочёный вензель, а не картины пака.
            //
            // Картины пробовались первыми: спека прямо просит «картины
            // в тяжёлых рамах» (4.7). Но холсты Casino — крупная абстракция
            // в жёлтом, бирюзовом и малиновом, и в зале, где светлое пятно
            // обязано быть ровно одно, они стали вторым: рендер 04.09 показал
            // две светящиеся рамы прямо за коробками. Перекрасить их нельзя —
            // холст и рама сидят в одном атласе, и заливка превратила бы
            // картину в тёмную плиту. Вензель даёт ту же «дорогую стену»
            // и не спорит с лампой.
            Prop(group, "WallArt", WallArtAltPath, new Vector3(-wallFace + WallGap, 0.3f, -4.9f), 90f);
            Prop(group, "WallArt", WallArtAltPath, new Vector3(-wallFace + WallGap, 0.3f, 4.9f), 90f);
        }

        // ========== СВЕТ ЗАЛА ==========

        /// <summary>
        /// Тёплые точки над баром и мягкой зоной.
        ///
        /// Зачем они, если зал по спеке тёмный: без них восток и запад
        /// проваливаются в ту же черноту, что север и юг, и предметы,
        /// которые мы только что поставили, в кадре не существуют. Светят
        /// они втрое слабее лампы над столом и не достают до неё — светлое
        /// пятно в кадре обязано остаться ровно одно.
        ///
        /// Ставятся кодом, а не инспектором: настройки света в YAML сцены
        /// не переживают слияние веток.
        /// </summary>
        private static void Lights(Transform hall)
        {
            Transform group = Group(hall, "Lights");
            var warm = new Color(1f, 0.82f, 0.62f);

            Lamp(group, "BarLight", new Vector3(10.4f, 3.2f, 2.3f), warm, 2.5f, 7f);
            Lamp(group, "BarLight", new Vector3(10.4f, 3.2f, -2.3f), warm, 2.5f, 7f);
            Lamp(group, "LoungeLight", new Vector3(-10.0f, 3.0f, 0f), warm, 2.0f, 8f);

            notes.Add("свет зала: две точки над баром (2.5), одна над диванами (2.0), тени выключены");
        }

        private static void Lamp(Transform parent, string name, Vector3 position, Color color, float intensity,
            float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;

            // Тени от заполняющего света стоят дорого и ничего не добавляют:
            // предметы у стен и так читаются силуэтом.
            light.shadows = LightShadows.None;
        }

        // ========== ОБЩЕЕ ==========

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// Поставить предмет пака по замеру его собственных габаритов:
        /// центр пятна — в заданную точку, низ — на заданную высоту.
        ///
        /// Именно по замеру, а не по пивоту: у моделей Synty точка отсчёта
        /// стоит где придётся — у шторы она на краю полотнища, у картины
        /// в плоскости рамы. Пивот, принятый за центр, ставит штору в стену
        /// наполовину.
        /// </summary>
        private static GameObject Prop(Transform parent, string name, string path, Vector3 place, float yaw,
            Vector3? scale = null)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (scale.HasValue)
            {
                go.transform.localScale = scale.Value;
            }

            Scenery(go);

            if (!TryBounds(go, out Bounds bounds))
            {
                return go;
            }

            go.transform.position += new Vector3(place.x - bounds.center.x, place.y - bounds.min.y,
                place.z - bounds.center.z);

            props++;
            if (TryBounds(go, out Bounds placed))
            {
                float distance = new Vector2(placed.center.x, placed.center.z).magnitude
                                 - new Vector2(placed.extents.x, placed.extents.z).magnitude;
                nearest = Mathf.Min(nearest, distance);
            }

            return go;
        }

        /// <summary>
        /// Пометить предмет декорацией: снять коллайдеры, увести на
        /// <c>Default</c>, погасить отброс теней.
        /// </summary>
        private static void Scenery(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i], true);
            }

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].gameObject.layer = 0;
            }

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
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
    }
}
