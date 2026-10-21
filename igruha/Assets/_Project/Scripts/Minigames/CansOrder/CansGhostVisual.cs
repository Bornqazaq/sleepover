using System.Collections.Generic;
using Igruha.Core.Player;
using Igruha.Minigames.Circus;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.CansOrder
{
    /// <summary>Baked render-only avatar: no gameplay components, bones, colliders or shared material edits.</summary>
    public sealed class CansGhostVisual : MonoBehaviour
    {
        private const float Height = .95f, BobHeight = .09f, EyeWidth = .055f, EyeStroke = .012f;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private Renderer[] originals;
        private bool[] originalVisibility;
        private Transform model;
        private ParticleSystem mist;
        private AudioSource voice;
        private AudioClip trickSound;
        private float trickAt = -100f;
        private bool released;
        public int EyeCrossCount { get; private set; }
        public int BodyMeshCount { get; private set; }

        public void Build(PlayerController player, Material bodyMaterial, Material glowMaterial, Material puffMaterial, AudioClip sound)
        {
            originals = player.GetComponentsInChildren<Renderer>(true);
            originalVisibility = new bool[originals.Length];
            var holder = new GameObject("Pale miniature").transform;
            holder.SetParent(transform, false); model = holder;
            var animator = player.GetComponentInChildren<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Vector3 faceRight = head != null ? player.transform.InverseTransformDirection(head.right).normalized : Vector3.right;
            Vector3 faceUp = head != null ? player.transform.InverseTransformDirection(head.up).normalized : Vector3.up;
            Vector3 faceForward = head != null ? player.transform.InverseTransformDirection(head.forward).normalized : Vector3.forward;
            Bounds bodyBounds = default, headBounds = default;
            bool hasBody = false, hasHead = false;
            var baked = new List<(Mesh mesh, Material[] source)>();
            foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || skin.sharedMesh == null || skin.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var mesh = new Mesh { name = "Ghost skin (temporary)" };
                skin.BakeMesh(mesh);
                meshes.Add(mesh);
                Matrix4x4 toPlayer = player.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                BoneWeight[] weights = skin.sharedMesh.boneWeights;
                Transform[] bones = skin.bones;
                // Classify each bone once; seven simultaneous ghosts otherwise repeat
                // native Transform hierarchy queries for every weighted vertex.
                var headBones = new bool[bones.Length];
                if (head != null)
                    for (int b = 0; b < bones.Length; b++)
                        headBones[b] = bones[b] != null && (bones[b] == head || bones[b].IsChildOf(head));
                for (int v = 0; v < vertices.Length; v++)
                {
                    vertices[v] = toPlayer.MultiplyPoint3x4(vertices[v]);
                    if (v < normals.Length) normals[v] = toPlayer.MultiplyVector(normals[v]).normalized;
                    if (!hasBody) { bodyBounds = new Bounds(vertices[v], Vector3.zero); hasBody = true; }
                    else bodyBounds.Encapsulate(vertices[v]);
                    if (head == null || v >= weights.Length || !HeadWeighted(weights[v], headBones)) continue;
                    Vector3 inFace = new Vector3(Vector3.Dot(vertices[v], faceRight),
                        Vector3.Dot(vertices[v], faceUp), Vector3.Dot(vertices[v], faceForward));
                    if (!hasHead) { headBounds = new Bounds(inFace, Vector3.zero); hasHead = true; }
                    else headBounds.Encapsulate(inFace);
                }
                mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
                baked.Add((mesh, skin.sharedMaterials));
            }
            float scale = Height / Mathf.Max(.3f, bodyBounds.size.y);
            Vector3 offset = new Vector3(bodyBounds.center.x, bodyBounds.min.y, bodyBounds.center.z);
            foreach (var item in baked)
            {
                Vector3[] vertices = item.mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - offset) * scale;
                item.mesh.vertices = vertices; item.mesh.RecalculateBounds();
                var go = new GameObject("Ghost body"); go.transform.SetParent(model, false);
                go.AddComponent<MeshFilter>().sharedMesh = item.mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var copies = new Material[item.mesh.subMeshCount];
                for (int i = 0; i < copies.Length; i++)
                {
                    var mat = new Material(bodyMaterial); materials.Add(mat); copies[i] = mat;
                    var src = item.source.Length > 0 ? item.source[Mathf.Min(i, item.source.Length - 1)] : null;
                    if (src != null)
                    {
                        if (src.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                        else if (src.HasProperty("_MainTex")) mat.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                    }
                }
                renderer.sharedMaterials = copies; BodyMeshCount++;
            }
            if (!hasHead)
            {
                faceRight = Vector3.right; faceUp = Vector3.up; faceForward = Vector3.forward;
                headBounds = new Bounds(offset + Vector3.up * bodyBounds.size.y * .88f,
                    Vector3.one * bodyBounds.size.y * .18f);
            }
            Vector3 face = (faceRight * headBounds.center.x +
                faceUp * (headBounds.center.y - headBounds.size.y * .11f) +
                faceForward * headBounds.max.z - offset) * scale + faceForward * .014f;
            float separation = Mathf.Clamp(headBounds.size.x * scale * .15f, .025f, .055f);
            for (int eye = 0; eye < 2; eye++)
            {
                Vector3 center = face + faceRight * (eye == 0 ? -separation : separation);
                Cross(center, faceRight, faceUp, glowMaterial);
            }
            var tail = new GameObject("Wavy spirit tail"); tail.transform.SetParent(model, false);
            Mesh tailMesh = Tail(); meshes.Add(tailMesh);
            tail.AddComponent<MeshFilter>().sharedMesh = tailMesh;
            var tailRenderer = tail.AddComponent<MeshRenderer>();
            var tailMat = new Material(glowMaterial); materials.Add(tailMat);
            tailMat.SetColor("_BaseColor", new Color(.62f, 1f, .92f, .28f)); tailMat.SetFloat("_Wave", .035f);
            tailRenderer.sharedMaterial = tailMat; tailRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var trail = holder.gameObject.AddComponent<TrailRenderer>();
            var trailMaterial = new Material(glowMaterial); materials.Add(trailMaterial);
            trailMaterial.SetFloat("_VertexTint", 1);
            trail.sharedMaterial = trailMaterial; trail.time = .35f; trail.minVertexDistance = .07f;
            trail.startWidth = .1f; trail.endWidth = 0f;
            trail.startColor = new Color(.4f, 1, .9f, .18f); trail.endColor = new Color(.5f, .8f, 1, 0);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            if (puffMaterial != null)
            {
                mist = CircusAngerEffect.CreatePuffs(model, "Spirit sparkles", puffMaterial, false);
                mist.transform.localPosition = Vector3.up * .3f;
                var main = mist.main; main.startColor = new Color(.55f, 1f, .88f, .55f);
                main.startSize = new ParticleSystem.MinMaxCurve(.035f, .09f); main.startSpeed = .15f;
                var emission = mist.emission; emission.rateOverTime = 8f;
                mist.Play(); mist.Emit(20);
            }
            trickSound = sound;
            voice = gameObject.AddComponent<AudioSource>(); voice.spatialBlend = 1f;
            voice.minDistance = 2f; voice.maxDistance = 18f; voice.playOnAwake = false;
            // Hide only after baking: source data remains usable even when the shelf camera had temporarily hidden it.
            for (int i = 0; i < originals.Length; i++)
            {
                originalVisibility[i] = originals[i].forceRenderingOff;
                originals[i].forceRenderingOff = true;
            }
        }

        private static bool HeadWeighted(BoneWeight w, bool[] headBones) =>
            HeadBone(w.boneIndex0, w.weight0, headBones) || HeadBone(w.boneIndex1, w.weight1, headBones) ||
            HeadBone(w.boneIndex2, w.weight2, headBones) || HeadBone(w.boneIndex3, w.weight3, headBones);
        private static bool HeadBone(int index, float weight, bool[] headBones) =>
            weight > .35f && index >= 0 && index < headBones.Length && headBones[index];

        private void Cross(Vector3 center, Vector3 right, Vector3 up, Material glow)
        {
            var mat = new Material(glow); materials.Add(mat);
            mat.SetColor("_BaseColor", new Color(.065f, .03f, .16f, 1f));
            for (int diagonal = 0; diagonal < 2; diagonal++)
            {
                var go = new GameObject("X eye"); go.transform.SetParent(model, false);
                var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
                line.sharedMaterial = mat; line.positionCount = 2; line.numCapVertices = 3;
                line.startWidth = line.endWidth = EyeStroke;
                float direction = diagonal == 0 ? 1f : -1f;
                line.SetPosition(0, center - (right + up * direction) * (EyeWidth * .5f));
                line.SetPosition(1, center + (right + up * direction) * (EyeWidth * .5f));
                line.shadowCastingMode = ShadowCastingMode.Off;
            }
            EyeCrossCount++;
        }

        private static Mesh Tail()
        {
            const int Rings = 9, Segments = 32;
            var vertices = new Vector3[(Rings + 1) * (Segments + 1)];
            var uv = new Vector2[vertices.Length]; var triangles = new int[Rings * Segments * 6];
            for (int r = 0; r <= Rings; r++)
            {
                float t = r / (float)Rings;
                float radius = Mathf.Lerp(.205f, .012f, Mathf.Pow(t, 1.8f));
                for (int j = 0; j <= Segments; j++)
                {
                    float angle = j * Mathf.PI * 2 / Segments; int v = r * (Segments + 1) + j;
                    vertices[v] = new Vector3(Mathf.Cos(angle) * radius + .11f * t * t,
                        .49f - t * .65f + Mathf.Sin(angle * 5f) * .035f * Mathf.Sin(t * Mathf.PI),
                        Mathf.Sin(angle) * radius - .15f * t * t);
                    uv[v] = new Vector2(j / (float)Segments, t);
                    if (r == Rings || j == Segments) continue;
                    int k = (r * Segments + j) * 6, n = v + Segments + 1;
                    triangles[k] = v; triangles[k + 1] = n; triangles[k + 2] = v + 1;
                    triangles[k + 3] = v + 1; triangles[k + 4] = n; triangles[k + 5] = n + 1;
                }
            }
            var mesh = new Mesh { name = "Spirit waves", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        public void Present(Vector3 position, float yaw)
        {
            float age = Time.time;
            transform.SetPositionAndRotation(position + Vector3.up * (Mathf.Sin(age * 2.8f) * BobHeight),
                Quaternion.Euler(0, yaw, 0));
            if (model != null)
            {
                float trick = Mathf.Clamp01((Time.time - trickAt) / CansGhostRules.SwapSeconds);
                model.localRotation = Quaternion.Euler(Mathf.Sin(age * 1.7f) * 4f,
                    trick < 1 ? Mathf.SmoothStep(0, 360f, trick) : 0, Mathf.Sin(age * 2.1f) * 7f);
            }
        }
        public void Trick(int target)
        {
            trickAt = Time.time; mist?.Emit(24);
            if (voice != null && trickSound != null) voice.PlayOneShot(trickSound, .7f);
        }
        public void Release()
        {
            if (released) return; released = true;
            for (int i = 0; originals != null && i < originals.Length; i++)
                if (originals[i] != null) originals[i].forceRenderingOff = originalVisibility[i];
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
            foreach (var material in materials) if (material != null) Destroy(material);
            meshes.Clear(); materials.Clear();
        }
        private void OnDestroy() => Release();
    }
}
