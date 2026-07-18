Shader "WaterPolo/Player Palette Swap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _CapTint ("Cap Tint", Color) = (0.85, 0.05, 0.1, 1)
        _SwimwearTint ("Swimwear Tint", Color) = (1, 1, 1, 1)
        _ColorKeyTolerance ("Marker Chroma Threshold", Range(0.02, 0.5)) = 0.18
        [MaterialToggle] _ZWrite ("ZWrite", Float) = 0

        // SpriteRenderer compatibility properties.
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex PaletteVertex
            #pragma fragment PaletteFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CapTint;
                half4 _SwimwearTint;
                half _ColorKeyTolerance;
            CBUFFER_END

            Varyings PaletteVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings output = CommonUnlitVertex(input);
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half ChromaKeyMask(half firstMarkerChannel, half secondMarkerChannel,
                               half oppositeChannel)
            {
                // The source art shades its marker regions: idle_floating contains no exact
                // #FF00FF pixels at all. Measuring chroma dominance follows those dark/light
                // marker shades and their anti-aliased edges without a broad RGB-distance sphere
                // that can accidentally reach skin. Magenta is min(R,B)-G; cyan is min(G,B)-R.
                half markerStrength = min(firstMarkerChannel, secondMarkerChannel);
                half dominance = markerStrength - oppositeChannel;
                half feather = max(0.035h, _ColorKeyTolerance * 0.35h);
                half chromaMask = smoothstep(_ColorKeyTolerance - feather,
                                             _ColorKeyTolerance + feather, dominance);
                half visibleColorGate = smoothstep(0.16h, 0.42h, markerStrength);
                return chromaMask * visibleColorGate;
            }

            half4 PaletteFragment(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half capMask = ChromaKeyMask(source.r, source.b, source.g);
                half swimwearMask = ChromaKeyMask(source.g, source.b, source.r);
                half luminance = dot(source.rgb, half3(0.2126h, 0.7152h, 0.0722h));

                half3 recolored = lerp(source.rgb, luminance * _CapTint.rgb, capMask);
                recolored = lerp(recolored, luminance * _SwimwearTint.rgb, swimwearMask);

                return half4(recolored * input.color.rgb, source.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
