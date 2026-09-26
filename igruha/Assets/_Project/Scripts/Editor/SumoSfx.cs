using System.IO;
using Igruha.Core.Audio;
using Igruha.Minigames.SumoRing;
using UnityEditor;
using UnityEngine;
using static Igruha.EditorTools.SumoArenaBuilder;

namespace Igruha.EditorTools
{
    public static class SumoSfx
    {
        [MenuItem("Igruha/Minigames/Audio Sumo Ring")]
        public static void Build()
        {
            RequireScene();
            SfxLibraryBuilder.Build(Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/art/sumo-ring-sfx.json")));
            var game=Object.FindFirstObjectByType<SumoMinigame>(); var arena=Object.FindFirstObjectByType<SumoArena>();
            var player=game.GetComponent<MinigameAudioPlayer>();if(player==null)player=game.gameObject.AddComponent<MinigameAudioPlayer>();
            var binding=game.GetComponent<SumoAudio>();if(binding==null)binding=game.gameObject.AddComponent<SumoAudio>();
            Set(player,"library",AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/SumoRing/SfxLibrary.asset"));
            Set(binding,"game",game);Set(binding,"arena",arena);Set(binding,"audioPlayer",player);
            var surface=arena.GetComponent<SurfaceAudio>();if(surface==null)surface=arena.gameObject.AddComponent<SurfaceAudio>();Set(surface,"kind",(int)SurfaceKind.Gravel);
            Save();
        }
        [MenuItem("Igruha/Minigames/Build complete Sumo Ring")]
        public static void BuildComplete() { SumoArenaBuilder.Build(); SumoArtBuilder.Build(); Build(); SumoArtBuilder.Capture(); }
    }
}
