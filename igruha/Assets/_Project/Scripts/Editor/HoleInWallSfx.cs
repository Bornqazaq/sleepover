using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Minigames.HoleInWall;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Звук «Дырки в стене» — подфаза 4.5. Ставит на арену проигрыватель
    /// с библиотекой слотов и <see cref="HoleInWallAudio"/>, который пускает
    /// слоты по событиям игры.
    ///
    /// <b>Ставится кодом вместе с ареной, а не мышью.</b> Причина та же, по
    /// которой в подфазе нет ни одного ручного перетаскивания клипов: звук
    /// уже дважды не пережил именно шаг «связать в инспекторе» — на Duck Hunt
    /// и «Порядке банок» файлы были, а до игры не доехали. Пересборка арены
    /// восстанавливает и звук тоже.
    ///
    /// Саму библиотеку собирает <c>Igruha/Арт/Собрать библиотеку звука</c>
    /// из манифеста и папки клипов; здесь она только подхватывается по пути.
    /// </summary>
    internal static class HoleInWallSfx
    {
        private const string AudioRoot = "_Audio";

        /// <summary>Где лежит собранная библиотека слотов этой игры.</summary>
        private const string LibraryPath = "Assets/_Project/Audio/HoleInWall/SfxLibrary.asset";

        /// <summary>
        /// Дистанция полного затухания — в шагах между дорожками.
        ///
        /// Замер, а не вкус. Шаг дорожки здесь 10.8 м, а по умолчанию
        /// проигрыватель гасит звук к 40 м: на линейном затухании это
        /// оставляет соседской дорожке <b>79 % громкости своей</b>, и четыре
        /// дорожки, разрешающиеся одновременно, сливаются в кашу — игрок
        /// перестаёт понимать, чей «дзынь» он слышит. На двух шагах сосед
        /// звучит вполовину, а дорожка через одну не слышна вовсе: свой
        /// исход слышно как свой, соседский — как соседский.
        /// </summary>
        private const float FalloffInTrackPitches = 2f;

        public static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks)
        {
            Transform root = Group(arena, AudioRoot);

            var player = root.gameObject.AddComponent<MinigameAudioPlayer>();
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);

            if (library == null)
            {
                Debug.LogWarning($"[Звук] Библиотека не найдена — {LibraryPath}. " +
                                 "Собери её пунктом «Igruha/Арт/Собрать библиотеку звука», " +
                                 "иначе игра будет немой.");
            }

            var playerObject = new SerializedObject(player);
            playerObject.FindProperty("library").objectReferenceValue = library;
            playerObject.FindProperty("maxDistance").floatValue = config.TrackPitch * FalloffInTrackPitches;
            playerObject.ApplyModifiedPropertiesWithoutUndo();

            Wire(root.gameObject, config, tracks, player);
        }

        /// <summary>
        /// Связать расставленное с рантайм-компонентом ссылками, а не поиском
        /// по сцене: <c>FindObjectsByType</c> в рантайме запрещён правилами
        /// проекта.
        /// </summary>
        private static void Wire(GameObject root, HoleInWallConfig config, HoleInWallTrack[] tracks,
            MinigameAudioPlayer player)
        {
            var audio = root.AddComponent<HoleInWallAudio>();
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>(FindObjectsInactive.Include);

            if (game == null)
            {
                Debug.LogWarning("В сцене нет HoleInWallMinigame — звук не на что вешать");
            }

            var so = new SerializedObject(audio);
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("audioPlayer").objectReferenceValue = player;

            SerializedProperty list = so.FindProperty("tracks");
            list.arraySize = tracks.Length;
            for (int i = 0; i < tracks.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = tracks[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Group(Transform parent, string groupName)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }
    }
}
