Shader "WindTrace/Foliage Billboard"
{
    Properties
    {
        _MainTex ("Foliage Cutout", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.38
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert alphatest:_Cutoff addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/VertexLit"
}
