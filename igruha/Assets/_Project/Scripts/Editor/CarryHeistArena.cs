using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Igruha.Minigames.CarryItem;
using Igruha.Core.Traps;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Authored flyover, lower routes and recessed receiving bays. Metres, ground top at y=0.</summary>
    internal static class CarryHeistArena
    {
        private const string Art = "Assets/_Project/Art/CarryItem/HeistRoutes/Models";
        private const string Road = "Assets/_Project/Art/CarryItem/Roadworks";
        internal const float UpperHeight = 4.5f;
        [MenuItem("Igruha/Minigames/Polish Carry Item Heist Routes")]
        internal static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().name != "CarryItem")
                throw new InvalidOperationException("Open CarryItem in Edit mode.");
            Import();
            ClearConflictingStorage();
            Transform arena = GameObject.Find("_Arena").transform;
            var old = arena.Find("HeistRoutes"); if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform root = Group(arena,"HeistRoutes");
            BuildFlyover(Group(root,"UpperRoute"));
            BuildLowerRoutes(Group(root,"LowerRoutes"));
            BuildBays(Group(root,"PumpBays"));
            BuildCrane(Group(root,"CraneHazard"));
            BuildDetails(Group(root,"WorkIslands"));
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        private static void ClearConflictingStorage()
        {
            var work=GameObject.Find("_Arena/Environment/WorkAreas");
            if(work==null)return;
            for(int i=work.transform.childCount-1;i>=0;i--)
            {
                var t=work.transform.GetChild(i);
                if(t.name=="CS_SiteCabin" || t.name=="CS_Wheelbarrow" || t.name=="CS_Scaffold" ||
                    t.name=="CS_Barricade" || t.name=="CS_Sandbags" || t.name=="CS_CableReel" ||
                    t.name=="CS_SignBoard" || t.name=="CS_Cone" && t.position.x>0)
                    Object.DestroyImmediate(t.gameObject);
            }
        }
        private static void Import()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art,"*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.bakeAxisConversion=true; importer.addCollider=false;
                importer.importAnimation=importer.importLights=importer.importCameras=false;
                importer.animationType=ModelImporterAnimationType.None;
                foreach (string material in Directory.GetFiles(Road+"/Materials","*.mat"))
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),Path.GetFileNameWithoutExtension(material)),AssetDatabase.LoadAssetAtPath<Material>(material));
                importer.SaveAndReimport();
            }
        }
        private static void BuildFlyover(Transform root)
        {
            // 16.9 degree ramps, continuous collision. Four holders have 4.4 m deck width.
            DeckRun(root,new Vector3(-22,0,0),new Vector3(-7.2f,UpperHeight,0),4.4f,true,false);
            DeckRun(root,new Vector3(-7.2f,UpperHeight,0),new Vector3(7.2f,UpperHeight,0),4.4f,false,false);
            DeckRun(root,new Vector3(7.2f,UpperHeight,0),new Vector3(22,0,0),4.4f,true,false);
            BuildFlyoverCollider(root);
            foreach(int side in new[]{-1,1})
            {
                GuardrailRun(root,-7.2f,-1.2f,side*2.16f);
                GuardrailRun(root,5.6f,7.2f,side*2.16f);
            }
            foreach(float x in new[]{-3.8f,5.8f})
            {
                var t=Place(root,"Trestle",new Vector3(x,0,0));
                foreach(int s in new[]{-1,1}) Box(t,"Leg",new Vector3(0,2.1f,s*2.7f),new Vector3(.24f,4.2f,.24f),true);
            }
            foreach(float x in new[]{-6f,-3f,6f})
                CarryRoadArena.Chevron(root,new Vector3(x,UpperHeight+.025f,0),90,Mat("Ochre"),.65f);
            // Open waiting strips precede the swept area; painted circle matches actual beam reach.
            const int segments=40;const float radius=3.05f;
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
                Vector3 p=new Vector3(2.2f+Mathf.Cos(a)*radius,UpperHeight+.027f,Mathf.Sin(a)*radius);
                Vector3 q=new Vector3(2.2f+Mathf.Cos(b)*radius,UpperHeight+.027f,Mathf.Sin(b)*radius);
                if(Mathf.Abs(p.z)<2.1f && Mathf.Abs(q.z)<2.1f) Line(root,p,q,.09f,Mat("Ochre"));
            }
        }
        private static void DeckRun(Transform root,Vector3 a,Vector3 b,float width,bool railing,bool collision=true)
        {
            Vector3 d=b-a;float length=d.magnitude;int n=Mathf.CeilToInt(length/4f);
            Quaternion rotation=Quaternion.FromToRotation(Vector3.right,d.normalized);
            if(collision)
            {
                var surface=Group(root,"Continuous bridge collision");surface.position=(a+b)*.5f;surface.rotation=rotation;
                Box(surface,"Deck support",new Vector3(0,-.12f,0),new Vector3(length,.24f,width),false);
            }
            for(int i=0;i<n;i++)
            {
                float part=length/n;var t=Place(root,width>3?"Deck":"SideDeck",Vector3.Lerp(a,b,(i+.5f)/n));
                t.rotation=rotation;t.localScale=new Vector3(part/4f,1,1);

                if(railing && !(t.position.x>-1 && t.position.x<6 && t.position.y>4))
                foreach(int s in new[]{-1,1})
                {
                    var rail=Place(t,"Rail",Vector3.zero);rail.localPosition=new Vector3(0,0,s*(width*.5f-.04f));
                    Box(rail,"Guardrail",new Vector3(0,.57f,0),new Vector3(4,1.14f,.08f),true);
                }
            }
        }
        private static void GuardrailRun(Transform root,float from,float to,float z)
        {
            int pieces=Mathf.CeilToInt((to-from)/4f);float length=(to-from)/pieces;
            for(int i=0;i<pieces;i++)
            {
                var rail=Place(root,"Rail",new Vector3(from+(i+.5f)*length,UpperHeight,z));
                rail.localScale=new Vector3(length/4,1,1);
                Box(rail,"Guardrail",new Vector3(0,.57f,0),new Vector3(4,1.14f,.08f),true);
            }
        }
        private static void BuildFlyoverCollider(Transform root)
        {
            // One closed static surface: panel boundaries must not have internal vertical
            // collider faces that catch an upright cart when reversing across a seam.
            const string path="Assets/_Project/Art/CarryItem/HeistRoutes/FlyoverCollision.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created=mesh==null;if(created)mesh=new Mesh{name="Continuous flyover"};else mesh.Clear();
            var profile=new[]{new Vector2(-22,0),new Vector2(-7.2f,UpperHeight),new Vector2(7.2f,UpperHeight),new Vector2(22,0)};
            var vertices=new Vector3[16];var triangles=new System.Collections.Generic.List<int>();
            for(int i=0;i<4;i++)
            {
                float x=profile[i].x,y=profile[i].y;int a=i*4;
                vertices[a]=new Vector3(x,y,-2.2f);vertices[a+1]=new Vector3(x,y,2.2f);
                vertices[a+2]=new Vector3(x,y-.24f,-2.2f);vertices[a+3]=new Vector3(x,y-.24f,2.2f);
                if(i==3)continue;int b=a+4;
                triangles.AddRange(new[]{a,a+1,b+1,a,b+1,b, a+2,b+2,b+3,a+2,b+3,a+3,
                    a,b,a+2,a+2,b,b+2, a+1,a+3,b+3,a+1,b+3,b+1});
            }
            triangles.AddRange(new[]{0,2,3,0,3,1,12,13,15,12,15,14});
            mesh.vertices=vertices;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(created)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);}
            var t=Group(root,"Continuous flyover collision");t.gameObject.layer=LayerMask.NameToLayer("Ground");
            t.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        private static void BuildLowerRoutes(Transform root)
        {
            foreach(int s in new[]{-1,1})
            {
                // Alternative crossings meet the existing broad outer bypasses.
                DeckRun(root,new Vector3(-14.1f,.04f,s*11.25f),new Vector3(-4.6f,.04f,s*11.25f),2.6f,true);
                DeckRun(root,new Vector3(8.9f,.04f,s*11.25f),new Vector3(17,.04f,s*11.25f),2.6f,true);
                foreach(float x in new[]{-12f,-8f,10.5f,14.5f})
                    CarryRoadArena.Joint(root,new Vector3(x,.052f,s*11.25f),2.3f,0,.5f);
                // Foot stairs let saboteurs get up to the existing material platforms.
                for(int i=0;i<9;i++)
                {
                    float height=(i+1)*.24f;
                    var step=Cube(root,"Access step",new Vector3(-3.5f+i*.42f,height*.5f,s*6.2f),new Vector3(.43f,height,1.5f),Mat("Concrete"),false);
                    Cube(root,"Step edge",new Vector3(-3.5f+i*.42f,height+.005f,s*6.2f),new Vector3(.06f,.01f,1.42f),Mat("Ochre"),null);
                }
            }
        }
        private static void BuildBays(Transform root)
        {
            Transform team=GameObject.Find("_Arena/TeamProps").transform;
            foreach(int s in new[]{-1,1})
            {
                string suffix=s>0?"A":"B";Material color=CarrySkyscraperAssets.Material(s>0?"TeamA":"TeamB");
                Transform tank=team.Find("Tank_"+suffix);tank.SetPositionAndRotation(new Vector3(23.04f,0,s*11f),Quaternion.identity);
                CarryRoadArena.Outline(root,new Vector3(19.6f,0,s*11f),new Vector2(3.8f,3.6f),color);
                CarryRoadArena.Polyline(root,new[]{new Vector3(18.1f,0,s*5.04f),new Vector3(19.5f,0,s*7f)},color,.16f);
                foreach(float z in new[]{8.3f})CarryRoadArena.Chevron(root,new Vector3(19.5f,.03f,s*z),s>0?0:180,color,.48f);
                var sign=Place(root,"PumpSign",new Vector3(22.3f,0,s*7.6f)); sign.rotation=Quaternion.Euler(0,s>0?180:0,0);SolidBounds(sign);
                CarryRoadArena.Place(root,"Curb",new Vector3(23.8f,0,s*8.35f),0,true);
            }
            // Remove outdated receiving outlines and the old workshop in the new driveway.
            var road=GameObject.Find("_Arena/Roadworks");
            if(road!=null) for(int i=road.transform.childCount-1;i>=0;i--)
            {
                var t=road.transform.GetChild(i);
                if(t.position.x>17 && Mathf.Abs(t.position.z)<10 &&
                    (t.name=="RW_ToolBench"||t.name=="RoadPaint"))Object.DestroyImmediate(t.gameObject);
            }
            // Bot finish approach stays in the through lane until it turns into its own bay.
            var routes=GameObject.Find("_Arena/BotRoutes");
            if(routes!=null)foreach(var t in routes.GetComponentsInChildren<Transform>())
                if(t.name=="Tank")t.position=new Vector3(19.6f,0,t.parent.name.Contains("A")?11:-11);
        }
        private static void BuildCrane(Transform root)
        {
            Place(root,"Crane",new Vector3(2.2f,0,3.7f)); // Existing core supports its foot.
            var beam=Place(root,"SuspendedBeam",new Vector3(2.2f,UpperHeight+.9f,0));
            var trigger=beam.gameObject.AddComponent<BoxCollider>();trigger.size=new Vector3(5.8f,.58f,.40f);trigger.isTrigger=true;
            var trap=beam.gameObject.AddComponent<SwingingBeamTrap>();trap.Radius=0;trap.Period=8;
            beam.gameObject.AddComponent<CarryCraneHazard>();
        }
        private static void BuildDetails(Transform root)
        {
            foreach(int s in new[]{-1,1})
            {
                SolidBounds(Place(root,"CableReel",new Vector3(-17.2f,0,s*8.6f)));
                SolidBounds(Place(root,"Generator",new Vector3(-24.4f,0,s*11.5f)));
                SolidBounds(Place(root,"CableReel",new Vector3(25.3f,0,s*6.5f)));
                CarryRoadArena.Place(root,"ToolBench",new Vector3(24.5f,0,s*8.3f),90,true);
                CarryRoadArena.Place(root,"BrickPallet",new Vector3(-16.8f,0,s*2.8f),s*12,true);
                Place(root,"Generator",new Vector3(3.4f,2.16f,s*8.3f));
                for(int i=0;i<3;i++)CarryRoadArena.Place(root,"Debris",new Vector3(-25+i*3,.005f,s*13.5f),i*29,false);
            }
        }
        private static Transform Place(Transform parent,string model,Vector3 position)
        {
            var t=Group(parent,"HR_"+model);t.position=position;
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/HR_"+model+".fbx");
            PrefabUtility.InstantiatePrefab(asset,t);return t;
        }
        private static void SolidBounds(Transform t)
        {
            Quaternion q=t.rotation;t.rotation=Quaternion.identity;Bounds bounds=CarryItemDress.BoundsOf(t.gameObject);
            var c=t.gameObject.AddComponent<BoxCollider>();c.center=t.InverseTransformPoint(bounds.center);c.size=bounds.size;t.rotation=q;t.gameObject.layer=LayerMask.NameToLayer("Cover");
        }
        private static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Road+"/Materials/RW_"+name+".mat");
        private static void Box(Transform parent,string name,Vector3 center,Vector3 size,bool cover)
        {
            var t=Group(parent,name);var c=t.gameObject.AddComponent<BoxCollider>();c.center=center;c.size=size;t.gameObject.layer=LayerMask.NameToLayer(cover?"Cover":"Ground");
        }
        private static Transform Cube(Transform parent,string name,Vector3 position,Vector3 size,Material mat,bool? cover,bool local=false)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            if(local)go.transform.localPosition=position;else go.transform.position=position;
            go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
            if(cover==null)Object.DestroyImmediate(go.GetComponent<Collider>());else go.layer=LayerMask.NameToLayer(cover.Value?"Cover":"Ground");
            return go.transform;
        }
        private static void Line(Transform root,Vector3 a,Vector3 b,float width,Material mat)
        { var t=Cube(root,"Safety arc",(a+b)*.5f,new Vector3(width,.008f,Vector3.Distance(a,b)),mat,null);t.rotation=Quaternion.LookRotation(b-a); }
    }
}
