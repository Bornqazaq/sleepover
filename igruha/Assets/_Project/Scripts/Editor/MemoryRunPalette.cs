using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class MemoryRunPalette
    {
        internal enum Tone { Plate, Structure, Deck, Wall, Wainscot, Ceiling, PitWall, Grime, Pit, PitRim, Door, ExitLamp, Gate }
        private static readonly Dictionary<Tone, Material> Cache = new Dictionary<Tone, Material>();
        internal static void Begin() { Cache.Clear(); }
        internal static void Flush() { AssetDatabase.SaveAssets(); }
        internal static Color ColorOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Plate: return new Color(.52f, .56f, .56f);
                case Tone.Deck: return new Color(.34f, .38f, .38f);
                case Tone.Wall: return new Color(.35f, .31f, .27f);
                case Tone.PitWall: return new Color(.15f, .19f, .18f);
                case Tone.Pit: return new Color(.025f, .033f, .039f);
                case Tone.PitRim: return new Color(.90f, .59f, .14f);
                case Tone.ExitLamp: return new Color(.15f, .9f, .36f);
                case Tone.Door: return new Color(.13f, .32f, .29f);
                case Tone.Gate: return new Color(.8f, .9f, 1, .012f);
                default: return new Color(.13f, .17f, .18f);
            }
        }
        internal static Material Get(Tone tone)
        {
            if (Cache.TryGetValue(tone, out var value)) return value;
            string path = MemoryFoundryAssets.Materials + "/MF_Surface_" + tone + ".mat";
            value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (value == null)
            {
                value = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(value, path);
            }
            value.SetColor("_BaseColor", ColorOf(tone));
            value.SetFloat("_Smoothness", .25f); value.enableInstancing = true;
            if (tone == Tone.Gate)
                Igruha.Minigames.HoleInWall.HoleInWallMaterials.ConfigureTransparent(value, ColorOf(tone), 0);
            if (tone == Tone.ExitLamp)
            {
                value.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                value.EnableKeyword("_EMISSION"); value.SetColor("_EmissionColor", ColorOf(tone) * 3);
            }
            EditorUtility.SetDirty(value); Cache[tone] = value; return value;
        }
        internal static string Report() => "Foundry palette: original URP materials; no sampled store textures.";
    }
}
