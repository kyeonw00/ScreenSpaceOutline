Shader "Hidden/Outline/Outline"
{
//    Properties
//    {
//        _SourceTexture ("Source Texture", 2D) = "white" {}
//        _MaskTexture ("Mask Texture", 2D) = "white" {}
//    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "OutlineDetection"
            
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off
            ColorMask RGBA
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            TEXTURE2D(_MaskTexture);
            SAMPLER(sampler_MaskTexture);
            
            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            
            float _OutlineThickness;
            float4 _MaskTexture_TexelSize;
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }
            
            half DetectEdge(float2 uv)
            {
                float2 texelSize = _MaskTexture_TexelSize.xy * _OutlineThickness;
                half center = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv).r;

                // Sample by 8-direction
                half samples[9];
                samples[0] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-texelSize.x, texelSize.y)).a;
                samples[1] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(0, texelSize.y)).a;
                samples[2] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(texelSize.x, texelSize.y)).a;
                samples[3] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-texelSize.x, 0)).a;
                samples[5] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(texelSize.x, 0)).a;
                samples[6] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-texelSize.x, -texelSize.y)).a;
                samples[7] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(0, -texelSize.y)).a;
                samples[8] = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(texelSize.x, -texelSize.y)).a;
                
                half sobelX = samples[0] + 2.0 * samples[3] + samples[6] - (samples[2] + 2.0 * samples[5] + samples[8]);
                half sobelY = samples[0] + 2.0 * samples[1] + samples[2] - (samples[6] + 2.0 * samples[7] + samples[8]);
                half edge = sqrt(sobelX * sobelX + sobelY * sobelY);
                
                // anti-aliasing
                half diagonal1 = abs(samples[0] - samples[8]);
                half diagonal2 = abs(samples[2] - samples[6]);
                edge = max(edge, max(diagonal1, diagonal2));
                
                if (center > 0.5)
                    edge = 0.0;

                edge = smoothstep(0.1, 0.3, edge);
                
                return saturate(edge);
            }

            half3 SampleNearestMaskColor(float2 uv)
            {
                float2 t = _MaskTexture_TexelSize.xy * _OutlineThickness * 2;

                half4 center = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv);
                if (center.a > 0.5)
                    return center.rgb;

                half4 color;

                // vertical/horizontal sample
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(t.x, 0));
                if (color.a > 0.5)
                    return color.rgb;
                
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-t.x, 0));
                if (color.a > 0.5)
                    return color.rgb;
                
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2( 0 , t.y));
                if (color.a > 0.5)
                    return color.rgb;
                
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2( 0 ,-t.y));
                if (color.a > 0.5)
                    return color.rgb;

                // diagonal sample
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2( t.x,  t.y));
                if (color.a > 0.5h)
                    return color.rgb;
                    
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-t.x,  t.y));
                if (color.a > 0.5h)
                    return color.rgb;
                    
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2( t.x, -t.y));
                if (color.a > 0.5h)
                    return color.rgb;
                    
                color = SAMPLE_TEXTURE2D(_MaskTexture, sampler_MaskTexture, uv + float2(-t.x, -t.y));
                if (color.a > 0.5h)
                    return color.rgb;
                
                return 0.0;
            }
            
            half4 frag(Varyings input) : SV_Target
            {                
                half4 originalColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, input.texcoord);
                half edge = DetectEdge(input.texcoord);
                half3 color = SampleNearestMaskColor(input.texcoord);
                half3 finalColor = lerp(originalColor.rgb, color, edge);
                return half4(finalColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}