using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.Exam;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Original procedural dust and pit haze. Effects use the existing local
    /// door state on host and client; no separate result timer or network state.
    /// Systems live outside moving leaves and use our own radial alpha texture.
    /// </summary>
    internal static class ExamVfx
    {
        private const string DustPath = "OriginalDust";
        private const string GlowPath = "OriginalGlow";

        /// <summary>
        /// Размер частицы пыли. Подобран под дистанцию камеры зала — пятнадцать
        /// метров до платформ, — а не по чужому числу из другой игры.
        /// </summary>
        private const float DustSize = 0.30f;

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

            var previous = arena.transform.Find("Effects");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
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
            BuildSunDust(root);
            var allSystems=root.GetComponentsInChildren<ParticleSystem>(true);systems=allSystems.Length;capacity=0;
            foreach(var system in allSystems)capacity+=system.main.maxParticles;

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
                new Vector3(0f, 0.9f, 0f), 60, .7f, 1.2f);

            // Труха по периметру проёма, сыплющаяся вниз. Скорость направлена
            // в яму: это то, что делает провал провалом, а не исчезновением.
            ParticleSystem rim = Dust(group, "RimDust", centre + new Vector3(0f, -0.15f, 0f),
                new Vector3(config.PlatformWidth * 0.98f, 0.1f, config.PlatformDepth * 0.98f),
                new Vector3(0f, -2.2f, 0f), 48, 1.2f, 1.6f);

            // Хлопок при закрытии: короче и выше, чем труха.
            ParticleSystem close = Dust(group, "CloseDust", centre + new Vector3(0f, 0.06f, 0f),
                new Vector3(config.PlatformWidth * 0.98f, 0.05f, config.PlatformDepth * 0.98f),
                new Vector3(0f, 0.6f, 0f), 40, .6f, 1f);

            var mechanism=platform.GetComponent<ExamHatchMechanism>();
            if(mechanism!=null)
            {
                var steam=Dust(group,"PressureRelease",centre+Vector3.down*.15f,
                    new Vector3(config.PlatformWidth*.9f,.08f,config.PlatformDepth*.80f),Vector3.up*2.8f,32,.4f,.85f);
                var sm=steam.main;sm.startSize=new ParticleSystem.MinMaxCurve(.25f,.58f);sm.startColor=new Color(.78f,.72f,.59f,.23f);
                mechanism.Steam=steam;
                var paper=Dust(group,"ExamPapers",centre+Vector3.down*.1f,
                    new Vector3(config.PlatformWidth*.75f,.08f,config.PlatformDepth*.7f),Vector3.up*2.2f,18,.5f,2.4f);
                var pm=paper.main;pm.startSize=new ParticleSystem.MinMaxCurve(.7f,1.3f);pm.gravityModifier=.32f;
                pm.startRotation3D=true;pm.startRotationX=new ParticleSystem.MinMaxCurve(-3.14f,3.14f);
                pm.startRotationY=new ParticleSystem.MinMaxCurve(-3.14f,3.14f);
                var rot=paper.rotationOverLifetime;rot.enabled=true;rot.separateAxes=true;
                rot.x=new ParticleSystem.MinMaxCurve(-4f,4f);rot.y=new ParticleSystem.MinMaxCurve(-3f,3f);rot.z=new ParticleSystem.MinMaxCurve(-2f,2f);
                var pr=paper.GetComponent<ParticleSystemRenderer>();pr.renderMode=ParticleSystemRenderMode.Mesh;
                pr.mesh=PaperMesh();pr.sharedMaterial=ExamHallAssets.Material("Paper");
                var fade=paper.colorOverLifetime;fade.enabled=false;
                mechanism.Papers=paper;EditorUtility.SetDirty(mechanism);
            }
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
            lamp.color = new Color(0.72f, 0.62f, 0.43f);
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
        /// Собственная пылевая система: один залп по событию открытия или закрытия.
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
            main.startColor = new Color(0.64f, 0.52f, 0.36f, 0.32f);
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
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = position;
            var effect = go.AddComponent<ParticleSystem>();
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = effect.main; main.playOnAwake = false; main.loop = false;
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = DustMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var fade = effect.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            return effect;
        }

        private static Material DustMaterial()
        {
            string path = ExamHallAssets.Materials + "/EH_Dust.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool createMaterial=material==null;
            if(createMaterial) material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            string texPath = ExamHallAssets.Art + "/Textures/EH_Dust.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            bool createTexture=tex==null;
            {
                const int n = 64;
                if(createTexture)tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "EH_Dust", wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[n*n];
                for (int y=0;y<n;y++) for(int x=0;x<n;x++)
                {
                    float r = new Vector2((x+.5f)/n*2-1,(y+.5f)/n*2-1).magnitude;
                    float noise=Mathf.Clamp01((.60f*Mathf.PerlinNoise(3.1f+x*.07f,9.7f+y*.07f)+.30f*Mathf.PerlinNoise(13.4f+x*.19f,2.3f+y*.19f)+.10f*Mathf.PerlinNoise(5.7f+x*.41f,8.9f+y*.41f))*1.8f-.3f);
                    float cloud=Mathf.Pow(Mathf.Clamp01(1-r),1.45f)*Mathf.Clamp01(noise);
                    pixels[y*n+x] = new Color(1,1,1,cloud);
                }
                tex.SetPixels(pixels); tex.Apply(); if(createTexture)AssetDatabase.CreateAsset(tex,texPath);else EditorUtility.SetDirty(tex);
            }
            material.SetTexture("_BaseMap",tex); if(createMaterial)AssetDatabase.CreateAsset(material,path);else EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh PaperMesh()
        {
            string path=ExamHallAssets.Art+"/Models/EH_ParticleSheet.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null)return mesh;
            // Metre-scale copy of the same folded sheet authored in the Blender kit.
            mesh=new Mesh{name="EH_ParticleSheet"};
            mesh.vertices=new[]{new Vector3(-.12f,0,-.17f),new Vector3(.12f,.02f,-.17f),new Vector3(.12f,.045f,0),new Vector3(-.12f,.025f,0),new Vector3(-.12f,-.005f,.17f),new Vector3(.12f,.015f,.17f)};
            mesh.triangles=new[]{0,2,1,0,3,2,3,5,2,3,4,5,0,1,2,0,2,3,3,2,5,3,5,4};
            mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        private static void BuildSunDust(Transform root)
        {
            var dust=Dust(root,"SunMotes",new Vector3(0,2.8f,-1),new Vector3(15,4.2f,14),Vector3.up*.06f,0,8,10);
            var main=dust.main;main.loop=true;main.playOnAwake=true;main.startSize=new ParticleSystem.MinMaxCurve(.014f,.035f);
            main.startColor=new Color(1,.85f,.54f,.30f);main.maxParticles=90;main.gravityModifier=0;
            var em=dust.emission;em.rateOverTime=8;
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
            report.Append("✨ «Экзамен», собственные эффекты — ")
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
