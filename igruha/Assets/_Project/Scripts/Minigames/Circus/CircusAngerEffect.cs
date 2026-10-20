using System.Collections.Generic;
using Igruha.Core.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Igruha.Minigames.Circus
{
    /// <summary>Head-only skin overlays and steam. Never changes the avatar's materials, clips or prefab.</summary>
    public sealed class CircusAngerEffect : MonoBehaviour
    {
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<ParticleSystem> steam = new List<ParticleSystem>(3);
        private MaterialPropertyBlock block;
        private Transform head, frame;
        private PlayerController owner;
        public float Heat { get; private set; }
        public int OverlayCount => skins.Count;
        public int SteamSourceCount => steam.Count;
        public int ParticleCount { get { int count=0; foreach(var ps in steam) if(ps!=null)count+=ps.particleCount; return count; } }
        public Vector3 HeadPosition => frame != null ? frame.position : owner != null ? owner.Position + Vector3.up * 1.5f : transform.position;

        public void Bind(PlayerController player, CircusAngerLibrary library, Material tint, Material puffs)
        {
            Clear(); owner = player;
            if (player == null || library == null || tint == null || puffs == null) return;
            var animator = player.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            block = block ?? new MaterialPropertyBlock();
            Bounds bounds = new Bounds(head.position, Vector3.zero); bool hasBounds = false;
            foreach (var original in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = library.Find(original.sharedMesh);
                if (mesh == null) continue;
                var go = new GameObject("Circus anger — temporary head tint");
                go.transform.SetParent(original.transform, false);
                var skin = go.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh; skin.bones = original.bones; skin.rootBone = original.rootBone;
                skin.localBounds = original.localBounds; skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = ShadowCastingMode.Off; skin.receiveShadows = false;
                var sourceMaterials = original.sharedMaterials;var copies = new Material[mesh.subMeshCount];
                for (int i=0;i<copies.Length;i++)
                {
                    var mat = new Material(tint);materials.Add(mat);copies[i]=mat;
                    var source = sourceMaterials.Length > 0 ? sourceMaterials[Mathf.Min(i,sourceMaterials.Length-1)] : null;
                    if (source != null)
                    {
                        Texture map=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
                        mat.SetTexture("_BaseMap",map);
                        mat.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.color);
                    }
                }
                skin.sharedMaterials=copies; skins.Add(skin);
                var baked = new Mesh(); skin.BakeMesh(baked, true);
                foreach (var v in baked.vertices)
                {
                    Vector3 world = skin.transform.TransformPoint(v);
                    if (!hasBounds) { bounds=new Bounds(world,Vector3.zero);hasBounds=true; }
                    else bounds.Encapsulate(world);
                }
                Destroy(baked);
            }
            var holder=new GameObject("Circus steam — temporary");frame=holder.transform;frame.SetParent(head,true);
            frame.position = hasBounds ? bounds.center : head.position + Vector3.up * .12f;
            frame.rotation=player.transform.rotation;
            float radius=hasBounds?Mathf.Clamp(Mathf.Min(bounds.extents.x,bounds.extents.z),.10f,.22f):.14f;
            for(int i=0;i<3;i++)
            {
                Vector3 offset=i==0?new Vector3(-radius,.015f,0):i==1?new Vector3(radius,.015f,0):new Vector3(0,-.035f,radius);
                Vector3 direction=i==0?new Vector3(-1,.40f,0):i==1?new Vector3(1,.40f,0):new Vector3(0,.30f,1);
                var ps=CreatePuffs(frame,"Steam "+i,puffs,false);
                ps.transform.localPosition=offset;ps.transform.localRotation=Quaternion.LookRotation(direction);
                steam.Add(ps);
            }
            SetHeat(0);
        }

        public void SetHeat(float value)
        {
            Heat=Mathf.Clamp01(value);
            if (block==null) return;
            block.SetFloat(HeatId,Heat);
            foreach(var skin in skins) if(skin!=null) {skin.enabled=Heat>.001f;skin.SetPropertyBlock(block);}
            foreach(var ps in steam)
            {
                if(ps==null)continue;
                var emission=ps.emission;emission.rateOverTime=Heat> .25f ? 20*Heat : 0;
                if(Heat>.25f && !ps.isPlaying)ps.Play();
            }
        }

        public void Clear()
        {
            Heat=0;
            foreach(var skin in skins) if(skin!=null) {skin.enabled=false;Destroy(skin.gameObject);}
            foreach(var mat in materials) if(mat!=null)Destroy(mat);
            foreach(var ps in steam)if(ps!=null)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(frame!=null) {frame.gameObject.SetActive(false);Destroy(frame.gameObject);}
            skins.Clear();materials.Clear();steam.Clear();frame=null;head=null;owner=null;
        }

        public static ParticleSystem CreatePuffs(Transform parent, string name, Material material, bool burst)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=!burst;main.duration=1;
            main.startLifetime=burst?.65f:.48f;main.startSpeed=burst?1.1f:.65f;
            main.startSize=new ParticleSystem.MinMaxCurve(burst?.18f:.07f,burst?.43f:.14f);
            main.startColor=new Color(.93f,.94f,1,.8f);main.gravityModifier=-.06f;main.maxParticles=100;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=ps.emission;emission.rateOverTime=0;
            var shape=ps.shape;shape.shapeType=burst?ParticleSystemShapeType.Sphere:ParticleSystemShapeType.Cone;
            shape.radius=burst?.35f:.018f;shape.angle=14;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.77f,.82f,.9f),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.15f),new GradientAlphaKey(0,1)});color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.4f,1,1.8f));
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            return ps;
        }
        private void OnDisable()=>Clear();
        private void OnDestroy()=>Clear();
    }
}
