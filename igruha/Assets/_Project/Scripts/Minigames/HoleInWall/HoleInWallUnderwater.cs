using System.Collections.Generic;
using UnityEngine;
using Igruha.Core.Player;
using Igruha.Core.Minigame;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Local pool atmosphere: bubbles, moving caustic lights and reversible wet tint.</summary>
    public sealed class HoleInWallUnderwater : MonoBehaviour
    {
        [SerializeField] private HoleInWallMinigame game;
        [SerializeField] private HoleInWallConfig config;
        [SerializeField] private ParticleSystem[] bubbles;
        [SerializeField] private Light[] caustics;
        private readonly Dictionary<PlayerController, WetSkin> skins = new Dictionary<PlayerController, WetSkin>();

        private void Update()
        {
            if (game == null || config == null) return;
            bool playing = game.Phase == MinigamePhase.Round;
            if (!playing) { Clear(); return; }
            var tracks = game.PlayingTracks;
            for (int track = 0; track < tracks.Count; track++)
                for (int slot = 0; slot < tracks[track].Members.Count; slot++)
                {
                    var avatar = tracks[track].Members[slot].Avatar;
                    if (avatar == null) continue;
                    if (!skins.TryGetValue(avatar,out var skin)) { skin=new WetSkin(avatar); skins.Add(avatar,skin); }
                    float depth = config.WaterSurfaceY - avatar.Position.y;
                    skin.Apply(Mathf.Clamp01((depth-.25f)/1.1f));
                    var ps = bubbles[tracks[track].Index*2+slot];
                    bool wet = depth > .25f;
                    if (wet)
                    {
                        Vector3 point = avatar.CameraTarget.position;
                        point.y = Mathf.Min(point.y,config.WaterSurfaceY-.15f);
                        ps.transform.position = point;
                        if (!ps.isPlaying) ps.Play();
                    }
                    else if (ps.isPlaying) ps.Stop(true,ParticleSystemStopBehavior.StopEmitting);
                }
            for (int i=0;i<caustics.Length;i++)
                caustics[i].transform.rotation=Quaternion.Euler(90,0,Mathf.Sin(Time.time*.28f+i)*12);
        }

        private void Clear()
        {
            foreach (var skin in skins.Values) skin.Apply(0);
            foreach (var ps in bubbles) if(ps!=null&&ps.isPlaying) ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void OnDisable() { Clear(); foreach(var skin in skins.Values)skin.Restore(); }

        private sealed class WetSkin
        {
            private sealed class Slot
            {
                public Renderer Renderer; public int Index, Property;
                public Color Color; public MaterialPropertyBlock Original, Wet;
            }
            private readonly List<Slot> slots=new List<Slot>();
            private float previous=-1;
            public WetSkin(PlayerController player)
            {
                foreach(var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        var material=materials[i];if(material==null)continue;
                        int property=Shader.PropertyToID(material.HasProperty("_BaseColor")?"_BaseColor":"_Color");
                        if(!material.HasProperty(property))continue;
                        var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original,i);
                        var wet=new MaterialPropertyBlock();renderer.GetPropertyBlock(wet,i);
                        slots.Add(new Slot{Renderer=renderer,Index=i,Property=property,Color=material.GetColor(property),Original=original,Wet=wet});
                    }
                }
            }
            public void Apply(float amount)
            {
                if(Mathf.Abs(amount-previous)<.015f)return;previous=amount;
                foreach(var slot in slots)
                {
                    if(slot.Renderer==null)continue;
                    if(amount<=0){slot.Renderer.SetPropertyBlock(slot.Original,slot.Index);continue;}
                    Color filter=Color.Lerp(Color.white,new Color(.52f,.82f,.91f,1),amount);
                    slot.Wet.SetColor(slot.Property,slot.Color*filter);
                    slot.Renderer.SetPropertyBlock(slot.Wet,slot.Index);
                }
            }
            public void Restore(){previous=-1;Apply(0);}
        }
    }
}
