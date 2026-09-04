using UnityEditor;
using UnityEngine;
using Igruha.Core.Minigame;
using Igruha.Minigames.Circus;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Эффекты цирковой арены — подфаза 4.4, общие на обе игры.
    ///
    /// Здесь только расстановка и настройка партиклов; когда какой играть —
    /// решает <see cref="CircusEffects"/>, и решает по событиям, которые игра
    /// уже поднимает на каждой машине.
    ///
    /// <b>Автостарт снимается со всех одноразовых.</b> У каждого FX пака
    /// в префабе стоит <c>playOnAwake</c>, а у половины ещё и зацикливание.
    /// Без этой правки арена встретила бы игрока восемью облаками пыли,
    /// искрами и непрерывным конфетти — до того, как хоть что-то произошло.
    /// Зацикленными остаются ровно два: туман ямы и пыль в лучах. Они не
    /// события, а воздух шатра.
    /// </summary>
    internal static class CircusVfx
    {
        private const string Fx = "Assets/Synty/PolygonHorrorCarnival/Prefabs/FX/";

        private const string DustSpotsPath = Fx + "FX_Dust_Spots_01.prefab";
        private const string FogPath = Fx + "FX_Fog_01.prefab";
        private const string SmokeSmallPath = Fx + "FX_Smoke_Small_01.prefab";
        private const string SmokeBlastPath = Fx + "FX_Smoke_Blast_01.prefab";
        private const string SparksPath = Fx + "FX_SparksBurst_01.prefab";
        private const string ImpactPath = Fx + "FX_Gun_Smoke_01.prefab";
        private const string ConfettiPath = Fx + "FX_Confetti_Shower_01.prefab";

        /// <summary>На какой доле высоты фермы висят пятна пыли в лучах софитов.</summary>
        private const float DustHeightFactor = 0.62f;

        /// <summary>Высота тумана над опилками, м. Выше — он лезет в кадр из клетки.</summary>
        private const float FogHeight = 0.45f;

        /// <summary>
        /// Сколько облаков тумана по яме. Было пять на масштабе 2.6–3.2 —
        /// и арена утонула в молоке целиком: пропали и медведь, и клетки,
        /// и полосы купола. Спека 3.3 требует обратного: забег от медведя
        /// обязан читаться. Осталось два облака и вчетверо меньше плотность.
        /// </summary>
        private const int FogPatches = 2;

        /// <summary>Доля от штатного размера частицы у тумана. Атмосфера, а не стена.</summary>
        private const float FogDensity = 0.22f;

        /// <summary>Доля от штатного размера частицы у пыли в лучах.</summary>
        private const float DustDensity = 0.35f;

        internal static void Build(Transform arena, CircusArenaConfig config, CageStation[] cages, PitBear bear)
        {
            Transform root = ResetGroup(arena, "Effects");

            BuildAmbient(root, config);
            ParticleSystem[] descent = BuildDescentDust(cages);
            ParticleSystem landing = Spawn(root, "LandingBurst", SmokeBlastPath, Vector3.zero, 2.2f);
            ParticleSystem sparks = Spawn(root, "TauntSparks", SparksPath, Vector3.zero, 1f);
            ParticleSystem impact = Spawn(root, "CatchImpact", ImpactPath, Vector3.zero, 1.6f);
            ParticleSystem confetti = Spawn(root, "Confetti", ConfettiPath,
                Vector3.up * (config.RiggingHeight - 1.5f), 3.5f);

            Wire(root, cages, bear, descent, landing, sparks, impact, confetti);
        }

        /// <summary>
        /// Постоянное: пыль в лучах софитов и туман по дну ямы. Единственные
        /// два эффекта, которым зацикливание оставлено.
        /// </summary>
        private static void BuildAmbient(Transform root, CircusArenaConfig config)
        {
            Transform group = ResetGroup(root, "Ambient");

            for (int i = 0; i < config.CageAnchorCount; i++)
            {
                float angle = config.GetAnchorAngle(i) + 180f / config.CageAnchorCount;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = dir * (config.CageRingRadius + 1.1f)
                             + Vector3.up * (config.RiggingHeight * DustHeightFactor);
                Thin(Spawn(group, $"BeamDust_{i + 1:00}", DustSpotsPath, at, 1.3f, true), DustDensity);
            }

            // Туман по дну, но низкий и редкий: спека 3.3 требует, чтобы забег
            // от медведя читался.
            Thin(Spawn(group, "PitFog_01", FogPath, Vector3.up * FogHeight, 1.4f, true), FogDensity);
            for (int i = 1; i < FogPatches; i++)
            {
                float angle = 360f / FogPatches * i + 40f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Thin(Spawn(group, $"PitFog_{i + 1:00}", FogPath,
                    dir * (config.PitRadius * 0.6f) + Vector3.up * FogHeight, 1.2f, true), FogDensity);
            }
        }

        /// <summary>
        /// Труха под каждой клеткой. <b>Единственное, что прицеплено к клетке</b>:
        /// она никуда не девается, а труха обязана ехать вместе с ней вниз.
        /// </summary>
        private static ParticleSystem[] BuildDescentDust(CageStation[] cages)
        {
            var result = new ParticleSystem[cages.Length];
            for (int i = 0; i < cages.Length; i++)
            {
                if (cages[i] == null)
                {
                    continue;
                }

                Transform slot = ResetGroup(cages[i].transform, "DescentDust");
                result[i] = Spawn(slot, "Dust", SmokeSmallPath, cages[i].transform.position, 1.4f);
            }

            return result;
        }

        private static ParticleSystem Spawn(Transform parent, string name, string path, Vector3 at,
            float scale, bool ambient = false)
        {
            if (!DressKit.TryLoad(path, out GameObject prefab))
            {
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = at;
            go.transform.localScale = Vector3.one * scale;
            CircusDress.MarkAsScenery(go, false);

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.playOnAwake = ambient;
                if (!ambient)
                {
                    // Одноразовые обязаны быть одноразовыми: зацикленный
                    // «удар» продолжает сыпать искрами весь матч.
                    main.loop = false;
                }
            }

            return systems.Length > 0 ? systems[0] : null;
        }

        /// <summary>
        /// Разредить постоянный эффект: уменьшить частицу и эмиссию.
        ///
        /// Плотность держится <b>числами модуля, а не масштабом объекта</b>.
        /// Масштаб растягивает и объём, и частицу разом: уменьшив его, теряешь
        /// охват вместе с плотностью, а увеличив — получаешь молоко. Здесь
        /// охват задаёт масштаб, а густоту — эти два множителя.
        /// </summary>
        private static void Thin(ParticleSystem system, float density)
        {
            if (system == null)
            {
                return;
            }

            foreach (ParticleSystem child in system.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = child.main;
                main.startSizeMultiplier *= density;
                main.startLifetimeMultiplier *= Mathf.Lerp(1f, 0.7f, 1f - density);

                ParticleSystem.EmissionModule emission = child.emission;
                emission.rateOverTimeMultiplier *= density;
            }
        }

        private static void Wire(Transform root, CageStation[] cages, PitBear bear, ParticleSystem[] descent,
            ParticleSystem landing, ParticleSystem sparks, ParticleSystem impact, ParticleSystem confetti)
        {
            var effects = root.GetComponent<CircusEffects>();
            if (effects == null)
            {
                effects = root.gameObject.AddComponent<CircusEffects>();
            }

            var controller = Object.FindFirstObjectByType<MinigameControllerBase>(FindObjectsInactive.Include);

            var serialized = new SerializedObject(effects);
            SetArray(serialized, "descentDust", descent);
            SetArray(serialized, "cages", cages);
            serialized.FindProperty("landingBurst").objectReferenceValue = landing;
            serialized.FindProperty("tauntSparks").objectReferenceValue = sparks;
            serialized.FindProperty("catchImpact").objectReferenceValue = impact;
            serialized.FindProperty("confetti").objectReferenceValue = confetti;
            serialized.FindProperty("bear").objectReferenceValue = bear;
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedObject serialized, string field, Object[] values)
        {
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static Transform ResetGroup(Transform parent, string name)
        {
            Transform group = parent.Find(name);
            if (group == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                return go.transform;
            }

            for (int i = group.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(group.GetChild(i).gameObject);
            }

            return group;
        }
    }
}
