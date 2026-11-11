Shader "SmartGeoAR/HandDepth"
{
    Properties
    {
        [HideInInspector] _DebugColorMask ("Debug color mask", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry-20" }
        Cull Off
        ZWrite On
        ZTest LEqual
        ColorMask 0

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _SmartGeoAR_HandEyeOffset;

        struct Attributes
        {
            float4 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            // The rear-camera image is monocular in the viewer. Keep its
            // hand silhouette on the same image rays in both eye renders.
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(world + _SmartGeoAR_HandEyeOffset.xyz);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            return half4(0, 1, 1, 1);
        }
        ENDHLSL

        Pass
        {
            Name "HandDepth"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask [_DebugColorMask]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    FallBack Off
}
