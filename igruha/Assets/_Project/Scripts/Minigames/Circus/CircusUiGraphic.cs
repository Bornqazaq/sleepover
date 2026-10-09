using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Minigames.Circus
{
    /// <summary>Resolution-independent circus signs. No font glyphs or texture copies.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CircusUiGraphic : MaskableGraphic
    {
        public enum Sign { Ticket, Can, Mouse, Swap, Ring, Check, Hourglass, Shield, Down, Cage, Star }
        [SerializeField] private Sign sign;
        [SerializeField] private int symbol;
        [SerializeField, Range(0,1)] private float amount = 1;
        private Rect rect;
        public Sign Kind { get => sign; set { if(sign==value)return;sign=value;SetVerticesDirty(); } }
        public int Symbol { get => symbol; set { if(symbol==value)return;symbol=value;SetVerticesDirty(); } }
        public float Amount { get => amount; set { value=Mathf.Clamp01(value);if(Mathf.Abs(amount-value)<.002f)return;amount=value;SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();rect=GetPixelAdjustedRect();
            switch(sign)
            {
                case Sign.Ticket:
                    Poly(mesh,color,new Vector2(.04f,0),new Vector2(.96f,0),new Vector2(1,.1f),new Vector2(1,.9f),new Vector2(.96f,1),new Vector2(.04f,1),new Vector2(0,.9f),new Vector2(0,.1f));break;
                case Sign.Can:
                    Quad(mesh,.19f,.13f,.81f,.84f,color);Ellipse(mesh,.5f,.14f,.31f,.065f,color*.65f);
                    Quad(mesh,.2f,.14f,.26f,.82f,Color.Lerp(color,Color.white,.3f));
                    Ellipse(mesh,.5f,.85f,.32f,.075f,new Color(.82f,.84f,.81f,color.a));
                    Ellipse(mesh,.5f,.86f,.24f,.045f,new Color(.3f,.34f,.33f,color.a));
                    Quad(mesh,.19f,.15f,.81f,.19f,new Color(.82f,.84f,.81f,color.a));
                    SymbolShape(mesh,symbol,symbol==2?new Color(.13f,.07f,.08f,color.a):new Color(1,.97f,.85f,color.a));break;
                case Sign.Mouse:
                    Ellipse(mesh,.5f,.54f,.31f,.43f,color);Quad(mesh,.2f,.25f,.8f,.65f,color);
                    Ellipse(mesh,.5f,.29f,.3f,.2f,color);Line(mesh,new Vector2(.5f,.94f),new Vector2(.5f,.56f),.035f,new Color(.18f,.08f,.12f,color.a));
                    Line(mesh,new Vector2(.21f,.55f),new Vector2(.79f,.55f),.035f,new Color(.18f,.08f,.12f,color.a));
                    Ellipse(mesh,.365f,.75f,.09f,.13f,new Color(.89f,.32f,.2f,color.a));break;
                case Sign.Swap:
                    Line(mesh,new Vector2(.14f,.68f),new Vector2(.84f,.68f),.07f,color);
                    Poly(mesh,color,new Vector2(.67f,.88f),new Vector2(.91f,.68f),new Vector2(.67f,.48f));
                    Line(mesh,new Vector2(.86f,.30f),new Vector2(.16f,.30f),.07f,color);
                    Poly(mesh,color,new Vector2(.33f,.1f),new Vector2(.09f,.30f),new Vector2(.33f,.50f));break;
                case Sign.Ring:
                    for(int i=0;i<64;i++)
                    {
                        float a=(90-i*360f/64)*Mathf.Deg2Rad,b=(90-(i+1)*360f/64)*Mathf.Deg2Rad;
                        Color c=i/64f<amount?color:new Color(color.r,color.g,color.b,color.a*.15f);
                        Poly(mesh,c,P(a,.49f),P(b,.49f),P(b,.425f),P(a,.425f));
                    }break;
                case Sign.Check:
                    Line(mesh,new Vector2(.15f,.48f),new Vector2(.40f,.23f),.13f,color);
                    Line(mesh,new Vector2(.40f,.23f),new Vector2(.86f,.80f),.13f,color);break;
                case Sign.Hourglass:
                    Quad(mesh,.21f,.1f,.79f,.17f,color);Quad(mesh,.21f,.83f,.79f,.9f,color);
                    Poly(mesh,color,new Vector2(.27f,.78f),new Vector2(.73f,.78f),new Vector2(.5f,.49f));
                    Poly(mesh,color,new Vector2(.27f,.22f),new Vector2(.73f,.22f),new Vector2(.5f,.51f));break;
                case Sign.Shield:
                    Poly(mesh,color,new Vector2(.15f,.86f),new Vector2(.5f,.97f),new Vector2(.85f,.86f),new Vector2(.8f,.35f),new Vector2(.5f,.07f),new Vector2(.2f,.35f));break;
                case Sign.Down:
                    Quad(mesh,.39f,.4f,.61f,.91f,color);
                    Poly(mesh,color,new Vector2(.14f,.45f),new Vector2(.86f,.45f),new Vector2(.5f,.07f));break;
                case Sign.Cage:
                    Quad(mesh,.08f,.12f,.92f,.2f,color);Quad(mesh,.08f,.82f,.92f,.9f,color);
                    for(int i=0;i<4;i++)Quad(mesh,.15f+i*.22f,.16f,.2f+i*.22f,.88f,color);break;
                case Sign.Star: SymbolShape(mesh,3,color);break;
            }
        }
        private static Vector2 P(float angle,float r)=>new Vector2(.5f+Mathf.Cos(angle)*r,.5f+Mathf.Sin(angle)*r);
        private void SymbolShape(VertexHelper m,int id,Color c)
        {
            if(id==0)Ellipse(m,.5f,.5f,.17f,.17f,c);
            else if(id==1)Poly(m,c,new Vector2(.5f,.72f),new Vector2(.29f,.34f),new Vector2(.71f,.34f));
            else if(id==2)Quad(m,.33f,.33f,.67f,.67f,c);
            else if(id==3)
            {
                for(int i=0;i<5;i++)
                {
                    float a=(90+i*72)*Mathf.Deg2Rad,b=(126+i*72)*Mathf.Deg2Rad,d=(162+i*72)*Mathf.Deg2Rad;
                    Poly(m,c,new Vector2(.5f,.5f),P(a,.24f),P(b,.1f),P(d,.24f));
                }
            }
            else {Line(m,new Vector2(.34f,.34f),new Vector2(.66f,.66f),.13f,c);Line(m,new Vector2(.34f,.66f),new Vector2(.66f,.34f),.13f,c);}
        }
        private void Ellipse(VertexHelper m,float x,float y,float rx,float ry,Color c)
        {
            for(int i=0;i<32;i++)
            {float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;Poly(m,c,new Vector2(x,y),new Vector2(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry),new Vector2(x+Mathf.Cos(b)*rx,y+Mathf.Sin(b)*ry));}
        }
        private void Line(VertexHelper m,Vector2 a,Vector2 b,float width,Color c)
        {Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Poly(m,c,a-n,a+n,b+n,b-n);}
        private void Quad(VertexHelper m,float x,float y,float xx,float yy,Color c)=>Poly(m,c,new Vector2(x,y),new Vector2(xx,y),new Vector2(xx,yy),new Vector2(x,yy));
        private void Vertex(VertexHelper m,Color c,Vector2 p)
            =>m.AddVert(new Vector3(rect.xMin+p.x*rect.width,rect.yMin+p.y*rect.height,0),c,Vector2.zero);
        private void Poly(VertexHelper m,Color c,Vector2 a,Vector2 b,Vector2 d)
        {int n=m.currentVertCount;Vertex(m,c,a);Vertex(m,c,b);Vertex(m,c,d);m.AddTriangle(n,n+1,n+2);}
        private void Poly(VertexHelper m,Color c,Vector2 a,Vector2 b,Vector2 d,Vector2 e)
        {int n=m.currentVertCount;Vertex(m,c,a);Vertex(m,c,b);Vertex(m,c,d);Vertex(m,c,e);m.AddTriangle(n,n+1,n+2);m.AddTriangle(n,n+2,n+3);}
        private void Poly(VertexHelper m,Color c,params Vector2[] points)
        {
            int start=m.currentVertCount;
            for(int i=0;i<points.Length;i++)Vertex(m,c,points[i]);
            for(int i=1;i<points.Length-1;i++)m.AddTriangle(start,start+i,start+i+1);
        }
    }
}
