Shader "CodeBlueRush/MapSurfaceDepth"
{
    Properties
    {
        _MainTex ("Surface", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SurfaceKind ("0 Lawn / 1 Water / 2 Road / 3 Paving / 4 Timber", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _SurfaceKind;
            CBUFFER_END
            struct Input { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Output { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float2 world:TEXCOORD1; };
            Output Vert(Input v)
            {
                Output o; float3 world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(world); o.world=world.xy; o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            half Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            half4 Frag(Output v):SV_Target
            {
                half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*v.color;
                float2 map=float2(v.world.x*8+470.5,836-v.world.y*8);
                if(_SurfaceKind<.5)
                {
                    float2 cell=map/24; float2 f=frac(cell);
                    half a=Hash(floor(cell)); half b=Hash(floor(cell)+1);
                    half facet=lerp(a,b,smoothstep(.2,.85,(f.x+f.y)*.5));
                    c.rgb*=.95h+facet*.10h;
                }
                else if(_SurfaceKind<1.5)
                {
                    // Static low-poly water: no render textures, animation or extra texture fetches.
                    float2 tile=float2(map.x+map.y,map.y-map.x)/13;
                    float2 f=frac(tile);
                    half edge=(1-smoothstep(.012,.10,min(min(f.x,1-f.x),min(f.y,1-f.y))));
                    half facet=Hash(floor(tile));
                    half glint=smoothstep(.72,.98,facet)*(1-smoothstep(.03,.11,abs(f.x-.50)))*.04;
                    c.rgb*=.96h+facet*.07h+edge*.035h+glint;
                    half sea=smoothstep(1460,1645,map.y);
                    c.rgb*=lerp(half3(1.02,1.08,1.05),half3(.86,.94,1.04),sea);
                }
                else if(_SurfaceKind<2.5)
                {
                    c.rgb*=.97h+Hash(floor(map/4))*.04h;
                }
                else if(_SurfaceKind<3.5)
                {
                    c.rgb*=.97h+Hash(floor(map/8))*.05h;
                }
                else
                {
                    half plank=step(.94,frac(map.x/6));
                    c.rgb*=.94h+Hash(floor(map/6))*.10h-plank*.12h;
                }
                return c;
            }
            ENDHLSL
        }
    }
}
