using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Окружение и свет «Рейса на память» — подфаза 4.3.
    ///
    /// Строит цех вокруг выверенной геометрии: перекрытие с фермами, свет,
    /// обвес стен, шахту провала, наполнение зоны ожидания и выхода. Ничего из
    /// построенного здесь не имеет коллайдеров и не отбрасывает теней.
    ///
    /// <b>Главное правило этой подфазы — не плотность, а период.</b>
    ///
    /// Скилл требует сыпать детали туда, где игрок проводит весь раунд, то есть
    /// вдоль маршрута. Здесь это можно, но с одним условием: <b>всё, что стоит
    /// вдоль цепочки плит, повторяется ровно с шагом ряда и одинаково у каждого
    /// ряда.</b> Причина не в аккуратности. Спека (раздел 9.4) прямо запрещает
    /// показывать номер текущего шага кому бы то ни было: сбитый счёт шагов —
    /// часть игры. Значит любая деталь, по которой ряд можно отличить от
    /// соседнего, восстанавливает счётчик шагов и отменяет запрет.
    ///
    /// Отсюда следствие, неочевидное до первой попытки: <b>«через один» — тоже
    /// подсказка.</b> Вентиль на каждом втором пролёте не говорит, какая полоса
    /// безопасна, но говорит, какой это шаг, — а этого достаточно. Поэтому
    /// вдоль маршрута деталь либо на каждом пролёте, либо нигде.
    ///
    /// Зона ожидания, выход и торцы под это правило не подпадают: они видны
    /// целиком и ориентирами не работают. Там композиция свободная и плотная.
    ///
    /// <b>Ни одного случайного числа.</b> Как и в дрессе, генератор здесь не
    /// заводится вовсе: разброс вдоль маршрута — это подсказка, а в зонах
    /// свободной композиции расстановка выписана руками и повторяется от
    /// пересборки к пересборке байт в байт.
    /// </summary>
    internal static class MemoryRunEnvironment
    {
        private const string Props = "Assets/Synty/PolygonConstruction/Prefabs/Props/";
        private const string Buildings = "Assets/Synty/PolygonConstruction/Prefabs/Buildings/";
        private const string Env = "Assets/Synty/PolygonConstruction/Prefabs/Environments/";
        private const string ShopProps = "Assets/Synty/PolygonShops/Prefabs/Props/";
        private const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        private const string GenericProps = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        private const string GenericBuilding = "Assets/Synty/PolygonGeneric/Prefabs/Building/";
        private const string Dynamite = "Assets/Synty/PolygonHorrorCarnival/Prefabs/Weapons/SM_Wep_Dynamite_01.prefab";
        private const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";

        /// <summary>Внутренняя грань боковой стены: стена толщиной 0.4 стоит центром на половине ширины.</summary>
        private static float WallInner(MemoryRunConfig config)
        {
            return config.HallWidth * 0.5f - 0.2f;
        }

        /// <summary>
        /// Ниже этой отметки в провале можно ставить что угодно, выше — нельзя.
        /// Там проходит зона выбывания, и предмет над ней читается опорой,
        /// которой нет: коллайдеров у окружения не бывает, и упавший прошёл бы
        /// сквозь него насквозь.
        /// </summary>
        private const float PitFreeTopY = -7.14f;

        /// <summary>
        /// Выше этой отметки дым подниматься не имеет права. Между ней и
        /// поверхностью плит остаётся два метра чистого воздуха: маршрут не
        /// должен затягивать ничем, это и есть вся игра.
        /// </summary>
        private const float HazeTopY = -2f;

        /// <summary>
        /// Полуширина центрального коридора зоны ожидания. Внутри неё не стоит
        /// ничего выше пояса: там стоят восьмером и смотрят на маршрут поверх
        /// голов передних, ради чего площадку и подняли на 1 ШИ.
        ///
        /// Ряд плит — 10.08 м, то есть половина 5.04. Коридор взят на полметра
        /// шире: за его границей предмет уже не встаёт между зрителем и
        /// маршрутом ни с какой точки площадки.
        /// </summary>
        private const float ViewCorridorHalfWidth = 5.6f;

        /// <summary>
        /// Полоса у торцевой стены, которая под правило обзора не подпадает.
        /// Щиток и знак на дальней стене стоят <b>позади</b> всех восьмерых
        /// и перекрыть маршрут не могут физически.
        /// </summary>
        private const float BackWallBand = 1.5f;

        /// <summary>Высота, выше которой предмет уже перекрывает обзор стоящему сзади.</summary>
        private const float ViewBlockHeight = 1.0f;

        /// <summary>Насколько далеко от барьера очереди начинается зона, где можно ставить высокое.</summary>
        private const float GateClearance = 4f;

        private static int propCount;
        private static int lightCount;
        private static readonly List<string> notes = new List<string>(8);

        /// <summary>
        /// Построить цех. Вызывается пересборкой арены после того, как готова
        /// вся геометрия: окружение садится по её числам, а не по своим.
        /// </summary>
        internal static void Build(Transform arena, MemoryRunConfig config)
        {
            propCount = 0;
            lightCount = 0;
            notes.Clear();

            var root = new GameObject("_Environment");
            root.transform.SetParent(arena, false);
            root.layer = LayerMask.NameToLayer("Default");
            Transform parent = root.transform;

            BuildCeiling(parent, config);
            BuildWallRuns(parent, config);
            BuildRouteBays(parent, config);
            BuildPitShaft(parent, config);
            BuildStartZone(parent, config);
            BuildExitZone(parent, config);
            BuildFloorWear(parent, config);
            BuildLighting(parent, config);
        }

        // ─────────────────────────── перекрытие ───────────────────────────

        /// <summary>
        /// Перекрытие и его балки. В блокауте потолка нет вовсе — четыре стены
        /// и всё, — поэтому кадр вверх уходил в пустоту.
        ///
        /// Плита перекрытия <b>не отбрасывает тень</b>, как и весь остальной
        /// декор. Это не экономия: единственный источник теней в сцене —
        /// направленный свет, и накрытый им цех оказался бы в сплошной тени,
        /// а вместе с ним пропали бы и тени самих плит, ради которых он и
        /// включён.
        /// </summary>
        private static void BuildCeiling(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "Ceiling");
            float top = config.CeilingHeight;
            float inner = WallInner(config);

            Slab(group, "Deck", new Vector3(config.HallWidth, 0.4f, config.HallDepth),
                new Vector3(0f, top + 0.2f, 0f), MemoryRunPalette.Tone.Ceiling);

            // Прогоны вдоль цеха: четыре линии, уходящие в перспективу. Именно
            // они дают потолку глубину — поперечные фермы её не дают, они
            // рубят кадр на полосы.
            Material steel = MemoryRunPalette.Get(MemoryRunPalette.Tone.Structure);
            float[] purlinX = { -inner + 1.4f, -3.4f, 3.4f, inner - 1.4f };
            foreach (float x in purlinX)
            {
                Slab(group, "Purlin", new Vector3(0.28f, 0.40f, config.HallDepth - 1f),
                    new Vector3(x, top - 0.22f, 0f), steel);
            }

            // Кабель-каналы: две нитки под прогонами, с провисающими петлями.
            foreach (float x in new[] { -inner + 2.6f, inner - 2.6f })
            {
                Slab(group, "CableTray", new Vector3(0.34f, 0.14f, config.HallDepth - 2f),
                    new Vector3(x, top - 0.75f, 0f), steel);
            }
        }

        // ─────────────────────── непрерывные линии стен ───────────────────────

        /// <summary>
        /// То, что тянется вдоль всего цеха без разрывов: цоколь стен и две
        /// нитки труб. Непрерывное по определению одинаково у всех рядов, и
        /// счётчик шагов из него не построить.
        ///
        /// Труба пака — 4.31 м при шаге ряда 4.32. Совпадение до сантиметра:
        /// стыки труб садятся ровно на границы пролётов, и линия читается
        /// сплошной без единой подгонки.
        /// </summary>
        private static void BuildWallRuns(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "WallRuns");
            float inner = WallInner(config);
            Material wainscot = MemoryRunPalette.Get(MemoryRunPalette.Tone.Wainscot);

            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * inner;

                // Цоколь: без него стена читается пустым листом на всю высоту.
                Slab(group, "Wainscot", new Vector3(0.10f, 1.70f, config.HallDepth - 0.8f),
                    new Vector3(x - side * 0.05f, 0.85f, 0f), wainscot);

                // Карниз поверх цоколя — тонкая светлая полка, которая ловит свет
                // и отделяет низ стены от верха.
                Slab(group, "WainscotCap", new Vector3(0.18f, 0.10f, config.HallDepth - 0.8f),
                    new Vector3(x - side * 0.09f, 1.75f, 0f), MemoryRunPalette.Tone.Structure);

                // Средний пояс: между карнизом цоколя на 1.75 и трубами на 4.6
                // оставалось почти три метра голой стены.
                Slab(group, "MidRail", new Vector3(0.16f, 0.12f, config.HallDepth - 0.8f),
                    new Vector3(x - side * 0.08f, 3.25f, 0f), MemoryRunPalette.Tone.Structure);

                for (int i = 0; i < 16; i++)
                {
                    float z = -config.HallDepth * 0.5f + 1.6f + i * config.StepPitch;
                    Prop(group, Props + "SM_Prop_Pipe_01.prefab",
                        new Vector3(x - side * 0.30f, 5.35f, z),
                        new Vector3(0f, 90f, 0f), new Vector3(1f, 1f, 1.01f),
                        MemoryRunPalette.Tone.Plate);
                    Prop(group, Props + "SM_Prop_Pipe_01.prefab",
                        new Vector3(x - side * 0.24f, 4.62f, z),
                        new Vector3(0f, 90f, 0f), new Vector3(0.7f, 0.7f, 1.01f),
                        MemoryRunPalette.Tone.Structure);
                }
            }
        }

        // ───────────────────────── пролёт маршрута ─────────────────────────

        /// <summary>
        /// Пролёт над рядом плит. Один и тот же набор на всех десяти рядах,
        /// поставленный по <see cref="MemoryRunConfig.StepZ"/>: ферма, пара
        /// плафонов, кронштейн и вертикальный стояк на каждой стене.
        ///
        /// Ставить это «через один» или «на каждом третьем» нельзя — см. разбор
        /// периода в шапке класса.
        /// </summary>
        private static void BuildRouteBays(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "RouteBays");
            float inner = WallInner(config);
            float top = config.CeilingHeight;
            Material steel = MemoryRunPalette.Get(MemoryRunPalette.Tone.Structure);

            for (int step = 0; step < config.Steps; step++)
            {
                float z = config.StepZ(step);

                // Ферма поперёк пролёта: две половины, стык по центру.
                foreach (float side in new[] { -1f, 1f })
                {
                    Prop(group, Buildings + "SM_Bld_House_Truss_02.prefab",
                        new Vector3(side * config.HallWidth * 0.25f, top - 0.95f, z),
                        new Vector3(0f, 90f, 0f), new Vector3(1f, 0.42f, 0.90f),
                        MemoryRunPalette.Tone.Structure);
                }

                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * inner;

                    // Кронштейн, на котором висят трубы.
                    Prop(group, Props + "SM_Prop_I_Beam_01.prefab",
                        new Vector3(x - side * 0.42f, 5.85f, z),
                        new Vector3(0f, 0f, 90f), new Vector3(0.42f, 1f, 1f),
                        MemoryRunPalette.Tone.Structure);

                    // Стояк от цоколя до кронштейна.
                    Slab(group, "Conduit", new Vector3(0.13f, 4.0f, 0.13f),
                        new Vector3(x - side * 0.12f, 3.8f, z), steel);

                    // Вентиль на нижней трубе: цех выглядит работающим, а не
                    // нарисованным. Стоит у каждого ряда, поэтому шаг по нему
                    // не сосчитать.
                    Prop(group, GenericBuilding + "SM_Gen_Bld_Pipe_Valve_01.prefab",
                        new Vector3(x - side * 0.34f, 4.62f, z + 1.1f),
                        new Vector3(0f, side * 90f, 0f), Vector3.one * 1.3f,
                        MemoryRunPalette.Tone.Structure);
                }
            }
        }

        // ─────────────────────────── шахта провала ───────────────────────────

        /// <summary>
        /// Стены провала, пилястры по ним и завал на дне.
        ///
        /// Провал обязан читаться шахтой, а не чёрным низом, и держится это
        /// на вертикалях: пилястры сходятся в перспективе и дают глазу мерить
        /// глубину. Горизонтальных полок здесь нет намеренно — полка на стене
        /// провала читается ступенькой, на которую можно приземлиться, а
        /// коллайдера у неё нет и не будет.
        ///
        /// Всё объёмное на дне лежит ниже <see cref="PitFreeTopY"/>. Стены
        /// шахты под это правило не подпадают: у них верх на нуле, приземлиться
        /// на вертикальную грань нельзя.
        /// </summary>
        private static void BuildPitShaft(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "PitShaft");
            float inner = WallInner(config);
            float from = config.GateZ;
            float to = config.ExitPadZ;
            float depth = to - from;
            float bottom = -config.PitDepth + 0.2f;
            float height = -bottom;
            Material shaft = MemoryRunPalette.Get(MemoryRunPalette.Tone.PitWall);
            Material steel = MemoryRunPalette.Get(MemoryRunPalette.Tone.Structure);

            foreach (float side in new[] { -1f, 1f })
            {
                Slab(group, "ShaftWall", new Vector3(0.30f, height, depth),
                    new Vector3(side * inner, bottom + height * 0.5f, from + depth * 0.5f), shaft);
            }

            Slab(group, "ShaftEnd", new Vector3(config.HallWidth, height, 0.30f),
                new Vector3(0f, bottom + height * 0.5f, from), shaft);
            Slab(group, "ShaftEnd", new Vector3(config.HallWidth, height, 0.30f),
                new Vector3(0f, bottom + height * 0.5f, to), shaft);

            // Пилястры по стенам шахты — по одной на ряд, чтобы ритм низа
            // совпадал с ритмом плит и не давал отдельного счёта.
            for (int step = 0; step < config.Steps; step++)
            {
                float z = config.StepZ(step);
                foreach (float side in new[] { -1f, 1f })
                {
                    Slab(group, "ShaftRib", new Vector3(0.34f, height, 0.42f),
                        new Vector3(side * (inner - 0.2f), bottom + height * 0.5f, z), steel);
                }
            }

            // Завал на дне: читается только в силуэте и в дыму, но без него
            // низ выглядит крашеным листом, а не полом под цехом.
            // Высота завала ограничена не вкусом, а отметкой зоны выбывания:
            // от дна до неё 1.30 м, и всё, что здесь лежит, обязано влезть.
            // Труба SM_Prop_Pipe_Concrete_Huge_01 в натуральном виде 2.27 м
            // высотой — она уходит под масштаб, а не выбрасывается: в силуэте
            // на дне важен размах, а не паспортный размер.
            float floorY = bottom;
            var deep = MemoryRunPalette.Tone.PitWall;
            Prop(group, Props + "SM_Prop_Pipe_Concrete_Huge_01.prefab",
                new Vector3(-5.2f, floorY, -8f), new Vector3(0f, 12f, 0f), Vector3.one * 0.5f, deep);
            Prop(group, Props + "SM_Prop_Pipe_Concrete_Huge_01.prefab",
                new Vector3(4.4f, floorY, 9.5f), new Vector3(0f, -8f, 0f), Vector3.one * 0.5f, deep);
            Prop(group, Props + "SM_Prop_Pipe_Concrete_Large_01.prefab",
                new Vector3(6.8f, floorY, -12.5f), new Vector3(0f, 30f, 0f), Vector3.one, deep);
            Prop(group, Props + "SM_Prop_Pipe_Concrete_Large_01.prefab",
                new Vector3(-8.4f, floorY, 3.5f), new Vector3(0f, -50f, 0f), Vector3.one, deep);
            Prop(group, Props + "SM_Prop_Skip_Large_02.prefab",
                new Vector3(-7.4f, floorY, 16f), new Vector3(0f, 22f, 0f), Vector3.one * 0.85f, deep);
            Prop(group, Props + "SM_Prop_Rubble_Concrete_02.prefab",
                new Vector3(0.8f, floorY, 2f), new Vector3(0f, 45f, 0f), Vector3.one * 1.6f, deep);
            Prop(group, Props + "SM_Prop_Rubble_Concrete_03.prefab",
                new Vector3(-2.6f, floorY, 20f), new Vector3(0f, -20f, 0f), Vector3.one * 1.4f, deep);
            Prop(group, Props + "SM_Prop_Rubble_Concrete_01.prefab",
                new Vector3(8.2f, floorY, 4.5f), new Vector3(0f, 70f, 0f), Vector3.one * 1.8f, deep);

            BuildPitHaze(group, config, floorY);
            VerifyPitClearance(group);
        }

        /// <summary>
        /// Дым на дне провала. Спека описывает дно как теряющееся в темноте и
        /// дыму — темнота есть с подфазы 4.2, дым появляется здесь.
        ///
        /// Держит две вещи разом. Во-первых, глубину: слой дыма между плитами
        /// и дном даёт глазу воздушную перспективу, которой у чистого тёмного
        /// тона нет. Во-вторых, тайну: дно и завал на нём перестают читаться
        /// подробно, и смотреть вниз в поисках ориентиров становится незачем.
        ///
        /// Эмиттеры стоят через два ряда — <b>вдоль маршрута это можно</b>,
        /// в отличие от предметов: дым живой, он клубится и сносится, и по нему
        /// нельзя ни отсчитать шаг, ни узнать ряд. Правило периода запрещает
        /// приметы, а не движение.
        ///
        /// Автостарт и зацикливание у эффектов пака здесь <b>оставлены</b>: это
        /// фоновая атмосфера, а не событие. Ручной запуск нужен эффектам
        /// подфазы 4.4, которые ждут взрыва.
        /// </summary>
        private static void BuildPitHaze(Transform group, MemoryRunConfig config, float floorY)
        {
            var haze = Group(group, "Haze");
            for (int step = 0; step < config.Steps; step += 2)
            {
                float z = config.StepZ(step);
                Prop(haze, Fx + "FX_Smoke_Black_Large_01.prefab",
                    new Vector3(-3.2f, floorY + 0.4f, z), Vector3.zero, Vector3.one * 0.8f);
                Prop(haze, Fx + "FX_Smoke_Black_01.prefab",
                    new Vector3(3.6f, floorY + 0.3f, z + config.StepPitch),
                    Vector3.zero, Vector3.one * 0.7f);
            }

            Prop(haze, Fx + "FX_Steam_03.prefab",
                new Vector3(0f, floorY + 0.2f, config.ChainStartZ + 4f), Vector3.zero, Vector3.one * 0.9f);
            Prop(haze, Fx + "FX_Steam_03.prefab",
                new Vector3(0f, floorY + 0.2f, config.ExitPadZ - 6f), Vector3.zero, Vector3.one * 0.9f);

            foreach (var system in haze.GetComponentsInChildren<ParticleSystem>(true))
            {
                TuneHaze(system);
            }
        }

        /// <summary>
        /// Прижать дым ко дну и посчитать, до какой отметки он реально дойдёт.
        ///
        /// Эффекты пака рассчитаны на открытую сцену и в родном виде здесь
        /// катастрофичны: у <c>FX_Smoke_Black_Large_01</c> отрицательная
        /// гравитация, время жизни 10 с и частица размером 20 м — расчёт даёт
        /// подъём до отметки +31, то есть дым затянул бы весь цех вместе с
        /// маршрутом. У <c>FX_Steam_03</c> стартовая скорость 20 м/с, и он
        /// выстреливал бы из провала фонтаном.
        ///
        /// <b>Статичный кадр этого не показывает вовсе</b> — частицы в нём не
        /// симулируются, и рендер выглядит нормальным. Поэтому высота подъёма
        /// здесь не настраивается на глаз, а считается и сверяется с
        /// <see cref="HazeTopY"/>.
        /// </summary>
        private static void TuneHaze(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = 6f;
            main.startSpeed = 0.3f;
            main.startSize = 4f;
            main.gravityModifier = 0f;
            main.maxParticles = 24;
            main.simulationSpeed = 0.4f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 2.5f;

            float rise = main.startSpeed.constantMax * main.startLifetime.constantMax;
            float top = system.transform.position.y + rise + main.startSize.constantMax * 0.5f;
            if (top > HazeTopY)
            {
                notes.Add($"дым «{system.name}» доходит до {top:F1} м при пределе {HazeTopY:F1} — " +
                          "он затянет маршрут, а маршрут обязан быть виден");
            }
        }

        /// <summary>
        /// Проверить, что в шахте не осталось предмета, который читался бы
        /// опорой. Считается прямо при постройке: заметить это по кадру нельзя
        /// вовсе — падение длится доли секунды.
        /// </summary>
        private static void VerifyPitClearance(Transform group)
        {
            foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds bounds = renderer.bounds;

                // Стены шахты идут снизу до нуля и приземлить никого не могут:
                // приземляются на горизонтальную грань, а у них её нет.
                if (bounds.max.y > -0.5f)
                {
                    continue;
                }

                if (bounds.max.y > PitFreeTopY)
                {
                    notes.Add($"«{renderer.name}» в провале: верх на {bounds.max.y:F2} м, " +
                              $"а зона выбывания начинается с {PitFreeTopY:F2} — упавший приземлится вместо смерти");
                }
            }
        }

        // ───────────────────────── зона ожидания ─────────────────────────

        /// <summary>
        /// Наполнение площадки ожидания: здесь стоят семеро и смотрят, поэтому
        /// композиция подчинена обзору, а не плотности.
        ///
        /// Всё высокое прижато к боковым стенам, центральный коридор шириной
        /// <see cref="ViewCorridorHalfWidth"/> в обе стороны пуст выше пояса.
        /// Ближе <see cref="GateClearance"/> к барьеру не стоит ничего высокого
        /// вовсе: там столпятся все восемь.
        ///
        /// Ящики с динамитом стоят здесь и только здесь. Сеттинг фабрики
        /// динамита держится на них — во всех двенадцати паках связка шашек
        /// нашлась ровно одна, в «Карнавале ужасов».
        /// </summary>
        private static void BuildStartZone(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "StartZone");
            float lift = config.StartZoneLift;
            float inner = WallInner(config);
            float wall = inner - 0.1f;

            // Вся высокая мебель живёт в полосе z от −29.8 до −22.4 и прижата
            // к бортам. Ближе к барьеру нет ничего выше пояса: там в момент
            // объявления хода стоят восемь человек разом.

            // Левый борт: склад труб, штабели ящиков с динамитом, бочки.
            Prop(group, Props + "SM_Prop_StorageShelf_Large_Pipe_01.prefab",
                new Vector3(-wall + 1.1f, lift, -27.4f), new Vector3(0f, 90f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Crate_Stack_01.prefab",
                new Vector3(-wall + 0.9f, lift, -23.6f), new Vector3(0f, 8f, 0f), Vector3.one);
            Prop(group, Dynamite,
                new Vector3(-wall + 0.9f, lift + 1.64f, -23.6f), new Vector3(0f, 34f, 0f), Vector3.one * 1.5f);
            Prop(group, Props + "SM_Prop_Crate_Large_01.prefab",
                new Vector3(-wall + 2.0f, lift, -22.7f), new Vector3(0f, -18f, 0f), Vector3.one);
            Prop(group, Dynamite,
                new Vector3(-wall + 2.0f, lift + 0.95f, -22.7f), new Vector3(0f, -52f, 0f), Vector3.one * 1.5f);
            Prop(group, Props + "SM_Prop_BarrelStack_01.prefab",
                new Vector3(-wall + 1.5f, lift, -25.4f), new Vector3(0f, 24f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Wirespool_01.prefab",
                new Vector3(-wall + 2.6f, lift, -29.4f), new Vector3(0f, 0f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Slatbox_01.prefab",
                new Vector3(-wall + 2.8f, lift, -24.4f), new Vector3(0f, 22f, 0f), Vector3.one);

            // Правый борт: верстак, генератор, компрессор, поддоны.
            Prop(group, TownProps + "SM_Prop_Workbench_01.prefab",
                new Vector3(wall - 1.4f, lift, -27.8f), new Vector3(0f, -90f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_ToolBox_01.prefab",
                new Vector3(wall - 1.4f, lift + 0.95f, -28.1f), new Vector3(0f, 12f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Generator_Large_01.prefab",
                new Vector3(wall - 1.3f, lift, -24.9f), new Vector3(0f, -90f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Compressor_01.prefab",
                new Vector3(wall - 2.6f, lift, -26.4f), new Vector3(0f, -74f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_PalletStack_01.prefab",
                new Vector3(wall - 1.2f, lift, -22.9f), new Vector3(0f, 6f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Crate_02.prefab",
                new Vector3(wall - 2.7f, lift, -23.6f), new Vector3(0f, -34f, 0f), Vector3.one);
            Prop(group, Dynamite,
                new Vector3(wall - 2.7f, lift + 0.88f, -23.6f), new Vector3(0f, 18f, 0f), Vector3.one * 1.5f);
            Prop(group, Props + "SM_Prop_Pallet_01.prefab",
                new Vector3(wall - 2.4f, lift, -29.6f), new Vector3(0f, 14f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Barrel_03.prefab",
                new Vector3(wall - 3.6f, lift, -29.9f), new Vector3(0f, 0f, 0f), Vector3.one);

            // Торцевая стена: щитки, знаки, вентиляция. Плоское и на стене —
            // запас на отход камеры оно не съедает.
            float nearZ = config.NearEdgeZ + 0.25f;
            Prop(group, Props + "SM_Prop_PowerBoxes_01.prefab",
                new Vector3(-4.6f, lift, nearZ), Vector3.zero, Vector3.one);
            Prop(group, Props + "SM_Prop_PowerBoxes_03.prefab",
                new Vector3(-3.2f, lift, nearZ), Vector3.zero, Vector3.one);
            Prop(group, ShopProps + "SM_Prop_Wall_Vent_01.prefab",
                new Vector3(3.6f, lift + 3.4f, nearZ), Vector3.zero, Vector3.one * 1.6f);
            Prop(group, Props + "SM_Prop_Sign_10.prefab",
                new Vector3(1.4f, lift + 2.3f, nearZ + 0.05f), Vector3.zero, Vector3.one * 1.4f);
            Prop(group, Props + "SM_Prop_Sign_02.prefab",
                new Vector3(5.8f, lift + 2.2f, nearZ + 0.05f), Vector3.zero, Vector3.one * 1.3f);

            // Лестницы на боковых стенах: вертикаль, которой у стены больше нет.
            Prop(group, Props + "SM_Prop_Ladder_03.prefab",
                new Vector3(-wall + 0.4f, lift, -31.2f), new Vector3(0f, 90f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Ladder_03.prefab",
                new Vector3(wall - 0.4f, lift, -31.2f), new Vector3(0f, -90f, 0f), Vector3.one);

            VerifyViewCorridor(group, config);
        }

        /// <summary>
        /// Проверить, что обзор с площадки ожидания никто не перекрыл.
        ///
        /// Считается числом, потому что глазом это ловится только с места
        /// восьмого игрока, а туда никто не встаёт: на кадре из-за спины
        /// первого всё выглядит нормально.
        /// </summary>
        private static void VerifyViewCorridor(Transform group, MemoryRunConfig config)
        {
            float floor = config.StartZoneLift;
            foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds bounds = renderer.bounds;
                if (bounds.max.y - floor <= ViewBlockHeight)
                {
                    continue;
                }

                // Всё, что висит на торцевой стене, стоит позади зрителей и
                // маршрут перекрыть не может — оно из проверки исключено.
                if (bounds.center.z < config.NearEdgeZ + BackWallBand)
                {
                    continue;
                }

                bool inCorridor = Mathf.Abs(bounds.center.x) < ViewCorridorHalfWidth;
                bool nearGate = bounds.center.z > config.GateZ - GateClearance;

                if (inCorridor && bounds.center.z < config.GateZ)
                {
                    notes.Add($"«{renderer.name}» стоит в коридоре обзора на x={bounds.center.x:F1} " +
                              $"и выше пояса — стоящие сзади не увидят плиты");
                }
                else if (nearGate && bounds.center.z < config.GateZ)
                {
                    notes.Add($"«{renderer.name}» стоит в {config.GateZ - bounds.center.z:F1} м от барьера " +
                              $"и выше пояса — там столпятся восемь человек");
                }
            }
        }

        // ─────────────────────────── зона выхода ───────────────────────────

        /// <summary>
        /// Выходная площадка. Наполняется скромнее зоны ожидания: сюда добегают
        /// поодиночке, и всё, что здесь стоит, обязано оставить дверь главной
        /// в кадре.
        /// </summary>
        private static void BuildExitZone(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "ExitZone");
            float inner = WallInner(config);
            float wall = inner - 0.1f;
            float farZ = config.HallDepth * 0.5f - 0.45f;

            Prop(group, Props + "SM_Prop_StorageShelf_Plank_01.prefab",
                new Vector3(-wall + 1.1f, 0f, 29.4f), new Vector3(0f, 90f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_BarrelStack_02.prefab",
                new Vector3(-wall + 0.9f, 0f, 31.4f), new Vector3(0f, -14f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_PalletStack_02.prefab",
                new Vector3(wall - 1.1f, 0f, 29.8f), new Vector3(0f, 10f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Barrel_Oil_01.prefab",
                new Vector3(wall - 1.0f, 0f, 31.6f), new Vector3(0f, 40f, 0f), Vector3.one);
            Prop(group, Props + "SM_Prop_Barrel_01.prefab",
                new Vector3(wall - 1.9f, 0f, 31.2f), new Vector3(0f, -25f, 0f), Vector3.one);

            // Ограждение по бокам двери: коридор к цели, а не просто стена с дверью.
            foreach (float side in new[] { -1f, 1f })
            {
                Prop(group, Props + "SM_Prop_Barrier_Long_01.prefab",
                    new Vector3(side * 2.4f, 0f, farZ - 0.9f), new Vector3(0f, 90f, 0f), Vector3.one);
                Prop(group, Props + "SM_Prop_Cone_01.prefab",
                    new Vector3(side * 3.4f, 0f, farZ - 1.9f), Vector3.zero, Vector3.one);
                Prop(group, Props + "SM_Prop_PowerBoxes_02.prefab",
                    new Vector3(side * 5.0f, 0f, farZ + 0.1f), Vector3.zero, Vector3.one);
            }

            Prop(group, Props + "SM_Prop_Sign_11.prefab",
                new Vector3(-4.4f, 2.4f, farZ), Vector3.zero, Vector3.one * 1.3f);
            Prop(group, ShopProps + "SM_Prop_Wall_Vent_01.prefab",
                new Vector3(4.6f, 3.6f, farZ), Vector3.zero, Vector3.one * 1.6f);
        }

        // ─────────────────────── износ по настилам ───────────────────────

        /// <summary>
        /// Пыль и следы на настилах площадок.
        ///
        /// Скилл требует сыпать плоскую мелочь именно туда, где игрок проводит
        /// весь раунд, — то есть и на маршрут тоже. <b>Здесь на маршрут не
        /// падает ничего.</b> Пятно на плите — это примета, по которой плиту
        /// запомнят вместо маршрута, и оно ломает игру целиком. Весь износ
        /// уходит на площадку ожидания и выходную площадку.
        /// </summary>
        private static void BuildFloorWear(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "FloorWear");
            float lift = config.StartZoneLift + 0.02f;

            var starts = new[]
            {
                new Vector3(-3.4f, lift, -26.2f), new Vector3(2.8f, lift, -24.4f),
                new Vector3(-1.2f, lift, -21.6f), new Vector3(4.6f, lift, -28.6f),
                new Vector3(-5.6f, lift, -19.6f), new Vector3(0.6f, lift, -30.4f),
                new Vector3(6.4f, lift, -23.2f), new Vector3(-7.2f, lift, -25.6f)
            };
            float[] starYaw = { 12f, -40f, 74f, 128f, -96f, 158f, 40f, -14f };
            for (int i = 0; i < starts.Length; i++)
            {
                Prop(group, Env + "SM_Env_Dirt_Dust_01.prefab", starts[i],
                    new Vector3(0f, starYaw[i], 0f), new Vector3(0.62f, 1f, 0.62f),
                    MemoryRunPalette.Tone.Grime);
            }

            var exits = new[]
            {
                new Vector3(-2.6f, 0.02f, 29.2f), new Vector3(3.2f, 0.02f, 30.6f),
                new Vector3(0.4f, 0.02f, 28.0f), new Vector3(-6.0f, 0.02f, 30.2f)
            };
            float[] exitYaw = { 26f, -68f, 104f, -132f };
            for (int i = 0; i < exits.Length; i++)
            {
                Prop(group, Env + "SM_Env_Dirt_Dust_01.prefab", exits[i],
                    new Vector3(0f, exitYaw[i], 0f), new Vector3(0.58f, 1f, 0.58f),
                    MemoryRunPalette.Tone.Grime);
            }
        }

        // ─────────────────────────────── свет ───────────────────────────────

        /// <summary>
        /// Свет цеха. Задаётся кодом, а не инспектором: YAML сцены не переживает
        /// слияние веток, и чужая правка выиграла бы молча.
        ///
        /// <b>Над всеми десятью рядами свет одинаковый.</b> Это то же правило
        /// периода, что и у декора, только его нарушение заметнее всего:
        /// подсвеченный ярче ряд запоминается сам собой, безо всякого усилия
        /// игрока, и маршрут перестаёт быть тайной.
        ///
        /// Рассеянный поднят заметно выше единицы — цех перекрыт сверху, и на
        /// стандартном значении интерьер уходит в тёмно-серое, где реквизит
        /// перестаёт различаться (тот же вывод у «Верю / не верю», STATE 3.70).
        /// </summary>
        private static void BuildLighting(Transform parent, MemoryRunConfig config)
        {
            var group = Group(parent, "Lighting");
            float inner = WallInner(config);
            float lampY = config.CeilingHeight - 1.5f;
            var warm = new Color(1.00f, 0.95f, 0.86f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.41f, 0.45f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.075f, 0.085f, 0.105f);
            RenderSettings.fogDensity = 0.0115f;

            // Ряд плафонов вдоль каждой стороны маршрута — по паре на пролёт.
            for (int step = 0; step < config.Steps; step++)
            {
                float z = config.StepZ(step);
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * (inner - 3.6f);
                    Prop(group, ShopProps + "SM_Prop_Lighting_Ceiling_Bar_08.prefab",
                        new Vector3(x, lampY, z), new Vector3(0f, 90f, 0f), Vector3.one);
                    Lamp(group, new Vector3(x, lampY - 0.9f, z), warm, 17f, 3.1f);
                }
            }

            // Зона ожидания и выход: подвесные плафоны, теплее и реже.
            float[] startZ = { -29.6f, -25.4f, -21.2f };
            foreach (float z in startZ)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * 4.4f;
                    Prop(group, ShopProps + "SM_Prop_Lighting_Ceiling_Industrial_01.prefab",
                        new Vector3(x, config.CeilingHeight - 0.9f, z), Vector3.zero, Vector3.one * 1.6f);
                    Lamp(group, new Vector3(x, config.CeilingHeight - 2.4f, z), warm, 15f, 2.7f);
                }
            }

            foreach (float z in new[] { 28.6f, 31.4f })
            {
                Prop(group, ShopProps + "SM_Prop_Lighting_Ceiling_Industrial_01.prefab",
                    new Vector3(0f, config.CeilingHeight - 0.9f, z), Vector3.zero, Vector3.one * 1.6f);
                Lamp(group, new Vector3(0f, config.CeilingHeight - 2.4f, z), warm, 15f, 2.6f);
            }

            // Зелёная лампа над дверью — единственная зелёная точка кадра,
            // и она же самый дальний ориентир маршрута.
            Lamp(group, new Vector3(0f, 2.6f, config.HallDepth * 0.5f - 1.1f),
                new Color(0.33f, 0.95f, 0.42f), 14f, 4.2f);

            ConfigureSun();
        }

        /// <summary>
        /// Направленный свет — единственный источник теней в сцене.
        ///
        /// Он не изображает солнце: цех перекрыт, окон нет. Его работа — дать
        /// плитам и игрокам тень, по которой читается, что плита висит над
        /// пустотой, а не лежит на полу. Поэтому перекрытие и весь декор теней
        /// не отбрасывают: иначе крыша погасила бы ровно то, ради чего свет
        /// включён.
        /// </summary>
        private static void ConfigureSun()
        {
            Light sun = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional && light.transform.root.name != "_Arena")
                {
                    sun = light;
                    break;
                }
            }

            if (sun == null)
            {
                notes.Add("В сцене нет направленного света — плиты останутся без теней");
                return;
            }

            sun.transform.rotation = Quaternion.Euler(62f, 24f, 0f);
            sun.color = new Color(0.86f, 0.88f, 0.96f);
            sun.intensity = 0.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.62f;
        }

        // ────────────────────────────── помощники ──────────────────────────────

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("Default");
            return go.transform;
        }

        /// <summary>
        /// Крашеная коробка окружения: без коллайдера, на <c>Default</c>, тени
        /// не отбрасывает. Три этих свойства — договор всей подфазы, и держатся
        /// они здесь, а не на дисциплине вызывающего.
        /// </summary>
        private static GameObject Slab(Transform parent, string name, Vector3 size, Vector3 position,
            Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.layer = LayerMask.NameToLayer("Default");

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            propCount++;
            return go;
        }

        private static GameObject Slab(Transform parent, string name, Vector3 size, Vector3 position,
            MemoryRunPalette.Tone tone)
        {
            return Slab(parent, name, size, position, MemoryRunPalette.Get(tone));
        }

        /// <summary>
        /// Модель пака как декорация. Ставится <b>основанием в точку</b>, а не
        /// центром: пивоты моделей пака стоят где угодно, и посадка по корню
        /// закапывает половину предмета в пол.
        ///
        /// Коллайдеры срезаются всегда: столкновения на арене держит блокаут,
        /// а мешевый коллайдер декорации дал бы вторую поверхность поверх
        /// выверенной прыжком геометрии.
        /// </summary>
        private static GameObject Prop(Transform parent, string path, Vector3 position, Vector3 euler,
            Vector3 scale, MemoryRunPalette.Tone? paint = null)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.rotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider, true);
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            SetLayer(go, LayerMask.NameToLayer("Default"));

            // Перекраска — не украшение, а исправление: пак раздаёт цвета под
            // свой сеттинг, и часть их прямо противоречит брифу. Ферма приезжает
            // деревянной, двутавр и вентиль — красными, а красный здесь занят
            // кромкой провала и больше ничем.
            if (paint.HasValue)
            {
                DressKit.Repaint(go, MemoryRunPalette.Get(paint.Value));
            }

            // Предмет ставится основанием в точку: пивоты моделей пака стоят
            // где угодно, и посадка по корню закапывает половину в пол.
            // У эмиттеров частиц рендереров нет вовсе — они садятся по корню,
            // и это верно: точка эмиссии и есть то, что задаётся.
            if (TryWorldBounds(go, out Bounds bounds))
            {
                var anchor = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                go.transform.position = position + (go.transform.position - anchor);
            }
            else
            {
                go.transform.position = position;
            }

            propCount++;
            return go;
        }

        private static void Lamp(Transform parent, Vector3 position, Color color, float range, float intensity)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = LayerMask.NameToLayer("Default");

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;

            // Тени точечных источников здесь не нужны и стоят дорого: их
            // тридцать с лишним, а тени в сцене даёт направленный свет.
            light.shadows = LightShadows.None;

            lightCount++;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayer(go.transform.GetChild(i).gameObject, layer);
            }
        }

        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
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
        /// Отчёт пересборки: сколько поставлено и что при этом нарушено.
        /// Замечания печатаются списком, а не глотаются: предмет, перекрывший
        /// обзор, и предмет, повисший над зоной выбывания, глазами не ловятся.
        /// </summary>
        internal static string Report()
        {
            var report = new StringBuilder();
            report.Append("🏭 «Рейс на память», окружение и свет 4.3");
            report.Append("\n— предметов и коробок:    ").Append(propCount);
            report.Append("\n— источников света:       ").Append(lightCount);
            report.Append("\n— период вдоль маршрута:  шаг ряда, одинаково на всех десяти пролётах");

            if (notes.Count == 0)
            {
                report.Append("\n— замечаний:              нет ✔");
                return report.ToString();
            }

            report.Append("\n— замечаний:              ").Append(notes.Count).Append(" ✘");
            foreach (string note in notes)
            {
                report.Append("\n   • ").Append(note);
            }

            return report.ToString();
        }
    }
}
