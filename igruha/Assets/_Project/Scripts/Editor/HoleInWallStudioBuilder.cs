using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Igruha.Minigames.HoleInWall;
using static Igruha.EditorTools.HoleInWallStudioAssets;
using Object = UnityEngine.Object;

namespace Igruha.EditorTools
{
    /// <summary>Sunlit retro aquatic pavilion with vaulted glazing and ceramic game gates.</summary>
    public static class HoleInWallStudioBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/HoleInWall.unity";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";
        internal const float RubberDeckTop = .034f;
        private const float AudienceRowRise = .75f;
        private const int AudienceRows = 3;
        private const float VaultSpringY = 14.4f;
        private const float VaultModelHalfWidth = 31f;
        private static TMP_FontAsset font;

        [MenuItem("Igruha/Дырка в стене/Собственная студия — пересобрать")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before rebuilding.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open HoleInWall before rebuilding the studio.");
            Import();
            HoleInWallArenaBuilder.Build();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Audit();
        }

        internal static void Build(Transform arena, HoleInWallConfig config, HoleInWallTrack[] tracks)
        {
            var old = arena.Find("_Studio");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform studio = Group(arena, "_Studio");
            EnsureFont();
            Architecture(studio, config);
            PortalsAndBoards(studio, config, tracks);
            Audience(studio, config);
            PoolFinish(arena, studio, config);
            StageFinish(arena, config);
            Lighting(studio, config);
            var gameData=new SerializedObject(Object.FindFirstObjectByType<HoleInWallMinigame>());
            gameData.FindProperty("ropeMaterial").objectReferenceValue=MakeMaterial("Rope",new Color(.93f,.83f,.59f),.26f);
            gameData.FindProperty("ropeTracerMaterial").objectReferenceValue=MakeMaterial("RopeTracer",new Color(.045f,.42f,.46f),.3f);
            gameData.FindProperty("ropeCollarMaterial").objectReferenceValue=Mat("Gold");
            gameData.ApplyModifiedPropertiesWithoutUndo();
            foreach (Renderer renderer in studio.GetComponentsInChildren<Renderer>())
            {
                bool isFan = renderer.GetComponentInParent<HoleInWallCrowd>() != null;
                if (isFan || renderer.name == "HS_Seat") renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (!isFan && renderer.GetComponent<TMP_Text>() == null)
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Architecture(Transform studio, HoleInWallConfig c)
        {
            Transform shell = Group(studio, "Sunlit aquatic pavilion");
            float half = c.ArenaWidth * .5f;
            float far = c.ArenaFarZ + 9;
            float near = c.ArenaNearZ - 9;
            float centerZ = (far + near) * .5f;
            float floorY = HoleInWallProps.RimTopY(c) - .2f;
            float vaultScaleX = (half + 9) / VaultModelHalfWidth;
            float wainscotHeight = HoleInWallStudioSurfaces.WainscotTop - (floorY + .2f);
            float wainscotCenter = floorY + .2f + wainscotHeight * .5f;
            int windowBays = Mathf.FloorToInt((far - near) / 6);
            float windowPitch = (far - near) / windowBays;
            Panel(shell,"Far promenade",new Vector3(0,floorY,c.ArenaFarZ+4.5f),new Vector3(c.ArenaWidth,.4f,9),Mat("Ivory"),true);
            Panel(shell,"Near promenade",new Vector3(0,floorY,c.ArenaNearZ-4.5f),new Vector3(c.ArenaWidth,.4f,9),Mat("Ivory"),true);
            for(int side=-1;side<=1;side+=2)
            {
                Panel(shell,"Side promenade",new Vector3(side*(half+4.5f),floorY,centerZ),new Vector3(9,.4f,far-near),Mat("Ivory"),true);
                Panel(shell,"Sunwashed masonry",new Vector3(side*(half+9),6,centerZ),new Vector3(.7f,17,far-near),Mat("Plaster"),true);
                Panel(shell,"Ceramic wainscot",new Vector3(side*(half+8.58f),wainscotCenter,centerZ),new Vector3(.14f,wainscotHeight,far-near),Mat("Mint"));
                Panel(shell,"Gallery deck",new Vector3(side*(half+7.25f),4.9f,centerZ),new Vector3(3.2f,.48f,far-near),Mat("Ivory"));
                Panel(shell,"Gallery fascia",new Vector3(side*(half+5.55f),4.85f,centerZ),new Vector3(.18f,.60f,far-near),Mat("Coral"));
                for(int bay=0;bay<windowBays;bay++)
                {
                    float z=near+(bay+.5f)*windowPitch;
                    Place(shell,"BathWindow",new Vector3(side*(half+8.55f),5.4f,z),side*90);
                    float pierBottom=floorY+.2f;
                    Panel(shell,"Gallery pier",new Vector3(side*(half+5.8f),(pierBottom+4.7f)*.5f,z-2.9f),new Vector3(.48f,4.7f-pierBottom,.48f),Mat("Ivory"));
                    var railing=Place(shell,"BathRailing",new Vector3(side*(half+5.5f),5.18f,z),side*90);
                    railing.localScale=new Vector3(windowPitch/6,1,1);
                }
                // Planting lives in the open end promenades, clear of seats and balconies.
                foreach(float z in new[]{near+4.5f,far-4.5f})
                {
                    var palm=Place(shell,"PlanterPalm",new Vector3(side*(half+2.3f),floorY+.2f,z),side*25);
                    palm.localScale=Vector3.one*.8f;
                }
                foreach(float z in new[]{near+4,9f,far-4})
                    Place(shell,"Lifebuoy",new Vector3(side*(half+1.1f),floorY+.2f,z+2),side*90);
            }
            foreach(float z in new[]{far,near})
            {
                bool back=z==far;
                Panel(shell,"End masonry",new Vector3(0,6,z),new Vector3(c.ArenaWidth+18,17,.7f),Mat("Plaster"),true);
                Panel(shell,"End ceramic plinth",new Vector3(0,wainscotCenter,z+(back?-.43f:.43f)),new Vector3(c.ArenaWidth+18,wainscotHeight,.12f),Mat("Mint"));
                for(int side=-1;side<=1;side+=2)
                    for(int bay=0;bay<3;bay++)
                        Place(shell,"BathWindow",new Vector3(side*(11.4f+bay*7.6f),5.7f,z+(back?-.48f:.48f)),back?0:180);
                if (back) Place(shell,"SunMedallion",new Vector3(0,10.2f,z+(back?-.5f:.5f)),back?0:180);
                Panel(shell,"Crown moulding",new Vector3(0,VaultSpringY,z+(back?-.48f:.48f)),new Vector3(c.ArenaWidth+18,.45f,.55f),Mat("Ivory"));
            }
            // Contiguous curved bays replace the flat sky card. Glazing transmits daylight;
            // the separate structural ribs still cast the characteristic roof shadows.
            const float roofBayDepth = 6;
            int roofBays = windowBays;
            float bayDepth = (far-near)/roofBays;
            for(int bay=0;bay<roofBays;bay++)
            {
                var roof=Place(shell,"BathRoofBay",new Vector3(0,VaultSpringY,near+(bay+.5f)*bayDepth));
                roof.localScale=new Vector3(vaultScaleX,1,bayDepth/roofBayDepth);
                roof.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
            for(int bay=0;bay<=roofBays;bay++)
            {
                var rib=Place(shell,"BathRoofRib",new Vector3(0,VaultSpringY,near+bay*bayDepth));
                rib.localScale=new Vector3(vaultScaleX,1,1);
            }
            foreach(float z in new[]{near,far})
            {
                var end=Place(shell,"BathRoofEnd",new Vector3(0,VaultSpringY,z),z==far?0:180);
                end.localScale=new Vector3(vaultScaleX,1,1);
            }
            for(int side=-1;side<=1;side+=2)
            {
                float corniceX=side*(half+8.42f);
                Panel(shell,"Vault springing cornice",new Vector3(corniceX,VaultSpringY,centerZ),new Vector3(.55f,.45f,far-near),Mat("Ivory"));
                Panel(shell,"Cornice gold bead",new Vector3(side*(half+8.12f),VaultSpringY-.13f,centerZ),new Vector3(.06f,.09f,far-near),Mat("Gold"));
                foreach(float z in new[]{near,far})
                {
                    float cornerZ=z+(z==far?-.48f:.48f);
                    Panel(shell,"Cornice corner block",new Vector3(corniceX,VaultSpringY,cornerZ),new Vector3(.70f,.55f,.70f),Mat("Ivory"));
                }
            }
            HoleInWallStudioSurfaces.Build(shell, c);
            Place(shell,"BathClock",new Vector3(0,6.65f,near+.57f),180);
            Panel(shell,"Club sign frame",new Vector3(0,4.35f,near+.57f),new Vector3(9.4f,1.36f,.16f),Mat("Ivory"));
            Panel(shell,"Club sign enamel",new Vector3(0,4.35f,near+.67f),new Vector3(9.13f,1.1f,.07f),Mat("Blue"));
            var club=Label(shell,"Club name","АКВА-КЛУБ",new Vector3(0,4.38f,near+.73f),new Vector2(8.7f,.86f),6.5f,new Color(.97f,.94f,.77f));
            club.transform.localRotation=Quaternion.Euler(0,180,0);
            // Quiet, functional details in the rear promenade, outside every play lane.
            foreach(float x in new[]{-10f,10f})
                Place(shell,"BathDoor",new Vector3(x,floorY+.2f,near+.50f),180);
            foreach(float x in new[]{-18f,-3.6f,3.6f,18f})
            {
                var bench=Place(shell,"BathBench",new Vector3(x,floorY+.2f,near+1.05f),180);
                bench.gameObject.layer=LayerMask.NameToLayer("Cover");
                var collision=bench.gameObject.AddComponent<BoxCollider>();
                collision.center=new Vector3(0,.68f,0);collision.size=new Vector3(3.3f,1.36f,.82f);
            }
        }

        private static void PortalsAndBoards(Transform studio,HoleInWallConfig c,HoleInWallTrack[] tracks)
        {
            Transform entrances=Group(studio,"Lane portals");
            var game=Object.FindFirstObjectByType<HoleInWallMinigame>();
            for(int i=0;i<c.TrackCount;i++)
            {
                float x=c.TrackCenterX(i);
                Color lane=HoleInWallPalette.LaneAccent(i);
                Material accent=MakeMaterial("Lane"+i,lane,.45f,0,.15f);
                var portal=Place(entrances,"BathGate",new Vector3(x,-.1f,c.WallStartZ+1.8f));
                portal.localScale=new Vector3((c.PlatformWidth+.8f)/9.4f,1,1);
                float deckY=HoleInWallProps.RimTopY(c);
                for(int side=-1;side<=1;side+=2)
                {
                    float footX=x+side*4.75f*portal.localScale.x;
                    Panel(entrances,"Gate grounded pier",new Vector3(footX,(deckY-.1f)*.5f,c.WallStartZ+1.8f),
                        new Vector3(.72f,-.1f-deckY,.82f),Mat("Ivory"),true);
                    Panel(entrances,"Gate pier base",new Vector3(footX,deckY+.12f,c.WallStartZ+1.8f),
                        new Vector3(1.15f,.24f,1.3f),Mat("Mint"));
                }

                // Only the emissive channel is lane coded; the architecture stays coherent.
                var r=portal.GetComponent<Renderer>();
                r.sharedMaterials=r.sharedMaterials.Select(m=>m.name=="HS_Glow"?accent:m).ToArray();
                var board=Group(entrances,"Scoreboard_"+i);
                Vector3 p=new Vector3(x,5.35f,c.WallStartZ+1.25f);
                var instrument=Place(board,"Scoreboard",p);
                var display=instrument.GetComponent<Renderer>();
                display.sharedMaterials=display.sharedMaterials.Select(m=>m.name=="HS_Glow"?accent:m).ToArray();
                Label(board,"Lane number",(i+1).ToString("00"),p+new Vector3(-1.88f,.10f,-.73f),new Vector2(1.05f,1.1f),8,new Color(.035f,.20f,.19f));
                Label(board,"Score caption","ПРОХОДЫ",p+new Vector3(.55f,.94f,-.59f),new Vector2(2.8f,.35f),2.4f,new Color(.7f,.81f,.86f));
                var score=Label(board,"ScoreLine","00",p+new Vector3(.55f,.18f,-.59f),new Vector2(2.7f,1.1f),11,Color.white);
                var wall=Label(board,"WallLine","ОЖИДАНИЕ",p+new Vector3(.55f,-.63f,-.59f),new Vector2(3.0f,.38f),2.7f,lane);
                var progress=new Renderer[c.WallCount];
                for(int pip=0;pip<c.WallCount;pip++)
                    progress[pip]=Panel(board,"Progress "+pip,p+new Vector3(.55f+(pip-(c.WallCount-1)*.5f)*.36f,-1.04f,-.60f),
                        new Vector3(.26f,.07f,.025f),Mat("Steel")).GetComponent<Renderer>();
                var component=board.gameObject.AddComponent<HoleInWallScoreboard>();
                var so=new SerializedObject(component);
                so.FindProperty("game").objectReferenceValue=game;
                so.FindProperty("track").objectReferenceValue=tracks[i];
                so.FindProperty("wallLine").objectReferenceValue=wall;
                so.FindProperty("scoreLine").objectReferenceValue=score;
                var indicators=so.FindProperty("progressLights");indicators.arraySize=progress.Length;
                for(int n=0;n<progress.Length;n++)indicators.GetArrayElementAtIndex(n).objectReferenceValue=progress[n];
                so.FindProperty("accentColor").colorValue=lane;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static void Audience(Transform studio,HoleInWallConfig c)
        {
            Transform tiers=Group(studio,"Audience tiers");
            Transform crowd=Group(studio,"Original audience");
            Panel(tiers,"Rear stage foundation",new Vector3(0,.8f,c.ArenaFarZ+5.35f),
                new Vector3(41,6.1f,3.2f),Mat("Mint"),true);
            int count=0;
            for(int side=-1;side<=1;side+=2)
                for(int row=0;row<AudienceRows;row++)
                {
                    float x=side*(c.ArenaWidth*.5f+2.2f+row*1.5f);
                    float y=-.6f+row*AudienceRowRise;
                    float z=(c.ArenaFarZ+c.ArenaNearZ)*.5f;
                    float baseY=HoleInWallProps.RimTopY(c);
                    float tierHeight=y-baseY;
                    Panel(tiers,"Grounded ceramic tier",new Vector3(x,baseY+tierHeight*.5f,z),new Vector3(1.6f,tierHeight,c.ArenaDepth-3),Mat("Mint"),true);
                    Panel(tiers,"Tier trim",new Vector3(x-side*.77f,y-.05f,z),new Vector3(.09f,.12f,c.ArenaDepth-3),Mat("Ivory"));
                    for(float seatZ=c.ArenaNearZ+3;seatZ<c.ArenaFarZ-1;seatZ+=1.65f)
                    {
                        float yaw=side*90;
                        Place(tiers,"Seat",new Vector3(x,y,seatZ),yaw);
                        var fan=Place(crowd,"Fan"+((count*5+row)%7)+"_Idle",new Vector3(x,y,seatZ)+Quaternion.Euler(0,yaw,0)*new Vector3(0,0,-.45f),yaw,"Fan_"+count);
                        fan.localScale=Vector3.one*(.88f+(count%5)*.035f);
                        fan.localPosition+=new Vector3(((count*17)%7-3)*.025f,0,((count*13)%5-2)*.055f);
                        fan.localRotation*=Quaternion.Euler(0,((count*11)%9-4)*2.5f,0);
                        count++;
                    }
                }
            // A raised rear audience is visible over the approaching walls.
            for(int row=0;row<2;row++)
                for(int col=0;col<24;col++)
                {
                    float x=(col-11.5f)*1.65f;
                    float y=3.8f+row*.8f;
                    float z=c.ArenaFarZ+4.6f+row*1.5f;
                    if(col==0)Panel(tiers,"Rear tier",new Vector3(0,y-.5f,z),new Vector3(41,1,1.5f),Mat("Blue"),true);
                    Place(tiers,"Seat",new Vector3(x,y,z));
                    Place(crowd,"Fan"+((count*5+row)%7)+"_Idle",new Vector3(x,y,z-.45f),0,"Fan_"+count++);
                }
            foreach(var r in crowd.GetComponentsInChildren<Renderer>()) r.shadowCastingMode=ShadowCastingMode.Off;
            var component=crowd.gameObject.AddComponent<HoleInWallCrowd>();
            var so=new SerializedObject(component);
            so.FindProperty("game").objectReferenceValue=Object.FindFirstObjectByType<HoleInWallMinigame>();
            var wardrobe=so.FindProperty("wardrobe");wardrobe.arraySize=7;
            for(int i=0;i<7;i++)
            {
                wardrobe.GetArrayElementAtIndex(i).FindPropertyRelative("Idle").objectReferenceValue=Mesh("Fan"+i+"_Idle");
                wardrobe.GetArrayElementAtIndex(i).FindPropertyRelative("Cheer").objectReferenceValue=Mesh("Fan"+i+"_Cheer");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PoolFinish(Transform arena,Transform studio,HoleInWallConfig c)
        {
            Transform finish=Group(studio,"Pool detailing");
            float z=(c.ArenaFarZ+c.ArenaNearZ)*.5f;
            float half=c.ArenaWidth*.5f;
            float rim=HoleInWallProps.RimTopY(c);
            for(int side=-1;side<=1;side+=2)
            {
                Panel(finish,"Ceramic coping",new Vector3(side*half,rim+.08f,z),new Vector3(.75f,.17f,c.ArenaDepth),Mat("Ivory"));
                Panel(finish,"Pool light channel",new Vector3(side*(half-.24f),-2.37f,z),new Vector3(.08f,.10f,c.ArenaDepth-.7f),Mat("Glow"));
                for(float zz=c.ArenaNearZ+1;zz<c.ArenaFarZ;zz+=1.6f)
                    Panel(finish,"Coping joint",new Vector3(side*half,rim+.171f,zz),new Vector3(.73f,.012f,.025f),Mat("Blue"));
            }
            foreach(float zz in new[]{c.ArenaNearZ,c.ArenaFarZ})
            {
                Panel(finish,"End coping",new Vector3(0,rim+.08f,zz),new Vector3(c.ArenaWidth,.17f,.75f),Mat("Ivory"));
                Panel(finish,"End light channel",new Vector3(0,-2.37f,zz+(zz==c.ArenaNearZ?.24f:-.24f)),
                    new Vector3(c.ArenaWidth-.7f,.10f,.08f),Mat("Glow"));
            }
            for(int side=-1;side<=1;side+=2)
                foreach(float zz in new[]{c.ArenaNearZ,c.ArenaFarZ})
                    Panel(finish,"Coping corner stone",new Vector3(side*half,rim+.082f,zz),
                        new Vector3(.77f,.178f,.77f),Mat("Ivory"));
            for(int lane=0;lane<c.TrackCount;lane++)
            {
                float ladderX=c.TrackCenterX(lane)+c.PlatformWidth*.5f+c.TrackGap*.5f;
                var depth=Label(finish,"Pool depth",c.PoolDepth.ToString("0.0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))+" м",
                    new Vector3(ladderX-.95f,rim+.182f,c.ArenaNearZ),new Vector2(.9f,.36f),2.1f,new Color(.07f,.25f,.25f));
                depth.transform.localRotation=Quaternion.Euler(90,0,0);
            }
            var water=arena.Find("Pool/Water").GetComponent<Renderer>().sharedMaterial;
            water.SetColor("_ShallowColor",new Color(.13f,.73f,.69f));
            water.SetColor("_DeepColor",new Color(.025f,.42f,.47f));
            water.SetFloat("_RippleStrength",.065f);
            water.SetFloat("_RippleScale",1.15f);
            water.SetFloat("_WaveHeight",.055f);
            water.SetFloat("_SpecStrength",1.5f);
            water.SetFloat("_SpecPower",150);
            water.SetFloat("_FresnelStrength",.24f);
            water.SetFloat("_RefractionStrength",.012f);
            water.SetFloat("_UnderwaterClarity",1);
            water.SetFloat("_Opacity",.36f);
            EditorUtility.SetDirty(water);
        }

        private static void StageFinish(Transform arena,HoleInWallConfig c)
        {
            for(int i=0;i<c.TrackCount;i++)
            {
                Transform track=arena.Find("Track_"+i);
                const float rubberTop = RubberDeckTop;
                var platform = track.Find("Platform").GetComponent<BoxCollider>();
                // Collider lives under a unit cube scaled to the platform dimensions.
                platform.center = new Vector3(0, rubberTop / (2 * c.PlatformThickness), 0);
                platform.size = new Vector3(1, 1 + rubberTop / c.PlatformThickness, 1);
                Transform finish=Group(track,"Studio platform details");
                Transform wallFinish=Group(track.Find("Wall"),"Studio wall frame");
                float front=-c.WallThickness*.5f-.025f;
                Panel(wallFinish,"Folded cardboard top",new Vector3(0,c.WallHeight+.025f,0),
                    new Vector3(c.WallWidth+.10f,.10f,c.WallThickness+.06f),Mat("Cardboard"));
                Panel(wallFinish,"Top accent inlay",new Vector3(0,c.WallHeight-.075f,front),
                    new Vector3(c.WallWidth-.12f,.035f,.018f),Mat("Lane"+i));
                for(int side=-1;side<=1;side+=2)
                {
                    Panel(wallFinish,"Cardboard edge",new Vector3(side*(c.WallWidth*.5f+.025f),c.WallHeight*.5f,0),
                        new Vector3(.10f,c.WallHeight,c.WallThickness+.04f),Mat("Cardboard"));
                    Panel(wallFinish,"Corner reinforcement",new Vector3(side*(c.WallWidth*.5f-.16f),c.WallHeight-.17f,front),
                        new Vector3(.28f,.25f,.035f),Mat("Ivory"));
                    Panel(wallFinish,"Corner fixing",new Vector3(side*(c.WallWidth*.5f-.16f),c.WallHeight-.17f,front-.035f),
                        new Vector3(.07f,.07f,.025f),Mat("Gold"));
                }
                for(int side=0;side<2;side++)
                {
                    var original=track.Find("FloorHalf_"+side).GetComponent<Renderer>();
                    original.sharedMaterial=Mat(side==0?"Coral":"Teal");
                    original.enabled=false;
                    float x=(side==0?-1:1)*c.PlatformWidth*.25f;
                    var deck=Place(finish,side==0?"DeckCoral":"DeckTeal",new Vector3(x,.022f,0));
                    deck.localScale=new Vector3(c.PlatformWidth*.5f/6*.985f,1,c.PlatformDepth/5*.98f);
                    deck.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    for(int k=0;k<3;k++)
                    {
                        Transform stripe=Panel(finish,"Inset grip stripe",new Vector3(x,.048f,-c.PlatformDepth*.5f+.24f+k*.12f),
                            new Vector3(c.PlatformWidth*.43f,.009f,.035f),Mat("Ink"));
                        stripe.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    }
                    Label(finish,"Deck lane",(i+1).ToString("00"),new Vector3(x,.052f,-.95f),new Vector2(1.6f,.6f),3.5f,Color.white)
                        .transform.localRotation=Quaternion.Euler(90,0,0);
                }
                for(int edge=-1;edge<=1;edge+=2)
                {
                    var fascia=Place(finish,"DeckFascia",new Vector3(0,0,edge*(c.PlatformDepth*.5f+.015f)),edge<0?0:180);
                    fascia.localScale=new Vector3(c.PlatformWidth/12,1,1);
                    var sideFascia=Place(finish,"DeckFascia",new Vector3(edge*(c.PlatformWidth*.5f+.015f),0,0),edge*90);
                    sideFascia.localScale=new Vector3(c.PlatformDepth/12,1,1);
                }
                for(int s=-1;s<=1;s+=2)
                    Panel(finish,"Platform bumper",new Vector3(s*(c.PlatformWidth*.5f-.22f),-.31f,-c.PlatformDepth*.5f-.025f),
                        new Vector3(.3f,.42f,.12f),Mat("Ink"));
            }
        }

        private static void Lighting(Transform studio,HoleInWallConfig c)
        {
            var lighting=GameObject.Find("_Lighting");
            if(lighting!=null)
            {
                var old=lighting.transform.Find("HoleInWallStudio");
                if(old!=null)Object.DestroyImmediate(old.gameObject);
            }
            var key=lighting==null?null:lighting.GetComponentInChildren<Light>(true);
            if(key==null)key=Group(studio,"Studio key").gameObject.AddComponent<Light>();
            key.type=LightType.Directional;key.color=new Color(1,.88f,.68f);key.intensity=1.85f;
            key.transform.rotation=Quaternion.Euler(48,-32,0);key.shadows=LightShadows.Soft;
            key.shadowStrength=.8f;key.shadowBias=.035f;key.shadowNormalBias=.14f;
            RenderSettings.sun=key;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.58f,.73f,.78f);
            RenderSettings.ambientEquatorColor=new Color(.36f,.47f,.43f);
            RenderSettings.ambientGroundColor=new Color(.22f,.27f,.24f);
            RenderSettings.fog=false;
            // Broad reflected daylight; no floating fixture meshes in the playfield.
            var fill=Group(studio,"Reflected pool daylight").gameObject.AddComponent<Light>();
            fill.type=LightType.Directional;fill.color=new Color(.60f,.85f,1);fill.intensity=.35f;
            fill.transform.rotation=Quaternion.Euler(25,155,0);fill.shadows=LightShadows.None;
            const string profilePath="Assets/_Project/Settings/Volumes/HoleInWall_OriginalStudio.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
            if(!profile.TryGet(out Tonemapping tone))tone=profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet(out Bloom bloom))bloom=profile.Add<Bloom>();
            bloom.intensity.Override(.14f);bloom.threshold.Override(1.15f);bloom.scatter.Override(.6f);
            if(!profile.TryGet(out ColorAdjustments color))color=profile.Add<ColorAdjustments>();
            color.postExposure.Override(.10f);color.contrast.Override(8);color.saturation.Override(8);
            if(!profile.TryGet(out Vignette vignette))vignette=profile.Add<Vignette>();
            vignette.intensity.Override(.08f);vignette.smoothness.Override(.55f);
            foreach (VolumeComponent component in profile.components)
            {
                if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
            var volume=Group(studio,"Studio volume").gameObject.AddComponent<Volume>();
            volume.isGlobal=true;volume.priority=5;volume.sharedProfile=profile;
            var camera=Camera.main;
            if(camera!=null)
            {
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.52f,.77f,.84f);
            }
        }

        private static void EnsureFont()
        {
            string path=Materials+"/HS_Cyrillic.asset";
            font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if(font==null)
            {
                AssetDatabase.CopyAsset("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset",path);
                font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            }
        }

        private static TextMeshPro Label(Transform parent,string name,string value,Vector3 position,Vector2 size,float fontSize,Color color)
        {
            Transform root=Group(parent,name);root.localPosition=position;
            var label=root.gameObject.AddComponent<TextMeshPro>();label.font=font;label.text=value;
            label.fontSize=fontSize;label.alignment=TextAlignmentOptions.Center;label.color=color;
            label.fontStyle=FontStyles.Bold;label.textWrappingMode=TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta=size;label.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return label;
        }

        [MenuItem("Igruha/Дырка в стене/Проверить собственную студию")]
        public static void Audit()
        {
            Physics.SyncTransforms();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=ScenePath)throw new InvalidOperationException("Audit requires HoleInWall.");
            var arena=GameObject.Find("_Arena");
            var c=AssetDatabase.LoadAssetAtPath<HoleInWallConfig>(ConfigPath);
            HoleInWallStudioSurfaces.Audit(arena.transform,c);
            HoleInWallForegroundBuilder.Audit(arena.transform,c);
            HoleInWallCameraBuilder.Audit();
            var game = UnityEngine.Object.FindFirstObjectByType<HoleInWallMinigame>();
            var supportRefs = new SerializedObject(game).FindProperty("poolSupports");
            var supports = arena.GetComponentsInChildren<Collider>(true).Where(x => x.name == "Support").ToArray();
            if (supports.Length != c.TrackCount * 4 || supportRefs.arraySize != supports.Length)
                throw new InvalidOperationException("Pool body contacts need all platform supports.");
            var linkedSupports = new System.Collections.Generic.HashSet<Collider>();
            for (int i = 0; i < supportRefs.arraySize; i++)
            {
                var support = supportRefs.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
                if (support == null || !support.enabled || support.isTrigger || !supports.Contains(support) || !linkedSupports.Add(support))
                    throw new InvalidOperationException("Invalid or duplicate pool support contact reference.");
            }
            foreach (var cutout in arena.GetComponentsInChildren<WallCutout>(true))
            {
                var material = new SerializedObject(cutout).FindProperty("outlineMaterial").objectReferenceValue as Material;
                if (material == null || material.shader.name != "Igruha/HoleInWall/Cutout Outline")
                    throw new InvalidOperationException("Cutout needs the contrast outline shader in the build.");
            }
            var boards=arena.GetComponentsInChildren<HoleInWallScoreboard>(true);
            if(boards.Length!=c.TrackCount)throw new InvalidOperationException("Scoreboards do not match lanes.");
            foreach(var board in boards)
            {
                var so=new SerializedObject(board);
                foreach(string field in new[]{"game","track","wallLine","scoreLine"})
                    if(so.FindProperty(field).objectReferenceValue==null)throw new InvalidOperationException("Missing board reference: "+field);
                var progress = so.FindProperty("progressLights");
                if(progress.arraySize!=c.WallCount)throw new InvalidOperationException("Scoreboard progress does not match wall count.");
                for(int i=0;i<progress.arraySize;i++)
                    if(progress.GetArrayElementAtIndex(i).objectReferenceValue==null)
                        throw new InvalidOperationException("Missing scoreboard progress light.");
            }
            var gatePiers=arena.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Gate grounded pier").ToArray();
            if(gatePiers.Length!=c.TrackCount*2)throw new InvalidOperationException("Each game gate needs two grounded piers.");
            foreach(var pier in gatePiers)
            {
                var bounds=pier.GetComponent<BoxCollider>().bounds;
                if(Mathf.Abs(bounds.min.y-HoleInWallProps.RimTopY(c))>.01f || Mathf.Abs(bounds.max.y+.1f)>.01f)
                    throw new InvalidOperationException("Gate pier does not connect deck and gate.");
            }
            var roofs=arena.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="HS_BathRoofBay").OrderBy(t=>t.position.z).ToArray();
            if(roofs.Length==0)throw new InvalidOperationException("Vault glazing is missing.");
            for(int i=1;i<roofs.Length;i++)
                if(Mathf.Abs(roofs[i-1].GetComponent<Renderer>().bounds.max.z-roofs[i].GetComponent<Renderer>().bounds.min.z)>.025f)
                    throw new InvalidOperationException("Open gap between roof bays.");
            foreach(var name in new[]{"Glass","GlassLight"})
                if(!Mat(name).IsKeywordEnabled("_EMISSION") || (Mat(name).globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack)!=0)
                    throw new InvalidOperationException("Daylit glass lost emission after import.");
            var decks=arena.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="HS_DeckCoral"||t.name=="HS_DeckTeal").ToArray();
            if(decks.Length!=c.TrackCount*2)throw new InvalidOperationException("Each platform needs two rubber deck halves.");
            foreach(var deck in decks)
                if(deck.GetComponent<Renderer>().bounds.max.y-c.PlatformSurfaceY>.05f || deck.GetComponent<Collider>()!=null)
                    throw new InvalidOperationException("Deck detail diverges from the collision surface.");
            var allTransforms=arena.GetComponentsInChildren<Transform>(true);
            var cornices=allTransforms.Where(t=>t.name=="Crown moulding"||t.name=="Vault springing cornice").ToArray();
            if(cornices.Length!=4||cornices.Any(t=>Mathf.Abs(t.position.y-VaultSpringY)>.01f))
                throw new InvalidOperationException("Cornices must meet at one height above the window arches.");
            foreach(var palm in allTransforms.Where(t=>t.name=="HS_PlanterPalm"))
                foreach(var solid in allTransforms.Where(t=>t.name=="Gallery pier"||t.name=="Gallery deck"||t.name=="Gallery fascia"||t.name=="Sunwashed masonry"||t.name=="End masonry"||t.name=="Grounded ceramic tier"||t.name=="Rear stage foundation"||t.name=="Rear tier"))
                    if(palm.GetComponent<Renderer>().bounds.Intersects(solid.GetComponent<Renderer>().bounds))
                        throw new InvalidOperationException("Palm intersects architecture or spectator seating: "+solid.name);
            int missing=0;
            foreach(Transform t in arena.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            var renderers=arena.GetComponentsInChildren<Renderer>(true);
            int badMaterials=renderers.Count(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader"));
            if(missing+badMaterials>0)throw new InvalidOperationException("Missing components or invalid materials.");
            if(arena.GetComponentsInChildren<HoleInWallCrowd>().Length!=1)throw new InvalidOperationException("Audience duplicated or missing.");
            var clearZone = new Bounds(new Vector3(0, c.WallHeight * .5f, (c.WallStartZ + c.ArenaNearZ) * .5f),
                new Vector3(c.ArenaWidth - 1.2f, c.WallHeight + .4f, c.WallStartZ - c.ArenaNearZ));
            foreach (Collider collider in arena.transform.Find("_Studio").GetComponentsInChildren<Collider>())
                if (clearZone.Intersects(collider.bounds))
                    throw new InvalidOperationException("Studio collision blocks the game or camera: " + collider.name);
            foreach (BoxCollider collider in arena.GetComponentsInChildren<BoxCollider>())
            {
                Transform fitted = collider.transform.Find("HS_Panel");
                if (fitted == null) fitted = collider.transform.Find("HS_Ladder");
                if (fitted == null) continue;
                Bounds visual = fitted.GetComponent<Renderer>().bounds;
                Bounds physical = collider.bounds;
                if (collider.name == "Platform")
                {
                    // The collider includes the rubber surface above this structural slab.
                    physical.center -= Vector3.up * (RubberDeckTop * .5f);
                    physical.size -= Vector3.up * RubberDeckTop;
                }
                if (Vector3.Distance(visual.center, physical.center) > .01f ||
                    Vector3.Distance(visual.size, physical.size) > .02f || fitted.GetComponent<Collider>() != null)
                    throw new InvalidOperationException("Fitted module diverges from its collider: " + collider.name);
            }
            var effects = arena.GetComponentInChildren<HoleInWallEffects>();
            AuditUnderwater(arena.transform, c);
            if (effects == null) throw new InvalidOperationException("Gameplay effects are missing.");
            var effectData = new SerializedObject(effects);
            var waterSplashes = arena.GetComponentsInChildren<HoleInWallSplash>(true);
            if (waterSplashes.Length != c.TrackCount * 2) throw new InvalidOperationException("Water entry needs one pooled splash per player slot.");
            foreach (var splash in waterSplashes)
            {
                var splashData = new SerializedObject(splash);
                foreach (string field in new[] { "droplets", "surface", "surfaceRenderer" })
                    if (splashData.FindProperty(field).objectReferenceValue == null)
                        throw new InvalidOperationException("Unwired water splash: " + field);
                var spray = ((ParticleSystem)splashData.FindProperty("droplets").objectReferenceValue).GetComponent<ParticleSystemRenderer>();
                if (spray.renderMode != ParticleSystemRenderMode.Stretch || spray.sharedMaterial.shader.name != "Igruha/HoleInWall/Splash")
                    throw new InvalidOperationException("Water spray must use rounded, transparent droplets.");
                if (splash.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Water splash must not block players or cameras.");
            }
            foreach (string bank in new[] { "tracks", "splashes", "impacts", "flashes", "tethers", "mirrors", "morphs" })
            {
                var array = effectData.FindProperty(bank);
                int expected = c.TrackCount * (bank == "splashes" || bank == "impacts" || bank == "flashes" ? 2 : 1);
                if (array.arraySize != expected) throw new InvalidOperationException("Wrong effect bank size: " + bank);
                for (int i = 0; i < array.arraySize; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        throw new InvalidOperationException("Unwired effect: " + bank + " " + i);
            }
            var volume=arena.GetComponentInChildren<Volume>();
            if(volume==null||volume.sharedProfile==null||volume.sharedProfile.components.Count!=4||
                volume.sharedProfile.components.Any(component=>component==null||!AssetDatabase.Contains(component)))
                throw new InvalidOperationException("Post processing must contain four persistent volume components.");
            string[] dependencies=AssetDatabase.GetDependencies(scene.path,true);
            string[] retired={"HS_Marquee","HS_Portal","HS_Spot","HS_Truss","HS_StageVault","HS_Halo","HS_WallCassette","HS_CeilingCoffer"};
            if(dependencies.Any(p=>retired.Any(n=>p.EndsWith("/"+n+".fbx")||p.EndsWith("/"+n+".asset"))))
                throw new InvalidOperationException("Retired studio model returned to the aquatic pavilion.");
            string[] purchased=dependencies.Where(p=>p.StartsWith("Assets/Synty/")||p.Contains("/HoleInWall/Polygon")).ToArray();
            if (purchased.Length > 0) throw new InvalidOperationException("Purchased art returned to the studio: " + string.Join(", ", purchased));
            Debug.Log("HIW Studio audit: boards="+boards.Length+", renderers="+renderers.Length+
                ", shadow casters="+renderers.Count(r=>r.shadowCastingMode!=ShadowCastingMode.Off)+
                ", missing="+missing+", invalid materials="+badMaterials+", remaining purchased="+purchased.Length+
                "\n"+string.Join("\n",purchased));
        }

        private static void AuditUnderwater(Transform arena, HoleInWallConfig config)
        {
            var floor = arena.Find("Pool/PoolFloor").GetComponent<Renderer>();
            if (floor.sharedMaterial.shader.name != "Igruha/HoleInWall/Pool Ceramic")
                throw new InvalidOperationException("Pool floor lost its ceramic and caustics material.");
            var objects = arena.GetComponentsInChildren<Transform>(true);
            if (objects.Any(t => t.name == "Pool tile seam"))
                throw new InvalidOperationException("The retired luminous floor grid returned.");
            var supports = objects.Where(t => t.name == "Support").ToArray();
            if (supports.Length != config.TrackCount * 4 || supports.Any(t => t.GetComponent<BoxCollider>().bounds.size.x > .5f))
                throw new InvalidOperationException("Platforms need four narrow supports, with open underwater sightlines.");
            if (objects.Any(t => t.name.StartsWith("Recovery ladder ") || t.name == "Rescue cradle"))
                throw new InvalidOperationException("Retired recovery geometry obstructs the gameplay camera.");
            foreach (var platform in objects.Where(t => t.name == "Platform"))
            {
                var bounds = platform.GetComponent<BoxCollider>().bounds;
                if (Mathf.Abs(bounds.size.x - config.PlatformWidth) > .01f ||
                    Mathf.Abs(bounds.size.z - config.PlatformDepth) > .01f)
                    throw new InvalidOperationException("Recovery must not narrow the gameplay deck.");
            }
            var game = Object.FindFirstObjectByType<HoleInWallMinigame>();
            var bank = new SerializedObject(game).FindProperty("recoveries");
            if (bank.arraySize != config.TrackCount * 2)
                throw new InvalidOperationException("Wrong recovery bank size.");
            for (int i = 0; i < bank.arraySize; i++)
            {
                var recovery = bank.GetArrayElementAtIndex(i).objectReferenceValue as HoleInWallRecovery;
                if (recovery == null || new SerializedObject(recovery).FindProperty("jet").objectReferenceValue == null)
                    throw new InvalidOperationException("Recovery path is not wired: " + i);
                if (recovery.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("A water return must not add physical obstacles.");
                var jet = recovery.GetComponent<HoleInWallReturnJet>();
                var jetData = new SerializedObject(jet);
                foreach (string field in new[] { "surface", "surfaceRenderer", "spray" })
                    if (jetData.FindProperty(field).objectReferenceValue == null)
                        throw new InvalidOperationException("Unwired return effect: " + field);
                if (!EditorApplication.isPlaying && recovery.GetComponentInChildren<MeshRenderer>().enabled)
                    throw new InvalidOperationException("Return water must be invisible before a rescue.");
            }
            var atmosphere = arena.GetComponentInChildren<HoleInWallUnderwater>();
            if (atmosphere == null) throw new InvalidOperationException("Underwater atmosphere is missing.");
            var data = new SerializedObject(atmosphere);
            foreach (string field in new[] { "bubbles", "caustics" })
            {
                var array = data.FindProperty(field);
                int expected = config.TrackCount * (field == "bubbles" ? 2 : 1);
                if (array.arraySize != expected) throw new InvalidOperationException("Wrong underwater bank: " + field);
                for (int i = 0; i < expected; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        throw new InvalidOperationException("Unwired underwater effect: " + field);
            }
        }
    }
}
