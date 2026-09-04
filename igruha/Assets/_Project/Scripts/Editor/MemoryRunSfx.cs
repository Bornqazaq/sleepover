using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Minigames.MemoryRun;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Звук «Рейса на память» — подфаза 4.5. Ставит на арену проигрыватель
    /// с библиотекой слотов и <see cref="MemoryRunAudio"/>, который пускает
    /// слоты по событиям игры.
    ///
    /// <b>Ставится кодом вместе с ареной, а не мышью.</b> Причина известна
    /// по трём играм подряд: звук дважды не пережил именно шаг «связать в
    /// инспекторе» — на Duck Hunt и «Порядке банок» файлы были, а до игры
    /// не доехали. Пересборка арены восстанавливает и звук.
    ///
    /// Саму библиотеку собирает <c>Igruha/Арт/Собрать библиотеку звука</c>
    /// из манифеста <c>docs/art/memory-run-sfx.json</c>; здесь она только
    /// подхватывается по пути.
    /// </summary>
    internal static class MemoryRunSfx
    {
        private const string AudioRoot = "_Audio";
        private const string LibraryPath = "Assets/_Project/Audio/MemoryRun/SfxLibrary.asset";

        /// <summary>
        /// Дистанция полного затухания трёхмерных слотов, м.
        ///
        /// Замер, а не вкус. Самый дальний ряд плит стоит в 39 м от переднего
        /// края площадки ожидания, и взрыв на нём обязан быть слышен оттуда:
        /// зритель ловит момент по звуку раньше, чем разбирает глазами —
        /// на этом держится половина игры. Сорок пять метров оставляют
        /// дальнему ряду слышимость, ближним — разницу в громкости, по которой
        /// понятно, далеко идущий или рядом.
        /// </summary>
        private const float FalloffMeters = 45f;

        internal static void Build(Transform arena, MemoryRunConfig config, GameObject manager)
        {
            var root = new GameObject(AudioRoot).transform;
            root.SetParent(arena, false);

            var player = root.gameObject.AddComponent<MinigameAudioPlayer>();
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);

            if (library == null)
            {
                Debug.LogWarning($"[Звук] Библиотека не найдена — {LibraryPath}. " +
                                 "Собери её пунктом «Igruha/Арт/Собрать библиотеку звука» " +
                                 "по манифесту docs/art/memory-run-sfx.json, иначе игра будет немой.");
            }

            var playerObject = new SerializedObject(player);
            playerObject.FindProperty("library").objectReferenceValue = library;
            playerObject.FindProperty("maxDistance").floatValue = FalloffMeters;
            playerObject.ApplyModifiedPropertiesWithoutUndo();

            var audio = root.gameObject.AddComponent<MemoryRunAudio>();
            var game = manager != null ? manager.GetComponent<MemoryRunMinigame>() : null;

            if (game == null)
            {
                Debug.LogWarning("В сцене нет MemoryRunMinigame — звук не на что вешать");
            }

            var so = new SerializedObject(audio);
            so.FindProperty("audioPlayer").objectReferenceValue = player;
            so.FindProperty("game").objectReferenceValue = game;
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Что собрано и чем это грозит, если чего-то нет.</summary>
        internal static string Report()
        {
            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                return "🔊 «Рейс на память», звук 4.5 — библиотека не собрана, игра немая ✘";
            }

            int filled = 0;
            int silent = 0;
            var missing = new System.Text.StringBuilder();
            foreach (MinigameSfxLibrary.Entry entry in library.Entries)
            {
                if (entry.Clip != null)
                {
                    filled++;
                }
                else
                {
                    silent++;
                    missing.Append(missing.Length == 0 ? "" : ", ").Append(entry.Id);
                }
            }

            var report = new System.Text.StringBuilder();
            report.Append("🔊 «Рейс на память», звук 4.5");
            report.Append("\n— слотов с клипом:        ").Append(filled);
            report.Append("\n— немых слотов:           ").Append(silent)
                .Append(silent == 0 ? " ✔" : " ✘ " + missing);
            report.Append("\n— затухание 3D-слотов:    ").Append(FalloffMeters).Append(" м");
            return report.ToString();
        }
    }
}
