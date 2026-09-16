using TMPro;
using UnityEditor;
using UnityEngine;
using static Igruha.EditorTools.HubCompactPass;

namespace Igruha.EditorTools
{
    internal static class HubCompactDetails
    {
        internal static void Build(Transform root)
        {
            Rugs(root); Posters(root); Garland(root); Darts(root); Hoop(root); Popcorn(root); FitTelevision();
            var g=new HubOriginalGeometry();
            g.Box(new Vector3(4.5f,2.6f,9.66f),new Vector3(4.7f,1.65f,.12f),M("Sage"));g.Build(root,"ClubSignBacking");
            Label(root,"SLEEPOVER",new Vector3(4.5f,2.80f,9.575f),0,7.4f,new Vector2(4.35f,.65f));
            Label(root,"PLAY LATE  /  STAY AWHILE",new Vector3(4.5f,2.24f,9.57f),0,1.8f,new Vector2(4,.24f));
            Label(root,"SNACK CLUB",new Vector3(-9.62f,2.48f,1.2f),270,3.8f,new Vector2(3,.5f));
            Label(root,"ONE MORE ROUND",new Vector3(7.15f,2.60f,-9.61f),180,3.4f,new Vector2(3.7f,.5f));
            Label(root,"MAKE YOURSELF AT HOME",new Vector3(.65f,2.63f,-9.61f),180,2.3f,new Vector2(4.5f,.35f));
        }
        private static void Rugs(Transform root)
        {
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(HubOriginalAssets.Folder+"/Textures/LoungeWeave.png");
            var imp=(TextureImporter)AssetImporter.GetAtPath(HubOriginalAssets.Folder+"/Textures/LoungeWeave.png");
            imp.wrapMode=TextureWrapMode.Clamp;imp.anisoLevel=8;imp.mipmapEnabled=true;imp.filterMode=FilterMode.Trilinear;imp.maxTextureSize=2048;imp.SaveAndReimport();
            var mat=HubOriginalAssets.Mat("WovenRug","DED3BE");mat.SetTexture("_BaseMap",tex);mat.SetFloat("_Smoothness",.02f);
            var g=new HubOriginalGeometry();Rug(g,new Vector3(0,-.675f,-.56f),new Vector2(6.55f,4.4f),mat);
            Rug(g,new Vector3(6.85f,.025f,-6.95f),new Vector2(4.45f,3.45f),M("Sage"));
            Rug(g,new Vector3(-6.3f,.025f,1.27f),new Vector2(1.13f,4.62f),M("Terracotta"));
            Rug(g,new Vector3(-7.3f,.025f,-3.72f),new Vector2(3.5f,2.6f),M("Sage"));
            g.Build(root,"Rugs",castShadows:false);
        }
        private static void Rug(HubOriginalGeometry g,Vector3 p,Vector2 size,Material mat)
        {
            var x=Vector3.right*size.x*.5f;var z=Vector3.forward*size.y*.5f;
            g.Quad(p-x-z,p-x+z,p+x+z,p+x-z,mat,Vector2.one);
            if(mat.name!="HO_WovenRug")
            {
                foreach(float sx in new[]{-1,1})g.Box(p+Vector3.right*sx*(size.x*.5f-.09f)+Vector3.up*.003f,new Vector3(.019f,.004f,size.y-.15f),M("Cream"));
                foreach(float sz in new[]{-1,1})g.Box(p+Vector3.forward*sz*(size.y*.5f-.09f)+Vector3.up*.003f,new Vector3(size.x-.15f,.004f,.019f),M("Cream"));
            }
            int n=Mathf.CeilToInt(size.x/.065f);
            for(int i=0;i<n;i++)foreach(float sign in new[]{-1,1})g.Box(p+new Vector3(-size.x*.5f+(i+.5f)*size.x/n,0,sign*(size.y*.5f+.034f)),new Vector3(.025f,.007f,.07f),M("Cream"));
        }
        private static void Posters(Transform root)
        {
            Poster(root,"BOWLING",new Vector3(-4.0f,2.05f,9.66f),0,"Terracotta",0);
            Poster(root,"GOOD SPORT",new Vector3(1.15f,2.05f,9.66f),0,"Sage",1);
            Poster(root,"STAY & PLAY",new Vector3(9.66f,2.05f,3.65f),90,"Terracotta",2);
            Poster(root,"SIDE A",new Vector3(9.66f,2.05f,-.8f),90,"Blue",3);
            Poster(root,"GAME NIGHT",new Vector3(-9.66f,2.25f,-5.6f),270,"Ochre",2);
        }
        private static void Poster(Transform root,string title,Vector3 p,float yaw,string color,int icon)
        {
            var q=Quaternion.Euler(0,yaw,0);var g=new HubOriginalGeometry();
            g.Box(p,new Vector3(1.05f,1.4f,.05f),M("Oak"),q);g.Box(p+q*Vector3.back*.032f,new Vector3(.96f,1.30f,.018f),M("Cream"),q);
            g.Box(p+q*Vector3.back*.046f,new Vector3(.87f,1.21f,.008f),M(color),q);
            Vector3 center=p+q*new Vector3(0,-.08f,-.053f);
            if(icon==0)
            {
                for(int i=0;i<3;i++){var v=center+q*new Vector3((i-1)*.22f,0,0);g.Box(v,new Vector3(.095f,.40f,.008f),M("Cream"),q);g.Disc(v+Vector3.up*.22f,q*Vector3.left*.066f,Vector3.up*.066f,M("Cream"));}
                g.Disc(center+q*new Vector3(-.17f,-.16f,-.003f),q*Vector3.left*.14f,Vector3.up*.14f,M("Ink"));
            }
            else if(icon==1){g.Disc(center,q*Vector3.left*.24f,Vector3.up*.28f,M("Cream"));g.Line(center-Vector3.up*.15f,center-Vector3.up*.45f,.055f,M("Oak"));}
            else if(icon==2){g.Box(center,new Vector3(.58f,.29f,.01f),M("Ink"),q);g.Box(center+q*new Vector3(-.14f,0,-.01f),new Vector3(.035f,.16f,.01f),M("Cream"),q);g.Box(center+q*new Vector3(-.14f,0,-.012f),new Vector3(.15f,.035f,.01f),M("Cream"),q);g.Disc(center+q*new Vector3(.15f,0,-.015f),q*Vector3.left*.045f,Vector3.up*.045f,M("Ochre"));}
            else{g.Disc(center,q*Vector3.left*.29f,Vector3.up*.29f,M("Ink"));g.Disc(center+q*Vector3.back*.004f,q*Vector3.left*.11f,Vector3.up*.11f,M("Ochre"));}
            g.Build(root,"Print_"+title.Replace(" ","_"));
            Label(root,title,p+q*new Vector3(0,.42f,-.065f),yaw,1.95f,new Vector2(.90f,.22f));
            Label(root,"SLEEPOVER SOCIAL CLUB",p+q*new Vector3(0,-.49f,-.065f),yaw,.57f,new Vector2(.83f,.12f));
        }
        private static void Garland(Transform root)
        {
            var g=new HubOriginalGeometry();
            for(int side=0;side<4;side++)
            {
                var q=Quaternion.Euler(0,side*90,0);
                for(int i=0;i<100;i++)
                {float x=-9.6f+i*.192f,n=x+.192f;g.Line(q*new Vector3(x,3.52f-.16f*Mathf.Sin((x+9.6f)*Mathf.PI/3.84f),9.55f),q*new Vector3(n,3.52f-.16f*Mathf.Sin((n+9.6f)*Mathf.PI/3.84f),9.55f),.011f,M("Ink"));}
                for(int i=0;i<26;i++)
                {float x=-9.3f+i*.74f;var p=q*new Vector3(x,3.50f-.16f*Mathf.Sin((x+9.6f)*Mathf.PI/3.84f),9.55f);g.Box(p-Vector3.up*.035f,new Vector3(.035f,.07f,.035f),M("Ink"));g.Sphere(p-Vector3.up*.10f,.038f,M("Glow"));}
            }
            g.Build(root,"StringLights");
        }
        private static void Darts(Transform root)
        {
            var g=new HubOriginalGeometry();var p=new Vector3(-9.65f,1.65f,-3.45f);var right=Vector3.back;var up=Vector3.up;
            g.Disc(p,right*.55f,up*.55f,M("Walnut"));
            for(int i=0;i<20;i++)
            {float a=i*Mathf.PI*.1f,b=(i+1)*Mathf.PI*.1f;g.Quad(p+Vector3.right*.011f,p+right*Mathf.Cos(a)*.50f+up*Mathf.Sin(a)*.50f+Vector3.right*.011f,p+right*Mathf.Cos(b)*.50f+up*Mathf.Sin(b)*.50f+Vector3.right*.011f,p+Vector3.right*.011f,M(i%2==0?"Cream":"Ink"),Vector2.one);}
            g.Disc(p+Vector3.right*.022f,right*.075f,up*.075f,M("Red"));g.Build(root,"DartBoard");
        }
        private static void Hoop(Transform root)
        {
            var g=new HubOriginalGeometry();g.Box(new Vector3(-.5f,2.19f,9.61f),new Vector3(1.25f,.84f,.08f),M("Terracotta"));g.Box(new Vector3(-.5f,2.19f,9.56f),new Vector3(1.12f,.72f,.02f),M("Cream"));
            var c=new Vector3(-.5f,1.90f,9.20f);
            for(int i=0;i<36;i++)
            {float a=i*Mathf.PI/18,b=(i+1)*Mathf.PI/18;g.Line(c+new Vector3(Mathf.Cos(a)*.24f,0,Mathf.Sin(a)*.24f),c+new Vector3(Mathf.Cos(b)*.24f,0,Mathf.Sin(b)*.24f),.026f,M("Red"));}
            for(int i=0;i<12;i++){float a=i*Mathf.PI/6;g.Line(c+new Vector3(Mathf.Cos(a)*.24f,0,Mathf.Sin(a)*.24f),c+new Vector3(Mathf.Cos(a+.3f)*.13f,-.34f,Mathf.Sin(a+.3f)*.13f),.009f,M("Cream"));}
            g.Build(root,"BasketballHoop");
        }
        private static void Popcorn(Transform root)
        {
            var p=new Vector3(-7.6f,1.18f,2.7f);var g=new HubOriginalGeometry();
            g.Box(p+Vector3.up*.045f,new Vector3(.48f,.09f,.64f),M("Cream"));
            g.Box(p+Vector3.up*.69f,new Vector3(.53f,.13f,.69f),M("Red"));
            foreach(float x in new[]{-.21f,.21f})foreach(float z in new[]{-.28f,.28f})g.Box(p+new Vector3(x,.36f,z),new Vector3(.025f,.61f,.025f),M("Brass"));
            var random=new System.Random(72);
            for(int i=0;i<65;i++)g.Sphere(p+new Vector3((float)random.NextDouble()*.38f-.19f,.10f+(float)random.NextDouble()*.1f,(float)random.NextDouble()*.48f-.24f),.024f,M(i%5==0?"Ochre":"Paper"));
            g.Build(root,"PopcornMachine");
            Label(root,"POPCORN",p+new Vector3(.274f,.69f,0),270,1.05f,new Vector2(.58f,.12f));
        }
        private static void FitTelevision() => HubConsoleMenuBuilder.Apply();
        internal static void Label(Transform parent,string text,Vector3 pos,float yaw,float size,Vector2 rect)
        {
            var go=new GameObject("Label_"+text);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));
            var label=go.AddComponent<TextMeshPro>();label.font=HubBarAssets.LetteringFont();label.text=text;label.fontSize=size;label.alignment=TextAlignmentOptions.Center;label.color=HubCozyMaterials.Hex("F1DBAE");label.rectTransform.sizeDelta=rect;
            label.textWrappingMode=TextWrappingModes.NoWrap;label.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
