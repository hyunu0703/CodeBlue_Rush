Shader "CodeBlueRush/MapTerrain"
{
    Properties
    {
        _MainTex ("Surface", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WorldUV ("World Surface UV", Float) = 1
        _CellSize ("Cell Size", Float) = 20
        _MapRowX ("Map Row X", Vector) = (1,0,0,0)
        _MapRowY ("Map Row Y", Vector) = (0,1,0,0)
        _Join ("Shore Edge Blend", Float) = 0
        _Water ("Sea Depth Blend", Float) = 0
        _NearColor ("Shallow Water Tint", Color) = (1,1,1,1)
        _HarborColor ("Harbor Water Tint", Color) = (0.88,0.82,0.92,1)
        _ShoreY ("Shore Position", Float) = -14
        _HarborX ("Harbor Position", Float) = 40
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
                float4 _MainTex_TexelSize;
                half4 _Color;
                float _WorldUV;
                float _CellSize;
                float4 _MapRowX;
                float4 _MapRowY;
                float _Join;
                float _Water;
                half4 _NearColor;
                half4 _HarborColor;
                float _ShoreY;
                float _HarborX;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varying { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float2 map : TEXCOORD1; };
            Varying Vert(Input v)
            {
                Varying o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                float2 map = float2(dot(_MapRowX, float4(world,1)), dot(_MapRowY, float4(world,1)));
                o.uv = lerp(v.uv, map / _CellSize + 0.5, _WorldUV);
                o.map = map;
                o.color = v.color;
                return o;
            }
            half4 Frag(Varying v) : SV_Target
            {
                float2 uv = lerp(v.uv, 1 - abs(frac(v.uv * 0.5) * 2 - 1), _WorldUV);
                uv = clamp(uv, _MainTex_TexelSize.xy * 0.5, 1 - _MainTex_TexelSize.xy * 0.5);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_Join > 0)
                {
                    half4 edge = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(uv.x, _MainTex_TexelSize.y * 0.5)) + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(uv.x, 1 - _MainTex_TexelSize.y * 0.5))) * 0.5;
                    color = lerp(color, edge, 1 - smoothstep(0, _Join, min(v.uv.y, 1 - v.uv.y)));
                }
                half4 tint = _Color;
                if (_Water > 0)
                {
                    tint = lerp(_NearColor, tint, saturate((_ShoreY - v.map.y) / (_CellSize * 0.5)));
                    float harbor = (1 - smoothstep(0, _CellSize * 1.5, abs(v.map.x - _HarborX))) * (1 - smoothstep(_CellSize, _CellSize * 2.5, _ShoreY - v.map.y));
                    tint.rgb *= lerp(half3(1,1,1), _HarborColor.rgb, harbor);
                }
                return color * tint * v.color;
            }
            ENDHLSL
        }
    }
}
