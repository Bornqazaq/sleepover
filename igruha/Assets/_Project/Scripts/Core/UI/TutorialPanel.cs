using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Мягкие карточки без текстур: одинаково чёткие на любом размере окна.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TutorialPanel : MaskableGraphic
    {
        private const int CornerSegments = 8;
        public float Radius { get; set; } = 20f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(Radius, Mathf.Min(rect.width,rect.height)*.5f);
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int corner=0;corner<4;corner++)
            {
                Vector2 center = new Vector2(corner < 2 ? rect.xMax-radius : rect.xMin+radius,
                    corner == 0 || corner == 3 ? rect.yMax-radius : rect.yMin+radius);
                for (int step=0;step<=CornerSegments;step++)
                {
                    float angle=(90f-corner*90f-step*90f/CornerSegments)*Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius, color, Vector2.zero);
                }
            }
            int count=4*(CornerSegments+1);
            for(int i=0;i<count;i++)mesh.AddTriangle(0,i+1,(i+1)%count+1);
        }
    }
}
