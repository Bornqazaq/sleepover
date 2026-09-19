using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Igruha.Core.Ambient;
using Object=UnityEngine.Object;

namespace Igruha.EditorTools
{
    internal static partial class DuckHuntBarnArt
    {
        const string FairgroundRoot="Assets/_Project/Art/DuckHuntFairground";
        static readonly string[] FairgroundModels={"PrizeCart","WheelStand","PrizeDisplay","TargetPanel","ServiceRack","Swag","Bench","QueuePost","Barrel","Tent","CarouselFrame","CarouselHorse","Car","Grass","PaintedBoard"};
        static void FairgroundPreflight()
        {
            foreach(string key in FairgroundModels)
            {
                string p=FairgroundRoot+"/Models/DHF_"+key+".fbx";
                EnsureReadableModel(p);
                if(AssetDatabase.LoadAssetAtPath<GameObject>(p)==null)throw new InvalidOperationException("Export duck_hunt_fairground.py: "+key);
            }
        }

        static void FairgroundFloorDressing(Transform floor,Transform art,int index)
        {
            float y=index*5.76f;
            // Shallow rear relief remains behind the circulation strip. None is a cover.
            float[] bays=index%2==0?new[]{10.2f,15.3f,20.6f,25.8f}:new[]{8.6f,13.7f,19f,24.2f};
            for(int i=0;i<bays.Length;i++)
            {
                if(index==4 && (i==1 || i==2))continue; // Clear the GRAND PRIZE lettering.
                string module=(i+index)%3==0?"PrizeDisplay":((i+index)%3==1?"TargetPanel":"ServiceRack");
                Model(module,art,new Vector3(bays[i],y+.12f,9.73f),new Vector3(.99f,1.08f,.65f));
                Model("Swag",art,new Vector3(bays[i],y+4.98f,9.20f),new Vector3(.72f,.70f,1));
                Model("Lantern",art,new Vector3(bays[i]+2.20f,y+3.75f,9.03f),Vector3.one*.68f);
                if(i==0 || i==2)PointLight(art,new Vector3(bays[i],y+3.85f,7.7f),new Color(1,.70f,.37f),3.2f,8.5f);
            }
            for(int x=0;x<=48;x+=2)
                Model("Bulb",art,new Vector3(x*.72f,y-.18f,-.32f),Vector3.one*.70f);
            for(int j=0;j<5;j++)
                if(index!=4 || j!=2)
                Model("Swag",art,new Vector3(3.46f+j*6.91f,y+5.22f,.12f),new Vector3(1.12f,.68f,1));
            if(index==0)DressStartRoom(floor,art);
            if(index==4)DressFinalStage(floor,art);
        }

        static void DressStartRoom(Transform floor,Transform art)
        {
            // Interior horizontal detail doesn't change the eight spawn capsules.
            for(int i=0;i<8;i++)
            {
                float z=Mathf.Lerp(1.08f,9f,i/7f);
                Model("Lantern",art,new Vector3(.23f,2.6f,z),Vector3.one*.50f,Quaternion.Euler(0,90,0));
                var footprint=Solid(art,"Start arrow",new Vector3(1.55f,.012f,z),new Vector3(.50f,.013f,.09f),"Brass");
                foreach(int s in new[]{-1,1})Beam(art,new Vector3(1.63f,.023f,z+s*.14f),new Vector3(1.83f,.023f,z),.025f,"Brass");
            }
            PointLight(art,new Vector3(3,3.6f,4.7f),new Color(1,.74f,.43f),4,9);
            for(int i=0;i<4;i++)
            {
                Model("Wheel",art,new Vector3(.65f+i*1.35f,1.5f,-.20f),Vector3.one*.35f);
                Model("Duck",art,new Vector3(.65f+i*1.35f,1.35f,-.42f),Vector3.one*.30f);
            }
        }

        static void DressFinalStage(Transform floor,Transform art)
        {
            Label(art,"05",new Vector3(2.9f,26.5f,-.16f),1.7f,"Cream");
            foreach(Transform pad in floor.Cast<Transform>().Where(t=>t.name.StartsWith("ParkourPad_")))
            {
                int number=int.Parse(pad.name.Substring("ParkourPad_".Length));
                Bounds b=pad.GetComponent<Renderer>().bounds;Vector3 centre=b.center;
                string color=number%3==0?"Blue":number%3==1?"Red":"Teal";
                // Square landing top and every jump gap stay exactly as authored.
                for(int side=-1;side<=1;side+=2)
                {
                    Solid(art,"Painted podium fascia",new Vector3(centre.x,b.max.y-.30f,centre.z+side*(b.extents.z-.03f)),new Vector3(b.size.x,.48f,.09f),color);
                    Solid(art,"Podium side",new Vector3(centre.x+side*(b.extents.x-.03f),b.max.y-.30f,centre.z),new Vector3(.09f,.48f,b.size.z),"OakLight");
                }
                for(int j=0;j<6;j++)Model("Bulb",art,new Vector3(b.min.x+.15f+j*(b.size.x-.30f)/5,b.max.y-.19f,b.min.z-.015f),Vector3.one*.48f);
                Model(number%2==0?"Wheel":"Duck",art,new Vector3(centre.x,b.max.y-.55f,b.min.z-.06f),Vector3.one*.28f);
                // Timber trestles sit below the platform; don't provide footholds.
                foreach(int side in new[]{-1,1})
                {
                    Vector3 a=new Vector3(centre.x+side*.78f,23.12f,centre.z);
                    Beam(art,a,new Vector3(centre.x-side*.78f,b.min.y-.12f,centre.z),.11f,"OakDark");
                }
            }
            // Prize alcove behind the finish, with an unobstructed landing apron.
            Vector3 p=new Vector3(32.55f,25.20f,9.55f);
            Model("Duck",art,p,Vector3.one*1.35f);
            Model("Bear",art,p+new Vector3(-1,0,0),Vector3.one*.75f);
            Model("Rabbit",art,p+new Vector3(1,0,0),Vector3.one*.68f);
            Model("Swag",art,p+new Vector3(0,2.15f,.1f),new Vector3(.53f,1.5f,1));
        }

        static void Rope(Transform art,Vector3 a,Vector3 b,float sag,string color="Red")
        {
            Vector3 last=a;
            for(int i=1;i<=10;i++)
            {
                float t=i/10f;Vector3 next=Vector3.Lerp(a,b,t)-Vector3.up*(Mathf.Sin(t*Mathf.PI)*sag);
                Beam(art,last,next,.032f,color);last=next;
            }
        }

        static void Queue(Transform art,Vector3 start,int count,Vector3 direction)
        {
            for(int i=0;i<count;i++)
            {
                Vector3 p=start+direction*i;Model("QueuePost",art,p,Vector3.one);
                if(i>0)Rope(art,p+Vector3.up, p-direction+Vector3.up,.23f);
            }
        }

