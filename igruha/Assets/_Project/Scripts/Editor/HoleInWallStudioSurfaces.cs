using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Minigames.HoleInWall;
using static Igruha.EditorTools.HoleInWallStudioAssets;

namespace Igruha.EditorTools
{
    /// <summary>Continuous, metre-aligned ceramic courses. Each surface is one baked mesh,
    /// with deliberate grout gaps over solid backing, rather than overlapping tile patches.</summary>
    internal static class HoleInWallStudioSurfaces
    {
        internal const float WainscotTop = 2.4f;
        private const float FloorTile = .9f;
        private const float WallTile = .6f;
        private const float Grout = .009f;
        private const float SurfaceLift = .012f;
        private const float BorderWidth = .16f;

        internal static void Build(Transform shell, HoleInWallConfig c)
        {
            float half = c.ArenaWidth * .5f;
            float near = c.ArenaNearZ - 9;
            float far = c.ArenaFarZ + 9;
            float floor = HoleInWallProps.RimTopY(c);
            var floorMats = new[] {
                MakeMaterial("FloorCream", new Color(.81f,.79f,.68f), .28f),
                MakeMaterial("FloorPearl", new Color(.77f,.78f,.68f), .28f), Mat("Blue") };
            var grout=MakeMaterial("FloorGrout",new Color(.72f,.72f,.63f),.18f);
            foreach(Transform child in shell)
                if(child.name=="Near promenade"||child.name=="Far promenade"||child.name=="Side promenade")
                    child.GetComponent<Renderer>().sharedMaterial=grout;
            var wallMats = new[] {
                MakeMaterial("CeramicJade", new Color(.24f,.49f,.43f), .42f),
                MakeMaterial("CeramicSage", new Color(.26f,.51f,.45f), .42f), Mat("Ivory") };
            Drain(shell, c, floor);
            var origin = new Vector3(0, floor + SurfaceLift, 0);
            Tiles(shell, "Near promenade tiles", origin, Vector3.right, Vector3.forward,
                -half, half, near + .36f, c.ArenaNearZ - .38f, FloorTile, floorMats, true);
            Tiles(shell, "Far promenade tiles", origin, Vector3.right, Vector3.forward,
                -half, half, c.ArenaFarZ + .38f, far - .36f, FloorTile, floorMats, true);
            for (int side = -1; side <= 1; side += 2)
            {
                float left = side < 0 ? -half - 8.64f : half + .38f;
                float right = side < 0 ? -half - .38f : half + 8.64f;
                Tiles(shell, "Side promenade tiles " + side, origin, Vector3.right, Vector3.forward,
                    left, right, near + .36f, far - .36f, FloorTile, floorMats, true);
                var wallOrigin = new Vector3(side * (half + 8.49f), floor, 0);
                Tiles(shell, "Side ceramic courses " + side, wallOrigin, Vector3.forward * -side, Vector3.up,
                    side < 0 ? near + .4f : -far + .4f, side < 0 ? far - .4f : -near - .4f,
                    0, WainscotTop - floor, WallTile, wallMats, false);
                Panel(shell, "Wainscot cap", new Vector3(side * (half + 8.48f), WainscotTop + .055f, (near + far) * .5f),
                    new Vector3(.21f,.11f,far-near-.7f), Mat("Ivory"));
                Panel(shell, "Promenade border", new Vector3(side * (half + .51f), floor + .015f, (near + far) * .5f),
                    new Vector3(BorderWidth,.024f,far-near-.74f), Mat("Blue"));
                // Returns close the open end of the balcony balustrade against masonry.
                foreach (float z in new[] {near + .5f, far - .5f})
                {
                    var rail = Place(shell, "BathRailing", new Vector3(side * (half + 7.04f),5.18f,z));
                    rail.localScale = new Vector3(.50f,1,1);
                }
            }
            foreach (float z in new[] {near,far})
            {
                bool back = z == far;
                Tiles(shell, "End ceramic courses " + (back ? "far" : "near"),
                    new Vector3(0,floor,z + (back ? -.51f : .51f)), back ? Vector3.right : Vector3.left, Vector3.up,
                    -half-8.5f, half+8.5f, 0, WainscotTop-floor, WallTile, wallMats, false);
                Panel(shell,"Wainscot cap",new Vector3(0,WainscotTop+.055f,z+(back?-.51f:.51f)),
                    new Vector3(c.ArenaWidth+17.2f,.11f,.21f),Mat("Ivory"));
                Panel(shell,"Promenade border",new Vector3(0,floor+.015f,back?c.ArenaFarZ+.51f:c.ArenaNearZ-.51f),
                    new Vector3(c.ArenaWidth+1.18f,.024f,BorderWidth),Mat("Blue"));
            }
        }

