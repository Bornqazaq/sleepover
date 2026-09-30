using UnityEngine;

namespace Igruha.Minigames.Mosquitoes
{
    public sealed class MosquitoBiteFeedback : MonoBehaviour
    {
        private LineRenderer ring;
        private readonly LineRenderer[] sparks = new LineRenderer[8];
        private Material material;
        private float age;
        public static void Show(Vector3 position)
        {
            var go = new GameObject("ConfirmedBite"); go.transform.position = position;
            go.AddComponent<MosquitoBiteFeedback>();
        }
        private void Awake()
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            ring = Line("ImpactRing", 33, .014f);
            for (int i = 0; i < sparks.Length; i++) sparks[i] = Line("ImpactRay", 2, .017f);
        }
        private LineRenderer Line(string name, int count, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
            line.useWorldSpace = false; line.positionCount = count; line.widthMultiplier = width;
            line.numCapVertices = 3; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; return line;
        }
        private void Update()
        {
            age += Time.deltaTime; float t = age / .65f;
            if (t >= 1) { Destroy(gameObject); return; }
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
            float radius = Mathf.Lerp(.055f, .24f, Mathf.Sqrt(t));
            Color color = Color.Lerp(new Color(1, .97f, .64f), new Color(1, .25f, .08f), t);
            color.a = 1 - t; ring.startColor = ring.endColor = color;
            for (int i = 0; i < 33; i++)
            { float a = i * Mathf.PI * 2 / 32; ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius); }
            for (int i = 0; i < sparks.Length; i++)
            {
                float a = i * Mathf.PI * .25f; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                sparks[i].SetPosition(0, d * (radius + .025f)); sparks[i].SetPosition(1, d * (radius + .085f * (1 - t)));
                sparks[i].startColor = sparks[i].endColor = color;
            }
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
