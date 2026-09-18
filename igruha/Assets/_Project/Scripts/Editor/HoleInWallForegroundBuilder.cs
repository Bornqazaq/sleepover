using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using Igruha.Minigames.HoleInWall;
using static Igruha.EditorTools.HoleInWallStudioAssets;

namespace Igruha.EditorTools
{
    /// <summary>Foreground materials: real metre-sized seams, coated fabric and glazed ceramic.</summary>
    internal static class HoleInWallForegroundBuilder
    {
        private const string PanelShader = "Igruha/HoleInWall/Show Panel";
        private const string CeramicShader = "Igruha/HoleInWall/Pool Ceramic";

        internal static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks)
        {
            var previousRoot = arena.Find("_Foreground");
            if (previousRoot != null) Object.DestroyImmediate(previousRoot.gameObject);
            var foreground = new GameObject("_Foreground").transform;
            foreground.SetParent(arena, false);
            foreach (var track in tracks)
            {
                var moving = PanelMaterial("MovingPanel" + track.Index, config, track.Index, false);
                foreach (var renderer in track.Wall.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name.StartsWith("Panel_") || renderer.name.StartsWith("Lintel_"))
                        renderer.sharedMaterial = moving;
                var oldPrint = track.Wall.transform.Find("Foreground print");
                if (oldPrint != null) Object.DestroyImmediate(oldPrint.gameObject);
                var print = new GameObject("Foreground print").transform;
                print.SetParent(track.Wall.transform, false);
                Stamp(print, "АКВА-КЛУБ", new Vector3(-config.WallWidth * .5f + 1.25f,
                    config.WallHeight - .27f, -config.WallThickness * .5f - .012f), new Vector2(2f, .25f));
                Stamp(print, (track.Index + 1).ToString("00"), new Vector3(config.WallWidth * .5f - .65f,
                    config.WallHeight - .27f, -config.WallThickness * .5f - .012f), new Vector2(.6f, .25f));

                var previous = track.transform.Find("Waiting artwork");
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                var root = new GameObject("Waiting artwork").transform;
                // Unoccupied tracks are disabled by the game. Their gate artwork must
                // still finish the room, so it belongs to the arena rather than the track.
                root.SetParent(foreground, false);
                root.localPosition = new Vector3(config.TrackCenterX(track.Index), 0, 0);
                var idle = PanelMaterial("WaitingPanel" + track.Index, config, track.Index, true);
                // At the gate, away from the platform/camera; disappears as the wall launches.
                var panel = Panel(root, "Printed shutter", new Vector3(0, config.WallHeight * .5f,
                    config.WallStartZ + config.WallThickness * .5f - .045f),
                    new Vector3(config.WallWidth, config.WallHeight, config.WallThickness), idle);
                panel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var state = new SerializedObject(root.gameObject.AddComponent<HoleInWallWaitingPanel>());
                state.FindProperty("wall").objectReferenceValue = track.Wall;
                state.FindProperty("artwork").objectReferenceValue = panel.gameObject;
                state.ApplyModifiedPropertiesWithoutUndo();
            }

            var walls = SurfaceMaterial("PoolWallCeramic", CeramicShader);
            walls.SetColor("_BaseColor", new Color(.52f, .72f, .66f));
            walls.SetFloat("_WaterLevel", config.WaterSurfaceY);
            walls.SetFloat("_WallSurface", 1);
            foreach (Transform rim in arena.Find("Pool"))
                if (rim.name.StartsWith("Rim_"))
                    foreach (var renderer in rim.GetComponentsInChildren<MeshRenderer>(true))
                        renderer.sharedMaterial = walls;
            var floor = Mat("PoolCeramic");
            floor.SetFloat("_WallSurface", 0);
            EditorUtility.SetDirty(floor);
            EditorUtility.SetDirty(walls);
            HoleInWallPavilionDetail.Build(arena, config);
        }

        private static void Stamp(Transform parent, string text, Vector3 position, Vector2 size)
        {
            var go = new GameObject("Panel imprint");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var label = go.AddComponent<TextMeshPro>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Materials + "/HS_Cyrillic.asset");
            label.text = text;
            label.fontSize = 2.1f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(.9f, .91f, .81f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = size;
            label.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        internal static void Audit(Transform arena, HoleInWallConfig config)
        {
            var root = arena.Find("_Foreground");
            if (root == null || root.childCount != config.TrackCount || root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new System.InvalidOperationException("Waiting gates missing, duplicated, or obstructing play.");
            foreach (var waiting in root.GetComponentsInChildren<HoleInWallWaitingPanel>(true))
            {
                var data = new SerializedObject(waiting);
                if (data.FindProperty("wall").objectReferenceValue == null || data.FindProperty("artwork").objectReferenceValue == null)
                    throw new System.InvalidOperationException("Waiting artwork lost its wall reference.");
                var bounds = waiting.GetComponentInChildren<Renderer>().bounds;
                if (bounds.min.z < config.WallStartZ - .1f)
                    throw new System.InvalidOperationException("Waiting artwork intrudes into a moving wall's lane.");
            }
            foreach (var wall in arena.GetComponentsInChildren<SweepingWall>(true))
                if (wall.transform.Find("Panel_Left").GetComponent<Renderer>().sharedMaterial.shader.name != PanelShader)
                    throw new System.InvalidOperationException("Moving wall lost its foreground material.");
            foreach (Transform rim in arena.Find("Pool"))
                if (rim.name.StartsWith("Rim_"))
                    foreach (var renderer in rim.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.sharedMaterial.shader.name != CeramicShader)
                            throw new System.InvalidOperationException("Pool wall lost its ceramic finish.");
        }

        private static Material PanelMaterial(string name, HoleInWallConfig config, int lane, bool emblem)
        {
            var material = SurfaceMaterial(name, PanelShader);
            material.SetColor("_BaseColor", new Color(.91f, .91f, .89f));
            material.SetColor("_AccentColor", Color.Lerp(new Color(.12f, .34f, .29f),
                HoleInWallPalette.LaneAccent(lane), emblem ? .83f : .2f));
            material.SetFloat("_LaneCenter", config.TrackCenterX(lane));
            material.SetFloat("_PanelWidth", config.WallWidth);
            material.SetFloat("_PanelHeight", config.WallHeight);
            material.SetFloat("_Emblem", emblem ? 1 : 0);
            material.SetFloat("_LaneSymbol", lane);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material SurfaceMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Missing foreground shader: " + shaderName);
            string path = Materials + "/HS_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "HS_" + name, enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }
    }
}
