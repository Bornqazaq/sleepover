using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты «Дырки в стене» — подфаза 4.4. Расставляет партиклы пака
    /// <c>POLYGON Particle FX</c> и связывает их с
    /// <see cref="HoleInWallEffects"/>, который пускает их по событиям игры.
    ///
    /// <b>Ни один эффект не вложен в игровой объект.</b> Все они лежат
    /// отдельной группой <c>_Effects</c> и лишь переставляются в нужную точку
    /// перед запуском. Причина ровно та, из-за которой правило и появилось:
    /// вспышка, вложенная в контур выреза, гаснет вместе со стеной — то есть
    /// ровно в тот момент, ради которого её ставили, — а облако удара,
    /// вложенное в аватар, уезжает вместе с ним на платформу через четыре
    /// секунды.
    ///
    /// <b>Партиклы пака приходят с автостартом и зацикливанием.</b> Оставить
    /// их так — значит получить четыре десятка вечно работающих систем: восемь
    /// всплесков посреди сухой воды, восемь ударов в пустоту. Поэтому каждому
    /// одноразовому эффекту здесь снимаются <c>playOnAwake</c> и <c>loop</c>,
    /// и запускает его только код по событию.
    ///
    /// <b>Зацикленных ровно два вида, и оба намеренно.</b> Туман над водой —
    /// не событие, а воздух студии, он обязан идти всегда. Искры на тросе идут,
    /// пока трос натянут, и гасятся тем же кодом, что их зажёг.
    ///
    /// <b>Света эффекты не добавляют ни одного.</b> Дополнительных источников
    /// в сцене ровно четыре — по софиту на дорожку, — и это предел URP-ассета,
    /// а не округление: вода бассейна один меш на всю арену, пятый свет на ней
    /// не отрисуется и одна из дорожек молча потеряет цвет
    /// (<see cref="HoleInWallEnvironment"/>). Свечение эффектов даёт bloom
    /// студийного Volume, а не лампы.
    /// </summary>
    internal static class HoleInWallVfx
    {
        private const string EffectsRoot = "_Effects";

        /// <summary>Мест на дорожке: пара. Столько же вырезов у её стены.</summary>
        private const int SlotsPerTrack = 2;

        private const string Pack = "Assets/Synty/PolygonParticleFX/Prefabs/";

        /// <summary>
        /// Всплеск с расходящимися кругами одним префабом: у него внутри и
        /// брызги, и кольца по зеркалу воды, и водяная пыль. Собирать то же
        /// самое из трёх отдельных эффектов значило бы синхронизировать их
        /// запуск руками.
        /// </summary>
        private const string SplashPrefab = Pack + "FX_Impact_Water_Ripple_01.prefab";

        /// <summary>Удар по телу: щепки и пыль широким конусом.</summary>
        private const string ImpactPrefab = Pack + "FX_Impact_Large_01.prefab";

        /// <summary>Вспышка в пройденном вырезе.</summary>
        private const string FlashPrefab = Pack + "FX_Sparkle_Orbit_01.prefab";

        /// <summary>
        /// Свечение на натянутом тросе.
        ///
        /// <b>Не искры пака.</b> `FX_Sparks_01` — это сварка: чешуйки по 5–30 см
        /// узким конусом. Трос натягивается на 4.32 м между двумя людьми,
        /// и смотрит на него игрок с той же платформы, метров с шести —
        /// на рендере даже втрое увеличенный сноп давал одну белую точку.
        /// У дуги размер частицы 1 м, и «трос на пределе» она говорит прямее:
        /// не сыплется, а трещит.
        /// </summary>
        private const string TetherPrefab = Pack + "FX_Electricity_02.prefab";

        /// <summary>След зеркального переворота: широкий мазок поперёк стены.</summary>
        private const string MirrorPrefab = Pack + "FX_Slash_Large_01.prefab";

        /// <summary>Смена формы: шестиугольники, сходящиеся к центру стены.</summary>
        private const string MorphPrefab = Pack + "FX_Hexagon_02.prefab";

        /// <summary>
        /// Туман над водой.
        ///
        /// <b>Не «туман» пака.</b> У <c>FX_Fog_Small_01</c> прозрачность по
        /// жизни упирается в <b>0.02</b> — он рассчитан на клоки в 20–50 м,
        /// которыми затягивают целую локацию. На размере, который здесь
        /// разрешён (клок не имеет права дорасти до пола платформы), от него
        /// не остаётся ничего: замер по кадру дал 1 % изменившихся пикселей
        /// при пятистах частицах и белом цвете в полную силу, то есть его
        /// не видно вовсе. У пара прозрачность выходит на единицу, и на низкой
        /// плотности он даёт ровно ту дымку над водой, которую просит бриф.
        /// </summary>
        private const string MistPrefab = Pack + "FX_Steam_03.prefab";

        // ========== РАЗМЕРЫ ЭФФЕКТОВ ==========
        //
        // Масштаб корня, а не правка каждой системы внутри префаба: у эффектов
        // пака размеры, скорости и гравитация настроены друг под друга, и
        // тянуть одно число из десяти — верный способ получить брызги, которые
        // летят как камни.

        /// <summary>
        /// Во сколько раз всплеск крупнее пакового.
        ///
        /// Паковый — на попадание пули в лужу: капли по 2 см, кольца по метру.
        /// Сюда с высоты 4 ШП падает человек, и на своём размере всплеск
        /// терялся в кадре целиком.
        /// </summary>
        private const float SplashScale = 5f;

        /// <summary>
        /// Во сколько раз клок брызг крупнее пакового.
        ///
        /// Отдельно от <see cref="SplashScale"/>, потому что делает разное:
        /// масштаб корня растягивает и разлёт, и скорости, а это — только
        /// размер самого клока. На одном масштабе всплеск выходил редкой
        /// цепочкой капель: разлёт правильный, а воды в нём нет.
        /// </summary>
        private const float SplashSizeBoost = 1.4f;

        /// <summary>Удар по телу берётся почти как есть: конус пака и так с человека.</summary>
        private const float ImpactScale = 1.2f;

        /// <summary>Радиус, по которому разлетаются искры вспышки, м. Под самый узкий силуэт.</summary>
        private const float FlashRadius = 0.9f;

        /// <summary>Размер искры вспышки, м.</summary>
        private const float FlashSizeMin = 0.35f;

        private const float FlashSizeMax = 0.75f;

        /// <summary>
        /// Во сколько раз крупнее пакового разряд на тросе. Подобран рендером
        /// с ракурса игрока: дуга должна занимать заметную часть верёвки,
        /// но не выходить за пару.
        /// </summary>
        private const float TetherScale = 1.8f;

        /// <summary>Мазок переворота растягивается на ширину стены: 3 даёт мазок в 9 м при плите в 8.64.</summary>
        private const float MirrorScale = 3f;

        /// <summary>Кольцо шестиугольников пака — радиусом 6 м, стена ниже: поджимаем к её высоте.</summary>
        private const float MorphScale = 0.6f;

        // ========== ТУМАН ==========

        /// <summary>
        /// Насколько облако тумана поднято над зеркалом воды, м. Не ноль:
        /// половина облака иначе уходит под воду и пропадает.
        /// </summary>
        private const float MistLift = 0.1f;

        /// <summary>Толщина слоя тумана, м: он стелется, а не клубится столбом.</summary>
        private const float MistLayer = 0.3f;

        /// <summary>
        /// Размер клока тумана, м. Потолок здесь не эстетика, а правило брифа:
        /// между игроком и стеной не должно быть ничего выше пола платформы.
        ///
        /// Считается насквозь: верх короба рождения (<c>WaterSurfaceY + 0.1 +
        /// 0.15</c>), плюс всплывание за полную жизнь (<c>8 × 0.06</c>), плюс
        /// половина самого крупного клока — итого 0.45 м <b>под</b> полом
        /// платформы. Число печатают замеры арта, и оно обязано остаться
        /// отрицательным: над полом платформы между игроком и стеной нельзя
        /// ничего, а туман идёт постоянно.
        /// </summary>
        private const float MistSizeMin = 2.4f;

        private const float MistSizeMax = 3.4f;

        /// <summary>Сколько клоков рождается в секунду. При жизни 6–8 с в воздухе держится около двух с половиной сотен.</summary>
        private const float MistRate = 34f;

        private const float MistLifeMin = 6f;

        private const float MistLifeMax = 8f;

        /// <summary>Потолок числа клоков: паковый в 1000 здесь не нужен и только жрёт заполнение.</summary>
        private const int MistMaxParticles = 400;

        /// <summary>Скорость всплывания, м/с. Почти ноль: туман стоит, а не поднимается к софитам.</summary>
        private const float MistRise = 0.06f;

        /// <summary>
        /// Плотность тумана.
        ///
        /// Низкая намеренно, и не из осторожности. Частицы пака — низкополигональные
        /// меши с жёстким силуэтом, а не мягкие спрайты: на любой заметной
        /// плотности они читаются не дымкой, а хлопьями пены на воде. На 0.14
        /// они складываются в еле заметную взвесь, которую с дорожки видно
        /// сквозь вырез, и объектами уже не читаются.
        /// </summary>
        private const float MistAlpha = 0.14f;

        /// <summary>Цвет тумана: холодный белый. Бирюза воды и так под ним, добавлять её второй раз незачем.</summary>
        private static readonly Color MistColor = new Color(0.82f, 0.96f, 1f, MistAlpha);

        /// <summary>
        /// Цвет брызг: почти белый с холодным сдвигом.
        ///
        /// <b>Не бирюза воды.</b> Первым прогоном всплеск домножался на неё —
        /// и пропал целиком: у <see cref="HoleInWallPalette.Water"/> и цвет
        /// тёмный, и прозрачность 0.45, а шейдеры пака аддитивные, то есть
        /// подмешивают свой цвет к уже яркому зеркалу бассейна. Тёмное
        /// в аддитивном смешении не видно вовсе.
        /// </summary>
        private static readonly Color SplashColor = new Color(0.86f, 0.97f, 1f, 1f);

        /// <summary>Яркость искр троса и вспышки: они читаются свечением, а не размером.</summary>
        private const float SparkAlpha = 1f;

        /// <summary>
        /// Построить эффекты. Зовётся пересборкой арены после павильона: точки
        /// берутся из конфига, а дорожки должны быть уже собраны.
        /// </summary>
        internal static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks)
        {
            var root = new GameObject(EffectsRoot);
            root.transform.SetParent(arena, false);

            int trackCount = config.TrackCount;

            var splashes = new ParticleSystem[trackCount * SlotsPerTrack];
            var impacts = new ParticleSystem[trackCount * SlotsPerTrack];
            var flashes = new ParticleSystem[trackCount * SlotsPerTrack];
            var tethers = new ParticleSystem[trackCount];
            var mirrors = new ParticleSystem[trackCount];
            var morphs = new ParticleSystem[trackCount];

            Transform splashGroup = Group(root.transform, "Splash");
            Transform impactGroup = Group(root.transform, "Impact");
            Transform flashGroup = Group(root.transform, "Flash");
            Transform tetherGroup = Group(root.transform, "Tether");
            Transform trickGroup = Group(root.transform, "Trick");

            for (int track = 0; track < trackCount; track++)
            {
                Color lane = HoleInWallPalette.LaneAccent(track);

                for (int slot = 0; slot < SlotsPerTrack; slot++)
                {
                    int key = track * SlotsPerTrack + slot;
                    splashes[key] = BuildSplash(splashGroup, $"Splash_{track}_{slot}");
                    impacts[key] = BuildImpact(impactGroup, $"Impact_{track}_{slot}");
                    flashes[key] = BuildFlash(flashGroup, $"Flash_{track}_{slot}");
                }

                tethers[track] = BuildTether(tetherGroup, $"Tether_{track}", lane);
                mirrors[track] = BuildMirror(trickGroup, $"Mirror_{track}");
                morphs[track] = BuildMorph(trickGroup, $"Morph_{track}");
            }

            BuildMist(root.transform, config);
            Wire(root, config, tracks, splashes, impacts, flashes, tethers, mirrors, morphs);
        }

        // ========== ОДНОРАЗОВЫЕ ==========

        /// <summary>
        /// Всплеск. Цвет пака домножается, а не заменяется: у брызг внутри
        /// свои прозрачности — кольца на зеркале идут вполсилы, — и плоская
        /// замена превратила бы их в белые обручи.
        /// </summary>
        private static ParticleSystem BuildSplash(Transform parent, string effectName)
        {
            ParticleSystem effect = Spawn(parent, effectName, SplashPrefab, SplashScale);
            if (effect == null)
            {
                return null;
            }

            var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.startSizeMultiplier = SplashSizeBoost;
            }

            Tint(effect, SplashColor);
            return effect;
        }

        /// <summary>
        /// Удар стены по телу. Родная пыль пака — земляная, бежевая; здесь
        /// в тело прилетает белая глянцевая плита, и облако обязано быть её
        /// цвета, иначе читается как падение на грунт.
        /// </summary>
        private static ParticleSystem BuildImpact(Transform parent, string effectName)
        {
            ParticleSystem effect = Spawn(parent, effectName, ImpactPrefab, ImpactScale);
            if (effect == null)
            {
                return null;
            }

            Paint(effect, HoleInWallPalette.Plastic, SparkAlpha);
            return effect;
        }

        /// <summary>
        /// Вспышка в пройденном вырезе — голубым неоном контура. Тот же цвет,
        /// которым обведён сам вырез: вспышка обязана читаться как «контур
        /// полыхнул», а не как новое пятно рядом с ним.
        ///
        /// Разлёт задаётся радиусом формы, а не масштабом корня: у этого
        /// эффекта масштабирование стоит в режиме <c>Shape</c>, и корень тянул
        /// бы форму, не трогая размер искры.
        /// </summary>
        private static ParticleSystem BuildFlash(Transform parent, string effectName)
        {
            ParticleSystem effect = Spawn(parent, effectName, FlashPrefab, 1f);
            if (effect == null)
            {
                return null;
            }

            ParticleSystem.ShapeModule shape = effect.shape;
            shape.radius = FlashRadius;

            ParticleSystem.MainModule main = effect.main;
            main.startSize = new ParticleSystem.MinMaxCurve(FlashSizeMin, FlashSizeMax);

            Paint(effect, HoleInWallPalette.NeonCyan, SparkAlpha);
            return effect;
        }

        /// <summary>
        /// Искры на тросе — цветом дорожки.
        ///
        /// <b>Не половинами пола.</b> Трос один на двоих, а половин пола две —
        /// «своя» и «партнёра», — и любая из них на общем тросе соврала бы
        /// одному из пары. Цвет дорожки не принадлежит никому из двоих и
        /// принадлежит обоим сразу; им же обведены кромка платформы и рамка
        /// табло, так что связь «это наша пара» уже установлена.
        ///
        /// Зацикленность оставлена: искры идут, пока трос натянут, и гасит их
        /// тот же код, что зажёг.
        /// </summary>
        private static ParticleSystem BuildTether(Transform parent, string effectName, Color lane)
        {
            ParticleSystem effect = Spawn(parent, effectName, TetherPrefab, TetherScale, keepLoop: true);
            if (effect == null)
            {
                return null;
            }

            Paint(effect, lane, SparkAlpha);
            return effect;
        }

        /// <summary>
        /// След зеркального переворота — розовым неоном. Тот же цвет, которым
        /// после подвоха моргает контур выреза (<see cref="WallCutout"/>):
        /// подмена рисунка обязана читаться одним цветом, а не двумя разными
        /// на стене и в воздухе.
        /// </summary>
        private static ParticleSystem BuildMirror(Transform parent, string effectName)
        {
            ParticleSystem effect = Spawn(parent, effectName, MirrorPrefab, MirrorScale);
            if (effect == null)
            {
                return null;
            }

            Paint(effect, HoleInWallPalette.NeonPink, SparkAlpha);
            return effect;
        }

        /// <summary>Смена формы — тем же розовым, но сходящимся кольцом: не мазок вбок, а перестройка.</summary>
        private static ParticleSystem BuildMorph(Transform parent, string effectName)
        {
            ParticleSystem effect = Spawn(parent, effectName, MorphPrefab, MorphScale);
            if (effect == null)
            {
                return null;
            }

            Paint(effect, HoleInWallPalette.NeonPink, SparkAlpha);
            return effect;
        }

        // ========== ТУМАН ==========

        /// <summary>
        /// Туман над водой: один короб на весь бассейн.
        ///
        /// <b>Один, а не по клоку на дорожку.</b> Бассейн общий и перегородок
        /// между дорожками нет — четыре отдельных облака дали бы четыре
        /// прямоугольных пятна с сухими промежутками ровно там, где бриф
        /// требует видеть соседей.
        ///
        /// <b>Идёт всегда и зациклен.</b> Это единственное исключение из правила
        /// «партиклы пака переводить в ручной запуск», и оно осознанное: туман
        /// не событие, а воздух студии. Пустить его кодом означало бы, что до
        /// первого раунда бассейн стоит сухой, а в редакторе его не видно
        /// вовсе.
        /// </summary>
        private static void BuildMist(Transform parent, HoleInWallConfig config)
        {
            ParticleSystem effect = Spawn(parent, "Mist", MistPrefab, 1f, keepLoop: true, keepAwake: true);
            if (effect == null)
            {
                return;
            }

            effect.transform.position = new Vector3(0f, config.WaterSurfaceY + MistLift,
                (config.ArenaFarZ + config.ArenaNearZ) * 0.5f);

            // Паковый пар бьёт узким конусом вверх: это струя из трубы. Здесь
            // из неё делается стоячий слой над всем бассейном — короб во всю
            // арену, тяга вверх почти в ноль и притяжение снято совсем.
            // Без последнего клок за восемь секунд жизни уходит на тридцать
            // метров вверх, к самым фермам.
            ParticleSystem.ShapeModule shape = effect.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(config.ArenaWidth, MistLayer, config.ArenaDepth);
            shape.angle = 0f;

            ParticleSystem.MainModule main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize = new ParticleSystem.MinMaxCurve(MistSizeMin, MistSizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(MistLifeMin, MistLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(MistRise * 0.3f, MistRise);
            main.gravityModifier = 0f;
            main.maxParticles = MistMaxParticles;
            main.startColor = MistColor;

            ParticleSystem.EmissionModule emission = effect.emission;
            emission.rateOverTime = MistRate;
        }

        // ========== ОБЩЕЕ ==========

        /// <summary>
        /// Поставить эффект пака и перевести его в ручной запуск.
        ///
        /// Ручной запуск снимается со <b>всех</b> систем внутри префаба, а не
        /// с корневой: у всплеска их три — брызги, кольца и водяная пыль, — и
        /// оставленная зацикленной средняя гоняла бы кольца по сухому месту
        /// вечно.
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
            }

            MarkAsEffect(go);
            return go.GetComponent<ParticleSystem>();
        }

        /// <summary>Домножить цвет всех систем эффекта на заданный: свои прозрачности пака сохраняются.</summary>
        private static void Tint(ParticleSystem effect, Color color)
        {
            var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.startColor = main.startColor.color * color;
            }
        }

        /// <summary>
        /// Перекрасить все системы эффекта в один цвет с заданной плотностью.
        ///
        /// Шлейф красится отдельно от частицы, и забыть его нельзя: у вспышки
        /// в вырезе искры уходят голубыми, а хвосты за ними остаются рыжими
        /// из пака — на белой плите стены это читается как ржавые царапины.
        /// </summary>
        private static void Paint(ParticleSystem effect, Color color, float alpha)
        {
            color.a = alpha;

            var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.startColor = color;

                ParticleSystem.TrailModule trails = systems[i].trails;
                trails.colorOverLifetime = color;
                trails.colorOverTrail = color;
            }
        }

        /// <summary>
        /// Пометить эффект: без коллайдеров, на <c>Default</c>, без теней и без
        /// статической пакетной отрисовки.
        ///
        /// Последнее — не мелочь: у окружения статичность включена, а партикл,
        /// помеченный статичным, Unity вмораживает в общий меш вместе с его
        /// текущим положением, и переставить его перед запуском уже нельзя.
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
                // Свет эффекта съел бы один из четырёх дополнительных
                // источников арены, и одна из дорожек потеряла бы цвет.
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

        private static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        /// <summary>
        /// Связать расставленное с рантайм-компонентом. Ссылками, а не поиском
        /// по сцене: <c>FindObjectsByType</c> в рантайме запрещён правилами
        /// проекта, а расстановка и без того делается здесь.
        /// </summary>
        private static void Wire(GameObject root, HoleInWallConfig config, HoleInWallTrack[] tracks,
            ParticleSystem[] splashes, ParticleSystem[] impacts, ParticleSystem[] flashes,
            ParticleSystem[] tethers, ParticleSystem[] mirrors, ParticleSystem[] morphs)
        {
            var effects = root.AddComponent<HoleInWallEffects>();
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);
            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — эффекты не на что вешать");
            }

            var so = new SerializedObject(effects);
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("config").objectReferenceValue = config;

            FillArray(so.FindProperty("tracks"), tracks);
            FillArray(so.FindProperty("splashes"), splashes);
            FillArray(so.FindProperty("impacts"), impacts);
            FillArray(so.FindProperty("flashes"), flashes);
            FillArray(so.FindProperty("tethers"), tethers);
            FillArray(so.FindProperty("mirrors"), mirrors);
            FillArray(so.FindProperty("morphs"), morphs);

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
    }
}
