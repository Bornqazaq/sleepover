using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Minigames.BelieveOrNot;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Звук «Верю / не верю» — подфаза 4.5. Ставит на арену проигрыватель
    /// с библиотекой слотов и <see cref="BelieveOrNotAudio"/>, который пускает
    /// слоты по событиям игры.
    ///
    /// <b>Ставится кодом вместе с ареной, а не мышью.</b> Причина та же, что
    /// и на «Дырке в стене»: звук уже дважды не пережил именно шаг «связать
    /// в инспекторе» — на Duck Hunt и «Порядке банок» файлы были, а до игры
    /// не доехали. Пересборка арены восстанавливает и звук.
    ///
    /// Саму библиотеку собирает <c>Igruha/Арт/Собрать библиотеку звука</c>
    /// из манифеста и папки клипов; здесь она только подхватывается по пути.
    /// </summary>
    internal static class BelieveOrNotSfx
    {
        private const string AudioRoot = "_Audio";

        /// <summary>Где лежит собранная библиотека слотов этой игры.</summary>
        private const string LibraryPath = "Assets/_Project/Audio/BelieveOrNot/SfxLibrary.asset";

        /// <summary>
        /// Дистанция полного затухания трёхмерных слотов, м.
        ///
        /// Замер, а не вкус. Трёхмерных звуков здесь ровно четыре, и все они
        /// у стола: реплики, обмен, крышки, облачко. Свободная зона зрителей —
        /// радиус 7.2 м, и зритель на её краю обязан слышать стол ясно, а не
        /// «где-то там»: разговор за столом и есть игра. Полное затухание
        /// на 14 м оставляет краю зоны около половины громкости и гасит звук
        /// у стен, где стоят бар и диваны.
        /// </summary>
        private const float FalloffMeters = 14f;

        internal static void Build(Transform arena, BelieveOrNotConfig config, BelieveTable table)
        {
            var root = new GameObject(AudioRoot).transform;
            root.SetParent(arena, false);

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
            playerObject.FindProperty("maxDistance").floatValue = FalloffMeters;
            playerObject.ApplyModifiedPropertiesWithoutUndo();

            Wire(root.gameObject, config, table, player);
        }

        /// <summary>
        /// Связать компонент ссылками, а не поиском по сцене:
        /// <c>FindObjectsByType</c> в рантайме запрещён правилами проекта.
        /// </summary>
        private static void Wire(GameObject root, BelieveOrNotConfig config, BelieveTable table,
            MinigameAudioPlayer player)
        {
            var audio = root.AddComponent<BelieveOrNotAudio>();
            var game = Object.FindFirstObjectByType<BelieveOrNotMinigame>(FindObjectsInactive.Include);
            var stageState = Object.FindFirstObjectByType<MinigameStageState>(FindObjectsInactive.Include);

            if (game == null)
            {
                Debug.LogWarning("В сцене нет BelieveOrNotMinigame — звук не на что вешать");
            }

            var so = new SerializedObject(audio);
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("table").objectReferenceValue = table;
            so.FindProperty("stageState").objectReferenceValue = stageState;
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("audioPlayer").objectReferenceValue = player;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
