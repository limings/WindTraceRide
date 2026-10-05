Shader "WindTrace/Reference Mountain"
{
    Properties{_Color("Rock",Color)=(.28,.3,.26,1) _Snowline("Snowline",Float)=67 _MainTex("Rock detail",2D)="white" {}}
    SubShader
    {
        Tags{"RenderType"="Opaque"}
        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0
        fixed4 _Color;float _Snowline;
        sampler2D _MainTex;
        struct Input{float3 worldPos;float3 worldNormal;};
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float3 weights=pow(abs(IN.worldNormal),3);weights/=max(.001,weights.x+weights.y+weights.z);
            float3 p=IN.worldPos*.11;
            float3 detail=tex2D(_MainTex,p.yz).rgb*weights.x+tex2D(_MainTex,p.xz).rgb*weights.y+tex2D(_MainTex,p.xy).rgb*weights.z;
            float snow=smoothstep(_Snowline-5,_Snowline+4,IN.worldPos.y+sin(IN.worldPos.x*.1)*3);
            snow*=smoothstep(.18,.60,IN.worldNormal.y);
            o.Albedo=lerp(_Color.rgb*lerp(.72,1.35,detail),float3(.84,.86,.85),snow);
            o.Smoothness=.1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
