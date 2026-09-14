using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.UI;
using Igruha.Minigames.Circus;
using Igruha.Minigames.CansOrder;

namespace Igruha.EditorTools
{
    /// <summary>Original fairground, viewing windows and replacement of the remaining bought scenery.</summary>
    internal static class CircusCraftBuilder
    {
        internal static bool IsBought(string path) => !string.IsNullOrEmpty(path) &&
            (path.Contains("/Synty/") || path.Contains("/PolygonHorrorCarnival/") || path.Contains("/PolygonCasino/") ||
             path.Contains("/PolygonGeneric/") || path.Contains("/PolygonNightclubs/"));

        internal static void Apply(Transform root,Transform arena,CircusArenaConfig config)
        {
            // Unpack only scene prefab instances whose sources retain bought dependencies.
            foreach(var t in arena.GetComponentsInChildren<Transform>(true))
            {
                if(t==null || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))continue;
                string path=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if(!string.IsNullOrEmpty(path) && AssetDatabase.GetDependencies(path).Any(IsBought))
                    PrefabUtility.UnpackPrefabInstance(t.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            }
            var oldEnvironment=arena.Find("Environment");
            if(oldEnvironment!=null)UnityEngine.Object.DestroyImmediate(oldEnvironment.gameObject);
            // Fit the full face inside the front opening, above the can shelf.
            // The shared camera can still look up from the lower cage levels.
            var scoreboard=arena.Find("Scoreboard");
            scoreboard.position=new Vector3(0,config.ScoreboardHeight+1.73f,0);
            scoreboard.localScale=Vector3.one*.82f;
            BuildFairground(root,config);
            ApplyWindowsAndHud(arena);
            BuildChains(root,arena,config);
            ReplaceShelfDecor(arena);
            BuildScoreTrim(arena);
            CircusCraftEffects.Build(arena,config);
            // Old dressed dirt, bulbs and decorative anchors sometimes survive as disabled meshes.
            // Remove their asset references as well as their visible renderers.
            foreach(var filter in arena.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null || !IsBought(AssetDatabase.GetAssetPath(filter.sharedMesh)))continue;
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer!=null)UnityEngine.Object.DestroyImmediate(renderer);
                UnityEngine.Object.DestroyImmediate(filter);
            }
            foreach(var renderer in arena.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++)
                    if(materials[i]!=null && IsBought(AssetDatabase.GetAssetPath(materials[i])))
                    {materials[i]=CircusNightAssets.Material("CN_BlackIron");changed=true;}
                if(changed)renderer.sharedMaterials=materials;
            }
        }

        internal static void ApplyWindowsAndHud(Transform arena)
        {
            foreach(var cage in arena.GetComponentsInChildren<CageStation>(true))
            {
                Hide(cage.transform.Find("Wall_1"));
                Hide(cage.transform.Find("Roof"));
                Hide(cage.transform.Find("Frame/Rail_1_1"));
                Hide(cage.transform.Find("Frame/Rail_1_2"));
                Replace(cage.transform,"CN_CageWindow",Vector3.zero);
            }
            var canvas=GameObject.Find("_UI/Canvas");
            if(canvas==null)return;
            var hud=canvas.transform.Find("StopwatchHud");
            if(hud!=null)UnityEngine.Object.DestroyImmediate(hud.gameObject);
            var board=arena.GetComponentInChildren<WorldScoreboard>();
            if(board!=null)board.SetFaces(board.GetComponentsInChildren<WorldScoreboardFace>(true));
            var status=canvas.transform.Find("StopwatchStatus") as RectTransform;
            if(status!=null)
            {
                status.anchorMin=status.anchorMax=new Vector2(.5f,0);status.pivot=new Vector2(.5f,0);
                status.anchoredPosition=new Vector2(0,132);status.sizeDelta=new Vector2(900,60);
                var label=status.GetComponent<TMP_Text>();label.alignment=TextAlignmentOptions.Center;
            }
        }

        private static void BuildFairground(Transform root,CircusArenaConfig config)
        {
            var fair=new GameObject("OriginalFairground").transform;fair.SetParent(root,false);
            float floor=config.TentFloorHeight;
            for(int i=0;i<16;i++)
            {
                float angle=i*22.5f+11.25f;
                var seating=Add(fair,"CN_Bleachers",Radial(angle,12.3f,floor),angle);
                Add(seating.transform,"CN_SeatPads",new Vector3(0,.43f,-.55f),0);
                Add(seating.transform,"CN_SeatPads",new Vector3(0,1.21f,.89f),0);
                if(i%2==0)Add(fair,"CN_ShowPennant",Radial(angle-9,13.8f,floor),angle);
            }
            string[] signs={"БИЛЕТЫ","ПРИЗЫ","ПОПКОРН","СЛАДОСТИ","БИЛЕТЫ","ПРИЗЫ","ПОПКОРН","ШОУ БРУНО"};
            for(int i=0;i<8;i++)
            {
                float angle=i*45+22.5f;
                var stall=Add(fair,"CN_Stall_"+(i%2),Radial(angle,15.65f,floor),angle);
                Label(stall.transform,signs[i],new Vector3(0,2.33f,-.86f),new Vector2(2.1f,.3f),1.9f);
                string prop=i%3==0?"CN_PopcornTubs":i%3==1?"CN_PrizeShelf":"CN_JugglingSet";
                Add(stall.transform,prop,new Vector3(0,i%3==1?1.02f:1.17f,.12f),0,i%3==1?.70f:.78f);
                Add(fair,"CN_Balloons",Radial(angle+6.5f,15.4f,floor),angle);
                Add(stall.transform,"CN_TicketRolls",new Vector3(-.98f,1.165f,-.40f),0);
                if(i%2==0)Add(fair,"CN_TouringDrum",Radial(angle+8,15.5f,floor),angle+15);
                Add(fair,"CN_PawTrail",Radial(angle,6.5f,.02f),angle+80);
            }
            for(int i=0;i<4;i++)
            {
                float angle=i*90;
                Add(fair,i%2==0?"CN_PopcornCart":"CN_PrizeWheel",Radial(angle,15.8f,floor),angle);
                Add(fair,"CN_TouringTrunk",Radial(angle+10,14.05f,floor),angle-10);
                Add(fair,"CN_Crate",Radial(angle+15,14.1f,floor),angle+8);
                Add(fair,"CN_RopeCoil",Radial(angle+8,13.9f,floor),angle);
                Add(fair,"CN_BalancePedestal",Radial(angle-9,15.9f,floor),angle);
                Add(fair,"CN_JugglingSet",Radial(angle-9,15.9f,floor+.71f),angle, .8f);
            }
            for(int i=0;i<12;i++)
            {
                float angle=i*30+15;
                var poster=Add(fair,"CN_BearPoster",Radial(angle,17.68f,3.3f),angle);
                Label(poster.transform,i%2==0?"БРУНО":"БОЛЬШОЕ ШОУ",new Vector3(0,1.76f,-.14f),new Vector2(1.02f,.3f),1.1f);
                Label(poster.transform,"ЦИРК",new Vector3(0,.24f,-.14f),new Vector2(1,.18f),.9f);
            }
            foreach(var light in root.GetComponentsInChildren<Light>().Where(l=>l.type==LightType.Spot && !l.name.StartsWith("CanvasWash") && !l.name.StartsWith("Cupola")))
            {
                var fixture=Add(fair,"CN_StageProjector",light.transform.position,0);
                fixture.transform.rotation=Quaternion.FromToRotation(Vector3.up,light.transform.forward);
                Vector3 at=light.transform.position;
                Vector3 mount=at.y<8 ? new Vector3(Mathf.Sign(at.x)*2.88f*.82f,config.ScoreboardHeight+1.73f-1.08f*.82f,Mathf.Sign(at.z)*2.88f*.82f)
                    : new Vector3(at.x,config.RiggingHeight,at.z);
                Rod(fair,"ProjectorSupport",at-light.transform.forward*.15f,mount,.035f);
            }
        }

        private static void BuildChains(Transform root,Transform arena,CircusArenaConfig config)
        {
            var chainRoot=arena.Find("Chains");
            for(int i=chainRoot.childCount-1;i>=0;i--)
                if(chainRoot.GetChild(i).name.StartsWith("OriginalSecondaryChain_"))
                    UnityEngine.Object.DestroyImmediate(chainRoot.GetChild(i).gameObject);
            foreach(var chain in chainRoot.GetComponentsInChildren<CageChain>(true))
            {
                var data=new SerializedObject(chain);
                var target=(Transform)data.FindProperty("target").objectReferenceValue;
                var cage=target.GetComponentInParent<CageStation>();
                var left=ChainAnchor(cage.transform,"OriginalChainAnchor_L",new Vector3(-1.42f,config.CageInnerHeight+.05f,-.78f));
                var right=ChainAnchor(cage.transform,"OriginalChainAnchor_R",new Vector3(1.42f,config.CageInnerHeight+.05f,-.78f));
                var second=new GameObject("OriginalSecondaryChain_"+cage.name).AddComponent<CageChain>();
                second.transform.SetParent(chainRoot,false);
                BindChain(chain,left,config);BindChain(second,right,config);
                Rod(root,"SuspensionCrossbar",chain.transform.position,second.transform.position,.075f);
            }
        }

        private static Transform ChainAnchor(Transform cage,string name,Vector3 position)
        {
            var anchor=cage.Find(name);
            if(anchor==null){anchor=new GameObject(name).transform;anchor.SetParent(cage,false);}
            anchor.localPosition=position;return anchor;
        }

        private static void BindChain(CageChain chain,Transform target,CircusArenaConfig config)
        {
            chain.transform.position=new Vector3(target.position.x,config.RiggingHeight,target.position.z);
            for(int i=chain.transform.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(chain.transform.GetChild(i).gameObject);
            const float step=.936f;
            int count=Mathf.CeilToInt((config.RiggingHeight-config.GetCageBottomHeight(0)-config.CageInnerHeight)/step)+1;
            var data=new SerializedObject(chain);var links=data.FindProperty("links");links.arraySize=count;
            for(int i=0;i<count;i++)links.GetArrayElementAtIndex(i).objectReferenceValue=Add(chain.transform,"CN_ChainSegment",Vector3.down*(step*i),0).transform;
            data.FindProperty("target").objectReferenceValue=target;data.FindProperty("segmentLength").floatValue=step;
            data.ApplyModifiedPropertiesWithoutUndo();chain.Apply();
        }

        private static void ReplaceShelfDecor(Transform arena)
        {
            foreach(var shelf in arena.GetComponentsInChildren<CanShelf>(true))
            {
                var bench=shelf.transform.Find("Bench");if(bench!=null)UnityEngine.Object.DestroyImmediate(bench.gameObject);
                Replace(shelf.transform,"CN_GameShelf",Vector3.zero);
                var bell=shelf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Bell");
                if(bell!=null)
                {
                    var parent=bell.parent;UnityEngine.Object.DestroyImmediate(bell.gameObject);
                    Replace(parent,"CN_DeskBell",Vector3.zero);
                }
            }
        }

        private static void BuildScoreTrim(Transform arena)
        {
            var board=arena.Find("Scoreboard");
            var old=board.Find("OriginalMarquee");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var trim=new GameObject("OriginalMarquee").transform;trim.SetParent(board,false);
            for(int i=0;i<4;i++)Add(trim,"CN_ScoreBulbs",Quaternion.Euler(0,i*90,0)*Vector3.forward*3.025f,i*90+180);
            foreach(var renderer in board.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name.StartsWith("BoardFrame"))renderer.sharedMaterial=CircusNightAssets.Material("CN_AgedBrass");
            foreach(var renderer in arena.Find("Rigging").GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial=CircusNightAssets.Material(renderer.name.Contains("Brace")?"CN_AgedBrass":"CN_BlackIron");
            // Cans results use one text field for both name and status. Give it the
            // full right column instead of the generic name-only field width.
            if(board.Find("ArrangementPanel")!=null)
            {
                foreach(var face in board.GetComponentsInChildren<WorldScoreboardFace>(true))
                    for(int i=5;i<=8;i++)
                    {
                        var label=face.transform.Find("Row_"+i+"_Label").GetComponent<TMP_Text>();
                        var at=label.rectTransform.anchoredPosition;at.x=155;
                        label.rectTransform.anchoredPosition=at;label.rectTransform.sizeDelta=new Vector2(240,30);
                        label.overflowMode=TextOverflowModes.Ellipsis;
                    }
                var panel=board.GetComponentInChildren<CanOrderArrangementPanel>();
                var data=new SerializedObject(panel);var materials=data.FindProperty("symbolMaterials");
                for(int i=0;i<materials.arraySize;i++)
                {
                    var oldMaterial=(Material)materials.GetArrayElementAtIndex(i).objectReferenceValue;
                    string path=CircusNightAssets.Materials+"/CN_BoardSymbol_"+i+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
                    material.SetColor("_BaseColor",oldMaterial.GetColor("_BaseColor"));material.SetFloat("_Cull",0);
                    EditorUtility.SetDirty(material);materials.GetArrayElementAtIndex(i).objectReferenceValue=material;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                foreach(var label in panel.GetComponentsInChildren<TMP_Text>(true))
                    if(label.name.EndsWith("_Name")){label.fontSize=1.5f;label.enableAutoSizing=true;label.fontSizeMin=.9f;label.fontSizeMax=1.5f;}
            }
        }

        private static void Rod(Transform parent,string name,Vector3 a,Vector3 b,float width)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent,false);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);
            go.transform.localScale=new Vector3(width,(b-a).magnitude*.5f,width);
            go.GetComponent<MeshRenderer>().sharedMaterial=CircusNightAssets.Material("CN_BlackIron");
        }

        internal static GameObject Add(Transform parent,string name,Vector3 position,float yaw=0,float scale=1)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(CircusNightAssets.Prefabs+"/"+name+".prefab");
            if(prefab==null)throw new InvalidOperationException("Missing original circus prop: "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=name;
            go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;
            return go;
        }

        private static void Replace(Transform parent,string name,Vector3 at)
        {
            var old=parent.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            Add(parent,name,at);
        }

        private static void Hide(Transform t)
        {if(t!=null)foreach(var r in t.GetComponentsInChildren<Renderer>(true))r.enabled=false;}

        private static Vector3 Radial(float angle,float radius,float y)=>Quaternion.Euler(0,angle,0)*Vector3.forward*radius+Vector3.up*y;

        private static void Label(Transform parent,string text,Vector3 at,Vector2 size,float fontSize)
        {
            var go=new GameObject("Lettering");go.transform.SetParent(parent,false);go.transform.localPosition=at;
            var label=go.AddComponent<TextMeshPro>();label.text=text;label.fontSize=fontSize;label.fontSizeMax=fontSize;label.fontSizeMin=fontSize*.55f;
            label.enableAutoSizing=true;label.fontStyle=FontStyles.Bold;label.alignment=TextAlignmentOptions.Center;
            label.color=new Color(.98f,.89f,.68f);label.rectTransform.sizeDelta=size;
        }
    }
}
