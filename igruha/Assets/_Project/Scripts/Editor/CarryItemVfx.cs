using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.Traps;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>Own particle recipes; playback remains driven by existing CarryItemEffects events.</summary>
    internal static class CarryItemVfx
    {
        /// <summary>Сколько копий держит каждый пул. Больше — на восьмерых эффекты обрывают сами себя.</summary>
        private const int PoolSize = 4;

        /// <summary>Куда прячется неигранный эффект: под пол, чтобы не мозолить глаза в редакторе.</summary>
        private static readonly Vector3 Parking = new Vector3(0f, -50f, 0f);

        internal static void Build(Transform arena, CarryItemConfig config, GameObject manager,
            BottleStack[] stacks, TrapBase cart, Transform pipe, Transform beam)
        {
            Transform group = ResetGroup(arena, "Effects");

            // Собственные компактные рецепты: не перекрывают доски и столбик воды.
            ParticleSystem[] splashes = Pool(group, "Splash");
            ParticleSystem[] sprays = Pool(group, "Spray");
            ParticleSystem[] dusts = Pool(group, "Dust");
            ParticleSystem[] swooshes = Pool(group, "Swoosh");

            ParticleSystem cartBurst = Spawn(group, "CartBurst");
            if (cartBurst != null && cart != null)
            {
                cartBurst.transform.position = cart.transform.position;
            }

            BuildPipeJet(group, config, pipe);
            BuildBeamTrail(group, beam);
            BuildHaze(group, config);
            BuildChasmHaze(group, config);

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

            ParticleSystem jet = Spawn(group, "PipeJet",
                keepLoop: true, keepAwake: true);
            if (jet == null)
            {
                return;
            }

            float edgeZ = config.NeckWidth * 0.5f + .2f;
            jet.transform.position = new Vector3(
                pipe.position.x, .98f, config.ToMeters(edgeZ));

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

            ParticleSystem trail = Spawn(group, "BeamTrail",
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
                ParticleSystem haze = Spawn(group, $"Haze_{i + 1}",
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

        /// <summary>
        /// Дымка в глубине пропасти — то, чем обрыв отличается от траншеи.
        ///
        /// Пока дно видно целиком, глубина известна, и падение не пугает.
        /// Слой взвеси на середине глубины съедает нижний ярус наполовину: дно
        /// перестаёт читаться разом, и пропасть кажется глубже, чем есть. Сам
        /// нижний ярус при этом остаётся на месте — его видно сквозь дымку и
        /// целиком, если подойти к самой кромке и посмотреть вниз.
        ///
        /// Медленнее и прозрачнее наземной взвеси: та живёт под ногами и должна
        /// быть незаметной, эта висит в яме и должна читаться слоем.
        /// </summary>
        private static void BuildChasmHaze(Transform group, CarryItemConfig config)
        {
            var spots = new[]
            {
                new Vector3(-13f, -6f, -9f),
                new Vector3(-13f, -6f, 9f),
                new Vector3(18f, -6f, -9f),
                new Vector3(18f, -6f, 9f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                ParticleSystem haze = Spawn(group, $"ChasmHaze_{i + 1}",
                    keepLoop: true, keepAwake: true);
                if (haze == null)
                {
                    continue;
                }

                haze.transform.position = new Vector3(
                    config.ToMeters(spots[i].x), config.ToMeters(spots[i].y), config.ToMeters(spots[i].z));

                ParticleSystem.MainModule main = haze.main;
                main.startColor = new Color(0.78f, 0.76f, 0.72f, 0.34f);

                // Тяжесть положительная, а скорость почти нулевая, и это не
                // вкусовщина: масштаб в режиме Hierarchy умножает и скорость, и
                // тяжесть. При подъёме 0.12 и масштабе 2.2 облако за шесть
                // секунд выбиралось из ямы наружу и вставало столбом до 3.12 м —
                // выше предела постоянного эффекта вдвое. Пыль в котловане
                // обязана оседать, а не всплывать.
                main.startSpeed = 0.05f;
                main.startLifetime = 4.5f;
                main.gravityModifier = 0.03f;
            }
        }

        /// <summary>Пул одинаковых эффектов: играются по кругу, стоят под полом, пока не нужны.</summary>
        private static ParticleSystem[] Pool(Transform group, string kind)
        {
            var pool = new ParticleSystem[PoolSize];
            Transform holder = ResetGroup(group, kind);

            for (int i = 0; i < PoolSize; i++)
            {
                pool[i] = Spawn(holder, $"{kind}_{i + 1}");
                if (pool[i] != null)
                {
                    pool[i].transform.position = Parking;
                }
            }

            return pool;
        }

        /// <summary>Создать локальные частицы; события вручную, струя и дымка зациклены.</summary>
        private static ParticleSystem Spawn(Transform parent, string effectName,
            bool keepLoop = false, bool keepAwake = false)
        {
            var go = new GameObject(effectName); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            bool water = effectName.Contains("Splash") || effectName.Contains("Spray") || effectName == "PipeJet";
            bool haze = effectName.Contains("Haze");
            var main=ps.main; main.loop=keepLoop; main.playOnAwake=keepAwake;
            main.duration=1; main.startLifetime=water?.65f:.9f;
            main.startSpeed=water?new ParticleSystem.MinMaxCurve(1,2.7f):new ParticleSystem.MinMaxCurve(.15f,.6f);
            main.startSize=water?new ParticleSystem.MinMaxCurve(.035f,.09f):new ParticleSystem.MinMaxCurve(.08f,.25f);
            main.startColor=water?new Color(.60f,.82f,.94f,.8f):new Color(.76f,.73f,.65f,.22f);
            main.gravityModifier=water?.5f:0;main.maxParticles=haze?100:160;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=30;shape.radius=.1f;
            var emission=ps.emission;emission.rateOverTime=keepLoop?(haze?9:28):0;
            if(!keepLoop) emission.SetBursts(new[]{new ParticleSystem.Burst(0, (short)(water?28:18))});
            if(haze){shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(7,.5f,8);main.startSize=new ParticleSystem.MinMaxCurve(.015f,.055f);}
            if(effectName=="PipeJet")
            {
                main.startSpeed=4.5f;main.startLifetime=1.35f;main.gravityModifier=.12f;
                shape.angle=5;shape.radius=.07f;emission.rateOverTime=70;
                main.startSize=new ParticleSystem.MinMaxCurve(.05f,.13f);
            }
            var life=ps.colorOverLifetime;life.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(0,1)});life.color=gradient;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=ParticleMaterial();
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(water) { renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=1.6f;renderer.velocityScale=.04f; }
            return ps;
        }

        private static Material ParticleMaterial()
        {
            string path=CarrySkyscraperAssets.Materials+"/CS_Particle.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null)
            {
                mat=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",5);mat.SetFloat("_DstBlend",10);mat.SetFloat("_ZWrite",0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;
                mat.SetColor("_BaseColor",Color.white);
                mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(CarrySkyscraperAssets.Art+"/Textures/CS_Particle.png"));
                AssetDatabase.CreateAsset(mat,path);
            }
            return mat;
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
