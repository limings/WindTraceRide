Shader "WindTrace/Reference Waterfall"
{
    Properties { _Color("Water",Color)=(.23,.47,.51,1) _MainTex("UV carrier",2D)="white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0
        fixed4 _Color;
        struct Input { float2 uv_MainTex; };
        sampler2D _MainTex;
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            float2 p=IN.uv_MainTex;
            float streak=sin(p.x*117+sin(p.x*41)*3+p.y*8+_Time.y*7)*.5+.5;
            float drop=sin(p.y*88+_Time.y*17+p.x*13)*.5+.5;
            float edge=smoothstep(0,.08,p.x)*smoothstep(0,.08,1-p.x);
            o.Albedo=lerp(_Color.rgb,float3(.76,.88,.87),saturate(streak*.46+drop*.15+(1-edge)*.3));
            o.Emission=o.Albedo*.24;
            o.Smoothness=.38;
            o.Alpha=edge*.78;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
