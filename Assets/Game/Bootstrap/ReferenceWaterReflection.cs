using UnityEngine;

namespace WindTraceRide.Bootstrap
{
    public sealed class ReferenceWaterReflection : MonoBehaviour
    {
        private Camera reflectionCamera;
        private RenderTexture texture;
        private Material material;
        private int lastFrame=-100;
        private Vector3 lastPosition;
        private static bool rendering;
        public void Initialize(Material value){material=value;}

        public void RenderFor(Camera source,float height)
        {
            if(rendering||source==null||material==null)return;
            if(Application.isPlaying && Time.frameCount-lastFrame<4)return;
            if(!Application.isPlaying && lastFrame==Time.frameCount && (source.transform.position-lastPosition).sqrMagnitude<.01f)return;
            lastFrame=Time.frameCount;lastPosition=source.transform.position;
            if(reflectionCamera==null)
            {
                var go=new GameObject("Water reflection camera");go.hideFlags=HideFlags.HideAndDontSave;
                go.transform.SetParent(transform,false);reflectionCamera=go.AddComponent<Camera>();reflectionCamera.enabled=false;
                texture=new RenderTexture(512,288,16){name="512px water reflection"};texture.Create();
            }
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled=false;reflectionCamera.targetTexture=texture;
            reflectionCamera.cullingMask=source.cullingMask&~(1<<4);
            reflectionCamera.farClipPlane=Mathf.Min(240,source.farClipPlane);
            reflectionCamera.depthTextureMode=DepthTextureMode.None;
            reflectionCamera.allowMSAA=false;reflectionCamera.allowHDR=false;
            var matrix=Matrix4x4.identity;matrix.m11=-1;matrix.m13=2*height;
            reflectionCamera.worldToCameraMatrix=source.worldToCameraMatrix*matrix;
            reflectionCamera.transform.position=matrix.MultiplyPoint(source.transform.position);
            var clipPoint=reflectionCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0,height+.035f,0));
            var clipNormal=reflectionCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
            reflectionCamera.projectionMatrix=source.CalculateObliqueMatrix(new Vector4(clipNormal.x,clipNormal.y,clipNormal.z,-Vector3.Dot(clipPoint,clipNormal)));
            var invert=GL.invertCulling;
            try{rendering=true;GL.invertCulling=!invert;reflectionCamera.Render();}
            finally{GL.invertCulling=invert;rendering=false;}
            material.SetTexture("_PlanarReflection",texture);material.SetFloat("_HasReflection",1);
        }

        private void OnDestroy()
        {
            if(texture==null)return;
            texture.Release();
            if(Application.isPlaying)Destroy(texture);else DestroyImmediate(texture);
        }
    }

    public sealed class ReferenceWaterTile : MonoBehaviour
    {
        public ReferenceWaterReflection Reflection;
        private void OnWillRenderObject()
        {
            if(Reflection!=null)Reflection.RenderFor(Camera.current,GetComponent<Renderer>().bounds.center.y);
        }
    }
}
