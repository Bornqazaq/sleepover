using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Видимый конус луча. Нужен потому, что спот-лайт в URP освещает только
    /// поверхности — самого луча в воздухе нет, а без тумана и волюметрики
    /// Водящий не видит, куда светит, и Бегущие не видят, откуда уходить.
    /// На каркасе это ещё и отладочная отрисовка конуса из спеки: по ней
    /// проверяются мёртвые зоны при полном обороте.
    ///
    /// Меш строится из тех же угла и дальности, что и проверка засветки,
    /// поэтому картинка не может разъехаться с правилом. Внутри внешнего
    /// конуса лежит второй, узкий — яркое ядро луча; альфа вершин говорит
    /// шейдеру, какой из них рисуется плотнее. Пылинки в луче (дочерняя
    /// система частиц, если арт её добавил) получают тот же угол и дальность.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class KeeperBeamCone : MonoBehaviour
    {
        private const int Segments = 24;
        private const float CoreRadiusShare = 0.48f;
        private const float OuterShellWeight = 0.5f;
        private const float DustAngleShare = 0.88f;
        private const float DustLengthShare = 0.82f;

        [Tooltip("Прозрачность конуса: он не должен закрывать обзор")]
        [Range(0f, 1f)]
        [SerializeField] private float alpha = 0.22f;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private ParticleSystem dust;
        private Mesh mesh;
        private MaterialPropertyBlock properties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            dust = GetComponentInChildren<ParticleSystem>(true);
            properties = new MaterialPropertyBlock();
        }

        /// <summary>Построить конус под угол (полный, °) и дальность (юниты).</summary>
        public void Build(float coneAngle, float range)
        {
            if (meshFilter == null)
            {
                Awake();
            }

            float radius = range * Mathf.Tan(coneAngle * 0.5f * Mathf.Deg2Rad);

            if (mesh == null)
            {
                mesh = new Mesh { name = "KeeperBeamCone" };
                meshFilter.sharedMesh = mesh;
            }

            // Две оболочки с общей вершиной в точке фонаря, основания — вперёд
            // по локальной +Z. Каждая обходится дважды: конус смотрят и снаружи
            // (Бегущие), и изнутри — камера Водящего стоит ровно в его вершине.
            const int shells = 2;
            int ring = Segments + 1;
            var vertices = new Vector3[shells * (ring + 1)];
            var colors = new Color[vertices.Length];
            var triangles = new int[shells * Segments * 6];
            for (int shell = 0; shell < shells; shell++)
            {
                float shellRadius = shell == 0 ? radius : radius * CoreRadiusShare;
                float weight = shell == 0 ? OuterShellWeight : 1f;
                int apex = shell * (ring + 1);
                vertices[apex] = Vector3.zero;
                colors[apex] = new Color(1f, 1f, 1f, weight);
                for (int i = 0; i <= Segments; i++)
                {
                    float angle = (float)i / Segments * Mathf.PI * 2f;
                    vertices[apex + 1 + i] = new Vector3(Mathf.Cos(angle) * shellRadius, Mathf.Sin(angle) * shellRadius, range);
                    colors[apex + 1 + i] = new Color(1f, 1f, 1f, weight);
                }

                for (int i = 0; i < Segments; i++)
                {
                    int t = (shell * Segments + i) * 6;
                    triangles[t] = apex;
                    triangles[t + 1] = apex + i + 1;
                    triangles[t + 2] = apex + i + 2;

                    triangles[t + 3] = apex;
                    triangles[t + 4] = apex + i + 2;
                    triangles[t + 5] = apex + i + 1;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            ConfigureDust(coneAngle, range);
        }

        public void SetVisible(bool visible)
        {
            if (meshRenderer == null)
            {
                Awake();
            }

            meshRenderer.enabled = visible;
            if (dust != null)
            {
                dust.gameObject.SetActive(visible);
            }
        }

        /// <summary>Цвет конуса — та же обратная связь по счётчику, что и у света (14.7).</summary>
        public void SetColor(Color color)
        {
            // Проверять только рендерер нельзя. Перезагрузка домена — правка
            // скрипта под play-режимом — сохраняет ссылки на компоненты, но
            // обнуляет всё несериализуемое: рендерер выживает, блок свойств
            // пропадает, и по одной ссылке эта дыра не видна.
            if (meshRenderer == null || properties == null)
            {
                Awake();
            }

            color.a = alpha;
            properties.SetColor(BaseColorId, color);
            meshRenderer.SetPropertyBlock(properties);
        }

        private void ConfigureDust(float coneAngle, float range)
        {
            if (dust == null)
            {
                return;
            }

            // Пыль живёт чуть внутри светового конуса: у самой кромки луч уже
            // прозрачный, и висящие там искры выглядели бы как ошибка.
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            shape.angle = coneAngle * 0.5f * DustAngleShare;
            shape.length = range * DustLengthShare;
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
