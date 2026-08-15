using UnityEngine;

namespace Igruha.Minigames.CryingAngels
{
    /// <summary>
    /// Видимый конус луча. Нужен потому, что спот-лайт в URP освещает только
    /// поверхности — самого луча в воздухе нет, а без тумана и волюметрики
    /// (это арт-фаза) Водящий не видит, куда светит, и Бегущие не видят,
    /// откуда уходить. На каркасе это ещё и отладочная отрисовка конуса из
    /// спеки: по ней проверяются мёртвые зоны при полном обороте.
    ///
    /// Меш строится из тех же угла и дальности, что и проверка засветки,
    /// поэтому картинка не может разъехаться с правилом.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class KeeperBeamCone : MonoBehaviour
    {
        private const int Segments = 24;

        [Tooltip("Прозрачность конуса: он не должен закрывать обзор")]
        [Range(0f, 1f)]
        [SerializeField] private float alpha = 0.22f;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock properties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
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

            // Вершина в точке фонаря, основание — вперёд по локальной +Z.
            var vertices = new Vector3[Segments + 2];
            vertices[0] = Vector3.zero;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = (float)i / Segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, range);
            }

            // Два обхода: конус смотрят и снаружи (Бегущие), и изнутри — камера
            // Водящего стоит ровно в его вершине.
            var triangles = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                int t = i * 6;
                triangles[t] = 0;
                triangles[t + 1] = i + 1;
                triangles[t + 2] = i + 2;

                triangles[t + 3] = 0;
                triangles[t + 4] = i + 2;
                triangles[t + 5] = i + 1;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        public void SetVisible(bool visible)
        {
            if (meshRenderer == null)
            {
                Awake();
            }

            meshRenderer.enabled = visible;
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

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
