using UnityEngine;
using Igruha.Minigames.HoleInWall;
using Tone = Igruha.EditorTools.HoleInWallPaletteAssets.Tone;
using static Igruha.EditorTools.HoleInWallProps;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Зал «Дырки в стене»: трибуны вокруг бассейна и телекамеры трансляции.
    /// Публику на эти трибуны сажает <see cref="HoleInWallDecor"/>.
    ///
    /// <b>Переделан по разбору кадра 08.09.</b> До него зал был шестью
    /// отдельными скамьями на сторону, расставленными с полутораметровыми
    /// прогалами вдоль одной-единственной полосы у самой воды. В кадре это
    /// читалось не залом, а забытыми на бортике лавками: между водой и стенами
    /// павильона оставалось по девять метров голого пола на каждую сторону
    /// и четырнадцать за дальним бортом.
    ///
    /// Теперь зал идёт тремя частями, и каждая закрывает свою пустоту:
    ///
    /// <list type="bullet">
    /// <item><b>Нижний пояс</b> — сплошная лента скамей вдоль воды, вплотную
    /// друг к другу вместо шести островов.</item>
    /// <item><b>Верхний пояс</b> — второй ярус на подиуме почти вдвое выше,
    /// отодвинутый к стене павильона: он и съедает девятиметровую полосу, и
    /// поднимает зал над головой игрока, отчего площадка перестаёт читаться
    /// плоской.</item>
    /// <item><b>Дальний амфитеатр</b> — три ступени за торцом, поднимающиеся
    /// <b>выше стены</b>. Это главная часть: дальний торец в кадре всегда,
    /// туда смотрит вся игра, и до сих пор там была чёрная полоса.</item>
    /// </list>
    ///
    /// <b>Высота ступеней амфитеатра — не вкус.</b> Верх стены лежит на
    /// <see cref="HoleInWallConfig.WallHeight"/> = 3.60 м, и зритель на нижней
    /// ступени за стеной попросту невидим. Верхняя ступень поднята так, чтобы
    /// головы верхнего ряда встали <b>над</b> кромкой стены: зал виден и тогда,
    /// когда стена подъезжает, и тогда, когда уезжает.
    /// </summary>
    /// <remarks>
    /// <b>Ни одного коллайдера, слой <c>Default</c>, тени не отбрасываются.</b>
    /// Правила павильона, разобранные в шапке <see cref="HoleInWallEnvironment"/>,
    /// действуют здесь буквально — их держит <see cref="MarkAsScenery"/>
    /// внутри <see cref="SeatProp"/> и <see cref="Slab"/>.
    ///
    /// <b>Обе запретные зоны обойдены числами.</b> Боковые пояса стоят на
    /// X от 22.7 наружу при полуширине зоны 21.0; амфитеатр начинается с
    /// <see cref="AmphiNearGap"/> за <see cref="HoleInWallConfig.ArenaFarZ"/>,
    /// то есть за пятном арены, и вдобавок за фермами табло — иначе ноги табло
    /// прошли бы сквозь первую ступень.
    /// </remarks>
    internal static class HoleInWallStands
    {
        private const string Carnival = "Assets/Synty/PolygonHorrorCarnival/Prefabs/Props/";
        private const string Shops = "Assets/Synty/PolygonShops/Prefabs/Props/";

        private const string BleacherPrefab = Carnival + "SM_Prop_Bleachers_Straight_01.prefab";
        private const string TripodPrefab = Shops + "SM_Prop_Computer_Camera_Tripod_01.prefab";
        private const string CameraPrefab = Shops + "SM_Prop_Computer_Camera_DSLR_01.prefab";

        /// <summary>
        /// Длина секции трибуны, м — замер модели пака, а не число на глаз.
        /// По ней считается, сколько секций встаёт в ленту: раньше их было
        /// шесть на тридцать метров, то есть скамья через полтора метра
        /// пустоты.
        /// </summary>
        private const float BleacherLength = 3.01f;

        /// <summary>Зазор между секциями в ленте, м. Ноль дал бы вросшие друг в друга скамьи.</summary>
        private const float BleacherGap = 0.12f;

        // ========== БОКОВЫЕ ПОЯСА ==========

        /// <summary>Верх подиума нижнего пояса над полом платформы, ШП.</summary>
        private const float LowerDeckWidths = 1.2f;

        /// <summary>Насколько ось нижнего пояса вынесена за кромку бассейна, ШП.</summary>
        private const float LowerStandOutWidths = 4.5f;

        /// <summary>Ширина подиума пояса, ШП.</summary>
        private const float DeckWidths = 6f;

        /// <summary>Верх подиума верхнего пояса над полом платформы, ШП.</summary>
        private const float UpperDeckWidths = 3.8f;

        /// <summary>Насколько ось верхнего пояса вынесена за кромку бассейна, ШП.</summary>
        private const float UpperStandOutWidths = 10.9f;

        /// <summary>Сколько секций верхнего пояса не ставится у ближнего края: там их закрывает нижний.</summary>
        private const int UpperTrim = 1;

        // ========== ДАЛЬНИЙ АМФИТЕАТР ==========

        /// <summary>Сколько ступеней в амфитеатре за дальним торцом.</summary>
        private const int AmphiTiers = 3;

        /// <summary>
        /// Насколько первая ступень отставлена за пятно арены, м.
        ///
        /// Отставлена именно за фермы табло, а не просто за борт: табло стоит
        /// на <see cref="HoleInWallConfig.ArenaFarZ"/> плюс 2.52 м, и ступень,
        /// поставленная вплотную к борту, прошла бы сквозь его ноги.
        /// </summary>
        private const float AmphiNearGap = 3.6f;

        /// <summary>Глубина одной ступени, м: секция трибуны 1.78 м плюс проход.</summary>
        private const float AmphiTierDepth = 2.6f;

        /// <summary>Верх первой ступени над полом платформы, м.</summary>
        private const float AmphiFirstTop = 0.3f;

        /// <summary>
        /// Подъём на ступень, м.
        ///
        /// Три ступени по 1.05 м поднимают верхний ряд на 2.40 м, а его головы
        /// (рост модели 1.79 м) — на 4.19 м при верхе стены 3.60 м. То есть
        /// верхний ряд виден над стеной всегда, а нижние два — в промежутках
        /// между дорожками и когда стена уехала.
        /// </summary>
        private const float AmphiTierRise = 1.05f;

        /// <summary>Полуширина амфитеатра, м. Шире — и он уходит за стены павильона.</summary>
        private const float AmphiHalfWidth = 24f;

        // ========== БЛИЖНЯЯ ТРИБУНА ==========

        /// <summary>
        /// Насколько ближняя трибуна отставлена за ближний борт, м.
        ///
        /// Полоса отхода камеры — 5.76 м за бортом, то есть до −13.32. Отступ
        /// 6.2 ставит дальнюю грань первой ступени на −13.76: за полосой
        /// с запасом почти в полметра.
        /// </summary>
        private const float NearGap = 6.2f;

        /// <summary>Сколько ступеней помещается между полосой камеры и стеной павильона.</summary>
        private const int NearTiers = 3;

        /// <summary>Полуширина ближней трибуны, м.</summary>
        private const float NearHalfWidth = 18f;

        // ⚠️ Секции ближней трибуны названы Bleacher_N, а не Bleacher_A:
        // приставка «A» означает для рассадки плотную посадку амфитеатра
        // (два ряда по три), и ближний торец, который игрок видит только
        // при обороте камеры, стоил бы столько же, сколько дальний,
        // который в кадре всегда. Здесь садится один ряд, как на боковых
        // поясах, — разница в двух с половиной сотнях тысяч треугольников.

        /// <summary>Высота светящейся полосы по фасаду первой ступени, м.</summary>
        private const float FacadeBandHeight = 0.8f;

        /// <summary>
        /// Собрать зал. Возвращает построенную группу — публику по ней
        /// рассаживает декор.
        /// </summary>
        internal static Transform Build(Transform parent, HoleInWallConfig config)
        {
            Transform stands = Group(parent, "Stands");

            float unit = config.UnitsPerWidth;
            float floorY = RimTopY(config) - 0.02f;
            float halfWidth = config.ArenaWidth * 0.5f;

            float lowerX = halfWidth + LowerStandOutWidths * unit;
            float lowerTop = config.PlatformSurfaceY + LowerDeckWidths * unit;
            float upperX = halfWidth + UpperStandOutWidths * unit;
            float upperTop = config.PlatformSurfaceY + UpperDeckWidths * unit;
            float deckWidth = DeckWidths * unit;

            BuildSideBelt(stands, config, "L0", -lowerX, lowerTop, floorY, deckWidth, 0);
            BuildSideBelt(stands, config, "R0", lowerX, lowerTop, floorY, deckWidth, 0);
            BuildSideBelt(stands, config, "L1", -upperX, upperTop, floorY, deckWidth, UpperTrim);
            BuildSideBelt(stands, config, "R1", upperX, upperTop, floorY, deckWidth, UpperTrim);

            BuildAmphitheatre(stands, config, floorY);
            BuildNearStand(stands, config, floorY);
            BuildBroadcastCameras(stands, config, lowerX, lowerTop, deckWidth, floorY);

            return stands;
        }

        // ========== БОКОВЫЕ ПОЯСА ==========

        /// <summary>
        /// Пояс трибун вдоль длинной стороны: подиум, светящаяся кромка и
        /// сплошная лента секций.
        ///
        /// <b>Кромка подиума светится, и это не украшение.</b> Трибуна красится
        /// тоном бортика, за ней тёмная стена павильона, и без светящейся линии
        /// по переднему краю весь пояс сливается в одно чёрное пятно — ровно
        /// это и случилось с первым прогоном павильона 4.3.
        /// </summary>
        /// <param name="trim">Сколько секций снять с ближнего к камере края</param>
        private static void BuildSideBelt(Transform stands, HoleInWallConfig config, string beltName,
            float x, float deckTop, float floorY, float deckWidth, int trim)
        {
            float sign = Mathf.Sign(x);
            float centreZ = (config.ArenaFarZ + config.ArenaNearZ) * 0.5f;

            Slab(stands, $"Deck_{beltName}", new Vector3(deckWidth, deckTop - floorY, config.ArenaDepth),
                new Vector3(x, (deckTop + floorY) * 0.5f, centreZ), HoleInWallPaletteAssets.Get(Tone.PoolRim));

            Slab(stands, $"DeckEdge_{beltName}", new Vector3(0.14f, 0.12f, config.ArenaDepth),
                new Vector3(x - sign * (deckWidth * 0.5f - 0.07f), deckTop + 0.06f, centreZ),
                HoleInWallPaletteAssets.Get(Tone.Led));

            float pitch = BleacherLength + BleacherGap;
            int count = Mathf.FloorToInt(config.ArenaDepth / pitch);
            float from = centreZ - (count - 1) * pitch * 0.5f;

            for (int i = trim; i < count; i++)
            {
                SeatProp(stands, $"Bleacher_{beltName}_{i}", BleacherPrefab,
                    new Vector3(x, deckTop, from + i * pitch), sign < 0f ? 90f : -90f, Tone.PoolRim);
            }
        }

        // ========== ДАЛЬНИЙ АМФИТЕАТР ==========

        /// <summary>
        /// Три ступени за дальним торцом, поднимающиеся выше стены.
        ///
        /// Ступень — своя плита от пола студии до своего верха, а не
        /// нарастающая лесенка поверх предыдущей: плиты стоят одна за другой
        /// по глубине и каждая держит свой ряд, поэтому высоту любой из них
        /// можно поправить, не пересчитывая остальные.
        /// </summary>
        private static void BuildAmphitheatre(Transform stands, HoleInWallConfig config, float floorY)
        {
            float pitch = BleacherLength + BleacherGap;
            int perTier = Mathf.FloorToInt(AmphiHalfWidth * 2f / pitch);
            float from = -(perTier - 1) * pitch * 0.5f;

            for (int tier = 0; tier < AmphiTiers; tier++)
            {
                float nearZ = config.ArenaFarZ + AmphiNearGap + tier * AmphiTierDepth;
                float top = config.PlatformSurfaceY + AmphiFirstTop + tier * AmphiTierRise;

                Slab(stands, $"Deck_A{tier}",
                    new Vector3(AmphiHalfWidth * 2f + 1.2f, top - floorY, AmphiTierDepth),
                    new Vector3(0f, (top + floorY) * 0.5f, nearZ + AmphiTierDepth * 0.5f),
                    HoleInWallPaletteAssets.Get(Tone.PoolRim));

                // Кромка ступени горит цветом дорожки по кругу: три ступени
                // в трёх цветах читаются ярусами, а не одной серой массой.
                Slab(stands, $"DeckEdge_A{tier}",
                    new Vector3(AmphiHalfWidth * 2f + 1.2f, 0.12f, 0.14f),
                    new Vector3(0f, top + 0.06f, nearZ + 0.07f),
                    HoleInWallPaletteAssets.Get(HoleInWallPaletteAssets.LaneTone(tier)));

                if (tier == 0)
                {
                    Facade(stands, "DeckFacade_A", AmphiHalfWidth * 2f + 1.2f, floorY, top, nearZ - 0.09f);
                }

                for (int i = 0; i < perTier; i++)
                {
                    SeatProp(stands, $"Bleacher_A{tier}_{i}", BleacherPrefab,
                        new Vector3(from + i * pitch, top, nearZ + AmphiTierDepth * 0.5f), 180f, Tone.PoolRim);
                }
            }
        }

        /// <summary>
        /// Трибуна ближнего торца — за спиной у игрока.
        ///
        /// <b>Нужна ровно из-за орбитальной камеры.</b> Камера в игре ходит
        /// вокруг персонажа, и раз в раунд игрок обязательно видит то, что
        /// у него за спиной. До 08.09 там была чёрная стена с одинокой аркой:
        /// четырнадцать метров пола и восемь метров стены без единого
        /// предмета.
        ///
        /// <b>Стоит за полосой отхода камеры, и это условие, а не отступ.</b>
        /// Полоса кончается на <c>ArenaNearZ − CameraClearance</c> = −13.32 м;
        /// ближняя ступень начинается за <see cref="NearGap"/> от борта, то
        /// есть на −13.76. Предмет, поставленный ближе, закрывает игроку
        /// кадр целиком.
        /// </summary>
        private static void BuildNearStand(Transform stands, HoleInWallConfig config, float floorY)
        {
            float pitch = BleacherLength + BleacherGap;
            int perTier = Mathf.FloorToInt(NearHalfWidth * 2f / pitch);
            float from = -(perTier - 1) * pitch * 0.5f;

            for (int tier = 0; tier < NearTiers; tier++)
            {
                float farEdge = config.ArenaNearZ - NearGap - tier * AmphiTierDepth;
                float top = config.PlatformSurfaceY + AmphiFirstTop + tier * AmphiTierRise;

                Slab(stands, $"Deck_N{tier}",
                    new Vector3(NearHalfWidth * 2f + 1.2f, top - floorY, AmphiTierDepth),
                    new Vector3(0f, (top + floorY) * 0.5f, farEdge - AmphiTierDepth * 0.5f),
                    HoleInWallPaletteAssets.Get(Tone.PoolRim));

                Slab(stands, $"DeckEdge_N{tier}",
                    new Vector3(NearHalfWidth * 2f + 1.2f, 0.12f, 0.14f),
                    new Vector3(0f, top + 0.06f, farEdge - 0.07f),
                    HoleInWallPaletteAssets.Get(HoleInWallPaletteAssets.LaneTone(tier + 1)));

                if (tier == 0)
                {
                    Facade(stands, "DeckFacade_N", NearHalfWidth * 2f + 1.2f, floorY, top, farEdge + 0.09f);
                }

                for (int i = 0; i < perTier; i++)
                {
                    SeatProp(stands, $"Bleacher_N{tier}_{i}", BleacherPrefab,
                        new Vector3(from + i * pitch, top, farEdge - AmphiTierDepth * 0.5f), 0f, Tone.PoolRim);
                }
            }
        }

        /// <summary>
        /// Светящаяся полоса по фасаду первой ступени.
        ///
        /// ⚠️ <b>Имя обязано начинаться с <c>Deck</c>.</b> Замеры арта
        /// проверяют, что каждый предмет группы стоит на опоре, и пропускают
        /// по этой приставке сам подиум — полосы на его фасаде такая же его
        /// часть, как светящаяся кромка. Названные иначе, они честно
        /// числились бы четырьмя предметами, висящими в полутора метрах
        /// над полом.
        ///
        /// Подиум идёт от пола студии до первой ступени — это два с половиной
        /// метра глухой тёмной плиты между водой и первым рядом. В кадре она
        /// читалась чёрным провалом, отрезающим зал от арены. Полоса по
        /// середине фасада ставит зал на видимое основание — тот же приём,
        /// что светящаяся кромка подиума боковых поясов.
        /// </summary>
        private static void Facade(Transform stands, string facadeName, float width, float floorY, float top,
            float z)
        {
            float mid = (floorY + top) * 0.5f;
            Slab(stands, facadeName + "_Band", new Vector3(width, FacadeBandHeight, 0.1f),
                new Vector3(0f, mid, z), HoleInWallPaletteAssets.Get(Tone.Led));
            Slab(stands, facadeName + "_Rule", new Vector3(width, 0.09f, 0.12f),
                new Vector3(0f, mid + FacadeBandHeight * 0.5f + 0.1f, z), HoleInWallPaletteAssets.Get(Tone.NeonPink));
        }

        // ========== ТЕЛЕКАМЕРЫ ==========

        /// <summary>
        /// Камеры трансляции: две по бокам напротив линии проверки и две за
        /// дальним бортом, лицом на игроков.
        ///
        /// Боковые стоят на переднем крае нижнего подиума, перед трибуной:
        /// дальше подиум кончается, и камера повисла бы над полом студии —
        /// замеры это и поймали.
        /// </summary>
        private static void BuildBroadcastCameras(Transform stands, HoleInWallConfig config,
            float lowerX, float deckTop, float deckWidth, float floorY)
        {
            float sideX = lowerX - deckWidth * 0.5f + 0.9f;
            float sideZ = config.CheckLineZ - 1.2f;
            float backZ = config.ArenaFarZ + 6f * config.UnitsPerWidth;

            SeatCamera(stands, "TvCam_L", new Vector3(-sideX, deckTop, sideZ), 90f);
            SeatCamera(stands, "TvCam_R", new Vector3(sideX, deckTop, sideZ), -90f);
            SeatCamera(stands, "TvCam_FarL", new Vector3(-config.TrackPitch * 0.5f, floorY, backZ), 180f);
            SeatCamera(stands, "TvCam_FarR", new Vector3(config.TrackPitch * 0.5f, floorY, backZ), 180f);
        }

        /// <summary>
        /// Телекамера — штатив плюс камера на его верхней грани по замеру.
        ///
        /// Обе части лежат под общим узлом, и это не про порядок в иерархии:
        /// замеры проверяют, что предмет на площадке стоит на полу, а камера
        /// стоит на штативе. Отдельным предметом она честно числилась бы
        /// висящей в воздухе — и замер, который срабатывает на правильном,
        /// перестают читать.
        /// </summary>
        private static void SeatCamera(Transform parent, string cameraName, Vector3 ground, float yaw)
        {
            Transform mount = Group(parent, cameraName);

            GameObject tripod = SeatProp(mount, cameraName + "_Tripod", TripodPrefab, ground, yaw, Tone.Metal);
            if (tripod == null || !TryWorldBounds(tripod, out Bounds bounds))
            {
                return;
            }

            SeatProp(mount, cameraName + "_Body", CameraPrefab,
                new Vector3(ground.x, bounds.max.y, ground.z), yaw, Tone.Stage);
        }
    }
}
