using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Igruha.Minigames.HoleInWall
{
    /// <summary>Scene-local planar reflection. Half-size, throttled, absent underwater/headless.</summary>
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(Renderer))]
    public sealed class HoleInWallWaterReflection : MonoBehaviour
    {
        private const float RefreshInterval=1f/15f;
        private Camera source, reflection;
        private Renderer surface;
        private RenderTexture texture;
        private MaterialPropertyBlock properties;
        private UniversalRenderPipeline.SingleCameraRequest request;
        private float nextRender;
        private static readonly int Reflection=Shader.PropertyToID("_PlanarReflection");
        private static readonly int Projection=Shader.PropertyToID("_PlanarVP");
        private static readonly int Available=Shader.PropertyToID("_PlanarAvailable");
        private void Awake()
        {
            surface=GetComponent<Renderer>();
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null){enabled=false;return;}
            source=Camera.main;
            var go=new GameObject("Pavilion water reflection camera"){hideFlags=HideFlags.HideAndDontSave};
            reflection=go.AddComponent<Camera>();reflection.enabled=false;
            var data=go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows=false;data.renderPostProcessing=false;
            data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
            texture=new RenderTexture(1024,576,24,RenderTextureFormat.DefaultHDR){name="Pavilion planar reflection",hideFlags=HideFlags.HideAndDontSave};texture.Create();
            properties=new MaterialPropertyBlock();
            request=new UniversalRenderPipeline.SingleCameraRequest{destination=texture};
        }
        private void LateUpdate()
        {
            if(source==null||reflection==null||Time.unscaledTime<nextRender||source.transform.position.y<transform.position.y+.12f)return;
            nextRender=Time.unscaledTime+RefreshInterval;
            reflection.CopyFrom(source);reflection.enabled=false;
            reflection.cameraType=CameraType.Reflection;
            reflection.cullingMask=source.cullingMask & ~LayerMask.GetMask("UI");
            float level=transform.position.y;
            var mirror=Matrix4x4.identity;mirror.m11=-1;mirror.m13=2*level;
            reflection.transform.position=mirror.MultiplyPoint(source.transform.position);
            reflection.transform.rotation=Quaternion.Euler(-source.transform.eulerAngles.x,source.transform.eulerAngles.y,source.transform.eulerAngles.z);
            reflection.worldToCameraMatrix=source.worldToCameraMatrix*mirror;
            Vector3 point=reflection.worldToCameraMatrix.MultiplyPoint(new Vector3(0,level+.025f,0));
            Vector3 normal=reflection.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
            reflection.projectionMatrix=source.CalculateObliqueMatrix(new Vector4(normal.x,normal.y,normal.z,-Vector3.Dot(point,normal)));
            bool invert=GL.invertCulling, visible=surface.enabled;
            try {surface.enabled=false;GL.invertCulling=!invert;RenderPipeline.SubmitRenderRequest(reflection,request);}
            finally {surface.enabled=visible;GL.invertCulling=invert;}
            surface.GetPropertyBlock(properties);
            properties.SetTexture(Reflection,texture);
            properties.SetMatrix(Projection,GL.GetGPUProjectionMatrix(reflection.projectionMatrix,true)*reflection.worldToCameraMatrix);
            properties.SetFloat(Available,1);surface.SetPropertyBlock(properties);
        }
        private void OnDisable()
        {
            if(surface!=null&&properties!=null){properties.SetFloat(Available,0);surface.SetPropertyBlock(properties);}
        }
        private void OnDestroy()
        {
            if(texture!=null){texture.Release();Destroy(texture);}
            if(reflection!=null)Destroy(reflection.gameObject);
        }
    }
}
