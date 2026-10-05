Shader "WindTrace/Reference Sky"
{
    Properties { _Top("Zenith",Color)=(.21,.43,.63,1) _Horizon("Horizon",Color)=(.65,.77,.81,1) }
    SubShader
    {
        Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Top,_Horizon;
            struct v2f{float4 position:SV_POSITION;float3 ray:TEXCOORD0;};
            v2f vert(float4 vertex:POSITION){v2f o;o.position=UnityObjectToClipPos(vertex);o.ray=vertex.xyz;return o;}
            float h(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(h(i),h(i+float2(1,0)),f.x),lerp(h(i+float2(0,1)),h(i+1),f.x),f.y);}
            fixed4 frag(v2f i):SV_Target
            {
                float3 ray=normalize(i.ray);
                float3 sky=lerp(_Horizon.rgb,_Top.rgb,pow(saturate(ray.y),.55));
                float2 p=ray.xz/max(.10,ray.y)*1.2;
                float cloud=noise(p)*.52+noise(p*2.1)*.28+noise(p*4.7)*.14+noise(p*9.8)*.06;
                cloud=smoothstep(.50,.68,cloud)*smoothstep(.03,.20,ray.y);
                sky=lerp(sky,float3(.94,.93,.85),cloud*.87);
                return fixed4(sky,1);
            }
            ENDCG
        }
    }
}
