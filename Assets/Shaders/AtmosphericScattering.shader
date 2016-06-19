//  Copyright(c) 2016, Michal Skalsky
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without modification,
//  are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors
//     may be used to endorse or promote products derived from this software without
//     specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
//  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.IN NO EVENT
//  SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
//  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.



Shader "Sandbox/AtmosphericScattering"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_ZTest ("ZTest", Float) = 0
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		CGINCLUDE

		#define SHADOWS_NATIVE
        #define PI 3.14159265359

		#include "UnityCG.cginc"
		#include "UnityDeferredLibrary.cginc"

		sampler3D _NoiseTexture;
		sampler2D _DitherTexture;

		//sampler2D _ShadowMapTexture;

		struct appdata
		{
			float4 vertex : POSITION;
		};

		float4x4 _WorldViewProj;
		float4x4 _WorldView;
		float4x4 _MyLightMatrix0;
		float4x4 _MyWorld2Shadow;

		float3 _CameraForward;

		int _SampleCount;

        float _AtmosphereHeight;
        float _PlanetRadius;
        float2 _DensityScaleHeight;

        float3 _ScatteringR;
        float3 _ScatteringM;
        float3 _ExtinctionR;
        float3 _ExtinctionM;

		float4 _IncomingLight;
		float _MieG;
		float _DistanceMultiplier;

        sampler2D _ParticleDensityLUT;
        sampler2D _RandomVectors;

		struct v2f
		{
			float4 pos : SV_POSITION;
			float4 uv : TEXCOORD0;
			float3 wpos : TEXCOORD1;
		};

		v2f vert(appdata v)
		{
			v2f o;
			o.pos = mul(_WorldViewProj, v.vertex);
			o.uv = ComputeScreenPos(o.pos);
			o.wpos = mul(_Object2World, v.vertex);
			return o;
		}

		struct ScatteringOutput
		{
			float4 inscattering : SV_Target0;
			float4 extinction : SV_Target1;
		};

		//-----------------------------------------------------------------------------------------
		// GetCascadeWeights_SplitSpheres
		//-----------------------------------------------------------------------------------------
		inline fixed4 GetCascadeWeights_SplitSpheres(float3 wpos)
		{
			float3 fromCenter0 = wpos.xyz - unity_ShadowSplitSpheres[0].xyz;
			float3 fromCenter1 = wpos.xyz - unity_ShadowSplitSpheres[1].xyz;
			float3 fromCenter2 = wpos.xyz - unity_ShadowSplitSpheres[2].xyz;
			float3 fromCenter3 = wpos.xyz - unity_ShadowSplitSpheres[3].xyz;
			float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
#if SHADER_TARGET > 30
			fixed4 weights = float4(distances2 < unity_ShadowSplitSqRadii);
			weights.yzw = saturate(weights.yzw - weights.xyz);
#else
			fixed4 weights = float4(distances2 >= unity_ShadowSplitSqRadii);
#endif
			return weights;
		}

		//-----------------------------------------------------------------------------------------
		// GetCascadeShadowCoord
		//-----------------------------------------------------------------------------------------
		inline float4 GetCascadeShadowCoord(float4 wpos, fixed4 cascadeWeights)
		{
#if SHADER_TARGET > 30
			return mul(unity_World2Shadow[(int)dot(cascadeWeights, float4(1, 1, 1, 1))], wpos);
#else
			float3 sc0 = mul(unity_World2Shadow[0], wpos).xyz;
			float3 sc1 = mul(unity_World2Shadow[1], wpos).xyz;
			float3 sc2 = mul(unity_World2Shadow[2], wpos).xyz;
			float3 sc3 = mul(unity_World2Shadow[3], wpos).xyz;
			return float4(sc0 * cascadeWeights[0] + sc1 * cascadeWeights[1] + sc2 * cascadeWeights[2] + sc3 * cascadeWeights[3], 1);
#endif
		}

		//-----------------------------------------------------------------------------------------
		// UnityDeferredComputeShadow2
		//-----------------------------------------------------------------------------------------
		half UnityDeferredComputeShadow2(float3 vec, float fadeDist, float2 uv)
		{
#if defined(SHADOWS_DEPTH) || defined(SHADOWS_SCREEN) || defined(SHADOWS_CUBE)
			float fade = fadeDist * _LightShadowData.z + _LightShadowData.w;
			fade = saturate(fade);
#endif

#if defined(SPOT)
#if defined(SHADOWS_DEPTH)
			float4 shadowCoord = mul(_MyWorld2Shadow, float4(vec, 1));
				return saturate(UnitySampleShadowmap(shadowCoord) + fade);
#endif //SHADOWS_DEPTH
#endif

#if defined (DIRECTIONAL) || defined (DIRECTIONAL_COOKIE)
#if defined(SHADOWS_SCREEN)
			return saturate(tex2D(_ShadowMapTexture, uv).r + fade);
#endif
#endif //DIRECTIONAL || DIRECTIONAL_COOKIE

#if defined (POINT) || defined (POINT_COOKIE)
#if defined(SHADOWS_CUBE)
			return UnitySampleShadowmap(vec);
#endif //SHADOWS_CUBE
#endif

			return 1.0;
		}

		UNITY_DECLARE_SHADOWMAP(_CascadeShadowMapTexture);

        //-----------------------------------------------------------------------------------------
        // RaySphereIntersection
        //-----------------------------------------------------------------------------------------
        float2 RaySphereIntersection(float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius)
        {
            rayOrigin -= sphereCenter;
            float a = dot(rayDir, rayDir);
            float b = 2.0 * dot(rayOrigin, rayDir);
            float c = dot(rayOrigin, rayOrigin) - (sphereRadius * sphereRadius);
            float d = b * b - 4 * a * c;
            if (d < 0)
            {
                return -1;
            }
            else
            {
                d = sqrt(d);
                return float2(-b - d, -b + d) / (2 * a); // A must be positive here!!
            }

            // optimized version, rayDir must be normalized
            /*float b = dot(rayOrigin, rayDir);
            float c = dot(rayOrigin, rayOrigin) - (sphereRadius * sphereRadius);
            float d = b * b - c;

            if (d < 0)
            {
                return -1;
            }
            else
            {
                d = sqrt(d);
                return float2(-b - d, -b + d);
            }*/
        }

        float GetVisibility(float3 wpos)
        {
            return 1;
        }

        float Sun(float cosAngle)
        {
            float g = 0.99;
            float g2 = g * g;

            float sun = pow(1 - g, 2.0) / (4 * PI * pow(1.0 + g2 - 2.0*g*cosAngle, 1.5));
			return sun * 0.001;// 5;
        }

        void ApplyPhaseFunction(inout float3 scatterR, inout float3 scatterM, float cosAngle)
        {
            // r
            float phase = (3.0 / (16.0 * PI)) * (1 + (cosAngle * cosAngle));
            scatterR *= phase;

            // m
            float g = _MieG;
            float g2 = g * g;
            phase = (1.0 / (4.0 * PI)) * ((3.0 * (1.0 - g2)) / (2.0 * (2.0 + g2))) * ((1 + cosAngle * cosAngle) / (pow((1 + g2 - 2 * g*cosAngle), 3.0 / 2.0)));
			scatterM *= phase;
        }

		float3 RenderSun(in float3 scatterM, float cosAngle)
		{
			return scatterM * Sun(cosAngle);
		}

		void GetAtmosphereDensity(float3 position, float3 planetCenter, float3 lightDir, out float2 localDensity, out float2 densityToAtmTop)
		{
			float height = length(position - planetCenter) - _PlanetRadius;
			localDensity = exp(-height.xx / _DensityScaleHeight.xy);

			float cosAngle = dot(normalize(position - planetCenter), -lightDir.xyz);

			densityToAtmTop = tex2D(_ParticleDensityLUT, float2(cosAngle * 0.5 + 0.5, (height / _AtmosphereHeight))).rg;
		}

		void ComputeLocalInscattering(float2 localDensity, float2 densityPA, float2 densityCP, out float3 localInscatterR, out float3 localInscatterM)
		{
			float2 densityCPA = densityCP + densityPA;

			float3 Tr = densityCPA.x * _ExtinctionR;
			float3 Tm = densityCPA.y * _ExtinctionM;

			float3 extinction = exp(-(Tr + Tm));

			localInscatterR = localDensity.x * extinction;
            localInscatterM = localDensity.y * extinction;
		}

        float3 ComputeOpticalDepth(float2 density)
        {
            float3 Tr = density.x * _ExtinctionR;
            float3 Tm = density.y * _ExtinctionM;

            float3 extinction = exp(-(Tr + Tm));

            return _IncomingLight.xyz * extinction;
        }

        /*float4 LightScatter_MidPoint(float3 rayStart, float3 rayDir, float rayLength, float3 planetCenter)
        {
            float3 step = rayDir * (rayLength / _SampleCount);
            float stepSize = length(step);

            float2 densityCP = 0;
            float3 scatterR = 0;
            float3 scatterM = 0;

            // P - current integration point
            // C - camera position
            // A - top of the atmosphere
            [loop]
            for (float s = 0.5; s < _SampleCount; s += 1)
            {
                float3 p = rayStart + step * s;

				float2 localDensity;
				float2 densityPA;

				float visibility = GetVisibility(p);

				GetAtmosphereDensity(p, planetCenter, _LightDir.xyz, localDensity, densityPA);
                densityCP += localDensity * stepSize;

				float3 localInscatterR, localInscatterM;
				ComputeLocalInscattering(localDensity, densityPA, densityCP, localInscatterR, localInscatterM);

				localInscatterR *= visibility;
				localInscatterM *= visibility;

                scatterR += localInscatterR * stepSize;
                scatterM += localInscatterM * stepSize;
            }

            // phase function
            ApplyPhaseFunction(scatterR, scatterM, dot(rayDir, -_LightDir.xyz));
            float3 lightInscatter = (scatterR * _ScatteringR + scatterM * _ScatteringM) * _IncomingLight.xyz;
            float3 lightExtinction = exp(-(densityCP.x * _ExtinctionR + densityCP.y * _ExtinctionM));

            return float4(lightInscatter, 1);
        }*/
        
		ScatteringOutput LightScatter_TrapezoidIntegration(float3 rayStart, float3 rayDir, float rayLength, float3 planetCenter, float distanceMultiplier, float sunMultiplier, float3 lightDir)
		{
			float3 step = rayDir * (rayLength / _SampleCount);
			float stepSize = length(step) * distanceMultiplier;

			float2 densityCP = 0;
			float3 scatterR = 0;
			float3 scatterM = 0;

			float2 localDensity;
			float2 densityPA;

			float2 prevLocalDensity;
			float3 prevLocalInscatterR, prevLocalInscatterM;
			GetAtmosphereDensity(rayStart, planetCenter, lightDir, prevLocalDensity, densityPA);
			ComputeLocalInscattering(prevLocalDensity, densityPA, densityCP, prevLocalInscatterR, prevLocalInscatterM);

			// P - current integration point
			// C - camera position
			// A - top of the atmosphere
			[loop]
			for (float s = 1.0; s < _SampleCount; s += 1)
			{
				float3 p = rayStart + step * s;

				float visibility = GetVisibility(p);

				GetAtmosphereDensity(p, planetCenter, lightDir, localDensity, densityPA);
				densityCP += (localDensity + prevLocalDensity) * (stepSize / 2.0);

				prevLocalDensity = localDensity;

				float3 localInscatterR, localInscatterM;
				ComputeLocalInscattering(localDensity, densityPA, densityCP, localInscatterR, localInscatterM);

				localInscatterR *= visibility;
				localInscatterM *= visibility;

				scatterR += (localInscatterR + prevLocalInscatterR) * (stepSize / 2.0);
				scatterM += (localInscatterM + prevLocalInscatterM) * (stepSize / 2.0);

				prevLocalInscatterR = localInscatterR;
				prevLocalInscatterM = localInscatterM;
			}

			float3 m = scatterM;
			// phase function
			ApplyPhaseFunction(scatterR, scatterM, dot(rayDir, -lightDir.xyz));
			//scatterM = 0;
			float3 lightInscatter = (scatterR * _ScatteringR + scatterM * _ScatteringM) * _IncomingLight.xyz;
			lightInscatter += RenderSun(m, dot(rayDir, -lightDir.xyz)) * sunMultiplier;
			float3 lightExtinction = exp(-(densityCP.x * _ExtinctionR + densityCP.y * _ExtinctionM));

			/*float sun = dot(rayDir, -_LightDir.xyz);
			sun = saturate(sun-0.9998);
			sun = saturate(sun * 10000) * _LightColor;*/

			ScatteringOutput output;
			output.inscattering = float4(lightInscatter, 1);
			output.extinction = float4(lightExtinction, 0);

			return output;
		}

		ENDCG
            
		// pass 0 - precompute particle density
		Pass
		{
			ZTest Off
			Cull Off
			ZWrite Off
			Blend Off

			CGPROGRAM

            #pragma vertex vertQuad
            #pragma fragment particleDensityLUT
            #pragma target 4.0

            #define UNITY_HDR_ON

            #pragma shader_feature HEIGHT_FOG
            #pragma shader_feature NOISE
            #pragma shader_feature SHADOWS_DEPTH
            #pragma shader_feature SHADOWS_NATIVE
            #pragma shader_feature DIRECTIONAL_COOKIE
            #pragma shader_feature DIRECTIONAL

            #ifdef SHADOWS_DEPTH
            #define SHADOWS_NATIVE
            #endif

            struct v2p
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct input
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            v2p vertQuad(input v)
            {
                v2p o;
                o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

			float2 particleDensityLUT(v2p i) : SV_Target
			{
                float cosAngle = i.uv.x * 2.0 - 1.0;
                float sinAngle = sqrt(saturate(1 - cosAngle * cosAngle));
                float startHeight = lerp(0.0, _AtmosphereHeight, i.uv.y);

                float3 rayStart = float3(0, startHeight, 0);
                    float3 rayDir = float3(sinAngle, cosAngle, 0);
                float3 planetCenter = float3(0, -_PlanetRadius, 0);

                float stepCount = 250;

                float2 intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius);
                if (intersection.x > 0)
                {
                    // intersection with planet, write high density
                    return 1e+20;
                }

                intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius + _AtmosphereHeight);
                float3 rayEnd = rayStart + rayDir * intersection.y;

                // compute density along the ray
                float3 step = (rayEnd - rayStart) / stepCount;
                float stepSize = length(step);
                float2 density = 0;

                for (float s = 0.5; s < stepCount; s += 1.0)
                {
                    float3 position = rayStart + step * s;
                    float height = abs(length(position - planetCenter) - _PlanetRadius);
                    float2 localDensity = exp(-(height.xx / _DensityScaleHeight));

                    density += localDensity * stepSize;
                }

                return density;
			}

			ENDCG
		}

        // pass 1 - atm scattering
        Pass
        {
            ZTest Off
            Cull Off
            ZWrite Off
            Blend One Zero

            CGPROGRAM

#pragma vertex vert
#pragma fragment fragDir
#pragma target 4.0

#define UNITY_HDR_ON

#pragma shader_feature HEIGHT_FOG
#pragma shader_feature NOISE
#pragma shader_feature SHADOWS_DEPTH
#pragma shader_feature SHADOWS_NATIVE
#pragma shader_feature DIRECTIONAL_COOKIE
#pragma shader_feature DIRECTIONAL

#ifdef SHADOWS_DEPTH
#define SHADOWS_NATIVE
#endif		

			ScatteringOutput fragDir(v2f i)
            {
                float2 uv = i.uv.xy / i.uv.w;
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);

                float3 rayStart = _WorldSpaceCameraPos;
                float3 rayEnd = i.wpos;

                float3 rayDir = (rayEnd - rayStart);
                float rayLength = length(rayDir);
                rayDir /= rayLength;

                float linearDepth = LinearEyeDepth(depth);
                float projectedDepth = linearDepth / dot(_CameraForward, rayDir);

                rayLength = min(rayLength, projectedDepth);

				float distanceMultiplier = 1;
				float sunMultiplier = 0.5;
                
				float3 planetCenter = _WorldSpaceCameraPos;
				planetCenter = float3(0, -_PlanetRadius, 0);
				float2 intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius + _AtmosphereHeight);
				if (depth > 0.999999)
				{
					rayLength = 1e20;
				}
				else
				{
					distanceMultiplier = _DistanceMultiplier;
					sunMultiplier = 0;
				}

				rayLength = min(intersection.y, rayLength);

                intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius);
                if (intersection.x > 0)
                    rayLength = min(rayLength, intersection.x);
                
                //float4 color = LightScatter(rayStart, rayDir, rayLength, planetCenter);
				ScatteringOutput color = LightScatter_TrapezoidIntegration(rayStart, rayDir, rayLength, planetCenter, distanceMultiplier, sunMultiplier, _LightDir);
                    
                return color;
            }
                ENDCG
        }

			// pass 2 - ambient light LUT
			Pass
			{
				ZTest Off
				Cull Off
				ZWrite Off
				Blend Off

				CGPROGRAM

#pragma vertex vertQuad
#pragma fragment fragDir
#pragma target 4.0

			    struct v2p
			    {
				    float4 pos : SV_POSITION;
				    float2 uv : TEXCOORD0;
			    };

			    struct input
			    {
				    float4 vertex : POSITION;
				    float2 texcoord : TEXCOORD0;
			    };

			    v2p vertQuad(input v)
			    {
				    v2p o;
				    o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
				    o.uv = v.texcoord.xy;
				    return o;
			    }

				float4 fragDir(v2f i) : SV_Target
			    {
                    float cosAngle = i.uv.x * 2.0 - 1.0;
                    float sinAngle = sqrt(saturate(1 - cosAngle * cosAngle));
                    float startHeight = 0;

                    float3 lightDir = -normalize(float3(sinAngle, cosAngle, 0));

                    float3 rayStart = float3(0, startHeight, 0);
                    float3 planetCenter = float3(0, -_PlanetRadius + startHeight, 0);

                    float4 color = 0;

                    [loop]
                    for (int ii = 0; ii < 128; ++ii)
                    {
                        float3 rayDir = tex2D(_RandomVectors, float2(ii + (0.5 / 255.0), 0.5));
                        rayDir.y = abs(rayDir.y);

                        float2 intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius + _AtmosphereHeight);
                        float rayLength = intersection.y;

                        intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius);
                        if (intersection.x > 0)
                            rayLength = min(rayLength, intersection.x);

                        _SampleCount = 32;
                        color += (LightScatter_TrapezoidIntegration(rayStart, rayDir, rayLength, planetCenter, 1, 0, lightDir)).inscattering;
                    }

                    return color / 128;
			    }
				ENDCG
			}

				// pass 3 - dir light LUT
				Pass
			{
				ZTest Off
				Cull Off
				ZWrite Off
				Blend Off

				CGPROGRAM

#pragma vertex vertQuad
#pragma fragment fragDir
#pragma target 4.0

				struct v2p
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			struct input
			{
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			v2p vertQuad(input v)
			{
				v2p o;
				o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = v.texcoord.xy;
				return o;
			}

			float4 fragDir(v2f i) : SV_Target
			{
				float cosAngle = i.uv.x * 2.0 - 1.0;
				float sinAngle = sqrt(saturate(1 - cosAngle * cosAngle));
				float startHeight = 500;

				float3 rayStart = float3(0, startHeight, 0);
				float3 rayDir = normalize(float3(sinAngle, cosAngle, 0));

				float3 planetCenter = float3(0, -_PlanetRadius + startHeight , 0);

                float2 localDensity;
                float2 densityToAtmosphereTop;

                GetAtmosphereDensity(rayStart, planetCenter, -rayDir, localDensity, densityToAtmosphereTop);
                float4 color;
                color.xyz = ComputeOpticalDepth(densityToAtmosphereTop);
                color.w = 1;
                return color;
			}
				ENDCG
				}

			// pass 4 - combine pass
			Pass
			{
				ZTest Always Cull Off ZWrite Off
				Blend One Zero

				CGPROGRAM
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"

				sampler2D _MainTex;
			sampler2D _InscatteringTexture;
			sampler2D _ExtinctionTexture;

			struct appdata_t
			{
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f2
			{
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
			};

			v2f2 vert(appdata_t v)
			{
				v2f2 o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.texcoord = v.texcoord.xy;
				return o;
			}

			fixed4 frag(v2f2 i) : SV_Target
			{
				float4 background = tex2D(_MainTex, i.texcoord);
				float4 inscatter = tex2D(_InscatteringTexture, i.texcoord);
				float4 extinction = tex2D(_ExtinctionTexture, i.texcoord);

				return background * extinction + inscatter;
			}
				ENDCG
			}
	}
}