        // =====================================================================
        // Окружение ярмарки: рельеф, площадь, зоны, окраина города, движение.
        //
        // Прежняя версия расставляла палатки и аттракционы кольцом вокруг башни,
        // холмы делала десятью сферами, наполовину утопленными в плоскость,
        // а всё без исключения сваливала в один статический меш. Отсюда три
        // претензии разом: «сферы в Plane», «объекты стоят случайно» и
        // «ярмарка в глухом лесу». Здесь у площади есть аллеи и зоны, у земли —
        // рельеф, за оградой — дорога и окраина города, а аттракционы живут
        // в отдельном корне и поэтому умеют двигаться.
        // =====================================================================

        const float Ground=-3f;                  // верх земли вокруг башни
        static readonly Vector2 FairCentre=new Vector2(17.28f,-14f);
        const float PromenadeZ=-24.5f;           // ось главной аллеи
        const float PromenadeHalf=5.75f;
        const float GateZ=-46f;                  // вход на ярмарку
        const float RoadZ=-62f;                  // дорога к городу
        const float FerrisPeriod=96f;            // оборот колеса обозрения, с
        const float CarouselPeriod=27f;          // оборот карусели, с

        static Vector4[] hillField;              // купола рельефа: xy центр, z радиус, w высота

        /// <summary>
        /// Попадает ли точка в сектор огня Охотника.
        ///
        /// Луч всегда соединяет Охотника на площадке лифта и Утку внутри башни,
        /// и оба конца лежат выше нуля по Y. Значит всё, что целиком ниже нуля,
        /// перекрыть выстрел не может в принципе, а земля здесь на -3: внутри
        /// сектора помещается ровно 2.8 м высоты — тележки, лавки, бочки,
        /// низкая ограда, клумбы. Палатка, столб и дерево — уже нет.
        /// Площадка лифта стоит на z = -11.52, поэтому всё за z = -17 безопасно
        /// с запасом: туда луч не заходит вовсе.
        /// </summary>
        static bool InFiringSector(float x,float z)=>z>-17f && z<2f && x>-7f && x<41.5f;

        static bool OnFairApron(float x,float z)=>x>-61f && x<95f && z>-62f && z<30f;

        static bool InTownCorridor(float x,float z)=>z<-58f && x>-104f && x<148f;

        // ---------------------------------------------------------------------
        // Рельеф
        // ---------------------------------------------------------------------

        /// <summary>
        /// Доля рельефа в точке: 0 на ровной земле ярмарки и города, 1 за их краем.
        /// Переход длиной 64 м — за него холм успевает выйти из земли без ступеньки.
        /// </summary>
        static float FlatMask(float x,float z)
        {
            float dx=Mathf.Max(Mathf.Max(-84f-x,x-126f),0f);
            float dz=Mathf.Max(Mathf.Max(-212f-z,z-34f),0f);
            return Mathf.SmoothStep(0f,1f,Mathf.Clamp01(Mathf.Sqrt(dx*dx+dz*dz)/64f));
        }

        static float HillHeight(float x,float z)
        {
            float mask=FlatMask(x,z);
            if(mask<=0f)return 0f;
            float h=0f;
            foreach(Vector4 hill in hillField)
            {
                float d=Vector2.Distance(new Vector2(x,z),new Vector2(hill.x,hill.y));
                if(d>=hill.z)continue;
                // Сглаженный купол: у подножия наклон нулевой, поэтому холм
                // вырастает из земли, а не врезается в неё кромкой.
                float t=1f-d/hill.z;h+=hill.w*t*t*(3f-2f*t);
            }
            h+=(Mathf.PerlinNoise(x*.013f+31f,z*.013f+17f)-.5f)*3.4f;
            return h*mask;
        }

        static float TerrainY(float x,float z)=>Ground+HillHeight(x,z);

        static void FairgroundTerrain(Transform art)
        {
            var rng=new System.Random(90419);
            hillField=new Vector4[56];
            for(int i=0;i<hillField.Length;i++)
            {
                float a=(float)rng.NextDouble()*Mathf.PI*2,r=96+(float)rng.NextDouble()*186;
                hillField[i]=new Vector4(FairCentre.x+Mathf.Cos(a)*r,FairCentre.y+Mathf.Sin(a)*r,
                    34+(float)rng.NextDouble()*54,4.5f+(float)rng.NextDouble()*16.5f);
            }
            const float step=9f,half=288f;
            int n=Mathf.RoundToInt(half*2/step);
            var grass=new List<Vector3>(n*n*4);var sunlit=new List<Vector3>(n*n*2);
            for(int i=0;i<n;i++)for(int j=0;j<n;j++)
            {
                float x0=FairCentre.x-half+i*step,x1=x0+step;
                float z0=FairCentre.y-half+j*step,z1=z0+step;
                Vector3 a=new Vector3(x0,TerrainY(x0,z0),z0),b=new Vector3(x1,TerrainY(x1,z0),z0);
                Vector3 c=new Vector3(x1,TerrainY(x1,z1),z1),d=new Vector3(x0,TerrainY(x0,z1),z1);
                // Светлые пятна травы идут собственным шумом, а не по высоте:
                // иначе склон расслаивается ровными горизонталями.
                var into=Mathf.PerlinNoise(x0*.0075f+5f,z0*.0075f+2f)+(a.y-Ground)*.011f>.54f?sunlit:grass;
                into.Add(a);into.Add(c);into.Add(b);into.Add(a);into.Add(d);into.Add(c);
            }
            TerrainPiece(art,"Rolling ground",grass,"Leaf");
            TerrainPiece(art,"Rolling ground sunlit",sunlit,"LeafLight");
        }

        static void TerrainPiece(Transform art,string name,List<Vector3> corners,string material)
        {
            if(corners.Count==0)return;
            // Вершины намеренно не переиспользуются между треугольниками:
            // так RecalculateNormals даёт гранёное затенение, а не гладкое,
            // и рельеф остаётся в одном стиле с остальной сценой.
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            var uv=new Vector2[corners.Count];var tri=new int[corners.Count];
            for(int i=0;i<corners.Count;i++){uv[i]=new Vector2(corners[i].x*.08f,corners[i].z*.08f);tri[i]=i;}
            mesh.SetVertices(corners);mesh.uv=uv;mesh.triangles=tri;
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=Group(art,name).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=Mat(material);
        }

        // ---------------------------------------------------------------------
        // Общая точка входа
        // ---------------------------------------------------------------------

        static void FairgroundSurroundings(Transform art,Transform rides)
        {
            FairgroundTerrain(art);
            FairApron(art);
            Promenade(art);
            Forecourt(art);
            EntranceGate(art,rides);
            RideZone(art,rides);
            GameZone(art,rides);
            ServiceYard(art);
            Dressing(art,rides);
            TownEdge(art);
            Treeline(art);
            AuditFairgroundSpacing(art);
        }

