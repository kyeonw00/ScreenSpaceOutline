using System.Collections.Generic;
using UnityEngine;
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
        private readonly List<OutlineRenderer> m_OutlineRenderers = new();
        
        private RTHandle m_MaskTexture;
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
            descriptor.msaaSamples = 1;
            
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
            
            RenderingUtils.ReAllocateIfNeeded(ref m_MaskTexture, maskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineMask");
            RenderingUtils.ReAllocateIfNeeded(ref m_TempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineTemp");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // collects outlinable objects
            m_OutlineRenderers.Clear();
            OutlineRenderer.GetActiveRenderers(m_OutlineRenderers);
            
            if (m_OutlineRenderers.Count == 0)
                return;
            
            var cmd = CommandBufferPool.Get("Outline Rendering");
            
            try
            {
                var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                RenderMask(cmd, context, ref renderingData);
                RenderOutline(cmd, ref renderingData);
                
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
        
        private void RenderMask(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // setup mask render target
            cmd.SetRenderTarget(m_MaskTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // render outline just for cached `OutlineRenderer`s
            foreach (var outlineRenderer in m_OutlineRenderers)
            {
                if (outlineRenderer == null || !outlineRenderer.IsActiveAndEnabled())
                    continue;
                
                var renderer = outlineRenderer.GetComponent<Renderer>();
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                        cmd.DrawRenderer(renderer, m_MaskMaterial, i, 0);
                }
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void RenderOutline(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            
            // setup shader properties
            m_OutlineMaterial.SetFloat(OutlineThicknessID, m_Settings.outlineThickness);
            m_OutlineMaterial.SetColor(OutlineColorID, m_Settings.outlineColor);
            m_OutlineMaterial.SetTexture(MaskTextureID, m_MaskTexture);
            m_OutlineMaterial.SetTexture(SourceTextureID, cameraColorTarget);
            
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
            m_TempTexture?.Release();
        }
    }
}