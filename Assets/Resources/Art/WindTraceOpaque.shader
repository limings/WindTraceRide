Shader "WindTrace/Opaque"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("Wind Glow", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 100
        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _EmissionColor;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 lightDirection = normalize(float3(-0.35, 0.8, -0.45));
                float lighting = saturate(dot(normalize(input.worldNormal), lightDirection)) * 0.55 + 0.45;
                return fixed4(_Color.rgb * lighting + _EmissionColor.rgb, 1.0);
            }
            ENDCG
        }
    }
}

