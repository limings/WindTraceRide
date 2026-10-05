Shader "WindTrace/Reference Paving"
{
    Properties
    {
        _Color("Stone shadow",Color)=(.3,.3,.27,1)
        _SecondaryColor("Stone light",Color)=(.55,.54,.44,1)
        _BlockScale("Blocks per metre",Float)=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color,_SecondaryColor;
        float _BlockScale;
        struct Input { float3 worldPos; };
        float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float2 p=IN.worldPos.xz*_BlockScale*float2(1,.72);
            float row=floor(p.y);
            p.x+=fmod(row,2)*.5;
            float2 cell=floor(p),f=frac(p);
            float jitter=(sin(p.x*7.3)+sin(p.y*11.2))*.009;
            float2 edge=min(f,1-f)+jitter;
            float joint=smoothstep(.006,.035,min(edge.x,edge.y));
            float n=hash(cell);
            float detail=hash(floor(IN.worldPos.xz*45))*.035;
            o.Albedo=lerp(_Color.rgb*.67,lerp(_Color.rgb,_SecondaryColor.rgb,n)+detail,joint);
            o.Normal=normalize(float3((step(f.x,.07)-step(.93,f.x))*.17,
                (step(f.y,.07)-step(.93,f.y))*.17,1));
            o.Smoothness=.13;o.Occlusion=.65+joint*.35;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
