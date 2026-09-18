using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Core.Ambient;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Авторская композиция высотки (IGR-535, доработка IGR-537). Все размеры — метры.
    ///
    /// Слои сцены сверху вниз: облака и птицы; верхний недостроенный ярус над
    /// периметром; игровой этаж с рабочими зонами; шесть нижних ярусов с сетками
    /// и кабелями; дымка по высоте; улица и стройплощадка на −54 м; кольца
    /// города от ближних корпусов до дальних доминант в тумане. Свет — низкое
    /// тёплое солнце, длинные тени поперёк перекрытия.
    ///
    /// Группы: <c>Structure</c> и <c>WorkAreas</c> — твёрдые предметы с коллизией
    /// (аудит требует её у каждого), <c>Decor</c> — то, что стоит на недосягаемых
    /// ядрах или не имеет тела (разметка, гирлянды, шланги), <c>Horizon</c> —
    /// всё вне перекрытия, без единого коллайдера.
    /// </summary>
    internal static class CarryItemEnvironment
    {
        private const float HalfWidth = 14.4f;
        private const float End = 27.36f;
        private const float FloorHeight = 5.4f;
        private const float StreetY = -54f;
        private const float RouteZ = 5.04f;
        private const float TankX = 20.16f;
        private const int CitySeed = 537;

        /// <summary>Сторона одной планарной UV-плитки набора: разметка ставится за угол, не за центр.</summary>
        private const float DecalTile = 1f / 0.55f;

        internal static void Build(Transform arena, CarryItemConfig config, System.Random rng)
        {
            var root = Group(arena, "Environment");
            var structure = Group(root, "Structure");
            var props = Group(root, "WorkAreas");
            var decor = Group(root, "Decor");
            var backdrop = Group(root, "Horizon");
            BuildStructure(structure);
            BuildUpperStorey(structure, backdrop);
            BuildProps(props);
            BuildDecor(decor);
            BuildDepth(backdrop);
            BuildStreet(backdrop);
            BuildCranes(backdrop);
            BuildSkyline(backdrop);
            BuildSky(backdrop);
            BuildBirds(backdrop);
            BuildLight();
        }

        internal static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }

        internal static Transform Place(Transform parent, string model, Vector3 pos, float yaw = 0, bool solid = true)
        {
            var t = CarrySkyscraperAssets.Place(parent, model, pos, yaw);
            if (solid) AddSolid(t, model);
            return t;
        }

        private static void AddSolid(Transform t, string model)
        {
            var b = CarryItemDress.BoundsOf(t.gameObject);
            var c = t.gameObject.AddComponent<BoxCollider>();
            // Smooth envelopes avoid catching fingers, scaffold braces and handles.
            c.center = t.InverseTransformPoint(b.center);
            var size = b.size;
            if (Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, 90)) < 1 || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, 270)) < 1)
                size = new Vector3(size.z, size.y, size.x);
            c.size = size;
            if (model == "Scaffold") { c.center = new Vector3(0, 3.7f, 0); c.size = new Vector3(3.12f, 7.4f, 1.3f); }
            if (model == "Column") { c.center = new Vector3(0, 3.25f, 0); c.size = new Vector3(.74f, 6.5f, .74f); }
            if (model == "Guardrail") { c.center = new Vector3(0, .62f, 0); c.size = new Vector3(3, 1.24f, .24f); }
            if (model == "FlagMast") { c.center = new Vector3(0, 3, 0); c.size = new Vector3(.3f, 6, .3f); }
            if (model == "FloodTower") { c.center = new Vector3(0, 2.3f, 0); c.size = new Vector3(1.2f, 4.6f, 1.2f); }
            foreach (var tr in t.GetComponentsInChildren<Transform>()) tr.gameObject.layer = LayerMask.NameToLayer("Cover");
        }

        private static Transform Motion(Transform t, AmbientMotion.Mode mode, Vector3 axis, float amplitude, float period,
            float phase = 0, Vector3 secondAxis = default, float secondAmplitude = 0, float duty = .2f)
        {
            t.gameObject.AddComponent<AmbientMotion>().Configure(mode, axis, amplitude, period, phase, secondAxis, secondAmplitude, duty);
            return t;
        }

        // ========== Игровой этаж ==========

        private static void BuildStructure(Transform root)
        {
            // Columns sit outside the circulation corridors and leave an open view between bays.
            foreach (float x in new[] { -25.8f, -16.0f, -3.6f, 7.5f, 18.0f, 25.8f })
            foreach (float z in new[] { -13.5f, 13.5f }) Place(root, "Column", new Vector3(x, 0, z));
            // Только одна полоса кровли над самым началом и полосы по бокам: низкое
            // солнце должно доставать до штабелей, иначе старт стоит в сплошной тени.
            for (int z = 0; z < 7; z++) Slab(root, new Vector3(-25.3f, 7.0f, -12 + z * 4), Vector3.one, true);
            foreach (float x in new[] { -2.8f, 1.2f, 5.2f, 19.8f, 23.8f })
            foreach (float z in new[] { -12.2f, 12.2f }) Slab(root, new Vector3(x, 7.0f, z), Vector3.one, true);
            // Short unfinished walls, rather than a continuous opaque enclosure.
            foreach (float z in new[] { -12.7f, 12.7f })
            {
                Place(root, "Wall", new Vector3(-23, 0, z), z > 0 ? 0 : 180);
                Place(root, "Wall", new Vector3(22.5f, 0, z), z > 0 ? 0 : 180);
            }
            // Continuous rail on the outer edge, with visible physical barrier only where shown.
            for (float x = -25.5f; x < 27; x += 3)
            foreach (float z in new[] { -HalfWidth, HalfWidth }) Place(root, "Guardrail", new Vector3(x, 0, z), z > 0 ? 0 : 180);
            for (float z = -12.9f; z < 14; z += 3)
            foreach (float x in new[] { -End, End }) Place(root, "Guardrail", new Vector3(x, 0, z), 90);
        }

        /// <summary>
        /// Недостроенный ярус над головой: продолжение колонн, арматурные каркасы,
        /// леса и сетки по периметру кровельных полос. Небо над маршрутом остаётся
        /// открытым — и для солнца, и для камеры.
        /// </summary>
        private static void BuildUpperStorey(Transform solid, Transform far)
        {
            foreach (float z in new[] { -13.5f, 13.5f })
            {
                Place(far, "Column", new Vector3(-25.8f, 7.0f, z), 0, false).localScale = new Vector3(1, .8f, 1);
                Place(far, "Column", new Vector3(1.2f, 7.0f, z), 0, false).localScale = new Vector3(1, .7f, 1);
                Place(far, "Column", new Vector3(21.8f, 7.0f, z), 0, false).localScale = new Vector3(1, .75f, 1);
                foreach (float x in new[] { -16.0f, 7.5f, 18.0f }) Place(far, "RebarCage", new Vector3(x, 6.5f, z), 0, false);
                Place(far, "Formwork", new Vector3(5.2f, 7.0f, z * .9f), z > 0 ? 0 : 180, false);
                Place(far, "NetPanel", new Vector3(-2.8f, 7.0f, z * 1.07f), 0, false);
                Place(far, "NetPanel", new Vector3(23.8f, 7.0f, z * 1.07f), 0, false);
            }
            Place(far, "Scaffold", new Vector3(-25.3f, 7.0f, -7), 90, false);
            Place(far, "Tarp", new Vector3(-25.3f, 7.0f, 6), 20, false);
            Place(far, "CementBags", new Vector3(-25.3f, 7.0f, 1.5f), 0, false);
            Place(far, "Tarp", new Vector3(19.8f, 7.0f, 12.2f), -35, false);
            Place(far, "Lumber", new Vector3(23.8f, 7.0f, -12.2f), 0, false);
            // Кабели, свисающие с верхнего яруса к перекрытию: мелкая вертикаль в кадре.
            foreach (float x in new[] { -21.5f, -0.9f, 21.5f })
                Motion(Place(far, "CableDrop", new Vector3(x, 7.0f, 14.0f), 0, false), AmbientMotion.Mode.Sway, Vector3.right, 2.5f, 4.2f, x * .1f, Vector3.forward, 1.5f);
        }

        private static void Slab(Transform root, Vector3 pos, Vector3 scale, bool solid)
        {
            var t = Place(root, "Slab", pos, 0, false); t.localScale = scale;
            if (solid) { var c = t.gameObject.AddComponent<BoxCollider>(); c.center = new Vector3(0, -.36f, 0); c.size = new Vector3(4, .72f, 4); t.gameObject.layer = LayerMask.NameToLayer("Ground"); }
        }

        /// <summary>
        /// Рабочие группы на перекрытии. Всё твёрдое, всё с коллизией. Маршруты
        /// (полосы z=±5.04, горлышко, обходы у бортов), круг хватания у выдачи и
        /// 4.5 м позади рабочих точек остаются свободными — проверяет ArtAudit.
        /// </summary>
        private static void BuildProps(Transform root)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                float z = sign * 10.7f;
                Place(root, "Scaffold", new Vector3(-15.4f, 0, z), sign > 0 ? 0 : 180);
                Place(root, "Scaffold", new Vector3(7.4f, 0, z), sign > 0 ? 0 : 180);
                Place(root, "Lumber", new Vector3(-23f, 0, z), 90);
                Place(root, "CementBags", new Vector3(-18.5f, 0, z));
                Place(root, "CableReel", new Vector3(19.0f, 0, z));
                Place(root, "Workbench", new Vector3(23.3f, 0, sign * 10.5f), 90);
                Place(root, "BrickStack", new Vector3(6.8f, 0, sign * 8.1f));
                Place(root, "Bucket", new Vector3(-22.3f, 0, sign * 8.7f));
                Place(root, "Cone", new Vector3(-14.5f, 0, sign * 8.2f));
                Place(root, "Barricade", new Vector3(-14.35f, 0, sign * 11.5f), 90);
                Place(root, "Barricade", new Vector3(9.0f, 0, sign * 8f), 90);
                Place(root, "CableCoil", new Vector3(24.4f, 0, sign * 8.7f), 0, false);
                // Финишная зона: техника и снабжение позади баков, свет на вечер.
                Place(root, "FloodTower", new Vector3(26.0f, 0, sign * 12.2f));
                Place(root, "Sandbags", new Vector3(18.0f, 0, sign * 13.1f), sign * 40);
                Place(root, "SignBoard", new Vector3(17.6f, 0, sign * 10.6f), 90);
                Place(root, "WaterBarrel", new Vector3(26.3f, 0, sign * 2.2f));
                // Стартовая зона: обеденный уголок и сварочный пост.
                Place(root, "Sandbags", new Vector3(-5.6f, 0, sign * 13.0f), sign * 25);
                Place(root, "WaterBarrel", new Vector3(11.6f, 0, sign * 12.6f));
                Place(root, "Cone", new Vector3(11.0f, 0, sign * 11.9f));
                // Флаги команд у штабелей: команда A слева (+Z), B справа (−Z).
                var mast = Place(root, "FlagMast", new Vector3(-22.6f, 0, sign * 12.6f));
                TeamFlag(mast, sign > 0);
            }
            Place(root, "SiteCabin", new Vector3(-24.4f, 0, 0), 90);
            Place(root, "Generator", new Vector3(22.3f, 0, -11.1f), 15);
            Place(root, "Mixer", new Vector3(-18.2f, 0, 12.25f), -30);
            Place(root, "Wheelbarrow", new Vector3(-20, 0, -8.7f), 115);
            Place(root, "Bench", new Vector3(-25.9f, 0, -4.6f), 0);
            Place(root, "WeldCart", new Vector3(-25.6f, 0, 4.9f), 160);
            Place(root, "GasCylinders", new Vector3(-26.3f, 0, -8.3f), 10);
            Place(root, "Toolbox", new Vector3(-25.1f, 0, -6.6f), 30);
            Place(root, "RebarBundle", new Vector3(-24.3f, 0, -11.6f), 0);
            Place(root, "Tarp", new Vector3(-26.0f, 0, 11.4f), 15);
            Place(root, "CylinderRack", new Vector3(25.6f, 0, -9.4f), 0);
            Place(root, "Compressor", new Vector3(24.9f, 0, 9.7f), -60);
            Place(root, "PortaPotty", new Vector3(26.3f, 0, 6.6f), -90);
            Place(root, "Bench", new Vector3(26.0f, 0, -5.0f), 90);
            Place(root, "Toolbox", new Vector3(23.6f, 0, -12.6f), -20);
            Place(root, "SkipBin", new Vector3(-24.6f, 0, -13.2f), 0);
        }

        /// <summary>
        /// Бестелесное: разметка маршрутов цветом команд (два потока стрелок
        /// пересекаются в горлышке — так и задумано), гирлянды над штабелями и
        /// у баков, шланги от баков, реквизит на верху непроходимых ядер, лужи.
        /// </summary>
        private static void BuildDecor(Transform root)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                bool teamA = sign > 0;
                float s = sign;
                // От штабеля к первой доске, через горлышко наискосок, ко второй доске и к баку.
                Arrow(root, teamA, -17.6f, s * 7.6f, -s * 8);
                Arrow(root, teamA, -15.3f, s * 5.6f, 0);
                Arrow(root, teamA, -4.4f, s * 4.6f, -s * 28);
                Arrow(root, teamA, 0.6f, s * 1.9f, -s * 38);
                Arrow(root, teamA, 5.6f, -s * 1.7f, -s * 40);
                Arrow(root, teamA, 8.2f, -s * 4.6f, -s * 12);
                Arrow(root, teamA, 17.4f, -s * 5.1f, 0);
                Place(root, teamA ? "BuntingA" : "BuntingB", new Vector3(-20.9f, 3.3f, s * 12.9f), 0, false);
                Place(root, teamA ? "BuntingA" : "BuntingB", new Vector3(21.5f, 3.4f, -s * 9.8f), 0, false);
                Place(root, "Hose", new Vector3(23.4f, 0, -s * 3.0f), s * 25, false);
                // На ядрах: недосягаемо, поэтому без коллизии.
                Place(root, "Tarp", new Vector3(2.2f, 2.16f, s * 7.0f), s * 30, false);
                Place(root, "RebarCage", new Vector3(3.3f, 2.16f, s * 10.2f), 0, false);
                Place(root, "Formwork", new Vector3(1.0f, 2.16f, s * 4.6f), s > 0 ? 180 : 0, false);
                Place(root, "Puddle", new Vector3(-16.2f, .012f, sign * 7.7f), 30, false).localScale = new Vector3(1.35f, 1, 1.6f);
                Place(root, "Puddle", new Vector3(20, .012f, sign * 3.0f), -30, false);
            }
            Place(root, "Puddle", new Vector3(-24, 0, 8.3f), 0, false).localScale = new Vector3(1.4f, 1, 1.3f);
        }

        private static void Arrow(Transform root, bool teamA, float x, float z, float yaw)
        {
            var t = Place(root, teamA ? "ArrowA" : "ArrowB", new Vector3(x, .004f, z), yaw, false);
            // Плитка стрелки начинается в углу модели: центрируем её на точке.
            t.GetChild(0).localPosition = new Vector3(-DecalTile * .5f, 0, -DecalTile * .5f);
        }

        private static void TeamFlag(Transform mast, bool teamA)
        {
            var flag = Place(mast, "Flag", new Vector3(.05f, 5.2f, 0), 0, false);
            var material = CarrySkyscraperAssets.Material(teamA ? "TeamA" : "TeamB");
            foreach (var r in flag.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            Motion(flag, AmbientMotion.Mode.Sway, Vector3.up, 14f, 3.1f, teamA ? 0 : .4f, Vector3.forward, 5f);
        }

        // ========== Ниже перекрытия ==========

        private static void BuildDepth(Transform root)
        {
            // Six visible levels with the same two service voids; no collider below the kill volumes.
            for (int level = 1; level <= 6; level++)
            {
                float y = -level * FloorHeight;
                foreach (float x in new[] { -25.2f, -21.2f, -17.2f, -3.0f, 1f, 5f, 19.5f, 23.5f })
                for (int iz = 0; iz < 7; iz++) Slab(root, new Vector3(x, y, -12 + iz * 4), Vector3.one, false);
                foreach (float x in new[] { -25.8f, -15.4f, -4.6f, 8.8f, 17.2f, 25.8f })
                foreach (float z in new[] { -13.5f, -7.5f, 0, 7.5f, 13.5f })
                {
                    var t = Place(root, "Column", new Vector3(x, y, z), 0, false); t.localScale = new Vector3(1, FloorHeight / 6.5f, 1);
                }
                foreach (float x in new[] { -14.7f, 8.5f })
                foreach (float z in new[] { -10.8f, 10.8f }) Place(root, "Scaffold", new Vector3(x, y, z), 90, false).localScale = Vector3.one * .7f;
                // Сетки по фасаду: чередуются, чтобы ярусы читались, а не сливались.
                for (int i = 0; i < 12; i++)
                {
                    if ((i + level) % 3 == 0) continue;
                    float x = -24 + i * 4.4f;
                    Place(root, "NetPanel", new Vector3(x, y, HalfWidth + .2f), 0, false);
                    if ((i + level) % 2 == 0) Place(root, "NetPanel", new Vector3(x, y, -HalfWidth - .2f), 0, false);
                }
                if (level % 2 == 1) { Place(root, "Tarp", new Vector3(-9.5f, y, 9), level * 40, false); Place(root, "CementBags", new Vector3(-8.5f, y, -10), 0, false); }
                else { Place(root, "Lumber", new Vector3(-9.5f, y, -8), 90, false); Place(root, "Formwork", new Vector3(-8, y, 11), 0, false); }
                if (level <= 2) { Place(root, "FloodTower", new Vector3(-1.5f, y, -12.5f), 0, false); Place(root, "WeldCart", new Vector3(14.5f, y, 11), 40, false); }
            }
            foreach (float x in new[] { -12f, 3.5f, 14f })
                Motion(Place(root, "CableDrop", new Vector3(x, -.4f, -HalfWidth - .3f), 0, false), AmbientMotion.Mode.Sway, Vector3.right, 3f, 3.6f, x * .07f, Vector3.forward, 2f);
            // Мусоропровод по фасаду до контейнера на земле и грузовой подъёмник у финиша.
            for (int i = 0; i < 10; i++) Place(root, "ChuteRun", new Vector3(-24.0f, -.3f - i * 5.25f, -HalfWidth - 1.0f), 0, false);
            Place(root, "SkipBin", new Vector3(-24.0f, StreetY, -HalfWidth - 1.6f), 0, false);
            for (int i = 0; i < 11; i++) Place(root, "HoistMast", new Vector3(End + 1.8f, StreetY + i * 5.4f, 0), 0, false);
            Motion(Place(root, "HoistCage", new Vector3(End + 1.8f, -12.0f, 0), 0, false), AmbientMotion.Mode.Bob, Vector3.up, 3.0f, 26f);
            // Дымка по высоте: три листа, сквозь которые улица тонет, а верх города остаётся чистым.
            foreach (float y in new[] { -16f, -28f, -41f })
            {
                var t = Place(root, "HazeSheet", new Vector3(0, y, 0), 0, false); t.localScale = new Vector3(380, 1, 380);
                foreach (var r in t.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            }
        }

        /// <summary>Улица и стройплощадка далеко внизу: то, ради чего в проём страшно смотреть.</summary>
        private static void BuildStreet(Transform root)
        {
            var rng = new System.Random(CitySeed + 1);
            Place(root, "StreetGround", new Vector3(0, StreetY - .05f, 0), 0, false);
            foreach (float z in new[] { -44f, 64f })
            for (int i = -5; i < 5; i++) Place(root, "RoadStrip", new Vector3(i * 60 + 30, StreetY, z), 0, false);
            foreach (float x in new[] { -66f, 74f })
            for (int i = -5; i < 5; i++) Place(root, "RoadStrip", new Vector3(x, StreetY, i * 60 + 30), 90, false);
            foreach (float x in new[] { -66f, 74f }) foreach (float z in new[] { -44f, 64f }) Place(root, "RoadCross", new Vector3(x, StreetY + .01f, z), 0, false);
            // Забор стройплощадки, техника, отвалы, бытовки.
            for (float x = -50; x <= 50; x += 3.5f) foreach (float z in new[] { -34f, 40f }) Place(root, "FencePanel", new Vector3(x, StreetY, z), 0, false);
            for (float z = -32; z <= 40; z += 3.5f) foreach (float x in new[] { -52f, 52f }) Place(root, "FencePanel", new Vector3(x, StreetY, z), 90, false);
            Place(root, "DumpTruck", new Vector3(38, StreetY, -20), 25, false);
            Place(root, "DumpTruck", new Vector3(-38, StreetY, 30), 200, false);
            Place(root, "PumpTruck", new Vector3(-40, StreetY, 12), -80, false);
            Place(root, "Excavator", new Vector3(36, StreetY, 24), 140, false);
            for (int i = 0; i < 3; i++) Place(root, "Container", new Vector3(-45, StreetY, -26 + i * 3), 90, false);
            Place(root, "Container", new Vector3(44, StreetY, 34), 0, false);
            Place(root, "DirtPile", new Vector3(30, StreetY, 32), 0, false);
            Place(root, "DirtPile", new Vector3(-30, StreetY, -28), 60, false).localScale = new Vector3(1.4f, 1.2f, 1.1f);
            Place(root, "DirtPile", new Vector3(46, StreetY, 4), 120, false).localScale = new Vector3(.8f, .9f, .9f);
            for (int i = 0; i < 3; i++) Place(root, "PortaPotty", new Vector3(-47, StreetY, -6 + i * 1.6f), 90, false);
            for (int i = 0; i < 2; i++) Place(root, "SiteCabin", new Vector3(-47, StreetY, 8 + i * 2.6f), 90, false).localScale = Vector3.one * 2.2f;
            Place(root, "SkipBin", new Vector3(42, StreetY, -28), 15, false);
            Place(root, "CementBags", new Vector3(40, StreetY, 10), 0, false); Place(root, "Lumber", new Vector3(40, StreetY, 14), 0, false);
            for (int i = 0; i < 6; i++) Place(root, "RebarBundle", new Vector3(-20 + i * .9f, StreetY, 36), 0, false);
            // Кварталы вокруг: машины у обочин, деревья, фонари.
            for (int i = -6; i <= 6; i++)
            {
                float x = i * 9 + (float)rng.NextDouble() * 3;
                Place(root, "Car", new Vector3(x, StreetY, -44 + (i % 2 == 0 ? 4.2f : -4.2f)), i % 2 == 0 ? 0 : 180, false);
                Place(root, "Car", new Vector3(x + 4, StreetY, 64 + (i % 2 == 0 ? -4.2f : 4.2f)), i % 2 == 0 ? 180 : 0, false);
                Place(root, "Car", new Vector3(-66 + (i % 2 == 0 ? 4.2f : -4.2f), StreetY, i * 9 + 2), 90, false);
            }
            for (int i = -8; i <= 8; i++)
            {
                float x = i * 12 + 6;
                Place(root, "Tree", new Vector3(x, StreetY, -51), i * 37, false).localScale = Vector3.one * (.9f + (float)rng.NextDouble() * .4f);
                Place(root, "Tree", new Vector3(x + 5, StreetY, 71), i * 53, false).localScale = Vector3.one * (.9f + (float)rng.NextDouble() * .4f);
                Place(root, "Tree", new Vector3(-73, StreetY, x), i * 29, false).localScale = Vector3.one * (.9f + (float)rng.NextDouble() * .4f);
                if (i % 2 == 0) { Place(root, "StreetLamp", new Vector3(x, StreetY, -49), 180, false); Place(root, "StreetLamp", new Vector3(x, StreetY, 69), 0, false); }
            }
        }

        /// <summary>Три башенных крана в трёх подвижных частях: мачта от земли, медленно поворачивающаяся стрела, качающийся гак с бадьёй.</summary>
        private static void BuildCranes(Transform root)
        {
            Crane(root, new Vector3(18, 0, -25), -35, 1.08f, 60f, 0f);
            Crane(root, new Vector3(-34, 0, 36), 50, 1.22f, 84f, .37f);
            Crane(root, new Vector3(62, 0, 30), 145, 1.38f, 96f, .71f);
        }

        private static void Crane(Transform root, Vector3 at, float yaw, float mastScale, float slewPeriod, float phase)
        {
            var mast = Place(root, "CraneMast", new Vector3(at.x, StreetY, at.z), yaw, false);
            mast.localScale = new Vector3(1, mastScale, 1);
            float topY = StreetY + 60f * mastScale;
            var top = Place(root, "CraneTop", new Vector3(at.x, topY, at.z), yaw, false);
            Motion(top, AmbientMotion.Mode.Sway, Vector3.up, 24f, slewPeriod, phase);
            Motion(top, AmbientMotion.Mode.Blink, Vector3.up, 1f, 1.6f, phase, default, 0, .18f);
            var hook = Place(top, "CraneHook", new Vector3(19f, 1.1f, 0), 0, false);
            Motion(hook, AmbientMotion.Mode.Sway, Vector3.forward, 2.6f, 5.3f, phase, Vector3.right, 1.8f);
        }

        /// <summary>
        /// Город кольцами: ближние корпуса ниже перекрытия и с деталями, дальше —
        /// выше и бледнее в тумане, на горизонте — доминанты. Ни одного коллайдера.
        /// </summary>
        private static void BuildSkyline(Transform root)
        {
            var rng = new System.Random(CitySeed);
            string[] near = { "CityBrickA", "CityBandA", "CityGlassA", "CitySlab", "CityBrickB", "CityGlassA", "CityBandA", "CityBrickA" };
            string[] mid = { "CityGlassB", "CityGlassC", "CityBandB", "CityGlassA", "CityBrickB", "CityBandA", "CityDark", "CitySlab", "CityGlassB" };
            string[] far = { "CityDark", "CityLandmark", "CityGlassC", "CityBandB", "CityGlassB", "CityDark", "CityGlassC" };
            Ring(root, rng, near, 14, 82, 118, .85f, 1.15f);
            Ring(root, rng, mid, 22, 150, 235, .9f, 1.25f);
            Ring(root, rng, far, 16, 275, 390, 1.0f, 1.4f);
            // Соседняя стройка у дальнего крана и одна на другой стороне.
            Place(root, "CityFrameB", new Vector3(84, StreetY, 44), 20, false);
            Place(root, "CityFrameB", new Vector3(-120, StreetY, -70), -30, false).localScale = new Vector3(1.1f, .8f, 1.1f);
            Place(root, "CityFrame", new Vector3(-58, StreetY, 90), 70, false).localScale = new Vector3(1, 1.3f, 1);
        }

        private static void Ring(Transform root, System.Random rng, string[] types, int count, float minR, float maxR, float minH, float maxH)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2 / count + ((float)rng.NextDouble() - .5f) * (Mathf.PI / count);
                float radius = minR + (float)rng.NextDouble() * (maxR - minR);
                // Соседняя стройка и краны занимают сектор за финишем: башни отступают.
                if (minR < 100 && Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 25)) < 22) radius += 30;
                var pos = new Vector3(Mathf.Cos(a) * radius, StreetY, Mathf.Sin(a) * radius);
                string model = types[(i * 7 + rng.Next(3)) % types.Length];
                var t = Place(root, model, pos, (float)rng.NextDouble() * 360, false);
                float side = .85f + (float)rng.NextDouble() * .3f;
                t.localScale = new Vector3(side, minH + (float)rng.NextDouble() * (maxH - minH), side);
            }
        }

        // ========== Небо и свет ==========

        private static void BuildSky(Transform root)
        {
            // Облака — два плоских слоя над площадкой: нижний плотный, верхний тонкий и быстрее.
            CloudLayer(root, "Clouds_Low", 96f, 420f, 1f, 7f, .0022f, 0);
            CloudLayer(root, "Clouds_High", 150f, 520f, .55f, 12f, .0038f, .5f);
        }

        private static void CloudLayer(Transform root, string name, float y, float span, float alpha, float tiling, float drift, float phase)
        {
            var t = Place(root, "HazeSheet", new Vector3(0, y, 0), 0, false); t.name = name;
            t.localScale = new Vector3(span, 1, span);
            var material = CloudMaterial(name, alpha, tiling);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            Motion(t, AmbientMotion.Mode.ScrollUv, new Vector3(1, .35f, 0), drift, 1f, phase);
        }

        /// <summary>Свой ассет на слой: блок свойств в редакторе не сохраняется, а плотность слоёв разная.</summary>
        private static Material CloudMaterial(string layer, float alpha, float tiling)
        {
            string path = CarrySkyscraperAssets.Materials + "/CS_" + layer + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var source = CarrySkyscraperAssets.Material("Cloud");
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            material.SetColor("_BaseColor", new Color(1, .95f, .88f, alpha));
            // Лист покрывает сотни метров при UV в одну плитку: без тайлинга облако — одно пятно на всё небо.
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildBirds(Transform root)
        {
            var flock = Group(root, "Birds"); flock.localPosition = new Vector3(4, 15, 2);
            for (int i = 0; i < 7; i++)
            {
                var bird = Group(flock, $"Bird_{i + 1}");
                Place(bird, "BirdBody", Vector3.zero, 0, false);
                Place(bird, "BirdWing", Vector3.zero, 0, false).name = "WingL";
                var right = Place(bird, "BirdWing", Vector3.zero, 0, false); right.name = "WingR"; right.localScale = new Vector3(-1, 1, 1);
                bird.localScale = Vector3.one * 1.6f;
            }
            flock.gameObject.AddComponent<AmbientFlock>().Configure(44f, 52f, 5f, 6f);
        }

        /// <summary>
        /// Золотой час. Солнце низко (15°) слева-сзади от бегущего к баку:
        /// длинные тени колонн ложатся поперёк перекрытия, лица освещены,
        /// на обратном пути — контровой блик. Небо процедурное, чтобы диск и
        /// закатная полоса сами следовали за направлением света.
        /// </summary>
        private static void BuildLight()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.name != "CarryItemSkyFill") { sun = l; break; }
            if (sun == null) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.color = new Color(1f, .82f, .62f); sun.intensity = 1.55f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .92f; sun.shadowBias = .035f; sun.shadowNormalBias = .18f;
            sun.transform.rotation = Quaternion.Euler(15, 118, 0);
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.46f, .54f, .70f);
            RenderSettings.ambientEquatorColor = new Color(.66f, .52f, .44f);
            RenderSettings.ambientGroundColor = new Color(.27f, .25f, .27f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.70f, .68f, .70f); RenderSettings.fogStartDistance = 110; RenderSettings.fogEndDistance = 720;
            string skyPath = CarrySkyscraperAssets.Materials + "/CS_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
            // Толщина атмосферы умеренная: при 1.45 весь горизонт заливало жёлтым, и небо теряло синеву.
            sky.SetColor("_SkyTint", new Color(.40f, .50f, .74f)); sky.SetColor("_GroundColor", new Color(.52f, .46f, .46f));
            sky.SetFloat("_Exposure", 1.1f); sky.SetFloat("_AtmosphereThickness", 1.12f); sky.SetFloat("_SunSize", .045f); sky.SetFloat("_SunSizeConvergence", 4f);
            RenderSettings.skybox = sky; EditorUtility.SetDirty(sky);
            var lighting = GameObject.Find("_Lighting");
            var fillObject = GameObject.Find("CarryItemSkyFill");
            if (fillObject == null) fillObject = new GameObject("CarryItemSkyFill");
            fillObject.transform.SetParent(lighting.transform, false);
            var fill = fillObject.GetComponent<Light>();
            if (fill == null) fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(.62f, .74f, 1f);
            fill.intensity = .30f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(40, -62, 0);
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (v.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) Object.DestroyImmediate(v.gameObject);
            var vol = new GameObject("CarryItemGoldenHour").AddComponent<Volume>(); vol.transform.SetParent(lighting.transform, false); vol.isGlobal = true;
            string path = CarrySkyscraperAssets.Materials + "/CS_Daylight.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            if (!profile.TryGet<Tonemapping>(out var tone)) tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            if (!profile.TryGet<ColorAdjustments>(out var color)) color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-.05f); color.contrast.Override(18); color.saturation.Override(8);
            if (!profile.TryGet<Bloom>(out var bloom)) bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.15f); bloom.intensity.Override(.35f); bloom.scatter.Override(.66f);
            if (!profile.TryGet<WhiteBalance>(out var balance)) balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(10f); balance.tint.Override(3f);
            if (!profile.TryGet<Vignette>(out var vignette)) vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.2f); vignette.smoothness.Override(.4f);
            if (!profile.TryGet<SplitToning>(out var toning)) toning = profile.Add<SplitToning>(true);
            toning.shadows.Override(new Color(.38f, .45f, .64f)); toning.highlights.Override(new Color(1f, .82f, .58f)); toning.balance.Override(-12f);
            foreach (var component in profile.components)
                if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
            vol.sharedProfile = profile; EditorUtility.SetDirty(profile);
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) continue;
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }
    }
}
