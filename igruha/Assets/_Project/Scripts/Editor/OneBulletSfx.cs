using System.IO;
using Igruha.Core.Audio;
using Igruha.Minigames.OneBullet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Igruha.EditorTools
{
    internal static class OneBulletSfx
    {
        [MenuItem("Igruha/Звук/Озвучить Один патрон")]
        internal static void Build()
        {
            var game=Object.FindFirstObjectByType<OneBulletMinigame>();if(game==null)return;
            SfxLibraryBuilder.Build(Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/art/one-bullet-sfx.json")));
            var library=AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>("Assets/_Project/Audio/OneBullet/SfxLibrary.asset");
            var player=game.GetComponent<MinigameAudioPlayer>();if(player==null)player=game.gameObject.AddComponent<MinigameAudioPlayer>();
            var binding=game.GetComponent<OneBulletAudio>();if(binding==null)binding=game.gameObject.AddComponent<OneBulletAudio>();
            OneBulletArenaBuilder.Set(player,"library",library);OneBulletArenaBuilder.Set(player,"maxDistance",100f);
            OneBulletArenaBuilder.Set(game,"soundLibrary",library);OneBulletArenaBuilder.Set(binding,"game",game);OneBulletArenaBuilder.Set(binding,"audioPlayer",player);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        }
    }
}
