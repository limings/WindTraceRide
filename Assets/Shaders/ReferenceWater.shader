Shader "WindTrace/Reference Water"
{
    Properties
    {
        _Color("Deep water",Color)=(.015,.12,.16,1)
        _SecondaryColor("Shallows",Color)=(.08,.27,.24,1)
        _ReflectionColor("Sky fallback",Color)=(.22,.40,.48,1)
        _PlanarReflection("Live reflection",2D)="black" {}
        _HasReflection("Live reflection ready",Float)=0
        _WaveStrength("Ripples",Float)=.035
        _WaveScale("Wave scale",Float)=1
        _FlowSpeed("Flow",Float)=.5
        _Glossiness("Smoothness",Float)=.45
        _FoamColor("Shoreline",Color)=(.5,.66,.62,1)
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _PlanarReflection;
            fixed4 _Color,_SecondaryColor,_ReflectionColor,_FoamColor;
            float _WaveStrength,_WaveScale,_FlowSpeed,_Glossiness,_HasReflection;
            struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct v2f{float4 pos:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float4 screen:TEXCOORD2;UNITY_FOG_COORDS(3)};
            v2f vert(appdata v)
            {
                v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.uv=v.uv;o.screen=ComputeScreenPos(o.pos);UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.world.xz*_WaveScale;
                float t=_Time.y*_FlowSpeed;
                float phase=p.x*.42+p.y*.23+t;
                float micro=sin(p.x*8.1+p.y*6.3+t*2.7+sin(p.y*.35));
                float2 ripple=float2(cos(phase)*.03+micro*.012,sin(p.x*.27-p.y*.63-t*1.3)*.036+sin(p.x*6.1-p.y*9.7-t*3)*.008);
                float3 n=normalize(float3(ripple.x,1,ripple.y));
                float3 view=normalize(_WorldSpaceCameraPos-i.world);
                float fresnel=.08+.72*pow(1-saturate(dot(n,view)),3);
                float2 uv=i.screen.xy/i.screen.w+ripple*.024;
                float3 reflected=lerp(_ReflectionColor.rgb,tex2D(_PlanarReflection,uv).rgb,_HasReflection);
                float edge=min(i.uv.x,1-i.uv.x);
                float shallow=1-smoothstep(.003,.075,edge);
                float3 base=lerp(_Color.rgb,_SecondaryColor.rgb,shallow*.66+.08*sin(phase));
                float3 halfDir=normalize(view+normalize(_WorldSpaceLightPos0.xyz));
                float glint=pow(saturate(dot(n,halfDir)),128)*.15;
                float foam=shallow*pow(saturate(micro*.5+.5),8)*.08;
                fixed4 color=fixed4(lerp(base,reflected,fresnel)+_LightColor0.rgb*glint+_FoamColor.rgb*foam,.96);
                UNITY_APPLY_FOG(i.fogCoord,color);return color;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
