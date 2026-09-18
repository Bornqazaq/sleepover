using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Session;

namespace Igruha.Core.Hub
{
    /// <summary>
    /// Корона чемпиона катки (GDD 3, роадмап 4.13 и 23.5): в хабе победитель
    /// последней доигранной серии ходит с короной, пока не начнётся новая.
    /// При равенстве сумм корон столько, сколько чемпионов, — тай-брейк
    /// боем подушками ещё не собран (IGR-81).
    ///
    /// Живёт только в хабе. В мини-играх силуэты «Дырки в стене» и луч
    /// «Ангелов» читают рендереры персонажа, и лишний меш на голове менял
    /// бы правила игры. Компонент добавляет <see cref="HubBootstrap"/> на
    /// лету: сцена хаба общая, лишняя правка её YAML не нужна.
    ///
    /// Меш и материал строятся кодом: покупного реквизита короны в проекте
    /// нет. Сидит над макушкой капсулы, а не на кости головы: капсулы и
    /// рост заморожены и одинаково устроены у всех восьми, а кости у рига
    /// каждого персонажа повёрнуты по-своему.
    ///
    /// Чисто локальное представление: кто чемпион — решает табло, у всех
    /// машин оно одно, значит и корона у всех на одном и том же.
    /// </summary>
    public sealed class ChampionCrown : MonoBehaviour
    {
        private const int Segments = 16;
        private const int Spikes = 8;
        private const float Radius = 0.115f;
        private const float BandHeight = 0.05f;
        private const float SpikeHeight = 0.075f;

        /// <summary>Зазор между макушкой капсулы и ободом.</summary>
        private const float Lift = 0.01f;

        /// <summary>Как часто сверять список чемпионов с табло.</summary>
        private const float RefreshSeconds = 0.5f;

        private static readonly Color Gold = new Color(1f, 0.78f, 0.22f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private sealed class Crown
        {
            public Transform Root;
            public PlayerController Avatar;
            public CapsuleCollider Capsule;
        }

        private readonly Dictionary<int, Crown> crowns = new Dictionary<int, Crown>(8);
        private readonly List<int> stale = new List<int>(8);
        private Mesh mesh;
        private Material material;
        private float nextRefresh;

        private void Awake()
        {
            mesh = BuildMesh();
            material = BuildMaterial();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, Crown> pair in crowns)
            {
                if (pair.Value.Root != null)
                {
                    Destroy(pair.Value.Root.gameObject);
                }
            }

            crowns.Clear();

            if (mesh != null)
            {
                Destroy(mesh);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                SyncChampions();
            }

            Follow();
        }

        /// <summary>Сверить короны со списком чемпионов табло: снять лишние, надеть недостающие.</summary>
        private void SyncChampions()
        {
            ISessionScoreboard session = SessionScoreboard.Current;
            IReadOnlyList<int> champions = session?.Champions;

            stale.Clear();
            foreach (KeyValuePair<int, Crown> pair in crowns)
            {
                if (champions == null || !Contains(champions, pair.Key) || pair.Value.Root == null)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                Remove(stale[i]);
            }

            if (champions == null || material == null)
            {
                return;
            }

            for (int i = 0; i < champions.Count; i++)
            {
                int id = champions[i];
                PlayerController avatar = session.FindPlayer(id)?.Avatar;
                if (avatar == null)
                {
                    continue;
                }

                if (crowns.TryGetValue(id, out Crown crown))
                {
                    if (crown.Avatar != avatar)
                    {
                        Bind(crown, avatar);
                    }

                    continue;
                }

                crown = new Crown { Root = Build(id) };
                Bind(crown, avatar);
                crowns.Add(id, crown);
                Debug.Log($"👑 корона надета на «{avatar.name}» (игрок {id})");
            }
        }

        private void Follow()
        {
            foreach (KeyValuePair<int, Crown> pair in crowns)
            {
                Crown crown = pair.Value;
                if (crown.Root == null)
                {
                    continue;
                }

                // Аватар мог смениться (новый облик) или пропасть — до
                // следующей сверки корону просто прячем.
                if (crown.Avatar == null || crown.Capsule == null)
                {
                    crown.Root.gameObject.SetActive(false);
                    continue;
                }

                if (!crown.Root.gameObject.activeSelf)
                {
                    crown.Root.gameObject.SetActive(true);
                }

                Transform body = crown.Avatar.transform;
                Vector3 top = body.TransformPoint(crown.Capsule.center + Vector3.up * (crown.Capsule.height * 0.5f));
                crown.Root.SetPositionAndRotation(top + Vector3.up * Lift,
                    Quaternion.Euler(0f, body.eulerAngles.y, 0f));
            }
        }

        private static void Bind(Crown crown, PlayerController avatar)
        {
            crown.Avatar = avatar;
            crown.Capsule = avatar.GetComponent<CapsuleCollider>();
        }

        private void Remove(int id)
        {
            if (crowns.TryGetValue(id, out Crown crown) && crown.Root != null)
            {
                Destroy(crown.Root.gameObject);
            }

            crowns.Remove(id);
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform Build(int playerId)
        {
            var go = new GameObject($"ChampionCrown_{playerId}");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>
        /// Обод с зубцами. Обод — кольцо из <see cref="Segments"/> граней;
        /// зубец — треугольник на каждой второй грани, вершиной вверх.
        /// Обход вершин по часовой стрелке снаружи, чтобы лицевая сторона
        /// смотрела наружу; внутреннюю сторону показывает материал без отсечения.
        /// </summary>
        private static Mesh BuildMesh()
        {
            var vertices = new Vector3[Segments * 2 + Spikes];
            var triangles = new int[Segments * 6 + Spikes * 3];

            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                var ring = new Vector3(Mathf.Cos(angle) * Radius, 0f, Mathf.Sin(angle) * Radius);
                vertices[i] = ring;
                vertices[Segments + i] = ring + Vector3.up * BandHeight;
            }

            int spikeStep = Segments / Spikes;
            for (int i = 0; i < Spikes; i++)
            {
                float angle = (i * spikeStep + spikeStep * 0.5f) * Mathf.PI * 2f / Segments;
                vertices[Segments * 2 + i] = new Vector3(Mathf.Cos(angle) * Radius, BandHeight + SpikeHeight,
                    Mathf.Sin(angle) * Radius);
            }

            int t = 0;
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                int bottom = i, top = Segments + i, bottomNext = next, topNext = Segments + next;
                triangles[t++] = bottom;
                triangles[t++] = top;
                triangles[t++] = bottomNext;
                triangles[t++] = top;
                triangles[t++] = topNext;
                triangles[t++] = bottomNext;
            }

            for (int i = 0; i < Spikes; i++)
            {
                int left = Segments + i * spikeStep;
                int right = Segments + ((i + 1) * spikeStep) % Segments;
                triangles[t++] = left;
                triangles[t++] = Segments * 2 + i;
                triangles[t++] = right;
            }

            var built = new Mesh { name = "ChampionCrown" };
            built.vertices = vertices;
            built.triangles = triangles;
            built.RecalculateNormals();
            built.RecalculateBounds();
            return built;
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("ChampionCrown: шейдер URP Lit не найден — корона не показывается");
                return null;
            }

            var built = new Material(shader) { name = "ChampionCrown" };
            built.SetColor(BaseColorId, Gold);
            built.SetFloat(MetallicId, 0.85f);
            built.SetFloat(SmoothnessId, 0.7f);
            if (built.HasProperty(CullId))
            {
                built.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Off);
            }

            return built;
        }
    }
}
