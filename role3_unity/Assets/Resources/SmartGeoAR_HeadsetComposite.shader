Shader "SmartGeoAR/HeadsetComposite"
{
    Properties
    {
        _LeftEye ("Left virtual view", 2D) = "black" {}
        _RightEye ("Right virtual view", 2D) = "black" {}
        _CameraTex ("Rear camera", 2D) = "black" {}
        _EyeProjection ("Eye projection", Vector) = (1,1,0,0)
        _CameraProjection ("Camera projection", Vector) = (1,1,0,0)
        _Distortion ("Lens fit", Vector) = (0,0,0,0)
        _LensCenterShift ("Lens center shift", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LeftEye); SAMPLER(sampler_LeftEye);
            TEXTURE2D(_RightEye); SAMPLER(sampler_RightEye);
            TEXTURE2D(_CameraTex); SAMPLER(sampler_CameraTex);
            float4 _EyeProjection;
            float4 _CameraProjection;
            float4 _Distortion;
            float _LensCenterShift;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                bool rightEye = input.uv.x >= 0.5;
                float2 uv = float2(input.uv.x * 2.0 - (rightEye ? 1.0 : 0.0), input.uv.y);
                uv.x -= (rightEye ? -1.0 : 1.0) * _LensCenterShift;
                float2 radialPoint = (uv - 0.5) * 2.0;
                float radius = dot(radialPoint, radialPoint);
                radialPoint *= 1.0 + _Distortion.x * radius + _Distortion.y * radius * radius;
                uv = radialPoint * 0.5 + 0.5;
                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(0, 0, 0, 1);

                half4 eye = rightEye
                    ? SAMPLE_TEXTURE2D(_RightEye, sampler_RightEye, uv)
                    : SAMPLE_TEXTURE2D(_LeftEye, sampler_LeftEye, uv);
                float2 ray = (uv * 2.0 - 1.0 + _EyeProjection.zw) / _EyeProjection.xy;
                float2 cameraUv = (ray * _CameraProjection.xy - _CameraProjection.zw + 1.0) * 0.5;
                half3 camera = 0;
                if (all(cameraUv >= 0.0) && all(cameraUv <= 1.0))
                    camera = SAMPLE_TEXTURE2D(_CameraTex, sampler_CameraTex, cameraUv).rgb;

                // Both eyes share one camera image. Eye geometry has real
                // stereo disparity; the physical room has no binocular depth.
                return half4(eye.rgb + (1.0 - eye.a) * camera, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
