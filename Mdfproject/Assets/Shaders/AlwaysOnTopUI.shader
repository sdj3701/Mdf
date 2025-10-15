// 셰이더의 이름
Shader "Custom/AlwaysOnTopUI_Final"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Material Tint Color", Color) = (1,1,1,1) // 머티리얼 자체의 틴트 색상
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            
            // --- [추가] ---
            // UI 시스템은 Cull Off를 사용하는 것이 일반적입니다.
            // (이미지가 뒤집히거나 스케일이 음수일 때 사라지는 현상 방지)
            Cull Off 

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // --- [추가] ---
                // Image 컴포넌트의 Color 값을 받기 위한 버텍스 컬러 입력
                fixed4 color : COLOR; 
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                // --- [추가] ---
                // 버텍스 셰이더에서 프래그먼트 셰이더로 색상 정보를 전달하기 위한 변수
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // --- [추가] ---
                // 입력받은 버텍스 컬러(v.color)를 출력 구조체(o.color)로 그대로 전달
                o.color = v.color;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1. 텍스처에서 기본 색상을 샘플링
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // --- [수정] ---
                // 2. 텍스처 색상에 Image 컴포넌트의 색상(버텍스 컬러)을 곱함
                col *= i.color;

                // 3. 마지막으로 머티리얼 자체의 틴트 색상을 곱함
                col *= _Color;

                // 알파 값이 거의 없는 픽셀은 그리지 않음 (최적화)
                clip(col.a - 0.01);

                return col;
            }
            ENDCG
        }
    }
}