using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Карточки исхода, ореол за ними, облачко-гэг и бархат внутри шкатулок
    /// «Верю / не верю» (IGR-565). Картинки рисует <c>tools/ui/believe_club_art.py</c>,
    /// здесь — импорт, материалы и сборка в сцене.
    ///
    /// <b>Что было.</b> Щит 32 см из трёх серых коробок с «галочкой» из двух
    /// полос со швом; вокруг — искры и облачко из пака Synty, облачко —
    /// гроздь низкополигональных шаров, похожих на камни. Внутри шкатулки —
    /// голое дерево.
    ///
    /// <b>Что стало.</b> Карточка как игральная: кремовая бумага, двойная
    /// золотая рамка, знак в печати; тот же рисунок видит знающий на своём
    /// экране. Ореол цвета исхода, облачко пудры, бордовый бархат по дну,
    /// стенкам и под крышкой. Логика и сеть не тронуты: код игры включает
    /// карточку целиком, поднимает её опору и запускает частицы — всё это
    /// осталось как было.
    /// </summary>
    internal static class BelieveCardArt
    {
        internal const string Folder = "Assets/_Project/Art/BelieveCards";
        private const string Textures = Folder + "/Textures";
        private const string Materials = Folder + "/Materials";

        internal const string WinFace = Textures + "/BC_CardWin.png";
        internal const string LoseFace = Textures + "/BC_CardLose.png";
        private const string BackTexture = Textures + "/BC_CardBack.png";
        private const string HaloTexture = Textures + "/BC_Halo.png";
        private const string PuffTexture = Textures + "/BC_Puff.png";
        private const string VelvetTexture = Textures + "/BC_Velvet.png";

        // ---------- Карточка ----------

        /// <summary>Высота карточки, м. Игральная карта, увеличенная до читаемости с края зоны зрителей.</summary>
        internal const float CardHeight = 0.27f;

        /// <summary>Пропорция рисунка 512×716 — как у игральной карты 63×88 мм.</summary>
        private const float CardAspect = 512f / 716f;

        /// <summary>
        /// Высота центра поднятой карточки над полом, м.
        ///
        /// Камеры сидящих висят на 2 м и смотрят на соперника сверху вниз.
        /// Линия от камеры к подбородку соперника (~1.1 м в 4.3 м от камеры)
        /// над дальней коробкой проходит на 1.47 м. Карточка ниже этой линии —
        /// значит, лицо соперника в момент раскрытия открыто. А это самое
        /// смешное место игры. Прежний подъём ставил центр на 1.61 м — прямо
        /// в лицо.
        /// </summary>
        internal const float CardRevealCenterHeight = 1.28f;

        /// <summary>Ореол больше карточки: край света должен выходить из-за бумаги.</summary>
        private const float HaloScale = 2.3f;

        /// <summary>
        /// Сила ореола. При 1.6 галочку выжигало почти до белого: карточка
        /// встаёт в самое яркое пятно лампы, и ореол складывался с ним.
        /// </summary>
        private const float HaloIntensity = 0.85f;
        private const float HaloAlpha = 0.55f;

        // ---------- Шкатулка ----------

        // Полость шкатулки — из tools/blender/believe_table_props.py:
        // W = 0.432, D = 0.500, H = 0.135, стенка T = 0.020, верх штатной
        // вкладки дна на 0.030. Крышка: низ на 0.139, петля на (W/2, 0, 0.137).
        private const float CavityHalfWidth = 0.196f;
        private const float CavityHalfDepth = 0.230f;
        private const float CavityFloor = 0.030f;
        private const float CavityTop = 0.130f;
        private const float LidUnderside = 0.002f;
        private const float LidCenterFromHinge = -0.216f;

        /// <summary>Зазор бархата от дерева: меньше — мерцает на дистанции, больше — видна щель.</summary>
        private const float LiningGap = 0.0015f;

        private static readonly Color VelvetColor = new Color(0.36f, 0.055f, 0.085f, 1f);

        // ---------- Облачко ----------

        private const int PuffBurst = 26;
        private const float PuffLifetime = 1.35f;

        internal static void EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Materials))
            {
                AssetDatabase.CreateFolder(Folder, "Materials");
            }

            UiTheme.EnsureSprite(WinFace, Vector4.zero, mipmaps: true);
            UiTheme.EnsureSprite(LoseFace, Vector4.zero, mipmaps: true);
            EnsureTexture(BackTexture, TextureWrapMode.Clamp);
            EnsureTexture(HaloTexture, TextureWrapMode.Clamp);
            EnsureTexture(PuffTexture, TextureWrapMode.Clamp);
            EnsureTexture(VelvetTexture, TextureWrapMode.Repeat);
        }

        // ================= Карточка =================

        /// <summary>Лицо, рубашка и ореол под корнем знака. Корень — тот, что включает игра.</summary>
        internal static void BuildCard(Transform root, bool win)
        {
            float width = CardHeight * CardAspect;
            var size = new Vector3(width, CardHeight, 1f);

            // Опора разворачивается к камере осью +Z, а у квада лицо смотрит в −Z.
            // Лицо развёрнуто на 180°, рубашка — нет: её видно, пока карточка
            // поворачивается к зрителю на подъёме.
            Quad(root, "Face", CardMaterial(win ? "BC_CardWin" : "BC_CardLose", win ? WinFace : LoseFace),
                Vector3.zero, Quaternion.Euler(0f, 180f, 0f), size);
            Quad(root, "Back", CardMaterial("BC_CardBack", BackTexture),
                new Vector3(0f, 0f, -0.002f), Quaternion.identity, size);
            Quad(root, "Halo", HaloMaterial(win),
                new Vector3(0f, 0f, -0.012f), Quaternion.Euler(0f, 180f, 0f),
                new Vector3(CardHeight * HaloScale, CardHeight * HaloScale, 1f));
        }

        /// <summary>Сколько поднять опору, чтобы центр карточки встал на <see cref="CardRevealCenterHeight"/>.</summary>
        internal static float RevealLift(float pivotHomeHeight)
        {
            return Mathf.Max(0f, CardRevealCenterHeight - pivotHomeHeight);
        }

        private static Material CardMaterial(string name, string texturePath)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.32f);
            material.SetFloat("_Metallic", 0f);

            // Скругление углов — вырезом по альфе: карточка остаётся непрозрачной
            // и сортируется как обычная геометрия.
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");

            // Свечения у карточки нет намеренно. Пробовали подсветить её самой
            // картинкой — у здешнего URP/Lit нет переключателя «_EmissionEnabled»,
            // и проверка материала гасит ключевое слово при каждом сохранении:
            // в гите это давало бы вечно пляшущий .mat. Карточка поднимается
            // внутрь конуса лампы и читается его светом.

            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material HaloMaterial(bool win)
        {
            Material material = LoadOrCreate(win ? "BC_HaloWin" : "BC_HaloLose", "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(HaloTexture));
            Color tone = win ? UiTheme.Good : UiTheme.Bad;
            material.SetColor("_BaseColor", new Color(tone.r * HaloIntensity, tone.g * HaloIntensity, tone.b * HaloIntensity, HaloAlpha));
            Transparent(material, additive: true);
            return material;
        }

        // ================= Облачко =================

        /// <summary>
        /// Облачко пудры в лицо проигравшему. Объект и имя прежние — на
        /// <c>gagPuff</c> ссылается <c>BelieveBox</c>, игра зовёт только Play().
        /// </summary>
        internal static ParticleSystem BuildPuff(Transform parent, float boxSize)
        {
            var go = new GameObject("GagPuff");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, boxSize * 0.45f, 0f);
            go.transform.localRotation = Quaternion.Euler(-60f, 0f, 0f);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(PuffLifetime * 0.7f, PuffLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.30f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.97f, 0.94f, 0.88f, 0.95f), new Color(0.86f, 0.82f, 0.76f, 0.85f));
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = PuffBurst * 2;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, PuffBurst) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.08f;

            // Клуб тормозит, раздувается и тает — «пуф», а не струя.
            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.25f;
            limit.dampen = 0.18f;

            ParticleSystem.SizeOverLifetimeModule growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1.6f), new Keyframe(1f, 2.6f)));

            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.92f, 0.9f, 0.86f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            // Четыре кадра на листе 2×2: каждая частица берёт случайный клуб.
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
            sheet.cycleCount = 1;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = PuffMaterial();
            renderer.sortingFudge = -10f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static Material PuffMaterial()
        {
            Material material = LoadOrCreate("BC_Puff", "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexture));
            material.SetColor("_BaseColor", Color.white);
            Transparent(material, additive: false);
            return material;
        }

        // ================= Бархат =================

        /// <summary>
        /// Бархат по дну, стенкам и под крышкой. Дочерние модели шкатулки:
        /// едут вместе с ней при обмене и открываются вместе с крышкой.
        /// Коллайдеров нет — это отделка, а не геометрия.
        /// </summary>
        internal static void LineCasket(Transform bodyModel, Transform lidModel)
        {
            Material velvet = VelvetMaterial();
            float width = (CavityHalfWidth - LiningGap) * 2f;
            float depth = (CavityHalfDepth - LiningGap) * 2f;
            float floor = CavityFloor + LiningGap;
            float wallHeight = CavityTop - floor;
            float wallY = floor + wallHeight * 0.5f;

            var lining = Group(bodyModel, "Velvet");
            Quad(lining, "Floor", velvet, new Vector3(0f, floor, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(width, depth, 1f));
            Quad(lining, "WallRight", velvet, new Vector3(CavityHalfWidth - LiningGap, wallY, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(depth, wallHeight, 1f));
            Quad(lining, "WallLeft", velvet, new Vector3(-(CavityHalfWidth - LiningGap), wallY, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(depth, wallHeight, 1f));
            Quad(lining, "WallFar", velvet, new Vector3(0f, wallY, CavityHalfDepth - LiningGap), Quaternion.identity, new Vector3(width, wallHeight, 1f));
            Quad(lining, "WallNear", velvet, new Vector3(0f, wallY, -(CavityHalfDepth - LiningGap)), Quaternion.Euler(0f, 180f, 0f), new Vector3(width, wallHeight, 1f));

            var lidLining = Group(lidModel, "Velvet");
            Quad(lidLining, "Underside", velvet, new Vector3(LidCenterFromHinge, LidUnderside - LiningGap, 0f),
                Quaternion.Euler(-90f, 0f, 0f), new Vector3(width, depth, 1f));
        }

        private static Material VelvetMaterial()
        {
            Material material = LoadOrCreate("BC_Velvet", "Universal Render Pipeline/Lit");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(VelvetTexture));
            material.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            material.SetColor("_BaseColor", VelvetColor);
            material.SetFloat("_Smoothness", 0.22f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ================= Общее =================

        private static Transform Group(Transform parent, string name)
        {
            Transform previous = parent.Find(name);
            if (previous != null)
            {
                Object.DestroyImmediate(previous.gameObject);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return go.transform;
        }

        private static void Quad(Transform parent, string name, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.layer = parent.gameObject.layer;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localRotation = rotation;
            quad.transform.localScale = scale;

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material LoadOrCreate(string name, string shaderName)
        {
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader == null || material.shader.name != shaderName)
            {
                material.shader = Shader.Find(shaderName);
            }

            return material;
        }

        /// <summary>Прозрачный материал частиц — тем же способом, что MemoryFoundryAtmosphere.</summary>
        private static void Transparent(Material material, bool additive)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        private static void EnsureTexture(string path, TextureWrapMode wrap)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null)
            {
                throw new System.InvalidOperationException("Нет текстуры: " + path);
            }

            if (importer.textureType == TextureImporterType.Default && importer.wrapMode == wrap &&
                importer.alphaIsTransparency && importer.mipmapEnabled)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = wrap;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}
