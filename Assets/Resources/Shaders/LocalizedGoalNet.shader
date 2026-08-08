Shader "WaterPolo/LocalizedGoalNet"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
        [HideInInspector] _ImpactLocal("Impact Local", Vector) = (0,0,0,0)
        [HideInInspector] _DeformDirectionUV("Deform Direction UV", Vector) = (0,0,0,0)
        [HideInInspector] _DeformAmount("Deform Amount", Float) = 0
        [HideInInspector] _DeformRadius("Deform Radius", Float) = 1
        [HideInInspector] _WavePhase("Wave Phase", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NetVertex
            #pragma fragment NetFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color : COLOR;
                // TEXCOORD3 is occupied by normalWS in DEBUG_DISPLAY variants.
                float2 positionOS : TEXCOORD4;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _ImpactLocal;
                float4 _DeformDirectionUV;
                float _DeformAmount;
                float _DeformRadius;
                float _WavePhase;
            CBUFFER_END

            Varyings NetVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonLitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                output.positionOS = input.positionOS.xy;
                return output;
            }

            // The two existing goal textures combine yellow netting with red/white metalwork.
            // Select only the warm yellow/orange pixels: enough green to reject the red posts,
            // and a clear green-over-blue lead to reject white/silver frame highlights.
            half NetPixelMask(half4 sampleColor)
            {
                half greenShare = sampleColor.g / max(sampleColor.r, 0.001h);
                return step(0.03h, sampleColor.a) *
                       step(0.20h, sampleColor.r) *
                       step(0.08h, sampleColor.g) *
                       step(0.20h, greenShare) *
                       step(0.035h, sampleColor.g - sampleColor.b);
            }

            half4 LocalizedNetSample(float2 uv, float2 positionOS)
            {
                // This shader is used only by a transparent overlay. The untouched original goal
                // sprite remains beneath it, so returning transparent can never recolor the goal.
                if (abs(_DeformAmount) < 0.0001f) return half4(0, 0, 0, 0);

                float distanceFromHit = distance(positionOS, _ImpactLocal.xy);
                float falloff = saturate(1.0f - distanceFromHit / max(_DeformRadius, 0.001f));
                falloff *= falloff;

                // Distance phase lag makes nearby squares follow the impact instead of moving as
                // one rigid patch. Three filtered samples form a short elastic bridge from the
                // resting net to its displaced position, making the stretch readable at match zoom.
                float localWave = 1.0f + 0.16f * sin(_WavePhase - distanceFromHit * 2.15f);
                float2 shift = _DeformDirectionUV.xy * (_DeformAmount * falloff * localWave);
                half4 nearSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - shift * 0.38f);
                half4 midSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - shift * 0.70f);
                half4 farSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - shift);

                half nearWeight = NetPixelMask(nearSample) * 0.24h;
                half midWeight = NetPixelMask(midSample) * 0.42h;
                half farWeight = NetPixelMask(farSample) * 0.82h;
                half weightSum = nearWeight + midWeight + farWeight;
                if (weightSum < 0.001h) return half4(0, 0, 0, 0);

                half activation = saturate(abs(_DeformAmount) * 45.0h) * (half)falloff;
                half3 netColor = (nearSample.rgb * nearWeight + midSample.rgb * midWeight +
                                  farSample.rgb * farWeight) / weightSum;
                half netAlpha = max(nearSample.a * nearWeight,
                                    max(midSample.a * midWeight, farSample.a * farWeight));
                return half4(netColor, netAlpha * activation);
            }

            half4 NetFragment(Varyings input) : SV_Target
            {
                half4 main = input.color * LocalizedNetSample(input.uv, input.positionOS);
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

                #if defined(DEBUG_DISPLAY)
                    SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                    surfaceData.normalWS = input.normalWS;
                #endif

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // The match uses the URP 2D renderer, so this presentation-only material needs only
        // the Universal2D pass. The source Sprite-Lit shader's other passes are unnamed and
        // therefore cannot safely be referenced with UsePass.
    }
}