        /// <summary>
        /// Пересборка сама сообщает, какие уличные предметы залезли друг в друга.
        ///
        /// Проверять это глазами по скриншотам бессмысленно: после Combine
        /// отдельных объектов уже нет, а на снимке пересечение видно только
        /// под удачным углом. Ровно так в сцену и попала касса, воткнутая
        /// в карусель. Порог — 0.35 м перекрытия по каждой оси и 0.6 м³ объёма:
        /// стоящие вплотную лавка и бочка порогов не набирают, вложенные
        /// друг в друга модели набирают всегда.
        /// </summary>
        static void AuditFairgroundSpacing(Transform art)
        {
            var names=new List<string>();var boxes=new List<Bounds>();
            foreach(Transform child in art)
            {
                if(!FairgroundModels.Contains(child.name)&&!CarnivalModels.Contains(child.name)&&!Models.Contains(child.name))continue;
                // Трава, лампочки и драпировка — покрытие и подсветка: они и
                // должны лежать под навесами и на стойках, их пересечения
                // ни о чём не говорят.
                if(child.name=="Grass"||child.name=="Bulb"||child.name=="Swag")continue;
                var renderers=child.GetComponentsInChildren<MeshRenderer>();
                if(renderers.Length==0)continue;
                Bounds b=renderers[0].bounds;
                for(int i=1;i<renderers.Length;i++)b.Encapsulate(renderers[i].bounds);
                if(b.center.y>6f)continue;                                        // только наземный ярус
                if(b.center.x>-3f&&b.center.x<37.5f&&b.center.z>-2f)continue;      // убранство внутри башни
                names.Add(child.name);boxes.Add(b);
            }
            int overlaps=0;
            for(int i=0;i<boxes.Count;i++)
                for(int j=i+1;j<boxes.Count;j++)
                {
                    Bounds a=boxes[i],c=boxes[j];
                    float ox=Mathf.Min(a.max.x,c.max.x)-Mathf.Max(a.min.x,c.min.x);
                    float oy=Mathf.Min(a.max.y,c.max.y)-Mathf.Max(a.min.y,c.min.y);
                    float oz=Mathf.Min(a.max.z,c.max.z)-Mathf.Max(a.min.z,c.min.z);
                    if(ox<.35f||oy<.35f||oz<.35f||ox*oy*oz<.6f)continue;
                    overlaps++;
                    Debug.LogWarning(string.Format("Duck Hunt fairground overlap: {0} x {1}, {2:F1} m3 near ({3:F0}, {4:F0})",
                        names[i],names[j],ox*oy*oz,a.center.x,a.center.z));
                }
            Debug.Log(string.Format("Duck Hunt fairground spacing: {0} props checked, {1} overlaps",boxes.Count,overlaps));
        }

        // ---------------------------------------------------------------------
        // Площадка и аллеи
        // ---------------------------------------------------------------------

        static void FairApron(Transform art)
        {
            // Утоптанная земля с собственной границей: у ярмарки появляется
            // территория, и башня перестаёт выглядеть поставленной в поле.
            Solid(art,"Fair ground",new Vector3(17,Ground-.49f,-16),new Vector3(156,1,92),"Sand");
            Solid(art,"Fair ground edge",new Vector3(17,Ground-.62f,-16),new Vector3(160,1,96),"Gravel");
            PerimeterFence(art);
        }

        static void PerimeterFence(Transform art)
        {
            // Ограда идёт по краю площадки и расступается на входе.
            foreach(float z in new[]{-60f,28f})
                for(float x=-58f;x<=92f;x+=4f)
                {
                    if(z<0 && Mathf.Abs(x-FairCentre.x)<7f)continue; // проём ворот
                    FenceSpan(art,new Vector3(x,Ground,z),new Vector3(x+4,Ground,z));
                }
            foreach(float x in new[]{-58f,92f})
                for(float z=-60f;z<=28f;z+=4f)
                    FenceSpan(art,new Vector3(x,Ground,z),new Vector3(x,Ground,z+4));
        }

        static void FenceSpan(Transform art,Vector3 a,Vector3 b)
        {
            Beam(art,a,a+Vector3.up*1.55f,.10f,"OakDark");
            foreach(float h in new[]{.62f,1.34f})
                Beam(art,a+Vector3.up*h,b+Vector3.up*h,.07f,"OakLight");
        }

        static void Promenade(Transform art)
        {
            // Главная аллея вдоль фасада и поперечная от ворот к башне.
            // Обе лежат за z = -17, то есть вне сектора огня.
            Solid(art,"Promenade",new Vector3(17,Ground+.03f,PromenadeZ),new Vector3(130,.14f,PromenadeHalf*2),"Oak");
            foreach(int side in new[]{-1,1})
                Solid(art,"Promenade kerb",new Vector3(17,Ground+.08f,PromenadeZ+side*(PromenadeHalf+.25f)),
                    new Vector3(130,.24f,.5f),"OakDark");
            Solid(art,"Gate avenue",new Vector3(FairCentre.x,Ground+.03f,-33.5f),new Vector3(11,.14f,28),"Oak");

            // Фонарные столбы с гирляндами между ними задают ритм аллеи.
            for(float x=-44f;x<=78f;x+=11f)
                foreach(int side in new[]{-1,1})
                {
                    if(Mathf.Abs(x-FairCentre.x)<6.5f && side>0)continue;
                    Vector3 foot=new Vector3(x,Ground,PromenadeZ+side*(PromenadeHalf+.9f));
                    LampPost(art,foot,4.4f);
                    if(x<71f)Garland(art,foot+Vector3.up*4.1f,foot+new Vector3(11,4.1f,0),.9f);
                }
        }

        static void LampPost(Transform art,Vector3 foot,float height)
        {
            Beam(art,foot,foot+Vector3.up*height,.13f,"Iron");
            Model("Lantern",art,foot+Vector3.up*(height-.25f),Vector3.one*.95f);
            Solid(art,"Lamp glow",foot+Vector3.up*(height-.3f),Vector3.one*.22f,"Glow");
        }

        static void Forecourt(Transform art)
        {
            // Площадь перед башней. Здесь проходит каждый выстрел, поэтому
            // всё ниже 2.5 м: мощение, разметка, канат на низких столбиках,
            // клумбы и бочки. Ни одного предмета, поднимающегося выше нуля.
            Solid(art,"Forecourt",new Vector3(17.28f,Ground+.03f,-8f),new Vector3(48,.14f,18),"Gravel");
            for(float x=-2f;x<=38f;x+=5f)
                Solid(art,"Forecourt joint",new Vector3(x,Ground+.10f,-8f),new Vector3(.16f,.05f,18),"OakDark");
            foreach(float z in new[]{-16.4f,.4f})
                for(float x=-4f;x<=38.8f;x+=3.6f)
                {
                    Beam(art,new Vector3(x,Ground,z),new Vector3(x,Ground+.92f,z),.09f,"Brass");
                    if(x<35.2f)Rope(art,new Vector3(x,Ground+.86f,z),new Vector3(x+3.6f,Ground+.86f,z),.16f,"Red");
                }
            var rng=new System.Random(4211);
            for(int i=0;i<16;i++)
            {
                float x=-3f+(float)rng.NextDouble()*42f,z=-15.4f+(float)rng.NextDouble()*14f;
                if(Mathf.Abs(x-FairCentre.x)<4.5f)continue;       // проход к лифту
                if(rng.Next(2)==0)
                {
                    Solid(art,"Planter",new Vector3(x,Ground+.28f,z),new Vector3(1.5f,.56f,1.5f),"OakDark");
                    Model("Grass",art,new Vector3(x,Ground+.56f,z),Vector3.one*1.1f);
                }
                else Model("Barrel",art,new Vector3(x,Ground,z),Vector3.one*.9f,Quaternion.Euler(0,rng.Next(360),0));
            }
        }

