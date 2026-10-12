using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Real basement openings, transparent glass and bounded exterior scenery.</summary>
    public static class HubBasementWindowPass
    {
        internal const float Width = 2.8f, Height = 1.42f, GlassWidth = 2.48f, GlassHeight = 1.12f;
        internal const float WallDepth = 9.74f;
        internal static float[] WindowOffsets(int side) => side == 0 ? new[] { -3.4f, 3.4f }
            : side == 1 ? new[] { -6.9f, 4.25f } : new[] { -4.25f, 6.9f };
        internal static float WindowCenterHeight(int side) => side == 0
            ? 3.73f + HubCompactPass.CeilingRaise - .135f - .03f - Height * .5f
            : HubCompactPass.RoomHeight - .17f - Height * .5f;

        [MenuItem("Igruha/Хаб/Настоящие окна и объёмный дождливый двор")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Hub.unity")
                throw new InvalidOperationException("Open Hub outside Play Mode.");
            var root = HubCompactPass.Require(HubCompactPass.RootName).transform;
            // Update just the wall assets; preserve every existing room object and binding.
            var temporary = new GameObject("WindowWallBuild").transform;
            temporary.SetParent(root, false);
            try
            {
                HubBasementWallOpenings.BuildPanels(temporary);
                foreach (Transform child in temporary.Cast<Transform>().ToArray())
                {
                    var existing = root.Find(child.name);
                    if (existing != null)
                    {
                        existing.GetComponent<MeshFilter>().sharedMesh = child.GetComponent<MeshFilter>().sharedMesh;
                        AssetDatabase.SaveAssetIfDirty(existing.GetComponent<MeshFilter>().sharedMesh);
                    }
                    else child.SetParent(root, false);
                }
            }
            finally { Object.DestroyImmediate(temporary.gameObject); }
            Build(root);
            foreach (Transform child in root.Cast<Transform>().Where(t => t.name.StartsWith("StringLights_")).ToArray())
                Object.DestroyImmediate(child.gameObject);
            HubCompactDetails.Garland(root);
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("StringLights_") || f.name.StartsWith("BasementWallBacking_")))
                AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Hub: six real wall openings with transparent glass and compact 3D yards outside.");
        }

        internal static void Build(Transform root)
        {
            if (Shader.Find("Igruha/Hub/Weather Glass") == null || Shader.Find("Igruha/Hub/Dusk Sky") == null)
                throw new InvalidOperationException("Hub weather shaders are missing.");
            foreach (Transform child in root.Cast<Transform>().ToArray())
                if (child.name == "BasementWindows" || child.name.StartsWith("BasementWindows_") || child.name == "BasementYard")
                    Object.DestroyImmediate(child.gameObject);
            foreach (string side in new[] { "N", "S", "E", "W" })
                HubCompactPass.Require("_Room/Wall_" + side).GetComponent<MeshRenderer>().enabled = false;
            var group = HubCompactPass.Group(root, "BasementWindows");
            var paint = HubOriginalAssets.Mat("WindowPaint", "D8D4BE");
            var gasket = HubOriginalAssets.Mat("WindowSeal", "29302F");
            var masonry = HubOriginalAssets.Mat("WindowMasonry", "827F70");
            var glassMaterial = HubBasementYardBuilder.Transparent("WindowGlass", new Color(.36f,.49f,.55f,.045f), false);
            var wood = HubCompactPass.M("Oak");
            var mesh = GlassMesh();
            foreach (int side in new[] { 0, 1, 3 })
            foreach (float offset in WindowOffsets(side))
            {
                string id = (side == 0 ? "North" : side == 1 ? "East" : "West") + (offset < 0 ? "_L" : "_R");
                var window = HubCompactPass.Group(group, id);
                window.localRotation = Quaternion.Euler(0, side * 90, 0);
                window.localPosition = window.localRotation * new Vector3(offset, WindowCenterHeight(side), WallDepth);
                var frame = new HubOriginalGeometry();
                foreach (float sign in new[] { -1f, 1f })
                {
                    frame.Box(new Vector3(sign*(Width*.5f-.08f),0,.025f),new Vector3(.16f,Height,.23f),wood);
                    frame.Box(new Vector3(0,sign*(Height*.5f-.055f),.025f),new Vector3(Width,.11f,.23f),wood);
                    frame.Box(new Vector3(sign*(GlassWidth*.5f+.035f),0,-.065f),new Vector3(.07f,GlassHeight+.05f,.05f),paint);
                    frame.Box(new Vector3(0,sign*(GlassHeight*.5f+.026f),-.065f),new Vector3(2.62f,.052f,.05f),paint);
                    frame.Box(new Vector3(sign*(GlassWidth*.5f+.01f),0,.085f),new Vector3(.025f,GlassHeight,.035f),gasket);
                    frame.Box(new Vector3(sign*(GlassWidth*.5f+.08f),0,.32f),new Vector3(.10f,GlassHeight+.06f,.48f),masonry);
                    frame.Box(new Vector3(0,sign*(GlassHeight*.5f+.05f),.32f),new Vector3(2.62f,.10f,.48f),masonry);
                }
                frame.Box(new Vector3(0,-Height*.5f-.01f,-.105f),new Vector3(2.94f,.07f,.38f),paint);
                frame.Box(new Vector3(0,0,-.073f),new Vector3(.046f,GlassHeight,.055f),paint);
                frame.Box(new Vector3(.09f,-GlassHeight*.5f+.06f,-.11f),new Vector3(.13f,.025f,.03f),gasket);
                frame.Build(window,"BasementWindowFrame");
                var glass=HubCompactPass.Group(window,"Glass").gameObject;
                glass.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=glass.AddComponent<MeshRenderer>();renderer.sharedMaterial=glassMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                glass.layer=LayerMask.NameToLayer("Ground");
                var barrier=glass.AddComponent<BoxCollider>();barrier.center=new Vector3(0,0,.10f);barrier.size=new Vector3(GlassWidth,GlassHeight,.018f);
            }
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>()) AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            foreach (var mat in new[] {paint,gasket,masonry}) AssetDatabase.SaveAssetIfDirty(mat);
            HubBasementYardBuilder.Build(root);
        }

        private static Mesh GlassMesh()
        {
            string path=HubOriginalAssets.Folder+"/Meshes/BasementWindowGlass.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create=mesh==null;
            float x=GlassWidth*.5f,y=GlassHeight*.5f;
            if(create)mesh=new Mesh {name="BasementWindowGlass"};
            else mesh.Clear();
            mesh.vertices=new[]{new Vector3(-x,-y,.1f),new Vector3(-x,y,.1f),new Vector3(x,y,.1f),new Vector3(x,-y,.1f)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
            mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(create)AssetDatabase.CreateAsset(mesh,path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
