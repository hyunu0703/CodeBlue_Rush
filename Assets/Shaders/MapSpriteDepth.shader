Shader "CodeBlueRush/MapSpriteDepth"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Contrast ("Contrast", Range(1,1.25)) = 1.10
        _Saturation ("Saturation", Range(1,1.2)) = 1.06
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Contrast;
                half _Saturation;
            CBUFFER_END
            struct Input { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Output Vert(Input v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                SetUpSpriteInstanceProperties();
                Output o; o.positionCS=TransformObjectToHClip(UnityFlipSprite(v.positionOS.xyz,unity_SpriteProps.xy));
                o.uv=v.uv; o.color=v.color*_Color*unity_SpriteColor; return o;
            }
            half4 Frag(Output v):SV_Target
            {
                half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*v.color;
                half3 rgb=c.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    rgb=LinearToSRGB(rgb);
                #endif
                half luminance=dot(rgb,half3(.2126,.7152,.0722));
                rgb=lerp(luminance.xxx,rgb,_Saturation);
                // Preserve white roofs while separating the existing baked light/shadow faces.
                rgb=saturate((rgb-.5h)*_Contrast+.5h);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    rgb=SRGBToLinear(rgb);
                #endif
                return half4(rgb,c.a);
            }
            ENDHLSL
        }
    }
}
