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
    /// <summary>Original aquastage: octagonal entrances, recessed pool and tiered audience.</summary>
    public static class HoleInWallStudioBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Minigames/HoleInWall.unity";
        private const string ConfigPath = "Assets/_Project/Settings/Gameplay/Minigames/HoleInWallConfig.asset";
        private const float CeilingY = 15;
        private const float AudienceRowRise = .75f;
        private const int AudienceRows = 3;
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
            MakeMaterial("Ink", new Color(.035f,.070f,.15f), .5f);
            MakeMaterial("Blue", new Color(.055f,.23f,.52f), .4f, .05f);
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
            Transform shell = Group(studio, "Architecture");
            float half = c.ArenaWidth * .5f;
            float far = c.ArenaFarZ + 9;
            float near = c.ArenaNearZ - 9;
            float centerZ = (far + near) * .5f;
            float floorY = HoleInWallProps.RimTopY(c) - .2f;
            Panel(shell, "Far apron", new Vector3(0, floorY, c.ArenaFarZ + 4.5f),
                new Vector3(c.ArenaWidth, .4f, 9), Mat("Ink"), true);
            Panel(shell, "Near apron", new Vector3(0, floorY, c.ArenaNearZ - 4.5f),
                new Vector3(c.ArenaWidth, .4f, 9), Mat("Ink"), true);
            for (int s = -1; s <= 1; s += 2)
            {
                Panel(shell, "Side apron", new Vector3(s * (half + 4.5f), floorY, centerZ),
                    new Vector3(9, .4f, far-near), Mat("Ink"), true);
                Panel(shell, "Acoustic wall", new Vector3(s * (half + 9), 5.5f, centerZ),
                    new Vector3(.6f, 19, far-near), Mat("Blue"), true);
                for (float z = near+3; z < far-1; z += 4)
                    Place(shell,"WallCassette",new Vector3(s*(half+8.55f),8.8f,z),s*90);
                Panel(shell,"Wall dado",new Vector3(s*(half+8.55f),4.65f,centerZ),
                    new Vector3(.35f,.20f,far-near-1),Mat("Gold"));
                for (float z = c.ArenaNearZ+4; z < c.ArenaFarZ; z += 7)
                {
                    var lamp=Place(shell,"Spot",new Vector3(s*(half+2),2.1f,z), s*75);
                    lamp.localScale=Vector3.one*1.3f;
                }
            }
            Panel(shell,"Back wall",new Vector3(0,5.5f,far),new Vector3(c.ArenaWidth+18,19,.6f),Mat("Blue"),true);
            Panel(shell,"Front wall",new Vector3(0,5.5f,near),new Vector3(c.ArenaWidth+18,19,.6f),Mat("Blue"),true);
            Transform roof=Panel(shell,"Roof — open lighting grid",new Vector3(0,CeilingY,centerZ),
                new Vector3(c.ArenaWidth+18,.35f,far-near),Mat("Ink"));
            // This shell alone does not occlude the studio key; every solid prop casts.
            roof.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            for (float z=near+3;z<far-1;z+=5.4f)
                for(float x=-half-4;x<half+5;x+=8)
                {
                    Transform coffer=Place(shell,"CeilingCoffer",new Vector3(x,CeilingY-.35f,z));
                    coffer.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                }
            for(int side=-1;side<=1;side+=2)
            {
                Panel(shell,"Ceiling perimeter fascia",new Vector3(side*(half+8),13.9f,centerZ),
                    new Vector3(1.2f,.8f,far-near),Mat("Blue"));
                Panel(shell,"Ceiling perimeter light",new Vector3(side*(half+7.35f),13.65f,centerZ),
                    new Vector3(.08f,.09f,far-near-1),Mat("Glow"));
            }
            // The room itself is the hero: layered architectural arcs, no title billboard.
            Place(shell,"StageVault",new Vector3(0,4.6f,far-.8f));
            var innerVault=Place(shell,"StageVault",new Vector3(0,4.6f,far-3.2f));
            innerVault.localScale=new Vector3(.96f,.96f,1);
            for(int side=-1;side<=1;side+=2)
            {
                Panel(shell,"Rear pilaster",new Vector3(side*26,7.0f,far-1.0f),new Vector3(1.6f,14,.8f),Mat("Ivory"));
                Panel(shell,"Pilaster light channel",new Vector3(side*26,7.0f,far-1.48f),new Vector3(.12f,12,.08f),Mat("Glow"));
            }
            for(int row=0;row<2;row++)
                for(int side=-1;side<=1;side+=2)
                {
                    Transform halo=Place(shell,"Halo",new Vector3(side*11,12.5f,1+row*12));
                    halo.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                }
            Label(shell,"On air","●  В ЭФИРЕ",new Vector3(0,7.2f,near+.5f),new Vector2(10,1.5f),12,new Color(1,.3f,.24f)).transform.localRotation=Quaternion.Euler(0,180,0);
        }

        private static void PortalsAndBoards(Transform studio,HoleInWallConfig c,HoleInWallTrack[] tracks)
        {
            Transform entrances=Group(studio,"Lane portals");
            var game=Object.FindFirstObjectByType<HoleInWallMinigame>();
            for(int i=0;i<c.TrackCount;i++)
            {
                float x=c.TrackCenterX(i);
                Color lane=HoleInWallPalette.LaneAccent(i);
                Material accent=MakeMaterial("Lane"+i,lane,.35f,.05f,1.1f);
                var portal=Place(entrances,"Portal",new Vector3(x,-.1f,c.WallStartZ+1.8f));
                portal.localScale=new Vector3((c.PlatformWidth+.8f)/9.4f,1,1);
                // Only the emissive channel is lane coded; the architecture stays coherent.
                var r=portal.GetComponent<Renderer>();
                r.sharedMaterials=r.sharedMaterials.Select(m=>m.name=="HS_Glow"?accent:m).ToArray();
                var board=Group(entrances,"Scoreboard_"+i);
                Vector3 p=new Vector3(x,5.35f,c.WallStartZ+1.25f);
                var instrument=Place(board,"Scoreboard",p);
                var display=instrument.GetComponent<Renderer>();
                display.sharedMaterials=display.sharedMaterials.Select(m=>m.name=="HS_Glow"?accent:m).ToArray();
                Label(board,"Lane number",(i+1).ToString("00"),p+new Vector3(-1.88f,.10f,-.73f),new Vector2(1.05f,1.1f),8,lane);
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
                for(int side=-1;side<=1;side+=2)
                    Place(entrances,"Spot",new Vector3(x+side*4.7f,.5f,c.WallStartZ+2),0);
            }
        }

        private static void Audience(Transform studio,HoleInWallConfig c)
        {
            Transform tiers=Group(studio,"Audience tiers");
            Transform crowd=Group(studio,"Original audience");
            Panel(tiers,"Rear stage foundation",new Vector3(0,.9f,c.ArenaFarZ+5.35f),
                new Vector3(41,5.8f,3.2f),Mat("Ink"),true);
            int count=0;
            for(int side=-1;side<=1;side+=2)
                for(int row=0;row<AudienceRows;row++)
                {
                    float x=side*(c.ArenaWidth*.5f+2.2f+row*1.5f);
                    float y=-.6f+row*AudienceRowRise;
                    float z=(c.ArenaFarZ+c.ArenaNearZ)*.5f;
                    Panel(tiers,"Tier",new Vector3(x,y-.42f,z),new Vector3(1.6f,.84f,c.ArenaDepth-3),Mat("Ink"),true);
                    Panel(tiers,"Tier trim",new Vector3(x-side*.77f,y-.05f,z),new Vector3(.09f,.12f,c.ArenaDepth-3),Mat("WarmGlow"));
                    for(float seatZ=c.ArenaNearZ+3;seatZ<c.ArenaFarZ-1;seatZ+=1.65f)
                    {
                        float yaw=side*90;
                        Place(tiers,"Seat",new Vector3(x,y,seatZ),yaw);
                        var fan=Place(crowd,"Fan"+(count%3)+"_Idle",new Vector3(x,y,seatZ)+Quaternion.Euler(0,yaw,0)*new Vector3(0,0,-.45f),yaw,"Fan_"+count);
                        fan.localScale=Vector3.one*(.90f+(count%5)*.045f);
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
                    Place(crowd,"Fan"+(count%3)+"_Idle",new Vector3(x,y,z-.45f),0,"Fan_"+count++);
                }
            var component=crowd.gameObject.AddComponent<HoleInWallCrowd>();
            var so=new SerializedObject(component);
            so.FindProperty("game").objectReferenceValue=Object.FindFirstObjectByType<HoleInWallMinigame>();
            var wardrobe=so.FindProperty("wardrobe");wardrobe.arraySize=3;
            for(int i=0;i<3;i++)
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
            for(int side=-1;side<=1;side+=2)
            {
                Panel(finish,"Ceramic coping",new Vector3(side*half,-2.08f,z),new Vector3(.75f,.17f,c.ArenaDepth),Mat("Ivory"));
                Panel(finish,"Pool light channel",new Vector3(side*(half-.24f),-2.37f,z),new Vector3(.08f,.10f,c.ArenaDepth-.7f),Mat("Glow"));
                for(float zz=c.ArenaNearZ+1;zz<c.ArenaFarZ;zz+=1.6f)
                    Panel(finish,"Coping joint",new Vector3(side*half,-1.989f,zz),new Vector3(.73f,.012f,.025f),Mat("Blue"));
            }
            foreach(float zz in new[]{c.ArenaNearZ,c.ArenaFarZ})
            {
                Panel(finish,"End coping",new Vector3(0,-2.08f,zz),new Vector3(c.ArenaWidth,.17f,.75f),Mat("Ivory"));
                Panel(finish,"End light channel",new Vector3(0,-2.37f,zz+(zz==c.ArenaNearZ?.24f:-.24f)),
                    new Vector3(c.ArenaWidth-.7f,.10f,.08f),Mat("Glow"));
            }
            // Underwater seams remain subtle and establish depth through refraction.
            for(float x=-half+1;x<half;x+=1.8f)
                Panel(finish,"Pool tile seam",new Vector3(x,c.PoolBottomY+.012f,z),new Vector3(.018f,.015f,c.ArenaDepth-.6f),Mat("Teal"));
            for(float zz=c.ArenaNearZ+1;zz<c.ArenaFarZ;zz+=1.8f)
                Panel(finish,"Pool tile seam",new Vector3(0,c.PoolBottomY+.014f,zz),new Vector3(c.ArenaWidth-.6f,.015f,.018f),Mat("Teal"));
            var water=arena.Find("Pool/Water").GetComponent<Renderer>().sharedMaterial;
            water.SetColor("_ShallowColor",new Color(.025f,.58f,.61f));
            water.SetColor("_DeepColor",new Color(.012f,.32f,.39f));
            water.SetFloat("_RippleStrength",.065f);
            water.SetFloat("_RippleScale",1.15f);
            water.SetFloat("_WaveHeight",.055f);
            water.SetFloat("_SpecStrength",1.5f);
            water.SetFloat("_SpecPower",150);
            water.SetFloat("_FresnelStrength",.18f);
            water.SetFloat("_RefractionStrength",.012f);
            EditorUtility.SetDirty(water);
        }

        private static void StageFinish(Transform arena,HoleInWallConfig c)
        {
            for(int i=0;i<c.TrackCount;i++)
            {
                Transform track=arena.Find("Track_"+i);
                Transform finish=Group(track,"Studio platform details");
                Transform wallFinish=Group(track.Find("Wall"),"Studio wall frame");
                float front=-c.WallThickness*.5f-.025f;
                Panel(wallFinish,"Brushed top cap",new Vector3(0,c.WallHeight+.025f,0),
                    new Vector3(c.WallWidth+.10f,.10f,c.WallThickness+.06f),Mat("Steel"));
                Panel(wallFinish,"Top accent inlay",new Vector3(0,c.WallHeight-.075f,front),
                    new Vector3(c.WallWidth-.12f,.035f,.018f),Mat("Lane"+i));
                for(int side=-1;side<=1;side+=2)
                {
                    Panel(wallFinish,"Edge guard",new Vector3(side*(c.WallWidth*.5f+.025f),c.WallHeight*.5f,0),
                        new Vector3(.10f,c.WallHeight,c.WallThickness+.04f),Mat("Steel"));
                    Panel(wallFinish,"Corner reinforcement",new Vector3(side*(c.WallWidth*.5f-.16f),c.WallHeight-.17f,front),
                        new Vector3(.28f,.25f,.035f),Mat("Ink"));
                    Panel(wallFinish,"Corner fixing",new Vector3(side*(c.WallWidth*.5f-.16f),c.WallHeight-.17f,front-.035f),
                        new Vector3(.07f,.07f,.025f),Mat("Gold"));
                }
                for(int side=0;side<2;side++)
                {
                    var original=track.Find("FloorHalf_"+side).GetComponent<Renderer>();
                    original.sharedMaterial=Mat(side==0?"Coral":"Teal");
                    float x=(side==0?-1:1)*c.PlatformWidth*.25f;
                    for(int k=0;k<3;k++)
                    {
                        Transform stripe=Panel(finish,"Inset grip stripe",new Vector3(x,.023f,-c.PlatformDepth*.5f+.24f+k*.12f),
                            new Vector3(c.PlatformWidth*.43f,.009f,.035f),Mat("Ink"));
                        stripe.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    }
                    Label(finish,"Deck lane",(i+1).ToString("00"),new Vector3(x,.031f,-.95f),new Vector2(1.6f,.6f),3.5f,Color.white)
                        .transform.localRotation=Quaternion.Euler(90,0,0);
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
            key.type=LightType.Directional;key.color=new Color(1,.94f,.84f);key.intensity=1.6f;
            key.transform.rotation=Quaternion.Euler(52,-26,0);key.shadows=LightShadows.Soft;
            key.shadowStrength=.88f;key.shadowBias=.035f;key.shadowNormalBias=.14f;
            RenderSettings.sun=key;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.27f,.38f,.57f);
            RenderSettings.ambientEquatorColor=new Color(.16f,.21f,.31f);
            RenderSettings.ambientGroundColor=new Color(.07f,.105f,.16f);
            RenderSettings.fog=false;
            Transform lights=Group(studio,"Studio light fixtures");
            for(int i=0;i<c.TrackCount;i++)
            {
                var p=new Vector3(c.TrackCenterX(i),11,8);
                var lamp=Place(lights,"Spot",p,0);lamp.localRotation=Quaternion.Euler(55,0,0);
                var light=Group(lights,"Lane softbox "+i).gameObject.AddComponent<Light>();
                light.transform.position=p;
                light.transform.rotation=Quaternion.LookRotation(new Vector3(c.TrackCenterX(i),0,0)-p);
                light.type=LightType.Spot;light.color=new Color(.36f,.75f,1);light.intensity=360;
                light.range=25;light.spotAngle=70;light.innerSpotAngle=40;
                light.shadows=LightShadows.None;
            }
            const string profilePath="Assets/_Project/Settings/Volumes/HoleInWall_OriginalStudio.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
            if(!profile.TryGet(out Tonemapping tone))tone=profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet(out Bloom bloom))bloom=profile.Add<Bloom>();
            bloom.intensity.Override(.24f);bloom.threshold.Override(1.15f);bloom.scatter.Override(.6f);
            if(!profile.TryGet(out ColorAdjustments color))color=profile.Add<ColorAdjustments>();
            color.postExposure.Override(.15f);color.contrast.Override(12);color.saturation.Override(13);
            if(!profile.TryGet(out Vignette vignette))vignette=profile.Add<Vignette>();
            vignette.intensity.Override(.17f);vignette.smoothness.Override(.55f);
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
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.025f,.055f);
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
                if (Vector3.Distance(visual.center, physical.center) > .01f ||
                    Vector3.Distance(visual.size, physical.size) > .02f || fitted.GetComponent<Collider>() != null)
                    throw new InvalidOperationException("Fitted module diverges from its collider: " + collider.name);
            }
            var effects = arena.GetComponentInChildren<HoleInWallEffects>();
            if (effects == null) throw new InvalidOperationException("Gameplay effects are missing.");
            var effectData = new SerializedObject(effects);
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
            if(dependencies.Any(p=>p.Contains("HS_Marquee")))throw new InvalidOperationException("Removed title returned to the studio.");
            string[] purchased=dependencies.Where(p=>p.StartsWith("Assets/Synty/")||p.Contains("/HoleInWall/Polygon")).ToArray();
            if (purchased.Length > 0) throw new InvalidOperationException("Purchased art returned to the studio: " + string.Join(", ", purchased));
            Debug.Log("HIW Studio audit: boards="+boards.Length+", renderers="+renderers.Length+
                ", shadow casters="+renderers.Count(r=>r.shadowCastingMode!=ShadowCastingMode.Off)+
                ", missing="+missing+", invalid materials="+badMaterials+", remaining purchased="+purchased.Length+
                "\n"+string.Join("\n",purchased));
        }
    }
}
