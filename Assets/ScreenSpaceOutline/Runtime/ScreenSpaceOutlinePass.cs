using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceOutline
{
    public class ScreenSpaceOutlinePass : ScriptableRenderPass
    {
        private static readonly int OutlineThicknessID = Shader.PropertyToID("_OutlineThickness");
        private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
        private static readonly int MaskTextureID = Shader.PropertyToID("_MaskTexture");
        private static readonly int SourceTextureID = Shader.PropertyToID("_SourceTexture");
        
        private readonly ScreenSpaceOutline.OutlineSettings m_Settings;
        private readonly Material m_MaskMaterial;
        private readonly Material m_OutlineMaterial;
        
        private RTHandle m_MaskTexture;
        private RTHandle m_ResolvedMaskTexture;
        private RTHandle m_TempTexture;
        private FilteringSettings m_FilteringSettings;
        
        // private List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>()
        // {
        //     new ShaderTagId("UniversalForward"),
        //     new ShaderTagId("UniversalForwardOnly"),
        //     new ShaderTagId("SRPDefaultUnlit")
        // };
        
        public ScreenSpaceOutlinePass(ScreenSpaceOutline.OutlineSettings settings, Material maskMat, Material outlineMat)
        {
            m_Settings = settings;
            m_MaskMaterial = maskMat;
            m_OutlineMaterial = outlineMat;
            
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, settings.outlineLayerMask);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            
            // set to mask resolution to current resolution
            var maskWidth = descriptor.width;
            var maskHeight = descriptor.height;
            
            // if outline thickness is small enough, reduce the resolution
            if (m_Settings.outlineThickness <= 2f)
            {
                maskWidth = Mathf.RoundToInt(descriptor.width * m_Settings.renderTextureScale);
                maskHeight = Mathf.RoundToInt(descriptor.height * m_Settings.renderTextureScale);
            }
            
            var maskDescriptor = descriptor;
            maskDescriptor.width = maskWidth;
            maskDescriptor.height = maskHeight;
            maskDescriptor.msaaSamples = m_Settings.useMSAA ? 4 : 1;
            
            RenderingUtils.ReAllocateIfNeeded(ref m_MaskTexture, maskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineMask");

            if (m_Settings.useMSAA)
            {
                var resolvedDescriptor = maskDescriptor;
                resolvedDescriptor.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref m_ResolvedMaskTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineMaskResolved");
            }
            
            RenderingUtils.ReAllocateIfNeeded(ref m_TempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineTemp");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // collects outlinable objects
            var renderersByColor = OutlineRenderer.GetRenderersByColor();
            
            if (renderersByColor.Count == 0)
                return;
            
            var cmd = CommandBufferPool.Get("Outline Rendering");
            
            try
            {
                foreach (var colorGroup in renderersByColor)
                {
                    RenderMask(cmd, colorGroup.Value, context, ref renderingData);
                    RenderOutline(cmd, colorGroup.Key, ref renderingData);
                }
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
        
        private void RenderMask(CommandBuffer cmd, List<OutlineRenderer> renderers, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // setup mask render target
            cmd.SetRenderTarget(m_MaskTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);

            var camera = renderingData.cameraData.camera;
            var screenHeight = Mathf.Max(1f, camera.pixelHeight);
            
            // render outline just for cached `OutlineRenderer`s
            foreach (var outlineRenderer in renderers)
            {
                if (outlineRenderer == null || !outlineRenderer.IsActiveAndEnabled())
                    continue;

                var renderer = outlineRenderer.Renderer;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                var thickness = m_Settings.outlineThickness;
                
                if (m_Settings.distanceAttenuation)
                {
                    var distance = Vector3.Distance(camera.transform.position, renderer.bounds.center);

                    var normalizedDistance = 0f;
                    if (distance <= m_Settings.minimumDistance)
                        normalizedDistance = 0f;
                    else if (distance >= m_Settings.maximumDistance)
                        normalizedDistance = 1f;
                    else
                        normalizedDistance = (distance - m_Settings.minimumDistance) / (m_Settings.maximumDistance - m_Settings.minimumDistance);

                    thickness = Mathf.Lerp(m_Settings.maximumThickness, m_Settings.minimumThickness, normalizedDistance);
                    thickness = Mathf.Clamp01(thickness / screenHeight);
                }

                cmd.SetGlobalColor("_MaskWriteColor", outlineRenderer.OutlineColor);
                cmd.SetGlobalFloat("_MaskThickness", thickness);
                
                for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                    cmd.DrawRenderer(renderer, m_MaskMaterial, i, 0);
            }

            if (m_Settings.useMSAA)
                cmd.CopyTexture(m_MaskTexture, m_ResolvedMaskTexture);
        }
        
        private void RenderOutline(CommandBuffer cmd, Color outlineColor, ref RenderingData renderingData)
        {
            var finalMask = m_Settings.useMSAA ? m_ResolvedMaskTexture : m_MaskTexture;
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            
            // setup shader properties
            
            // m_OutlineMaterial.SetFloat(OutlineThicknessID, m_Settings.outlineThickness);
            // m_OutlineMaterial.SetColor(OutlineColorID, outlineColor);
            // m_OutlineMaterial.SetTexture(MaskTextureID, finalMask);
            // m_OutlineMaterial.SetTexture(SourceTextureID, cameraColorTarget);
            
            cmd.SetGlobalFloat(OutlineThicknessID, m_Settings.outlineThickness);
            cmd.SetGlobalColor(OutlineColorID, outlineColor);
            cmd.SetGlobalTexture(MaskTextureID, finalMask.nameID);
            cmd.SetGlobalTexture(SourceTextureID, cameraColorTarget.nameID);
            
            // draw outline to temporary render target
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_TempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineTemp");
            
            // apply outline
            cmd.SetRenderTarget(m_TempTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawProcedural(Matrix4x4.identity, m_OutlineMaterial, 0, MeshTopology.Triangles, 3, 1);
            
            // blit temporary to camera color target
            cmd.SetRenderTarget(cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.Blit(m_TempTexture, cameraColorTarget);
        }
        
        public void Dispose()
        {
            m_MaskTexture?.Release();
            m_ResolvedMaskTexture?.Release();
            m_TempTexture?.Release();
        }
    }
}