        // ---------------------------------------------------------------------
        // Вход
        // ---------------------------------------------------------------------

        static void EntranceGate(Transform art,Transform rides)
        {
            float x=FairCentre.x;
            foreach(int side in new[]{-1,1})
            {
                Vector3 foot=new Vector3(x+side*5.6f,Ground,GateZ);
                Solid(art,"Gate pier",foot+Vector3.up*3.1f,new Vector3(1.5f,6.2f,1.5f),"Red");
                Solid(art,"Gate pier cap",foot+Vector3.up*6.35f,new Vector3(1.9f,.3f,1.9f),"Gold");
                for(int i=0;i<7;i++)
                    Model("Bulb",art,foot+new Vector3(0,.8f+i*.8f,-.82f),Vector3.one);
                Model("TicketStand",art,foot+new Vector3(side*3.4f,0,1.6f),Vector3.one*1.5f,Quaternion.Euler(0,side*22,0));
                Model("Bench",art,foot+new Vector3(side*6.6f,0,2.8f),Vector3.one*1.2f,Quaternion.Euler(0,side*12,0));
            }
            Solid(art,"Gate span",new Vector3(x,Ground+6.9f,GateZ),new Vector3(13.6f,1.4f,1.1f),"Red");
            Marquee(art,"DUCK HUNT FAIR",new Vector3(x,Ground+8.4f,GateZ-.7f),new Vector2(13,2.1f),.92f);
            // Флаги на воротах покачиваются, лампы по краю вывески мигают.
            foreach(int side in new[]{-1,1})
            {
                Vector3 mast=new Vector3(x+side*6.4f,Ground+6.6f,GateZ);
                Beam(art,mast,mast+Vector3.up*2.6f,.09f,"Brass");
                Transform flag=Group(rides,"Gate flag");flag.position=mast+Vector3.up*2.45f;
                Solid(flag,"Flag cloth",flag.position+new Vector3(side*.62f,-.2f,0),new Vector3(1.25f,.72f,.04f),side<0?"Red":"Blue");
                Motion(flag,AmbientMotion.Mode.Sway,Vector3.up,16f,3.4f,side<0?0f:.35f);
            }
            for(int i=0;i<6;i++)
            {
                Transform bulb=Group(rides,"Gate bulb");
                bulb.position=new Vector3(x-5.2f+i*2.08f,Ground+7.75f,GateZ-1.25f);
                Solid(bulb,"Bulb glass",bulb.position,Vector3.one*.26f,"Glow");
                Motion(bulb,AmbientMotion.Mode.Blink,Vector3.up,1.6f,2.2f,i/6f);
            }
            Queue(art,new Vector3(x-9.5f,Ground,GateZ+4.5f),6,new Vector3(2.2f,0,0));
            Queue(art,new Vector3(x+3.2f,Ground,GateZ+4.5f),6,new Vector3(2.2f,0,0));
        }

        // ---------------------------------------------------------------------
        // Западная зона — аттракционы
        // ---------------------------------------------------------------------

        static void RideZone(Transform art,Transform rides)
        {
            FerrisWheel(art,rides,new Vector3(-44,Ground+16.5f,-8f),15.5f);
            Carousel(art,rides,new Vector3(-27,Ground,PromenadeZ),1.55f);
            DropTower(art,rides,new Vector3(-57,Ground,-36f),22f,"Blue");
            DropTower(art,rides,new Vector3(-14,Ground,-45f),18f,"Red");
            // Палатки стоят двумя рядами вдоль аллеи с проверенным зазором:
            // шатёр 6.75 м в поперечнике, значит шаг ряда — от 9 м, а не «на глаз».
            for(int i=0;i<4;i++)
                Model("Tent",art,new Vector3(-52+i*9f,Ground,-36.5f),Vector3.one*1.15f,Quaternion.Euler(0,i%2==0?8:-9,0));
            for(int i=0;i<3;i++)
                Model("Tent",art,new Vector3(-46+i*10f,Ground,-11f),Vector3.one*1.25f,Quaternion.Euler(0,180+(i%2==0?-7:6),0));
            Model("Booth",art,new Vector3(-14,Ground,-34f),Vector3.one*1.8f,Quaternion.Euler(0,14,0));
            Model("PopcornCart",art,new Vector3(-10.5f,Ground,-31f),Vector3.one*1.4f,Quaternion.Euler(0,-24,0));
            Model("Balloons",art,new Vector3(-8.2f,Ground,-29.6f),Vector3.one*1.25f);
            foreach(float x in new[]{-35f,-21f,-8f})
            {
                Model("Bench",art,new Vector3(x,Ground,PromenadeZ+6.6f),Vector3.one*1.2f,Quaternion.Euler(0,180,0));
                Model("Barrel",art,new Vector3(x+2.4f,Ground,PromenadeZ+6.6f),Vector3.one*.85f);
            }
        }

