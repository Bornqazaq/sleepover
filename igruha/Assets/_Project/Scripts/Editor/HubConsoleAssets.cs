using System;
using System.Linq;
using Igruha.Core.Hub;
using Igruha.Core.Minigame;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Igruha.EditorTools
{
    internal static class HubConsoleAssets
    {
        internal const string Folder = "Assets/_Project/Art/Hub/Console";
        private const string Definitions = "Assets/_Project/Settings/Gameplay/Minigames/";

        internal static ConsoleArtworkLibrary Library()
        {
            HubCozyMaterials.EnsureFolder(Folder);
            string path = Folder + "/ConsoleArtwork.asset";
            var library = AssetDatabase.LoadAssetAtPath<ConsoleArtworkLibrary>(path);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ConsoleArtworkLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }
            string[] ids = { "CryingAngels", "Stopwatch", "CansOrder", "Exam", "BelieveOrNot", "MemoryRun", "CarryItem", "HoleInWall", "DuckHunt", "Infection", "OneBullet", "SumoRing" };
            string[] definitions = { "CryingAngelsDefinition", "StopwatchDefinition", "CansOrder", "Exam", "BelieveOrNot", "MemoryRun", "CarryItem", "HoleInWall", "DuckHuntDefinition", "Infection", "OneBullet", "SumoRing" };
            string[] accents = { "98CED9", "F4BE81", "DFC38F", "DFACE9", "C7D39C", "BBB3F0", "91D6CD", "F4ADB2", "F2D47D", "9BE07A", "EFC18B", "E7BA74" };
            string[] genres = { "ПРЯТКИ В ТЕМНОТЕ", "ПОЧУВСТВУЙ ВРЕМЯ", "ЛОГИКА И ИНТУИЦИЯ", "УГАДАЙ МЫСЛИ ВЕДУЩЕГО", "БЛЕФ ЗА ОДНИМ СТОЛОМ", "ЗАПОМНИ БЕЗОПАСНЫЙ ПУТЬ", "ВМЕСТЕ ДО ПОСЛЕДНЕЙ КАПЛИ", "ПОПАДИ В СИЛУЭТ", "ГОНКА НА ВЕРШИНУ", "БЕГИ, ПОКА ЧИСТЫЙ", "ОДИН ВЫСТРЕЛ — ОДИН ШАНС", "ОСТАНЬСЯ НА РИНГЕ" };
            string[] summaries =
            {
                "Подкрадись к ведущему и замри, когда на тебя попадёт свет. Одно лишнее движение — и ты выдашь себя.",
                "Удерживай кнопку и отпусти её точно вовремя. Чувство ритма спасёт твою клетку от встречи с медведем.",
                "Разгадай тайную расстановку цветных банок. Каждая попытка подскажет, сколько уже стоит на своих местах.",
                "Выбери платформу с ответом, который загадал ведущий. Здесь важнее понять друга, чем знать правильный ответ.",
                "Оставить свою коробку или поменяться? Читай эмоции соперника и найди карту, которая принесёт победу.",
                "Перед тобой три дорожки и только один безопасный шаг. Запоминай путь и доберись до финиша.",
                "Донесите воду до своей ёмкости всей командой. Спешить можно — но каждая пролитая капля отдаляет победу.",
                "Вы связаны одной верёвкой, а стена уже близко. Займите нужные позы и пройдите сквозь отверстия вместе.",
                "Утки штурмуют башню, охотник мешает им добраться до вершины. Ловушки и пять этажей до победы!",
                "Один заражён, остальные бегут. Зелёная краска липнет от касания, и с каждым новым заражённым бежать становится некуда.",
                "Затеряйся в каменном лабиринте. Найди единственный револьвер, слушай шаги и реши, когда потратить последний патрон.",
                "Выталкивай соперников с глиняного ринга. Край трещит и осыпается — займи центр и останься последним на ногах."
            };
            var data = new SerializedObject(library);
            var entries = data.FindProperty("entries");
            entries.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                string imagePath = Folder + "/Covers/" + ids[i] + ".png";
                var importer = AssetImporter.GetAtPath(imagePath) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing console cover: " + imagePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                var entry = entries.GetArrayElementAtIndex(i);
                var game = AssetDatabase.LoadAssetAtPath<MinigameDefinition>(Definitions + definitions[i] + ".asset");
                if (game == null) throw new InvalidOperationException("Missing console game: " + definitions[i]);
                entry.FindPropertyRelative("game").objectReferenceValue = game;
                entry.FindPropertyRelative("cover").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
                entry.FindPropertyRelative("accent").colorValue = HubCozyMaterials.Hex(accents[i]);
                entry.FindPropertyRelative("genre").stringValue = genres[i];
                entry.FindPropertyRelative("summary").stringValue = summaries[i];
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            return library;
        }

        internal static TMP_FontAsset Font(bool bold)
        {
            string path = Folder + "/Fonts/ConsoleHD" + (bold ? "Bold" : "Regular") + ".asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null)
            {
                AddSymbolFallback(font, bold);
                return font;
            }
            HubCozyMaterials.EnsureFolder(Folder + "/Fonts");
            string source = bold ? "Assets/TextMesh Pro/Examples & Extras/Fonts/Roboto-Bold.ttf" : "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(source), 96, 12,
                GlyphRenderMode.SDF, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            font.name = bold ? "Console HD Bold" : "Console HD Regular";
            string characters = new string(Enumerable.Range(32, 95).Concat(Enumerable.Range(0x400, 96)).Select(c => (char)c).ToArray()) + "–—·…" + (bold ? "←→↑↓" : string.Empty);
            font.TryAddCharacters(characters, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("Console font missing glyphs: " + missing);
            AssetDatabase.CreateAsset(font, path);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            // Catalog titles may grow: dynamic mode uses this bundled TTF, never an OS font.
            EditorUtility.SetDirty(font);
            AddSymbolFallback(font, bold);
            return font;
        }
        private static void AddSymbolFallback(TMP_FontAsset font, bool bold)
        {
            if (bold) return;
            var symbols = Font(true);
            if (!font.fallbackFontAssetTable.Contains(symbols))
            {
                font.fallbackFontAssetTable.Add(symbols);
                EditorUtility.SetDirty(font);
            }
        }
    }
}