        private static void Drain(Transform shell, HoleInWallConfig c, float floor)
        {
            const float pitch = .12f;
            float half = c.ArenaWidth * .5f - .35f;
            float z = c.ArenaNearZ - .76f;
            Panel(shell,"Rear overflow channel",new Vector3(0,floor+.018f,z),new Vector3(half*2,.024f,.22f),Mat("Ink"));
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (float x = -half; x < half - .06f; x += pitch)
            {
                int n=vertices.Count;
                vertices.Add(new Vector3(x,floor+.035f,z-.1f));
                vertices.Add(new Vector3(x+.065f,floor+.035f,z-.1f));
                vertices.Add(new Vector3(x+.065f,floor+.035f,z+.1f));
                vertices.Add(new Vector3(x,floor+.035f,z+.1f));
                triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
            }
            string path=Art+"/Meshes/HS_RearDrain.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh{name="Rear overflow grille"};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var go=new GameObject("Rear overflow grille");go.transform.SetParent(shell,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=Mat("Ivory");renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        internal static void Audit(Transform arena, HoleInWallConfig c)
        {
            var all=arena.GetComponentsInChildren<Transform>(true);
            if(all.Count(t=>t.name.Contains("promenade tiles"))!=4 || all.Count(t=>t.name.Contains("ceramic courses"))!=4)
                throw new InvalidOperationException("Continuous floor and wall courses are incomplete.");
            foreach(var t in all.Where(t=>t.name.StartsWith("FloorHalf_")))
                if(t.GetComponent<Renderer>().enabled)
                    throw new InvalidOperationException("Old floor paint intersects the raised rubber deck.");
            foreach(var t in all.Where(t=>t.name=="Platform"))
                if(Mathf.Abs(t.GetComponent<Collider>().bounds.max.y-(c.PlatformSurfaceY+HoleInWallStudioBuilder.RubberDeckTop))>.002f)
                    throw new InvalidOperationException("Walking collider must meet the rubber deck top.");
            foreach(var t in all.Where(t=>t.name=="Support"))
                if(Mathf.Abs(t.GetComponent<Collider>().bounds.min.y-c.PoolBottomY)>.01f)
                    throw new InvalidOperationException("Platform foundation must meet the pool floor.");
            foreach(var t in all.Where(t=>t.name.StartsWith("Ladder_")))
                if(Mathf.Abs(t.position.z-(c.ArenaNearZ+.12f))>.01f ||
                    Mathf.Abs(t.GetComponent<Collider>().bounds.min.y-c.PoolBottomY)>.01f)
                    throw new InvalidOperationException("Pool ladder is not grounded at the coping.");
            var sideRails=all.Where(t=>t.name=="HS_BathRailing" && Mathf.Abs(t.localScale.x-.5f)>.01f)
                .GroupBy(t=>Mathf.Sign(t.position.x));
            foreach(var side in sideRails)
            {
                var rails=side.OrderBy(t=>t.position.z).ToArray();
                for(int i=1;i<rails.Length;i++)
                    if(Mathf.Abs(rails[i-1].GetComponent<Renderer>().bounds.max.z-rails[i].GetComponent<Renderer>().bounds.min.z)>.025f)
                        throw new InvalidOperationException("Gap in gallery handrail.");
            }
        }

        private static void Tiles(Transform parent, string name, Vector3 origin, Vector3 u, Vector3 v,
            float minU, float maxU, float minV, float maxV, float pitch, Material[] materials, bool diamonds)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new[] {new List<int>(),new List<int>(),new List<int>()};
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, int material)
            {
                int n = vertices.Count;
                foreach (var p in new[] {a,b,c,d}) {vertices.Add(origin+u*p.x+v*p.y);uv.Add(p);}
                triangles[material].AddRange(new[] {n,n+2,n+1,n,n+3,n+2});
            }
            for (int row = Mathf.FloorToInt(minV/pitch); row < Mathf.CeilToInt(maxV/pitch); row++)
                for (int col = Mathf.FloorToInt(minU/pitch); col < Mathf.CeilToInt(maxU/pitch); col++)
                {
                    float x0=Mathf.Max(minU,col*pitch+Grout*.5f), x1=Mathf.Min(maxU,(col+1)*pitch-Grout*.5f);
                    float y0=Mathf.Max(minV,row*pitch+Grout*.5f), y1=Mathf.Min(maxV,(row+1)*pitch-Grout*.5f);
                    if(x1<=x0 || y1<=y0)continue;
                    var a=new Vector2(x0,y0);var b=new Vector2(x1,y0);var c=new Vector2(x1,y1);var d=new Vector2(x0,y1);
                    int material = ((col*17+row*31)&7)==0 ? 1 : 0;
                    bool full=x1-x0>pitch*.95f && y1-y0>pitch*.95f;
                    if (diamonds && full && col%4==0 && row%4==0)
                    {
                        Vector2 center=(a+c)*.5f;
                        float radius=pitch*.15f;
                        var l=center+Vector2.left*radius;var r=center+Vector2.right*radius;
                        var t=center+Vector2.up*radius;var bottom=center+Vector2.down*radius;
                        Quad(a,b,r,bottom,material);Quad(b,c,t,r,material);
                        Quad(c,d,l,t,material);Quad(d,a,bottom,l,material);
                        Quad(bottom,r,t,l,2);
                    }
                    else Quad(a,b,c,d,material);
                }
            string path=Art+"/Meshes/HS_"+name.Replace(' ','_')+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh{name=name};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.subMeshCount=materials.Length;
            for(int i=0;i<materials.Length;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
}