        static void FerrisWheel(Transform art,Transform rides,Vector3 centre,float radius)
        {
            // Опоры, площадка и касса — статика; крутится только колесо,
            // а люльки висят ровно за счёт встречного вращения с тем же периодом.
            foreach(int side in new[]{-1,1})
            {
                float footX=centre.x+side*radius*.66f;
                foreach(float dz in new[]{-3.1f,3.1f})
                    Beam(art,new Vector3(footX,Ground,centre.z+dz),centre+Vector3.forward*dz*.42f,.44f,"OakDark");
                Beam(art,new Vector3(footX,Ground+.2f,centre.z-3.1f),new Vector3(footX,Ground+.2f,centre.z+3.1f),.32f,"OakDark");
                for(int i=1;i<5;i++)
                {
                    float t=i/5f;
                    Vector3 a=Vector3.Lerp(new Vector3(footX,Ground,centre.z-3.1f),centre-Vector3.forward*1.3f,t);
                    Vector3 b=Vector3.Lerp(new Vector3(footX,Ground,centre.z+3.1f),centre+Vector3.forward*1.3f,t);
                    Beam(art,a,b,.16f,"Brass");
                }
            }
            Solid(art,"Wheel hub housing",centre,new Vector3(1.7f,1.7f,7f),"Iron");
            Solid(art,"Wheel platform",new Vector3(centre.x,Ground+.18f,centre.z-radius-1.4f),new Vector3(9,.36f,5),"Oak");
            Model("TicketStand",art,new Vector3(centre.x+3.4f,Ground+.36f,centre.z-radius-1.6f),Vector3.one*1.4f,Quaternion.Euler(0,180,0));

            Transform wheel=Ride(rides,"FerrisWheel",centre);
            Transform rim=Group(wheel,"Rim");
            const int arcs=32;
            for(int i=0;i<arcs;i++)
            {
                float a=i*Mathf.PI*2/arcs,b=(i+1)*Mathf.PI*2/arcs;
                Vector3 v=centre+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0);
                Vector3 w=centre+new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,0);
                foreach(float dz in new[]{-1.55f,1.55f})
                {
                    Beam(rim,v+Vector3.forward*dz,w+Vector3.forward*dz,.20f,"Cream");
                    if(i%2==0)Beam(rim,centre+Vector3.forward*dz*.5f,v+Vector3.forward*dz,.11f,"Brass");
                }
                if(i%2==0)Model("Bulb",rim,v,Vector3.one*1.1f);
            }
            Combine(rim,"FerrisRim");

