Shader "WindTrace/Foliage Double Sided"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        LOD 150
        Cull Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;

            v2f vert(appdata input)
            {
                v2f output;
                output.pos = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, input.uv) * _Color;
                clip(albedo.a - _Cutoff);
                float3 normal = normalize(input.worldNormal) * (facing >= 0 ? 1 : -1);
                fixed diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed3 ambient = ShadeSH9(float4(normal, 1));
                albedo.rgb *= ambient + _LightColor0.rgb * (diffuse * .72 + .22);
                return albedo;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Cutout/VertexLit"
}
