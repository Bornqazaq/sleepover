using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.CarryItem;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Targeted, repeatable pump/water upgrade; does not rebuild the arena or glass materials.</summary>
    internal static class CarryPumpArt
    {
        private const string Prefabs = "Assets/_Project/Prefabs/Minigames/CarryItem/";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/CarryItemConfig.asset";
        private static readonly Vector3 PumpPosition = new Vector3(-2.1f, 0f, 1.45f);
        private static readonly Vector3 DockPosition = new Vector3(-3.45f, 0f, 0f);
        [Serializable] private sealed class Palette { public Entry[] materials; }
        [Serializable] private sealed class Entry
        {
            public string name; public float[] color; public float roughness; public float metallic;
        }

        internal static void Import()
        {
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(CarrySkyscraperAssets.Art + "/pump-palette.json"));
            var materials = new Dictionary<string, Material>();
            foreach (var entry in palette.materials)
            {
                string path = CarrySkyscraperAssets.Materials + "/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", new Color(entry.color[0], entry.color[1], entry.color[2], 1f));
                material.SetFloat("_Metallic", entry.metallic);
                material.SetFloat("_Smoothness", 1f - entry.roughness);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                materials.Add(entry.name, material);
            }
            foreach (string path in Directory.GetFiles(CarrySkyscraperAssets.Art + "/Models", "CS_WaterPump*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion = true;
                importer.addCollider = false;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false;
                foreach (var material in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
                importer.SaveAndReimport();
            }
        }

        [MenuItem("Igruha/Переноска предмета/Обновить насос и воду")]
        internal static void Apply()
        {
            Import();
            var config = AssetDatabase.LoadAssetAtPath<CarryItemConfig>(ConfigPath);
            var settings = new SerializedObject(config);
            settings.FindProperty("pourRate").floatValue = 25f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditPrefab("WaterCart", ApplyToCart);
            EditPrefab("Tank", ApplyToTank);
            CarryWaterArt.WaterMaterial();
            AssetDatabase.SaveAssets();
        }

        private static void EditPrefab(string name, Action<GameObject> change)
        {
            string path = Prefabs + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { change(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        internal static void ApplyToCart(GameObject root)
        {
            Remove(root.transform, "PourRibbon");
            Remove(root.transform, "Body/PourJet");
            var cart = new SerializedObject(root.GetComponent<WaterCart>());
            var pivot = (Transform)cart.FindProperty("waterMesh").objectReferenceValue;
            ConfigureWater(pivot, 0.44f);
            cart.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void ApplyToTank(GameObject root)
        {
            Remove(root.transform, "CS_WaterReceivingHopper");
            Remove(root.transform, "PumpStation");
            var station = Child(root.transform, "PumpStation", Vector3.zero);
            var pump = CarrySkyscraperAssets.Place(station, "WaterPump", PumpPosition);
            pump.gameObject.layer = LayerMask.NameToLayer("Ground");
            var solid = pump.gameObject.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, 0.46f, 0f);
            solid.size = new Vector3(0.76f, 0.92f, 1.15f);
            var intake = root.transform.Find("WaterInlet") ?? Child(root.transform, "WaterInlet", Vector3.zero);
            intake.localPosition = PumpPosition + new Vector3(0f, 0.44f, -0.66f);
            var rotor = CarrySkyscraperAssets.Place(pump, "WaterPumpRotor", new Vector3(0f, 0.44f, 0.205f));
            var dock = Child(station, "CartDock", DockPosition);
            var parked = Child(station, "ParkedNozzle", PumpPosition + new Vector3(-0.52f, 0.36f, -0.25f));
            var nozzle = CarrySkyscraperAssets.Place(station, "WaterPumpNozzle", parked.localPosition);
            var hose = Child(station, "SuctionHose", Vector3.zero).gameObject;
            var hoseFilter = hose.AddComponent<MeshFilter>();
            hose.AddComponent<MeshRenderer>().sharedMaterial = Material("CP_Hose");
            // Painted parking marks, flat to the deck and never blocking wheels or hands.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                    Mark(station, DockPosition + new Vector3(side * 1.0f, 0.012f, (i - 1) * 0.78f), new Vector3(0.06f, 0.012f, 0.48f));
            Mark(station, DockPosition + new Vector3(0f, 0.012f, 1.15f), new Vector3(2f, 0.012f, 0.06f));
            Mark(station, DockPosition + new Vector3(0f, 0.012f, -1.15f), new Vector3(2f, 0.012f, 0.06f));

            var tank = new SerializedObject(root.GetComponent<WaterTank>());
            var pivot = (Transform)tank.FindProperty("waterMesh").objectReferenceValue;
            ConfigureWater(pivot, 2.4f);
            BoxCollider zone = null;
            foreach (var collider in root.GetComponents<BoxCollider>()) if (collider.isTrigger) zone = collider;
            if (zone == null) zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.center = DockPosition + Vector3.up * 0.65f;
            zone.size = new Vector3(1.7f, 1.3f, 1.9f);
            tank.FindProperty("pourZone").objectReferenceValue = zone;
            tank.FindProperty("waterInlet").objectReferenceValue = intake;
            tank.FindProperty("cartDock").objectReferenceValue = dock;
            tank.ApplyModifiedPropertiesWithoutUndo();
            var presentation = root.GetComponent<WaterPumpPresentation>() ?? root.AddComponent<WaterPumpPresentation>();
            var so = new SerializedObject(presentation);
            so.FindProperty("intake").objectReferenceValue = intake;
            so.FindProperty("parkedNozzle").objectReferenceValue = parked;
            so.FindProperty("nozzle").objectReferenceValue = nozzle;
            so.FindProperty("rotor").objectReferenceValue = rotor;
            so.FindProperty("hose").objectReferenceValue = hoseFilter;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureWater(Transform pivot, float height)
        {
            var visual = pivot.GetComponent<WaterVolumeVisual>() ?? pivot.gameObject.AddComponent<WaterVolumeVisual>();
            var so = new SerializedObject(visual);
            so.FindProperty("filledHeight").floatValue = height;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var renderer in pivot.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = CarryWaterArt.WaterMaterial();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(CarrySkyscraperAssets.Materials + "/" + name + ".mat");
        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false); child.localPosition = position;
            return child;
        }
        private static void Mark(Transform parent, Vector3 position, Vector3 size)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = "DockPaint"; mark.transform.SetParent(parent, false);
            mark.transform.localPosition = position; mark.transform.localScale = size;
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            var renderer = mark.GetComponent<Renderer>();
            renderer.sharedMaterial = Material("CP_Enamel"); renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        private static void Remove(Transform root, string path)
        {
            var child = root.Find(path);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }
    }
}
