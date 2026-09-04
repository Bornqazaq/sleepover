using UnityEditor;
using UnityEngine;
using Igruha.Core.Audio;
using Igruha.Core.Minigame;
using Igruha.Core.Traps;
using Igruha.Minigames.CarryItem;

namespace Igruha.EditorTools
{
    /// <summary>
    /// Постановка звука «Переноски предмета» — подфаза 4.5.
    ///
    /// Здесь только связывание: проигрыватель на арену, компонент привязки на
    /// него же и ссылки на всё, что он слушает. Клипы приходят генерацией по
    /// манифесту `docs/art/carry-item-sfx.json`, библиотека собирается пунктом
    /// меню `Igruha/Арт/Собрать библиотеку звука`.
    ///
    /// <b>Ссылками, а не поиском по сцене.</b> `FindObjectsByType` в рантайме
    /// запрещён правилами проекта, и это не формальность: на восьмерых он
    /// стоит кадра, а звук зовётся по несколько раз в секунду.
    ///
    /// <b>Немая игра говорит вслух.</b> Библиотеки может не быть — клипы
    /// генерируются отдельно и в репозиторий приезжают через LFS. Тогда
    /// пересборка печатает предупреждение: молчащий звук ничем не отличается
    /// от забытого звука, и однажды этим отличием стал целый долг Duck Hunt.
    /// </summary>
    internal static class CarryItemSfx
    {
        private const string LibraryPath = "Assets/_Project/Audio/CarryItem/SfxLibrary.asset";

        /// <summary>Радиус слышимости, ШИ. Горлышко шириной 8 — звук трубы обязан доставать до подхода.</summary>
        private const float FalloffUnits = 22f;

        internal static void Build(Transform arena, CarryItemConfig config, GameObject manager,
            BottleStack[] stacks, WaterTank[] tanks, TrapBase cart, Transform pipe, Transform beam)
        {
            if (manager == null)
            {
                return;
            }

            var player = manager.GetComponent<MinigameAudioPlayer>();
            if (player == null)
            {
                player = manager.AddComponent<MinigameAudioPlayer>();
            }

            var library = AssetDatabase.LoadAssetAtPath<MinigameSfxLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogWarning($"[Звук] Библиотека не найдена — {LibraryPath}. " +
                                 "Сгенерируй клипы по docs/art/carry-item-sfx.json и собери её пунктом " +
                                 "«Igruha/Арт/Собрать библиотеку звука», иначе игра будет немой.");
            }

            var playerObject = new SerializedObject(player);
            playerObject.FindProperty("library").objectReferenceValue = library;
            playerObject.FindProperty("maxDistance").floatValue = config.ToMeters(FalloffUnits);
            playerObject.ApplyModifiedPropertiesWithoutUndo();

            var audio = manager.GetComponent<CarryItemAudio>();
            if (audio == null)
            {
                audio = manager.AddComponent<CarryItemAudio>();
            }

            var so = new SerializedObject(audio);
            FillArray(so.FindProperty("stacks"), stacks);
            FillArray(so.FindProperty("tanks"), tanks);
            FillArray(so.FindProperty("traps"), cart != null ? new Object[] { cart } : new Object[0]);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("roundTimer").objectReferenceValue = manager.GetComponent<RoundTimer>();
            so.FindProperty("pipe").objectReferenceValue = pipe;
            so.FindProperty("beam").objectReferenceValue = beam;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