            const int cars=10;
            for(int i=0;i<cars;i++)
            {
                float a=i*Mathf.PI*2/cars;
                Vector3 hang=centre+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0);
                Transform car=Group(wheel,"Gondola");car.position=hang-Vector3.up*1.45f;
                string colour=i%3==0?"Red":i%3==1?"Blue":"Gold";
                Solid(car,"Gondola body",car.position,new Vector3(1.7f,1.05f,2.2f),colour);
                Solid(car,"Gondola floor",car.position-Vector3.up*.56f,new Vector3(1.8f,.1f,2.3f),"OakDark");
                Solid(car,"Gondola roof",car.position+Vector3.up*.66f,new Vector3(1.95f,.12f,2.45f),"Cream");
                Beam(car,car.position+Vector3.up*.64f,hang,.075f,"Iron");
                Motion(car,AmbientMotion.Mode.Spin,Vector3.back,0f,FerrisPeriod);
            }
            Motion(wheel,AmbientMotion.Mode.Spin,Vector3.forward,0f,FerrisPeriod);
        }

        static void Carousel(Transform art,Transform rides,Vector3 centre,float scale)
        {
            Solid(art,"Carousel plinth",centre+Vector3.down*.22f,new Vector3(7.4f*scale,.44f,7.4f*scale),"Stone");
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*2/10;
                Vector3 p=centre+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*3.55f*scale;
                Beam(art,p,p+Vector3.up*.95f,.09f,"Brass");
                if(i%2==0)Model("Bulb",art,p+Vector3.up*1.02f,Vector3.one*.9f);
            }
            Transform ride=Ride(rides,"Carousel",centre);
            Model("CarouselFrame",ride,centre,Vector3.one*scale);
            const int horses=6;
            for(int i=0;i<horses;i++)
            {
                float a=i*Mathf.PI*2/horses;
                Vector3 p=centre+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*1.95f*scale;
                var horse=Model("CarouselHorse",ride,p,Vector3.one*scale,Quaternion.Euler(0,a*Mathf.Rad2Deg+90f,0));
                Retint(horse,"Cream",i%3==0?"Cream":i%3==1?"Rose":"Gold");
                // Лошадь ходит по своему шесту; фаза у каждой своя, иначе
                // все шесть качались бы одной доской.
                Motion(horse.transform,AmbientMotion.Mode.Bob,Vector3.up,.23f*scale,4.6f,i/(float)horses);
            }
            Motion(ride,AmbientMotion.Mode.Spin,Vector3.up,0f,CarouselPeriod);
        }

        static void DropTower(Transform art,Transform rides,Vector3 foot,float height,string colour)
        {
            foreach(int side in new[]{-1,1})
                Beam(art,foot+new Vector3(side*1.15f,0,0),foot+new Vector3(side*1.15f,height,0),.20f,colour);
            for(float y=0;y<height-2.6f;y+=2.6f)
            {
                Beam(art,foot+new Vector3(-1.15f,y,0),foot+new Vector3(1.15f,y+2.6f,0),.10f,"Cream");
                Beam(art,foot+new Vector3(1.15f,y,0),foot+new Vector3(-1.15f,y+2.6f,0),.10f,"Cream");
            }
            Solid(art,"Tower head",foot+Vector3.up*(height+.4f),new Vector3(3.4f,.8f,1.6f),"Gold");
            Solid(art,"Tower base",foot+Vector3.up*.25f,new Vector3(5.2f,.5f,4.2f),"Stone");
            Transform car=Ride(rides,"DropTowerCar",foot+Vector3.up*(height*.5f));
            Solid(car,"Car ring",car.position,new Vector3(3.1f,.55f,1.5f),colour);
            Solid(car,"Car canopy",car.position+Vector3.up*.5f,new Vector3(3.4f,.14f,1.8f),"Cream");
            for(int i=0;i<4;i++)
                Solid(car,"Seat",car.position+new Vector3(-1.15f+i*.77f,-.34f,0),new Vector3(.5f,.34f,.5f),"Iron");
            Motion(car,AmbientMotion.Mode.Bob,Vector3.up,height*.33f,19f,colour=="Red"?.4f:0f);
        }

        // ---------------------------------------------------------------------
        // Восточная зона — игры и еда
        // ---------------------------------------------------------------------

        static void GameZone(Transform art,Transform rides)
        {
            // Ряд игровых будок вдоль северной кромки аллеи, лицом к аллее.
            for(int i=0;i<4;i++)
            {
                float x=46+i*9f;
                Model("Booth",art,new Vector3(x,Ground,PromenadeZ+PromenadeHalf+3.2f),Vector3.one*1.75f,Quaternion.Euler(0,180,0));
                Model("PrizeDisplay",art,new Vector3(x,Ground,PromenadeZ+PromenadeHalf+5.6f),new Vector3(.9f,.85f,.9f),Quaternion.Euler(0,180,0));
                if(i%2==0)Model("Balloons",art,new Vector3(x+2.4f,Ground+1.6f,PromenadeZ+PromenadeHalf+3.0f),Vector3.one*1.2f);
            }
            // Одна будка с настоящим вращающимся колесом фортуны.
            Transform fortune=Ride(rides,"FortuneWheel",new Vector3(64,Ground+2.45f,PromenadeZ+PromenadeHalf+2.1f));
            Model("Wheel",fortune,fortune.position,Vector3.one*2.1f);
            Motion(fortune,AmbientMotion.Mode.Spin,Vector3.forward,0f,13f);

            // Еда — на южной кромке, напротив игр: между рядами остаётся проход.
            Model("SnackBar",art,new Vector3(48,Ground,PromenadeZ-PromenadeHalf-3.4f),Vector3.one*1.7f);
            Model("PopcornCart",art,new Vector3(55,Ground,PromenadeZ-PromenadeHalf-2.6f),Vector3.one*1.4f,Quaternion.Euler(0,-18,0));
            Model("MenuBoard",art,new Vector3(50.6f,Ground,PromenadeZ-PromenadeHalf-2.2f),Vector3.one*1.3f,Quaternion.Euler(0,16,0));
            Model("PrizeCart",art,new Vector3(61,Ground,PromenadeZ-PromenadeHalf-3.0f),Vector3.one*1.4f,Quaternion.Euler(0,12,0));
            for(int i=0;i<4;i++)
            {
                Model("Bench",art,new Vector3(52+i*5f,Ground,PromenadeZ-PromenadeHalf-6.4f),Vector3.one*1.2f);
                if(i%2==0)Model("Barrel",art,new Vector3(54.4f+i*5f,Ground,PromenadeZ-PromenadeHalf-6.4f),Vector3.one*.85f);
            }
            // Большой призовой шатёр замыкает ряд с востока.
            Model("Tent",art,new Vector3(82,Ground,PromenadeZ-3f),Vector3.one*1.6f,Quaternion.Euler(0,-14,0));
            Model("Tent",art,new Vector3(77,Ground,-6f),Vector3.one*1.3f,Quaternion.Euler(0,168,0));
            Model("Tent",art,new Vector3(88,Ground,-12f),Vector3.one*1.15f,Quaternion.Euler(0,196,0));
            Queue(art,new Vector3(62,Ground,PromenadeZ-8.5f),5,new Vector3(2.2f,0,0));
        }

        static void ServiceYard(Transform art)
        {
            // Хозяйственный двор сбоку от башни: объясняет, чем ярмарка живёт.
            Solid(art,"Service yard",new Vector3(70,Ground+.03f,10f),new Vector3(34,.12f,26),"Gravel");
            var rng=new System.Random(5150);
            for(int i=0;i<10;i++)
                Model("Barrel",art,new Vector3(57+i%5*2.2f,Ground,3f+i/5*2.4f),
                    Vector3.one*(.85f+(float)rng.NextDouble()*.3f),Quaternion.Euler(0,rng.Next(360),0));
            for(int i=0;i<6;i++)
                Model("CrateCover",art,new Vector3(72+i%3*2.4f,Ground,13.5f+i/3*2.6f),Vector3.one*.95f,Quaternion.Euler(0,rng.Next(30),0));
            Model("ServiceRack",art,new Vector3(82,Ground,18f),new Vector3(1.1f,1.1f,.8f),Quaternion.Euler(0,-90,0));
            var truck=Model("Car",art,new Vector3(60,Ground,18.5f),Vector3.one*1.25f,Quaternion.Euler(0,104,0));
            Retint(truck,"Teal","Earth");
            Model("Timber",art,new Vector3(76,Ground,4f),new Vector3(2.2f,1.6f,2.2f));
        }

        /// <summary>
        /// Мелочь, которая закрывает пустоты и снимает ощущение «предметы на плите»:
        /// бордюры у мощения, зелёная кайма под оградой, урны, указатели,
        /// расчалки с вымпелами вдоль аллеи ко входу.
        /// </summary>
        static void Dressing(Transform art,Transform rides)
        {
            // Бордюр обводит мощение: без него плита обрывается в землю кромкой.
            Kerb(art,new Vector3(17.28f,Ground,-8f),48f,18f);
            Kerb(art,new Vector3(FairCentre.x,Ground,-33.5f),11f,28f);
            Kerb(art,new Vector3(70,Ground,10f),34f,26f);

            // Зелёная кайма и живая изгородь по внутренней стороне ограды:
            // край утоптанной площадки перестаёт быть прямой линией.
            foreach(float z in new[]{-57f,25f})
                for(float x=-56f;x<=90f;x+=6f)
                {
                    if(z<0 && Mathf.Abs(x-FairCentre.x)<8f)continue;
                    Solid(art,"Hedge",new Vector3(x,Ground+.42f,z),new Vector3(5.6f,.84f,1.5f),"Leaf");
                }
            foreach(float x in new[]{-55f,89f})
                for(float z=-56f;z<=24f;z+=6f)
                    Solid(art,"Hedge",new Vector3(x,Ground+.42f,z),new Vector3(1.5f,.84f,5.6f),"Leaf");

            // Расчалки с вымпелами вдоль аллеи от ворот: раньше здесь
            // был самый большой пустой кусок площадки.
            for(int i=0;i<5;i++)
            {
                float z=GateZ+4f+i*5.2f;
                foreach(int side in new[]{-1,1})
                {
                    Vector3 foot=new Vector3(FairCentre.x+side*6.2f,Ground,z);
                    Beam(art,foot,foot+Vector3.up*3.6f,.11f,"OakDark");
                    if(i<4)Garland(art,foot+Vector3.up*3.3f,foot+new Vector3(0,3.3f,5.2f),.7f);
                    if(i%2!=0)continue;
                    Transform pennant=Group(rides,"Pennant");pennant.position=foot+Vector3.up*3.45f;
                    Solid(pennant,"Pennant cloth",pennant.position+new Vector3(side*.5f,-.12f,0),
                        new Vector3(.95f,.5f,.03f),i%4==0?"Red":"Blue");
                    Motion(pennant,AmbientMotion.Mode.Sway,Vector3.up,13f,3.8f+i*.3f,i*.21f);
                }
            }
            SignPost(art,new Vector3(FairCentre.x-7.6f,Ground,-30f),-24f);
            SignPost(art,new Vector3(FairCentre.x+7.6f,Ground,-20.5f),18f);

            // Урны и лавки по аллее: масштаб и обжитость.
            for(float x=-40f;x<=74f;x+=16f)
            {
                Model("Barrel",art,new Vector3(x,Ground,PromenadeZ-PromenadeHalf-1.3f),Vector3.one*.8f);
                // Восточнее 68 м начинается пятно большого шатра: лавка туда не влезает.
                if(Mathf.Abs(x-FairCentre.x)>9f && x+6f<68f)
                    Model("Bench",art,new Vector3(x+6f,Ground,PromenadeZ-PromenadeHalf-1.6f),Vector3.one*1.15f,
                        Quaternion.Euler(0,180,0));
            }
        }

        static void Kerb(Transform art,Vector3 centre,float width,float depth)
        {
            foreach(int side in new[]{-1,1})
            {
                Solid(art,"Kerb",centre+new Vector3(0,.12f,side*depth*.5f),new Vector3(width+.5f,.24f,.5f),"OakDark");
                Solid(art,"Kerb",centre+new Vector3(side*width*.5f,.12f,0),new Vector3(.5f,.24f,depth+.5f),"OakDark");
            }
        }

        static void SignPost(Transform art,Vector3 foot,float yaw)
        {
            Beam(art,foot,foot+Vector3.up*2.6f,.12f,"OakDark");
            var top=Solid(art,"Sign board",foot+Vector3.up*2.25f,new Vector3(1.9f,.42f,.08f),"Red");
            top.transform.rotation=Quaternion.Euler(0,yaw,0);
            var lower=Solid(art,"Sign board",foot+Vector3.up*1.68f,new Vector3(1.7f,.38f,.08f),"Blue");
            lower.transform.rotation=Quaternion.Euler(0,yaw-14f,0);
        }

        // ---------------------------------------------------------------------
        // Окраина города
        // ---------------------------------------------------------------------

        static void TownEdge(Transform art)
        {
            ApproachRoad(art);
            CarPark(art);
            Town(art);
        }

        static void ApproachRoad(Transform art)
        {
            Solid(art,"Road shoulder",new Vector3(20,Ground+.02f,RoadZ),new Vector3(266,.14f,12.6f),"Gravel");
            Solid(art,"Approach road",new Vector3(20,Ground+.05f,RoadZ),new Vector3(264,.2f,9.4f),"Ink");
            for(float x=-108f;x<150f;x+=9f)
                Solid(art,"Road dash",new Vector3(x,Ground+.16f,RoadZ),new Vector3(4.4f,.06f,.3f),"Cream");
            // Съезд к воротам ярмарки.
            Solid(art,"Gate slip road",new Vector3(FairCentre.x,Ground+.045f,-54f),new Vector3(12,.18f,18),"Ink");
            for(float x=-96f;x<148f;x+=24f)
            {
                Vector3 foot=new Vector3(x,Ground,RoadZ-6.6f);
                Beam(art,foot,foot+Vector3.up*7.2f,.16f,"Iron");
                Beam(art,foot+Vector3.up*7.2f,foot+new Vector3(0,7.4f,2.6f),.13f,"Iron");
                Solid(art,"Street light",foot+new Vector3(0,7.25f,2.7f),new Vector3(.5f,.22f,1.0f),"Glow");
            }
            // Столбы линии электропередачи уводят взгляд в сторону города.
            for(float x=-90f;x<150f;x+=34f)
            {
                Vector3 foot=new Vector3(x,Ground,RoadZ-12.5f);
                Beam(art,foot,foot+Vector3.up*9.6f,.26f,"OakDark");
                Beam(art,foot+new Vector3(-2.3f,8.4f,0),foot+new Vector3(2.3f,8.4f,0),.16f,"OakDark");
                if(x+34f>=150f)continue;
                foreach(int side in new[]{-1,1})
                    Rope(art,foot+new Vector3(side*2.1f,8.4f,0),foot+new Vector3(side*2.1f+34f,8.4f,0),1.7f,"Iron");
            }
        }

        static void CarPark(Transform art)
        {
            Solid(art,"Car park",new Vector3(70,Ground+.04f,-52f),new Vector3(52,.16f,15),"Gravel");
            var rng=new System.Random(8812);
            string[] paint={"Red","Blue","Cream","Gold","Teal","Rose"};
            for(int i=0;i<=9;i++)
            {
                float x=48+i*5.4f;
                Solid(art,"Bay line",new Vector3(x-2.6f,Ground+.12f,-52f),new Vector3(.16f,.05f,10),"Cream");
                if(i==9||rng.Next(5)==0)continue;              // не все места заняты
                var car=Model("Car",art,new Vector3(x,Ground+.12f,-52.4f+(float)rng.NextDouble()*.8f),
                    Vector3.one,Quaternion.Euler(0,rng.Next(2)==0?0:180,0));
                Retint(car,"Teal",paint[rng.Next(paint.Length)]);
            }
        }

        static void Town(Transform art)
        {
            var rng=new System.Random(20260919);
            string[] walls={"Cream","Earth","Stone","OakLight","Rose"};
            // Три плана: домики окраины, кварталы, дальние корпуса. Разные
            // высоты и шаг дают силуэт, в котором читается город, а не забор.
            TownRow(art,rng,walls,-88f,4.5f,7.5f,9f,14f,true,22);
            TownRow(art,rng,walls,-118f,9f,15f,13f,20f,false,18);
            TownRow(art,rng,walls,-156f,15f,29f,18f,28f,false,14);
            // Ориентиры силуэта.
            Landmark(art,new Vector3(-46,Ground,-126f),34f,"Cream");
            Landmark(art,new Vector3(96,Ground,-148f),40f,"Stone");
            WaterTower(art,new Vector3(128,Ground,-104f));
            Chapel(art,new Vector3(-18,Ground,-96f));
        }

        static void TownRow(Transform art,System.Random rng,string[] walls,float z,
            float minHeight,float maxHeight,float minWidth,float maxWidth,bool gable,int count)
        {
            float x=-104f;
            for(int i=0;i<count;i++)
            {
                float w=minWidth+(float)rng.NextDouble()*(maxWidth-minWidth);
                float d=w*(.7f+(float)rng.NextDouble()*.6f);
                float h=minHeight+(float)rng.NextDouble()*(maxHeight-minHeight);
                float zz=z+((float)rng.NextDouble()-.5f)*16f;
                TownBlock(art,new Vector3(x+w*.5f,Ground,zz),w,d,h,walls[rng.Next(walls.Length)],gable,rng);
                x+=w+3f+(float)rng.NextDouble()*9f;
                if(x>150f)break;
            }
        }

        static void TownBlock(Transform art,Vector3 foot,float w,float d,float h,string wall,bool gable,System.Random rng)
        {
            Solid(art,"Town wall",foot+Vector3.up*(h*.5f),new Vector3(w,h,d),wall);
            if(gable)
            {
                // Двускатная крыша из двух наклонных плит: у окраины свой силуэт.
                foreach(int side in new[]{-1,1})
                {
                    var slope=Solid(art,"Town roof",foot+new Vector3(0,h+.85f,side*d*.25f),new Vector3(w+.7f,.22f,d*.62f),"Red");
                    slope.transform.rotation=Quaternion.Euler(side*34f,0,0);
                }
            }
            else Solid(art,"Town roof",foot+Vector3.up*(h+.16f),new Vector3(w+.5f,.32f,d+.5f),"Iron");
            // Светящиеся окна: вечером именно они читаются как жилой город.
            int rows=Mathf.Max(1,Mathf.FloorToInt(h/3.1f)),cols=Mathf.Max(2,Mathf.FloorToInt(w/2.6f));
            for(int r=0;r<rows;r++)
                for(int c=0;c<cols;c++)
                {
                    if(rng.Next(10)<3)continue;                 // часть окон тёмная
                    float wx=foot.x-w*.5f+(c+.5f)*w/cols,wy=foot.y+1.5f+r*3.1f;
                    if(wy>foot.y+h-1f)continue;
                    Solid(art,"Town window",new Vector3(wx,wy,foot.z-d*.5f-.06f),new Vector3(w/cols*.45f,1.15f,.1f),"Glow");
                }
        }

        static void Landmark(Transform art,Vector3 foot,float h,string wall)
        {
            Solid(art,"Town tower",foot+Vector3.up*(h*.5f),new Vector3(13,h,13),wall);
            Solid(art,"Town tower cap",foot+Vector3.up*(h+.4f),new Vector3(14.4f,.8f,14.4f),"Iron");
            for(int r=0;r<Mathf.FloorToInt(h/3.4f);r++)
                for(int c=0;c<4;c++)
                    if((r+c)%3!=0)
                        Solid(art,"Town window",new Vector3(foot.x-4.8f+c*3.2f,foot.y+2.4f+r*3.4f,foot.z-6.6f),
                            new Vector3(1.5f,1.3f,.1f),"Glow");
        }

        static void WaterTower(Transform art,Vector3 foot)
        {
            foreach(int sx in new[]{-1,1})
                foreach(int sz in new[]{-1,1})
                    Beam(art,foot+new Vector3(sx*3.2f,0,sz*3.2f),foot+new Vector3(sx*1.5f,15f,sz*1.5f),.34f,"Iron");
            Solid(art,"Water tank",foot+Vector3.up*18f,new Vector3(8.4f,6.4f,8.4f),"Stone");
            Solid(art,"Tank roof",foot+Vector3.up*21.6f,new Vector3(9.2f,1.1f,9.2f),"Iron");
        }

        static void Chapel(Transform art,Vector3 foot)
        {
            Solid(art,"Chapel",foot+Vector3.up*4.5f,new Vector3(11,9,14),"Cream");
            Solid(art,"Chapel roof",foot+Vector3.up*10f,new Vector3(11.8f,2.2f,14.8f),"Red");
            Solid(art,"Bell tower",foot+new Vector3(0,9f,-6f),new Vector3(4.2f,18f,4.2f),"Cream");
            Solid(art,"Spire",foot+new Vector3(0,20.5f,-6f),new Vector3(2.4f,5f,2.4f),"Iron");
            Solid(art,"Chapel window",foot+new Vector3(0,5.5f,-7.1f),new Vector3(1.6f,3.2f,.12f),"Glow");
        }

        // ---------------------------------------------------------------------
        // Деревья
        // ---------------------------------------------------------------------

        static void Treeline(Transform art)
        {
            // Деревья растут группами на склонах и по кромке площадки, а не
            // поодиночке посреди аллей. Внутри площадки, в городском коридоре
            // и в секторе огня их нет вовсе.
            var rng=new System.Random(3307);
            // Крона сосны — 3.0 м в поперечнике на единичном масштабе, поэтому
            // занятое место считается кругом и новый ствол в чужой круг не встаёт.
            // Без этого в группе оказывалось по четыре дерева в одной точке.
            var taken=new List<Vector3>();
            int placed=0;
            for(int attempt=0;attempt<2600 && placed<150;attempt++)
            {
                float a=(float)rng.NextDouble()*Mathf.PI*2,r=64+(float)rng.NextDouble()*190;
                float cx=FairCentre.x+Mathf.Cos(a)*r,cz=FairCentre.y+Mathf.Sin(a)*r;
                if(OnFairApron(cx,cz)||InTownCorridor(cx,cz)||InFiringSector(cx,cz))continue;
                int cluster=2+rng.Next(5);
                for(int i=0;i<cluster && placed<150;i++)
                {
                    float x=cx+((float)rng.NextDouble()-.5f)*34f,z=cz+((float)rng.NextDouble()-.5f)*34f;
                    if(OnFairApron(x,z)||InTownCorridor(x,z)||InFiringSector(x,z))continue;
                    float s=1.4f+(float)rng.NextDouble()*1.7f;
                    if(!Reserve(taken,x,z,s*1.55f+1.2f))continue;
                    Model("Pine",art,new Vector3(x,TerrainY(x,z)-.15f,z),
                        new Vector3(s,s*(.85f+(float)rng.NextDouble()*.45f),s),Quaternion.Euler(0,rng.Next(360),0));
                    placed++;
                    if(rng.Next(3)!=0)continue;
                    float rx=x+s*2.6f,rz=z+((float)rng.NextDouble()-.5f)*3f,rs=.9f+(float)rng.NextDouble()*1.1f;
                    if(!Reserve(taken,rx,rz,rs*1.1f+.6f))continue;
                    Model("Rocks",art,new Vector3(rx,TerrainY(rx,rz)-.1f,rz),Vector3.one*rs);
                }
            }
            // Трава и цветы — только по краям площадки и вдоль аллей.
            for(int i=0;i<120;i++)
            {
                float x=-58f+(float)rng.NextDouble()*150f,z=-58f+(float)rng.NextDouble()*86f;
                if(InFiringSector(x,z))continue;
                if(Mathf.Abs(z-PromenadeZ)<PromenadeHalf+1f)continue;       // не на аллее
                if(Mathf.Abs(z-RoadZ)<8f)continue;
                Model("Grass",art,new Vector3(x,Ground+.01f,z),Vector3.one*(.85f+(float)rng.NextDouble()*1.2f));
            }
        }

        // ---------------------------------------------------------------------
        // Движение
        // ---------------------------------------------------------------------

        /// <summary>
        /// Занять круг на земле. Возвращает false, если место уже занято:
        /// так группа деревьев остаётся группой, а не кучей в одной точке.
        /// В Vector3 лежит x, радиус и z — отдельная структура ради трёх полей
        /// здесь была бы лишней.
        /// </summary>
        static bool Reserve(List<Vector3> taken,float x,float z,float radius)
        {
            // Проверка по коробке, а не по кругу: аудит сравнивает габаритные
            // коробки, и две кроны, разведённые по расстоянию между центрами,
            // всё равно цеплялись углами по диагонали.
            foreach(Vector3 t in taken)
                if(Mathf.Abs(t.x-x)<t.y+radius && Mathf.Abs(t.z-z)<t.y+radius)return false;
            taken.Add(new Vector3(x,radius,z));
            return true;
        }

        static Transform Ride(Transform rides,string name,Vector3 centre)
        {
            var go=new GameObject(name);go.transform.SetParent(rides,false);go.transform.position=centre;
            return go.transform;
        }

        /// <summary>
        /// Декоративное движение вешается общим <see cref="AmbientMotion"/>:
        /// он считает фазу от сетевых часов, поэтому колесо и карусель стоят
        /// в одном положении у хоста и у всех клиентов без единого байта
        /// трафика и без единого нового RPC.
        /// </summary>
        static void Motion(Transform target,AmbientMotion.Mode mode,Vector3 axis,float amplitude,float period,float phase=0f)
            => target.gameObject.AddComponent<AmbientMotion>().Configure(mode,axis,amplitude,period,phase);
    }
}
