Shader "WindTrace/Reference Surface"
{
    Properties
    {
        _Color("Tint",Color)=(1,1,1,1)
        _MainTex("Baked Blender albedo",2D)="white" {}
        _TextureScale("Repeats per metre",Float)=.5
        _Glossiness("Smoothness",Range(0,1))=.2
    }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _TextureScale,_Glossiness;
        struct Input{float3 worldPos;float3 worldNormal;};
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 blend=pow(abs(IN.worldNormal),4);
            blend/=max(.001,blend.x+blend.y+blend.z);
            float3 p=IN.worldPos*_TextureScale;
            float3 color=tex2D(_MainTex,p.yz).rgb*blend.x+tex2D(_MainTex,p.xz).rgb*blend.y+tex2D(_MainTex,p.xy).rgb*blend.z;
            o.Albedo=color*_Color.rgb;o.Smoothness=_Glossiness;o.Occlusion=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
