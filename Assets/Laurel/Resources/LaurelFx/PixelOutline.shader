Shader "Laurel/PixelOutline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _Thickness ("Outline Thickness", Float) = 1
        _OutlineOnly ("Outline Only", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _OutlineColor;
            float _Thickness;
            float _OutlineOnly;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (c.a > 0.1) return _OutlineOnly > 0.5 ? fixed4(0, 0, 0, 0) : c;

                int r = (int)round(_Thickness);
                if (r <= 0) return fixed4(0, 0, 0, 0);

                float2 t = _MainTex_TexelSize.xy;
                float a = 0;
                for (int dx = -4; dx <= 4; dx++)
                {
                    if (abs(dx) > r) continue;
                    for (int dy = -4; dy <= 4; dy++)
                    {
                        if (abs(dy) > r) continue;
                        if (dx == 0 && dy == 0) continue;
                        if (dx * dx + dy * dy > r * r) continue;
                        a = max(a, tex2D(_MainTex, i.uv + float2(dx * t.x, dy * t.y)).a);
                    }
                }

                if (a > 0.1) return _OutlineColor;
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
