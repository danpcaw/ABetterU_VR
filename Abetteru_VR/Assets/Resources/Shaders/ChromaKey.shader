// Makes a green-screen video see-through, so a person filmed on green looks like they are standing in the room.
// Same simple unlit style as Unity's own Sprites/Default shader (which this project already uses on the headset).
// A pixel that is much greener than it is red or blue becomes transparent, with a soft edge, and leftover green on the edges is removed.
Shader "ABetterU/ChromaKey"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _Cut ("How green before it turns see-through", Range(0, 1)) = 0.14
        _Soft ("Softness of the edge", Range(0.01, 1)) = 0.16
        _Despill ("Remove green tint left on edges", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cut;
            float _Soft;
            float _Despill;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float green = c.g - max(c.r, c.b);                         // how much greener than red or blue
                float alpha = 1.0 - smoothstep(_Cut, _Cut + _Soft, green);
                float spill = max(0.0, c.g - max(c.r, c.b));
                c.g -= spill * _Despill;                                   // take the green tint off hair and edges
                return fixed4(c.rgb, alpha);
            }
            ENDCG
        }
    }
}
