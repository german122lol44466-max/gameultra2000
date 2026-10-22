// Постобработка для встроенного рендера: bloom (порог + цепочка уменьшения/увеличения), тонмаппинг,
// цветокоррекция (контраст, насыщенность, тёплый/холодный оттенок) и виньетка.
Shader "Hidden/SW/PostFx"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    float _Threshold, _Knee, _Intensity;
    float _Exposure, _Contrast, _Saturation, _Vignette;
    float4 _Tint, _Lift;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    half3 Box4(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float2(-d, d).xxyy;
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25;
    }

    half4 fragPrefilter(v2f i) : SV_Target
    {
        half3 c = Box4(i.uv, 1);
        half br = max(c.r, max(c.g, c.b));
        half soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-4);
        half contrib = max(soft, br - _Threshold) / max(br, 1e-4);
        return half4(c * contrib, 1);
    }

    half4 fragDown(v2f i) : SV_Target { return half4(Box4(i.uv, 1), 1); }
    half4 fragUp(v2f i) : SV_Target { return half4(Box4(i.uv, 0.5), 1); }

    half3 ACES(half3 x)
    {
        const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    half4 fragComposite(v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb;
        c += tex2D(_BloomTex, i.uv).rgb * _Intensity;
        c *= _Exposure;
        c = ACES(c);
        c = pow(max(c, 0), 1.0 / 1.0);
        half l = dot(c, half3(0.2126, 0.7152, 0.0722));
        c = lerp(l.xxx, c, _Saturation);
        c = (c - 0.5) * _Contrast + 0.5;
        c = c * _Tint.rgb + _Lift.rgb;
        float2 d = i.uv - 0.5;
        c *= 1 - dot(d, d) * _Vignette;
        return half4(saturate(c), 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragPrefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragDown
               ENDCG }
        Pass { Blend One One
               CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragUp
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragComposite
               ENDCG }
    }
}
