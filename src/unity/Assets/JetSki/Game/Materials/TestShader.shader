// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Snow Particle" {
	Properties{
	_MainTex("Particle Texture", 2D) = "white" {}
	_TintColor("Tint Color", Color) = (0.5,0.5,0.5,0.5)
	_InvFade("Soft Particles Factor", Range(0.01,3.0)) = 1.0
	}

		Category{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
		Blend SrcAlpha One
		Lighting Off
		ColorMask RGB
		Cull Off ZWrite Off

		SubShader {
		Pass {

		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc"
		#pragma multi_compile_particles
		#pragma multi_compile_fog

		#include "UnityCG.cginc"

		sampler2D _MainTex;
		fixed4 _TintColor;
		struct VertexInput {
		float4 vertex : POSITION;       //local vertex position
		float2 texcoord0 : TEXCOORD0;   //uv coordinates
		fixed4 color : COLOR;
		 };

		 struct VertexOutput {
		 float4 vertex : SV_POSITION;              //screen clip space position and depth
		float2 uv0 : TEXCOORD0;                //uv coordinates
		float4 color : TEXCOORD2;
		 UNITY_FOG_COORDS(3)                    //this initializes the unity fog
		 #ifdef SOFTPARTICLES_ON
		float4 projPos : TEXCOORD4;
		 #endif
		 };

		 VertexOutput vert(VertexInput v) {
		 VertexOutput o = (VertexOutput)0;
		 o.uv0 = v.texcoord0;
		 o.vertex = UnityObjectToClipPos(v.vertex);
		 o.color = v.color;
		 #ifdef SOFTPARTICLES_ON
		o.projPos = ComputeScreenPos(o.vertex);
		COMPUTE_EYEDEPTH(o.projPos.z);
		#endif
		 //UNITY_TRANSFER_FOG(o,o.pos);
		 return o;
		 }

		 sampler2D_float _CameraDepthTexture;
		 float _InvFade;

		 fixed4 frag(VertexOutput i) : SV_Target
		 {
		 #ifdef SOFTPARTICLES_ON
		float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
		float projZ = i.projPos.z;
		float fade = saturate(_InvFade * (sceneDepth - projZ));
		i.color.a *= fade;
		#endif

		 fixed4 diffuseColor = 2.0f * i.color * _TintColor * tex2D(_MainTex, i.uv0);
		 UNITY_APPLY_FOG_COLOR(i.fogCoord, diffuseColor, fixed4(0,0,0,0)); // fog towards black due to our blend mode
		 return diffuseColor;
		 }
		 ENDCG
		 }
		 }
	}
}