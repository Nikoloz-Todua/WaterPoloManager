Shader "UI/Crest Mask Tint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Packed RGBA Mask", 2D) = "white" {}
        _PrimaryColor ("Primary", Color) = (0.12,0.56,1,1)
        _SecondaryColor ("Secondary", Color) = (1,1,1,1)
        _TertiaryColor ("Tertiary", Color) = (1,0.65,0.05,1)
        _OutlineColor ("Fixed Outline", Color) = (0.015,0.02,0.03,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CrestTint"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _PrimaryColor;
            fixed4 _SecondaryColor;
            fixed4 _TertiaryColor;
            fixed4 _OutlineColor;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 mask = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                fixed total = mask.r + mask.g + mask.b + mask.a;
                fixed alpha = saturate(total) * input.color.a;
                fixed3 rgb = (_PrimaryColor.rgb * mask.r +
                              _SecondaryColor.rgb * mask.g +
                              _TertiaryColor.rgb * mask.b +
                              _OutlineColor.rgb * mask.a) / max(total, 0.0001);
                fixed4 color = fixed4(rgb * input.color.rgb, alpha);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
