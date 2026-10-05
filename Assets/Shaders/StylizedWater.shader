Shader "WindTrace/Stylized Water"
{
    Properties
    {
        [HideInInspector] _MainTex ("UV carrier", 2D) = "white" {}
        _Color ("Deep Color", Color) = (0.01,0.18,0.27,1)
        _SecondaryColor ("Shallow Color", Color) = (0.05,0.43,0.52,1)
        _FoamColor ("Foam Color", Color) = (0.75,0.96,1,1)
        _ReflectionColor ("Sky Reflection", Color) = (0.34,0.68,0.88,1)
        _WaveStrength ("Wave Strength", Range(0,0.2)) = 0.09
        _WaveScale ("Wave Scale", Range(0.1,3)) = 1
        _FlowSpeed ("Flow Speed", Range(0,2)) = 0.65
        _Glossiness ("Smoothness", Range(0,1)) = 0.82
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 220
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        fixed4 _Color, _SecondaryColor, _FoamColor, _ReflectionColor;
        half _WaveStrength, _WaveScale, _FlowSpeed, _Glossiness;
        struct Input { float3 worldPos; float3 viewDir; float2 uv_MainTex; };

        void vert(inout appdata_full v)
        {
            float2 p = v.vertex.xz * _WaveScale;
            float t = _Time.y * _FlowSpeed;
            float broad = sin(p.x * .34 + p.y * .18 + t) * .58;
            float crossing = sin(p.x * -.21 + p.y * .49 - t * 1.27) * .29;
            float ripple = sin(p.x * 1.31 + p.y * .87 + t * 2.1) * .13;
            v.vertex.y += (broad + crossing + ripple) * _WaveStrength;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz * _WaveScale;
            float t = _Time.y * _FlowSpeed;
            // Analytic derivatives rather than using wave heights as normals:
            // avoids the bright regular grid of broad crossing specular bands.
            float a = sin(p.x * .34 + p.y * .18 + t);
            float b = sin(p.x * -.21 + p.y * .49 - t * 1.27);
            float c = sin(p.x * 1.31 + p.y * .87 + t * 2.1);
            float wave = saturate(.48 + a * .07 + b * .035 + c * .012);
            float micro=sin(p.x*7.1+p.y*4.7+sin(p.y*.9)+t*2.6);
            float2 slope = float2(cos(p.x*.34+p.y*.18+t)*.018 - cos(p.x*-.21+p.y*.49-t*1.27)*.011,
                                  cos(p.x*.34+p.y*.18+t)*.009 + cos(p.x*-.21+p.y*.49-t*1.27)*.025);
            slope+=float2(micro,sin(p.x*5.3-p.y*6.7-t*3.1))*.008;
            o.Normal = normalize(float3(slope.x, slope.y, 1));

            float fresnel = pow(1 - saturate(IN.viewDir.z), 3.2);
            float crest = pow(saturate(a * .5 + b * .35 + c * .15), 8);
            float edge = min(IN.uv_MainTex.x, 1 - IN.uv_MainTex.x);
            float shoreline = 1 - smoothstep(.002, .023, edge);
            float foam = saturate(shoreline * (.20 + crest * .3));
            float3 water = lerp(_Color.rgb, _SecondaryColor.rgb, wave);
            water = lerp(water, _ReflectionColor.rgb, fresnel * .58);
            o.Albedo = lerp(water, _FoamColor.rgb, foam * .82);
            o.Emission = _ReflectionColor.rgb * (fresnel * .08 + crest * .025) +
                         _FoamColor.rgb * foam * .08;
            o.Metallic = .08;
            o.Smoothness = lerp(_Glossiness, .36, foam);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
