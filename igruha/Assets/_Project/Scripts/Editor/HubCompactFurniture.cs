using UnityEngine;
using static Igruha.EditorTools.HubCompactPass;

namespace Igruha.EditorTools
{
    internal static class HubCompactFurniture
    {
        private static GameObject Prop(Transform p,string model,Vector3 pos,float yaw=0) => HubOriginalAssets.Prop(p,model,pos,yaw);
        internal static void Build(Transform root)
        {
            var lounge=Group(root,"Lounge");
            Seat(Prop(lounge,"Sofa",new Vector3(0,-.7f,-2.56f)),2.8f);
            Seat(Prop(lounge,"Armchair",new Vector3(-3f,-.7f,-.2f),30),1.18f);
            Seat(Prop(lounge,"Armchair",new Vector3(2.85f,-.7f,-.9f),-30),1.18f);
            var coffee=Prop(lounge,"CoffeeTable",new Vector3(0,-.7f,-.65f));ModelBox(coffee,new Vector3(0,.27f,0),new Vector3(1.48f,.54f,.88f));
            var cabinet=Prop(lounge,"ConsoleTable",new Vector3(0,-.7f,2.82f),180);ModelBox(cabinet,new Vector3(0,.3f,0),new Vector3(2.88f,.60f,.66f));
            var tv=Prop(lounge,"Television",new Vector3(0,-.03f,2.86f),180);ModelBox(tv,new Vector3(0,1,0),new Vector3(3.38f,1.90f,.18f));
            foreach(float x in new[]{-2.08f,2.08f}){var speaker=Prop(lounge,"Speaker",new Vector3(x,-.7f,2.71f),180);ModelBox(speaker,new Vector3(0,.58f,0),new Vector3(.34f,1.16f,.32f));}
            foreach(float x in new[]{-1.8f,1.8f}){var bag=Prop(lounge,"Beanbag",new Vector3(x,-.7f,1.4f),x>0?-120:120);ModelBox(bag,new Vector3(0,.3f,0),new Vector3(1,.6f,.90f));}
            Prop(lounge,"SnackTray",new Vector3(-.25f,-.13f,-.62f),-12);Prop(lounge,"Controller",new Vector3(.48f,-.13f,-.5f),35);
            Prop(lounge,"Controller",new Vector3(.86f,-.108f,2.69f),160);
            var games=Group(root,"Games");
            var bowling=Prop(games,"BowlingLane",new Vector3(-5.65f,0,8.2f),90);ModelBox(bowling,new Vector3(0,.13f,0),new Vector3(1.7f,.26f,5.8f));ModelBox(bowling,new Vector3(0,.65f,2.72f),new Vector3(1.65f,.9f,.22f));
            var table=Prop(games,"PingPong",new Vector3(-1.35f,0,6.35f));ModelBox(table,new Vector3(0,.435f,0),new Vector3(2.74f,.87f,1.525f));
            var pool=Prop(games,"Billiards",new Vector3(6.8f,0,1.7f));ModelBox(pool,new Vector3(0,.475f,0),new Vector3(1.62f,.95f,2.92f));
            var kicker=Prop(games,"Foosball",new Vector3(6.8f,0,-2.45f));ModelBox(kicker,new Vector3(0,.54f,0),new Vector3(1.16f,1.08f,2.1f));
            var podium=Group(root,"Podium");var g=new HubOriginalGeometry();
            g.Box(new Vector3(6.8f,.20f,7.55f),new Vector3(3.3f,.4f,2.75f),M("Walnut"));Solid(podium,"LowerStep",new Vector3(6.8f,.20f,7.55f),new Vector3(3.3f,.4f,2.75f));
            g.Box(new Vector3(6.8f,.52f,7.75f),new Vector3(2.55f,.24f,2.02f),M("Oak"));Solid(podium,"UpperStep",new Vector3(6.8f,.52f,7.75f),new Vector3(2.55f,.24f,2.02f));g.Build(podium,"PodiumSteps");
            var throne=Prop(podium,"CrownChair",new Vector3(6.8f,.64f,7.95f),180);Seat(throne,1.34f);
            ModelBox(throne,new Vector3(0,1.40f,-.34f),new Vector3(1.25f,1.02f,.25f));
            var bar=Group(root,"SnackBar");var counter=Prop(bar,"BarCounter",new Vector3(-7.6f,0,1.3f));ModelBox(counter,new Vector3(0,.58f,0),new Vector3(1.27f,1.17f,4.22f));
            for(int i=0;i<3;i++){var stool=Prop(bar,"Stool",new Vector3(-6.2f,0,-.12f+i*1.32f));ModelBox(stool,new Vector3(0,.43f,0),new Vector3(.55f,.86f,.55f));}
            var fridge=Prop(bar,"Fridge",new Vector3(-8.91f,0,4.37f),90);ModelBox(fridge,new Vector3(0,1.05f,0),new Vector3(1.04f,2.1f,.88f));
            var shelf=Prop(bar,"Shelf",new Vector3(-9.35f,0,1.15f),90);ModelBox(shelf,new Vector3(0,1.1f,0),new Vector3(2.4f,2.2f,.58f));
            Prop(bar,"RecordPlayer",new Vector3(-7.6f,1.18f,-.18f),90);Prop(bar,"SnackTray",new Vector3(-7.4f,1.18f,1.2f),60);
            var arcade=Group(root,"ArcadeCorner");
            for(int i=0;i<3;i++){var a=Prop(arcade,"Arcade",new Vector3(5.85f+i*1.36f,0,-8.25f+i*.24f),-12-i*8);ModelBox(a,new Vector3(0,1,0),new Vector3(.98f,2,.83f));}
            var library=Group(root,"WorkshopAndLibrary");
            foreach(float x in new[]{-.5f,2.25f}){var bookcase=Prop(library,"Shelf",new Vector3(x,0,-9.35f));ModelBox(bookcase,new Vector3(0,1.10f,0),new Vector3(2.4f,2.2f,.58f));}
            for(int i=0;i<3;i++){var c=Prop(library,"Crate",new Vector3(-2.25f-i*.58f,0,-9.26f+i*.12f),i*6);ModelBox(c,new Vector3(0,.26f,0),new Vector3(.65f,.55f,.56f));}
            var stackedCrate=Prop(library,"Crate",new Vector3(-2.83f,.55f,-9.14f),-8);ModelBox(stackedCrate,new Vector3(0,.26f,0),new Vector3(.65f,.55f,.56f));
            foreach(var p in new[]{new Vector3(-8.8f,0,5.95f),new Vector3(-4.5f,0,6.6f),new Vector3(4.72f,0,-4.12f),new Vector3(8.65f,0,5.55f),new Vector3(-9.22f,0,-5.4f)})
            {var plant=Prop(root,"Plant",p);ModelBox(plant,new Vector3(0,.22f,0),new Vector3(.51f,.44f,.51f));}
            HubCompactDetails.Build(root);
        }
        private static void Seat(GameObject go,float width)
        {
            ModelBox(go,new Vector3(0,.37f,0),new Vector3(width,.58f,.92f));
            ModelBox(go,new Vector3(0,.88f,-.35f),new Vector3(width,.69f,.25f));
            foreach(float x in new[]{-width*.5f+.14f,width*.5f-.14f})ModelBox(go,new Vector3(x,.72f,0),new Vector3(.28f,.50f,.95f));
        }
    }
}
