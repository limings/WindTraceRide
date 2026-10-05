Shader "WindTrace/Stylized Terrain"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.2,0.5,0.1,1)
        _SecondaryColor ("Variation", Color) = (0.4,0.7,0.2,1)
        _NoiseScale ("Noise Scale", Range(0.02,3)) = 0.3
        _Glossiness ("Smoothness", Range(0,1)) = 0.15
        _MainTex ("Ground Detail", 2D) = "white" {}
        _TextureScale ("Ground Detail Repeats Per Metre", Range(0.01,2)) = 0.35
        _TextureStrength ("Ground Detail Blend", Range(0,1)) = 0
        _AmbientLift ("Ambient Lift", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 180
        // Imported geodata can carry a mirrored transform after OBJ's coordinate
        // conversion. Render both faces so Android GLES/Vulkan does not discard
        // the terrain while desktop review still shows it.
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color, _SecondaryColor;
        sampler2D _MainTex;
        half _NoiseScale, _Glossiness, _TextureScale, _TextureStrength, _AmbientLift;
        struct Input { float3 worldPos; };
        float hash21(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
        float valueNoise(float2 p)
        {
            float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p=IN.worldPos.xz*_NoiseScale;
            float n=valueNoise(p)*.62+valueNoise(p*3.1)*.27+valueNoise(p*8.7)*.11;
            float track=pow(saturate(valueNoise(p*.42)),5)*.18;
            float3 proceduralColor=lerp(_Color.rgb,_SecondaryColor.rgb,saturate(n+track));
            float3 textureColor=tex2D(_MainTex,IN.worldPos.xz*_TextureScale).rgb;
            o.Albedo=lerp(proceduralColor,textureColor,_TextureStrength);
            o.Emission=o.Albedo*_AmbientLift;
            o.Smoothness=_Glossiness;
            o.Metallic=0;
            o.Occlusion=.82+n*.18;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
