using UnityEngine;
using UnityEngine.UI;

namespace Igruha.Core.UI
{
    /// <summary>Живой вид тренировочной арены; отдельная камера не меняет игровой риг.</summary>
    public sealed class TutorialArenaPreview : MonoBehaviour
    {
        private const int Width = 960;
        private const int Height = 384;
        private Camera source;
        private Camera preview;
        private RenderTexture texture;
        private RawImage image;
        private bool visible;

        public void Bind(RawImage target)
        {
            image = target;
            source = Camera.main;
            if (source == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            texture = new RenderTexture(Width, Height, 24) { name = "Tutorial arena" };
            texture.Create();
            var go = new GameObject("TutorialPreviewCamera");
            go.transform.SetParent(transform, false);
            preview = go.AddComponent<Camera>();
            preview.CopyFrom(source);
            preview.allowMSAA = false;
            preview.targetTexture = texture;
            preview.rect = new Rect(0,0,1,1);
            preview.aspect = (float)Width / Height;
            preview.enabled = false;
            image.texture = texture;
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (preview != null) preview.enabled = value;
        }

        private void LateUpdate()
        {
            if (!visible || source == null || preview == null) return;
            preview.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            preview.fieldOfView = source.fieldOfView;
        }

        private void OnDestroy()
        {
            if (preview != null) preview.targetTexture = null;
            if (image != null) image.texture = null;
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
        }
    }
}
